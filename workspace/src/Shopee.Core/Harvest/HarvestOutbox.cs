using System.Globalization;
using Microsoft.Data.Sqlite;
using Shopee.Core.Coordination;
using Shopee.Core.Infrastructure;

namespace Shopee.Core.Harvest;

/// <summary>Số liệu outbox cho UI. <see cref="PushedTotal"/> = số mục Hub đã nhận (ok=true, đã xoá khỏi hàng đợi),
/// <see cref="AppliedTotal"/> = phần trong đó Hub thật sự ghi (không tính trùng hash / bản cũ) — cả hai CỘNG DỒN bền trong
/// DB outbox (qua các lần mở app).</summary>
public sealed record HarvestOutboxStats(int Pending, int Failed, long PushedTotal, long AppliedTotal);

/// <summary>Một mục đang chờ đẩy. <see cref="Version"/> tăng mỗi lần dòng (account, sheet, row) bị ghi đè bằng bản mới
/// hơn: worker chỉ xoá / đánh lỗi đúng phiên bản nó đã gửi — bản mới lưu đè trong lúc lô đang bay không bị xoá oan.</summary>
internal sealed record HarvestOutboxEntry(long Id, long Version, HarvestedProductItem Item, int Attempts, long NextTryAtMs);

/// <summary>
/// Hàng đợi cục bộ (SQLite) của app Harvest — engine ghi SP vào đây (<see cref="IHarvestSink"/>), <see cref="HarvestOutboxWorker"/>
/// đẩy dần lên Hub. Hub sập / mất mạng thì dữ liệu nằm chờ ở đây, không mất.
/// <para>Bảng <c>pending</c>: một dòng mỗi (account_id, sheet, row_no) — UNIQUE. Lưu JSON ĐÃ làm sạch
/// (<see cref="HarvestPayloadCleaner"/>), <c>raw_hash</c>, và các cột tóm tắt đã parse sẵn (<c>item_dto</c> = JSON của
/// <see cref="HarvestedProductItem"/> thiếu <c>rawJson</c>) — parse lúc LƯU để lỗi parse lộ ra ngay ở dòng đó chứ không
/// nằm chờ trong hàng đợi. <c>status</c> = <c>pending</c> | <c>failed</c> (Hub từ chối mục — chờ người bấm "Đẩy lại mục
/// lỗi"). Bảng <c>counters</c>: số đã đẩy / đã ghi cộng dồn.</para>
/// <para>Đồng thời: WAL + <c>busy_timeout</c>; ghi trong process đi qua một cổng tuần tự; BUSY/LOCKED (process khác giữ
/// khoá) tự thử lại tới <see cref="BusyBudget"/> rồi mới ném. <c>synchronous=FULL</c>: <see cref="SaveAsync"/> trả về là
/// đã xuống đĩa, mất điện cũng không mất.</para>
/// </summary>
public sealed class HarvestOutbox : IHarvestSink
{
    /// <summary>Ổ chứa outbox phải còn trống ít nhất chừng này lúc <see cref="EnsureReadyAsync"/> (mỗi SP ~60–120 KB).</summary>
    public const long DefaultMinFreeBytes = 500L * 1024 * 1024;

    /// <summary>Tổng thời gian tối đa một lần ghi chờ khoá (cổng trong process + BUSY/LOCKED từ process khác).</summary>
    public static readonly TimeSpan DefaultBusyBudget = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions DtoJson = new(JsonSerializerDefaults.Web);

    /// <summary>Phiên bản schema (<c>PRAGMA user_version</c>). DB có version LỚN hơn → <see cref="EnsureReadyAsync"/> ném.</summary>
    public const int SchemaVersion = 1;

    private readonly string _dbPath;
    private readonly string _connString;
    private readonly long _minFreeBytes;
    private readonly Action<string>? _log;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _initLock = new();
    private bool _initialized;

    public TimeSpan BusyBudget { get; }

    public string DbPath => _dbPath;

    /// <summary>Đường dẫn mặc định: <c>&lt;SuitePaths.Root&gt;\harvest\harvest-outbox.db</c> (tạo thư mục nếu chưa có).</summary>
    public static string DefaultDbPath() => Path.Combine(SuitePaths.ModuleDir("harvest"), "harvest-outbox.db");

    /// <param name="log">Log một dòng cho các tình huống đáng biết nhưng không phải lỗi (vd bỏ link gốc quá dài).</param>
    public HarvestOutbox(string dbPath, long minFreeBytes = DefaultMinFreeBytes, TimeSpan? busyBudget = null,
        Action<string>? log = null)
    {
        _log = log;
        _dbPath = Path.GetFullPath(dbPath);
        _minFreeBytes = minFreeBytes;
        BusyBudget = busyBudget ?? DefaultBusyBudget;
        // Không pool: mỗi lần mở/đóng thật → không giữ file sau khi xong việc (xoá/di chuyển DB, dọn file tạm của test).
        // DefaultTimeout: Microsoft.Data.Sqlite TỰ lặp lại lệnh bị BUSY tới CommandTimeout (mặc định 30 s) — hạ xuống để
        // một lần thử ngắn, ngân sách tổng do WithWriteRetry giữ.
        _connString = new SqliteConnectionStringBuilder { DataSource = _dbPath, Pooling = false, DefaultTimeout = 1 }.ToString();
    }

    // ── IHarvestSink ────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Mở DB, tạo schema, thử ghi + xoá một dòng, kiểm đĩa trống — lỗi gì cũng ném (job dừng trước khi mở Brave).</summary>
    public Task EnsureReadyAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        WithWriteRetry(con =>
        {
            using var tx = con.BeginTransaction();
            using var cmd = con.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO probe (at) VALUES ($at); DELETE FROM probe;";
            cmd.Parameters.AddWithValue("$at", NowMs());
            cmd.ExecuteNonQuery();
            tx.Commit();
            return 0;
        }, cancellationToken);
        var free = DiskSpaceGuard.FreeBytesFor(_dbPath);
        if (free >= 0 && free < _minFreeBytes)
            throw new IOException($"ổ chứa outbox ({_dbPath}) chỉ còn {DiskSpaceGuard.ToGb(free)} trống, cần ≥ {DiskSpaceGuard.ToGb(_minFreeBytes)}");
    }, cancellationToken);

    /// <summary>Làm sạch + parse + upsert theo (account, sheet, row). Bản có <c>harvested_at</c> MỚI HƠN HOẶC BẰNG thay bản
    /// đang giữ (về lại <c>pending</c>, xoá đếm thử/lỗi); bản CŨ hơn bị bỏ qua (vẫn trả bình thường). Gửi lại cùng mục bao
    /// nhiêu lần cũng ra một dòng → idempotent. JSON không xử lý được → ném <see cref="HarvestItemRejectedException"/>
    /// (chưa lưu; engine ghi lỗi dòng rồi đi tiếp). Lỗi khác (đĩa, khoá quá ngân sách…) = hạ tầng → engine dừng job.</summary>
    public Task SaveAsync(HarvestedItem item, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = BuildRow(item);
        EnsureInitialized();
        WithWriteRetry(con =>
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = """
                INSERT INTO pending (account_id, sheet, row_no, item_id, raw_hash, harvested_at, json, item_dto, source,
                                     status, attempts, last_error, next_try_at, created_at, updated_at, version)
                VALUES ($acct, $sheet, $row, $item, $hash, $at, $json, $dto, $source, 'pending', 0, NULL, 0, $now, $now, 1)
                ON CONFLICT (account_id, sheet, row_no) DO UPDATE SET
                    item_id = excluded.item_id, raw_hash = excluded.raw_hash, harvested_at = excluded.harvested_at,
                    json = excluded.json, item_dto = excluded.item_dto, source = excluded.source,
                    status = 'pending', attempts = 0, last_error = NULL, next_try_at = 0,
                    updated_at = excluded.updated_at, version = pending.version + 1
                WHERE excluded.harvested_at >= pending.harvested_at;
                """;
            cmd.Parameters.AddWithValue("$acct", row.AccountKey);
            cmd.Parameters.AddWithValue("$sheet", row.SheetKey);
            cmd.Parameters.AddWithValue("$row", row.RowKey);
            cmd.Parameters.AddWithValue("$item", item.ItemId);
            cmd.Parameters.AddWithValue("$hash", row.Hash);
            cmd.Parameters.AddWithValue("$at", item.HarvestedAt.UtcTicks);
            cmd.Parameters.AddWithValue("$json", row.Json);
            cmd.Parameters.AddWithValue("$dto", row.Dto);
            cmd.Parameters.AddWithValue("$source", item.Source ?? "");
            cmd.Parameters.AddWithValue("$now", NowMs());
            return cmd.ExecuteNonQuery();
        }, cancellationToken);
    }, cancellationToken);

    private sealed record BuiltRow(string AccountKey, string SheetKey, int RowKey, string Json, string Hash, string Dto);

    /// <summary>Dựng dòng outbox từ mục engine giao: JSON sạch + hash + DTO (chưa có rawJson). Hàm thuần, không I/O.</summary>
    private BuiltRow BuildRow(HarvestedItem item)
    {
        var json = HarvestPayloadCleaner.Clean(item.Json);
        var hash = HarvestPayloadCleaner.Hash(json);
        var s = ShopeePdpParser.Parse(json);
        var acct = HarvestPayloadCleaner.StripInvalidText(item.AccountId);
        var sheet = HarvestPayloadCleaner.StripInvalidText(item.Sheet);
        // Bộ ba dòng nguồn đủ cả hoặc bỏ cả (Hub từ chối 1–2 trường). Engine luôn có đủ; phòng caller khác.
        var hasSource = !string.IsNullOrWhiteSpace(acct) && !string.IsNullOrWhiteSpace(sheet) && item.RowNo >= 1;
        var sourceUrl = HarvestPayloadCleaner.StripInvalidText(item.SourceLink);
        if (sourceUrl.Length > HarvestedProductLimits.MaxUrlLength)
        {
            // Hub từ chối CẢ SP nếu link gốc quá dài — bỏ link gốc (Hub lấy Url đã làm sạch) để không mất dữ liệu SP.
            try { _log?.Invoke($"Dòng {item.RowNo} ({item.Sheet}): link gốc dài {sourceUrl.Length} ký tự > {HarvestedProductLimits.MaxUrlLength} — không gửi link gốc, Hub dùng link đã làm sạch."); }
            catch { /* log hỏng không được chặn việc lưu */ }
            sourceUrl = "";
        }
        var dto = new HarvestedProductItem(
            item.ItemId, s.ShopId > 0 ? s.ShopId : item.ShopeeShopId ?? 0, HarvestPayloadCleaner.StripInvalidText(item.Url),
            s.Name, s.CatId, s.CategoryIds, s.BrandId, s.PriceMin, s.PriceMax, s.PriceBeforeDiscount, s.TierCount, s.ModelCount,
            s.ImageUrls, s.VideoUrls, s.SizeChartUrl, RawJson: null, RawHash: hash,
            hasSource ? acct : null, hasSource ? sheet : null, hasSource ? item.RowNo : null, item.HarvestedAt,
            string.IsNullOrWhiteSpace(sourceUrl) ? null : sourceUrl);
        return new BuiltRow(acct, sheet, item.RowNo, json, hash, JsonSerializer.Serialize(dto, DtoJson));
    }

    // ── Cho worker / UI ─────────────────────────────────────────────────────────────────────────────────────────────

    public Task<HarvestOutboxStats> GetStatsAsync() => Task.Run(GetStats);

    public HarvestOutboxStats GetStats()
    {
        EnsureInitialized();
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT (SELECT count(*) FROM pending WHERE status = 'pending'),
                   (SELECT count(*) FROM pending WHERE status = 'failed'),
                   (SELECT coalesce(sum(CASE WHEN key = 'pushed' THEN value END), 0) FROM counters),
                   (SELECT coalesce(sum(CASE WHEN key = 'applied' THEN value END), 0) FROM counters);
            """;
        using var rd = cmd.ExecuteReader();
        rd.Read();
        return new HarvestOutboxStats(rd.GetInt32(0), rd.GetInt32(1), rd.GetInt64(2), rd.GetInt64(3));
    }

    /// <summary>Đưa mọi mục <c>failed</c> về <c>pending</c> (đến hạn ngay, xoá đếm thử). Trả số mục.</summary>
    public int RequeueFailed() => WithWriteRetry(con =>
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = "UPDATE pending SET status = 'pending', attempts = 0, last_error = NULL, next_try_at = 0, updated_at = $now WHERE status = 'failed';";
        cmd.Parameters.AddWithValue("$now", NowMs());
        return cmd.ExecuteNonQuery();
    }, CancellationToken.None);

    /// <summary>Tối đa <paramref name="max"/> mục <c>pending</c> đến hạn (<c>next_try_at ≤ nowMs</c>), cũ trước (theo id),
    /// trừ các id trong <paramref name="excludeIds"/> (mục đang bị nghi vấn — chúng được gửi lẻ riêng).</summary>
    internal List<HarvestOutboxEntry> TakeDue(int max, long nowMs, IReadOnlyCollection<long>? excludeIds = null)
    {
        EnsureInitialized();
        using var con = Open();
        using var cmd = con.CreateCommand();
        var exclude = excludeIds is { Count: > 0 }
            ? " AND id NOT IN (" + string.Join(',', excludeIds.Select(i => i.ToString(CultureInfo.InvariantCulture))) + ")"
            : "";
        cmd.CommandText = $"""
            SELECT id, version, json, item_dto, attempts, next_try_at FROM pending
            WHERE status = 'pending' AND next_try_at <= $now{exclude} ORDER BY id LIMIT $max;
            """;
        cmd.Parameters.AddWithValue("$now", nowMs);
        cmd.Parameters.AddWithValue("$max", max);
        return ReadEntries(cmd);
    }

    /// <summary>Các mục <c>pending</c> trong <paramref name="ids"/> (bất kể hạn), theo id.</summary>
    internal List<HarvestOutboxEntry> TakeByIds(IReadOnlyCollection<long> ids)
    {
        EnsureInitialized();
        if (ids.Count == 0) return [];
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT id, version, json, item_dto, attempts, next_try_at FROM pending WHERE status = 'pending' AND id IN ("
                          + string.Join(',', ids.Select(i => i.ToString(CultureInfo.InvariantCulture))) + ") ORDER BY id;";
        return ReadEntries(cmd);
    }

    private static List<HarvestOutboxEntry> ReadEntries(SqliteCommand cmd)
    {
        var list = new List<HarvestOutboxEntry>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var dto = JsonSerializer.Deserialize<HarvestedProductItem>(rd.GetString(3), DtoJson)!;
            list.Add(new HarvestOutboxEntry(rd.GetInt64(0), rd.GetInt64(1), dto with { RawJson = rd.GetString(2) },
                rd.GetInt32(4), rd.GetInt64(5)));
        }
        return list;
    }

    /// <summary>Hub đã nhận (ok=true): xoá ĐÚNG phiên bản đã gửi + cộng đếm, cùng một transaction.</summary>
    internal void Complete(IReadOnlyList<(HarvestOutboxEntry Entry, bool Applied)> done)
    {
        if (done.Count == 0) return;
        WithWriteRetry(con =>
        {
            using var tx = con.BeginTransaction();
            long pushed = 0, applied = 0;
            foreach (var (e, a) in done)
            {
                using var del = con.CreateCommand();
                del.Transaction = tx;
                del.CommandText = "DELETE FROM pending WHERE id = $id AND version = $v;";
                del.Parameters.AddWithValue("$id", e.Id);
                del.Parameters.AddWithValue("$v", e.Version);
                del.ExecuteNonQuery();
                // Đếm theo phản hồi Hub, kể cả khi dòng đã bị bản mới đè (bản cũ vẫn là một lần Hub nhận).
                pushed++;
                if (a) applied++;
            }
            using var cnt = con.CreateCommand();
            cnt.Transaction = tx;
            cnt.CommandText = """
                INSERT INTO counters (key, value) VALUES ('pushed', $p), ('applied', $a)
                ON CONFLICT (key) DO UPDATE SET value = counters.value + excluded.value;
                """;
            cnt.Parameters.AddWithValue("$p", pushed);
            cnt.Parameters.AddWithValue("$a", applied);
            cnt.ExecuteNonQuery();
            tx.Commit();
            return 0;
        }, CancellationToken.None);
    }

    /// <summary>Đánh dấu <c>failed</c> kèm lý do (chỉ phiên bản đã gửi; bản mới đè thì giữ <c>pending</c>).</summary>
    internal void MarkFailed(IReadOnlyList<(HarvestOutboxEntry Entry, string Reason)> failed) =>
        Update(failed,
            "UPDATE pending SET status = 'failed', attempts = attempts + 1, last_error = $err, updated_at = $now WHERE id = $id AND version = $v;",
            0);

    /// <summary>Lỗi thoáng qua: giữ <c>pending</c>, tăng đếm thử, hẹn lại lúc <paramref name="nextTryAtMs"/>.</summary>
    internal void Defer(IReadOnlyList<HarvestOutboxEntry> entries, long nextTryAtMs, string error) =>
        Update(entries.Select(e => (e, error)).ToList(),
            "UPDATE pending SET attempts = attempts + 1, last_error = $err, next_try_at = $next, updated_at = $now WHERE id = $id AND version = $v;",
            nextTryAtMs);

    /// <summary>Một câu UPDATE cho từng mục (khoá theo id + version), chung một transaction.</summary>
    private void Update(IReadOnlyList<(HarvestOutboxEntry Entry, string Err)> ops, string sql, long next)
    {
        if (ops.Count == 0) return;
        WithWriteRetry(con =>
        {
            using var tx = con.BeginTransaction();
            var now = NowMs();
            foreach (var (e, err) in ops)
            {
                using var cmd = con.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("$id", e.Id);
                cmd.Parameters.AddWithValue("$v", e.Version);
                cmd.Parameters.AddWithValue("$now", now);
                cmd.Parameters.AddWithValue("$next", next);
                if (sql.Contains("$err", StringComparison.Ordinal)) cmd.Parameters.AddWithValue("$err", err);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return 0;
        }, CancellationToken.None);
    }

    /// <summary>Trạng thái hiện tại của một dòng (cho test / chẩn đoán).</summary>
    internal (string Status, int Attempts, string? LastError, long NextTryAtMs, long Version)? Inspect(string accountId, string sheet, int rowNo)
    {
        EnsureInitialized();
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT status, attempts, last_error, next_try_at, version FROM pending WHERE account_id = $a AND sheet = $s AND row_no = $r;";
        cmd.Parameters.AddWithValue("$a", accountId);
        cmd.Parameters.AddWithValue("$s", sheet);
        cmd.Parameters.AddWithValue("$r", rowNo);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;
        return (rd.GetString(0), rd.GetInt32(1), rd.IsDBNull(2) ? null : rd.GetString(2), rd.GetInt64(3), rd.GetInt64(4));
    }

    // ── Hạ tầng SQLite ─────────────────────────────────────────────────────────────────────────────────────────────

    private void EnsureInitialized()
    {
        lock (_initLock)
        {
            if (_initialized) return;
            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            WithWriteRetry(con =>
            {
                long version;
                using (var get = con.CreateCommand())
                {
                    get.CommandText = "PRAGMA user_version;";
                    version = (long)(get.ExecuteScalar() ?? 0L);
                }
                // DB do bản app MỚI hơn tạo (đã migrate lên schema mà bản này không hiểu) → không được ghi bừa.
                if (version > SchemaVersion)
                    throw new InvalidOperationException(
                        $"outbox {_dbPath} có schema v{version}, bản app này chỉ hiểu tới v{SchemaVersion} — cập nhật app rồi chạy lại");
                // Chỗ cho migration sau này: if (version < 2) { ALTER TABLE …; } — mỗi bước nâng user_version.
                using var cmd = con.CreateCommand();
                cmd.CommandText = $"""
                    PRAGMA journal_mode=WAL;
                    CREATE TABLE IF NOT EXISTS pending (
                        id           INTEGER PRIMARY KEY AUTOINCREMENT,
                        account_id   TEXT    NOT NULL,
                        sheet        TEXT    NOT NULL,
                        row_no       INTEGER NOT NULL,
                        item_id      INTEGER NOT NULL,
                        raw_hash     TEXT    NOT NULL,
                        harvested_at INTEGER NOT NULL,
                        json         TEXT    NOT NULL,
                        item_dto     TEXT    NOT NULL,
                        source       TEXT    NOT NULL DEFAULT '',
                        status       TEXT    NOT NULL DEFAULT 'pending',
                        attempts     INTEGER NOT NULL DEFAULT 0,
                        last_error   TEXT,
                        next_try_at  INTEGER NOT NULL DEFAULT 0,
                        created_at   INTEGER NOT NULL,
                        updated_at   INTEGER NOT NULL,
                        version      INTEGER NOT NULL DEFAULT 1,
                        UNIQUE (account_id, sheet, row_no)
                    );
                    CREATE INDEX IF NOT EXISTS ix_pending_due ON pending (status, next_try_at, id);
                    CREATE TABLE IF NOT EXISTS counters (key TEXT PRIMARY KEY, value INTEGER NOT NULL);
                    CREATE TABLE IF NOT EXISTS probe (at INTEGER NOT NULL);
                    PRAGMA user_version = {SchemaVersion};
                    """;
                cmd.ExecuteNonQuery();
                return 0;
            }, CancellationToken.None, gate: false);
            _initialized = true;
        }
    }

    private SqliteConnection Open()
    {
        var con = new SqliteConnection(_connString);
        con.Open();
        using var pragma = con.CreateCommand();
        // busy_timeout ngắn hơn ngân sách tổng: SQLite tự chờ một quãng, hết thì vòng thử lại bên ngoài lo tiếp.
        pragma.CommandText = "PRAGMA busy_timeout=1000; PRAGMA synchronous=FULL;";
        pragma.ExecuteNonQuery();
        return con;
    }

    /// <summary>Chạy một thao tác GHI: qua cổng tuần tự trong process, thử lại SQLITE_BUSY/LOCKED tới hết
    /// <see cref="BusyBudget"/> (tính cả thời gian chờ cổng); hết ngân sách → ném <see cref="TimeoutException"/> /
    /// lỗi SQLite cuối. Lỗi khác (đĩa đầy, file hỏng…) ném ngay.</summary>
    private T WithWriteRetry<T>(Func<SqliteConnection, T> work, CancellationToken ct, bool gate = true)
    {
        if (gate) EnsureInitialized();
        var sw = Stopwatch.StartNew();
        if (gate && !_writeGate.Wait(BusyBudget, ct))
            throw new TimeoutException($"outbox bận quá {BusyBudget.TotalSeconds:0} s (chờ lượt ghi trong app)");
        try
        {
            var delay = 25;
            while (true)
            {
                try
                {
                    using var con = Open();
                    return work(con);
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6 && sw.Elapsed < BusyBudget)
                {
                    ct.ThrowIfCancellationRequested();
                    Thread.Sleep(delay);
                    delay = Math.Min(delay * 2, 400);
                }
            }
        }
        finally
        {
            if (gate) _writeGate.Release();
        }
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
