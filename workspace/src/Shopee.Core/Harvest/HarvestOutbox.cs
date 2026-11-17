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
/// <para>Bảng <c>pending</c>: một dòng mỗi SP của mỗi (account_id, sheet) — UNIQUE (account_id, sheet, item_id), schema v2.
/// <c>row_no</c> chỉ là thông tin (dòng thấy SP gần nhất): workbook dời dòng mà Hub đang sập thì SP khác lưu vào đúng số dòng cũ
/// KHÔNG đè mất SP đang chờ (v1 khoá theo row_no nên đè — SP đã vào tập <c>done</c> mà không bao giờ lên Hub). Lưu JSON ĐÃ làm sạch
/// (<see cref="HarvestPayloadCleaner"/>), <c>raw_hash</c>, và các cột tóm tắt đã parse sẵn (<c>item_dto</c> = JSON của
/// <see cref="HarvestedProductItem"/> thiếu <c>rawJson</c>) — parse lúc LƯU để lỗi parse lộ ra ngay ở dòng đó chứ không
/// nằm chờ trong hàng đợi. <c>status</c> = <c>pending</c> | <c>failed</c> (Hub từ chối mục — chờ người bấm "Đẩy lại mục
/// lỗi"). Bảng <c>counters</c>: số đã đẩy / đã ghi cộng dồn. Bảng <c>done</c>: tập khoá SP ĐÃ LẤY XONG theo target
/// (<see cref="HarvestDoneTarget"/>, <see cref="HarvestItemKey"/>) — ghi CÙNG giao dịch với lần lưu SP, KHÔNG xoá khi Hub đã nhận;
/// app dựng lại tiến độ từ đây mỗi lần chạy (<see cref="HarvestDoneRebuild"/>). Bảng <c>failed</c>: khoá SP lỗi VĨNH VIỄN theo
/// target (<see cref="RecordPermanentFailureAsync"/>) — dựng lại không lên lịch, lưu thành công thì tự xoá.</para>
/// <para>Đồng thời: WAL + <c>busy_timeout</c>; ghi trong process đi qua một cổng tuần tự; BUSY/LOCKED (process khác giữ
/// khoá) tự thử lại tới <see cref="BusyBudget"/> rồi mới ném. <c>synchronous=FULL</c>: <see cref="SaveAsync"/> trả về là
/// đã xuống đĩa, mất điện cũng không mất.</para>
/// </summary>
public sealed class HarvestOutbox : IHarvestSink, IHarvestFailureSink
{
    /// <summary>Ổ chứa outbox phải còn trống ít nhất chừng này lúc <see cref="EnsureReadyAsync"/> (mỗi SP ~60–120 KB).</summary>
    public const long DefaultMinFreeBytes = 500L * 1024 * 1024;

    /// <summary>Tổng thời gian tối đa một lần ghi chờ khoá (cổng trong process + BUSY/LOCKED từ process khác).</summary>
    public static readonly TimeSpan DefaultBusyBudget = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions DtoJson = new(JsonSerializerDefaults.Web);

    /// <summary>Phiên bản schema (<c>PRAGMA user_version</c>). DB có version LỚN hơn → <see cref="EnsureReadyAsync"/> ném.
    /// v2: khoá <c>pending</c> đổi từ (account_id, sheet, row_no) sang (account_id, sheet, item_id) — migration dựng lại bảng
    /// (<see cref="MigrateV1ToV2"/>). Bảng <c>done</c> / <c>failed</c> thêm bằng <c>CREATE TABLE IF NOT EXISTS</c>.
    /// v3: <c>failed</c> thêm <c>hits</c> (số LƯỢT chạy khác nhau thấy lỗi) + <c>last_run_id</c> (<see cref="MigrateV2ToV3"/>).</summary>
    public const int SchemaVersion = 3;

    /// <summary>Khoá lỗi vĩnh viễn chỉ được bỏ qua khi đã thấy ở ít nhất chừng này LƯỢT chạy khác nhau (lỗi một lần lẻ — tài
    /// khoản / mạng trục trặc lúc đó — thì lượt sau vẫn thử lại).</summary>
    public const int FailedConfirmRuns = 2;

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

    /// <summary>Làm sạch + parse + upsert theo (account, sheet, item_id). Bản có <c>harvested_at</c> MỚI HƠN HOẶC BẰNG thay bản
    /// đang giữ CỦA CÙNG SP (về lại <c>pending</c>, xoá đếm thử/lỗi, row_no = dòng mới); bản CŨ hơn bị bỏ qua (vẫn trả bình
    /// thường). SP khác cùng số dòng là dòng riêng — không đè. Gửi lại cùng mục bao nhiêu lần cũng ra một dòng → idempotent. JSON không xử lý được → ném <see cref="HarvestItemRejectedException"/>
    /// (chưa lưu; engine ghi lỗi dòng rồi đi tiếp). Lỗi khác (đĩa, khoá quá ngân sách…) = hạ tầng → engine dừng job.
    /// Có <see cref="HarvestedItem.DoneTarget"/> → trong CÙNG giao dịch: khoá SP vào tập đã xong CHỈ KHI pending đang giữ một
    /// bản của chính SP này (bản vừa ghi, hoặc bản mới hơn đã có — bản này cũ hơn nên bị bỏ qua), và gỡ các khoá đó khỏi tập lỗi
    /// vĩnh viễn.</summary>
    public Task SaveAsync(HarvestedItem item, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = BuildRow(item);
        IReadOnlyList<string> doneKeys = string.IsNullOrWhiteSpace(item.DoneTarget)
            ? []
            : HarvestItemKey.ForSaved(item.SourceLink, item.ItemId, item.ShopeeShopId);
        EnsureInitialized();
        WithWriteRetry(con =>
        {
            using var tx = con.BeginTransaction();
            using var cmd = con.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO pending (account_id, sheet, row_no, item_id, raw_hash, harvested_at, json, item_dto, source,
                                     status, attempts, last_error, next_try_at, created_at, updated_at, version)
                VALUES ($acct, $sheet, $row, $item, $hash, $at, $json, $dto, $source, 'pending', 0, NULL, 0, $now, $now, 1)
                ON CONFLICT (account_id, sheet, item_id) DO UPDATE SET
                    row_no = excluded.row_no, raw_hash = excluded.raw_hash, harvested_at = excluded.harvested_at,
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
            var n = cmd.ExecuteNonQuery();
            if (doneKeys.Count > 0)
            {
                using var held = con.CreateCommand();
                held.Transaction = tx;
                held.CommandText = "SELECT count(*) FROM pending WHERE account_id = $acct AND sheet = $sheet AND item_id = $item;";
                held.Parameters.AddWithValue("$acct", row.AccountKey);
                held.Parameters.AddWithValue("$sheet", row.SheetKey);
                held.Parameters.AddWithValue("$item", item.ItemId);
                if ((long)(held.ExecuteScalar() ?? 0L) == 0) doneKeys = [];   // không giữ bản nào của SP này → chưa được tính xong
            }
            if (!string.IsNullOrWhiteSpace(row.SampleLink))
            {
                // Link mẫu cho kiểm chứng phiên (cầu dao notProduct): SP vừa lưu được gần nhất, bất kể target.
                using var meta = con.CreateCommand();
                meta.Transaction = tx;
                meta.CommandText = "INSERT INTO meta (key, value) VALUES ('sample_link', $v) ON CONFLICT (key) DO UPDATE SET value = excluded.value;";
                meta.Parameters.AddWithValue("$v", row.SampleLink);
                meta.ExecuteNonQuery();
            }
            foreach (var key in doneKeys)
            {
                using var done = con.CreateCommand();
                done.Transaction = tx;
                done.CommandText = """
                    INSERT INTO done (target, item_key, saved_at) VALUES ($t, $k, $now)
                    ON CONFLICT (target, item_key) DO UPDATE SET saved_at = excluded.saved_at;
                    """;
                done.Parameters.AddWithValue("$t", item.DoneTarget);
                done.Parameters.AddWithValue("$k", key);
                done.Parameters.AddWithValue("$now", NowMs());
                done.ExecuteNonQuery();
                using var unfail = con.CreateCommand();
                unfail.Transaction = tx;
                unfail.CommandText = "DELETE FROM failed WHERE target = $t AND item_key = $k;";
                unfail.Parameters.AddWithValue("$t", item.DoneTarget);
                unfail.Parameters.AddWithValue("$k", key);
                unfail.ExecuteNonQuery();
            }
            tx.Commit();
            return n;
        }, cancellationToken);
    }, cancellationToken);

    // ── Tập SP đã lấy xong ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Tập khoá SP đã lưu xong của <paramref name="target"/> (<see cref="HarvestDoneTarget.Of"/>).</summary>
    public IReadOnlySet<string> DoneKeys(string target)
    {
        EnsureInitialized();
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT item_key FROM done WHERE target = $t;";
        cmd.Parameters.AddWithValue("$t", target);
        var set = new HashSet<string>(StringComparer.Ordinal);
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) set.Add(rd.GetString(0));
        return set;
    }

    /// <summary>Xoá tập đã xong của ĐÚNG <paramref name="target"/> (nút Reset tiến độ). Trả số khoá đã xoá. Dữ liệu SP trong
    /// hàng đợi đẩy Hub không bị đụng.</summary>
    public int ClearDone(string target) => DeleteTarget("done", target);

    // ── Tập SP lỗi vĩnh viễn ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Ghi khoá SP lỗi VĨNH VIỄN (SP không tồn tại / không phải trang SP / JSON bị từ chối vì nội dung) của target.
    /// Mỗi LƯỢT chạy (<paramref name="runId"/>) chỉ tăng <c>hits</c> một lần; dựng lại chỉ bỏ qua khoá có <c>hits</c> ≥
    /// <see cref="FailedConfirmRuns"/>. Khoá đã có trong tập đã xong thì bỏ qua (đã lấy được là xong).</summary>
    public Task RecordPermanentFailureAsync(string target, IReadOnlyList<string> itemKeys, string reason, string runId,
        CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (string.IsNullOrWhiteSpace(target) || itemKeys.Count == 0) return;
        WithWriteRetry(con =>
        {
            using var tx = con.BeginTransaction();
            foreach (var key in itemKeys)
            {
                using var cmd = con.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO failed (target, item_key, reason, at, hits, last_run_id)
                    SELECT $t, $k, $r, $now, 1, $run WHERE NOT EXISTS (SELECT 1 FROM done WHERE target = $t AND item_key = $k)
                    ON CONFLICT (target, item_key) DO UPDATE SET reason = excluded.reason, at = excluded.at,
                        hits = failed.hits + CASE WHEN failed.last_run_id = excluded.last_run_id THEN 0 ELSE 1 END,
                        last_run_id = excluded.last_run_id;
                    """;
                cmd.Parameters.AddWithValue("$run", runId ?? "");
                cmd.Parameters.AddWithValue("$t", target);
                cmd.Parameters.AddWithValue("$k", key);
                cmd.Parameters.AddWithValue("$r", reason ?? "");
                cmd.Parameters.AddWithValue("$now", NowMs());
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return 0;
        }, cancellationToken);
    }, cancellationToken);

    /// <summary>Tập khoá SP lỗi vĩnh viễn ĐÃ XÁC NHẬN (thấy ở ≥ <paramref name="minRuns"/> lượt chạy khác nhau) của
    /// <paramref name="target"/>.</summary>
    public IReadOnlySet<string> FailedKeys(string target, int minRuns = FailedConfirmRuns)
    {
        EnsureInitialized();
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT item_key FROM failed WHERE target = $t AND hits >= $min;";
        cmd.Parameters.AddWithValue("$t", target);
        cmd.Parameters.AddWithValue("$min", minRuns);
        var set = new HashSet<string>(StringComparer.Ordinal);
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) set.Add(rd.GetString(0));
        return set;
    }

    /// <summary>Xoá tập lỗi vĩnh viễn của ĐÚNG <paramref name="target"/> (nút "Cào lại dòng đã bỏ" / Reset) → lượt sau thử lại.</summary>
    public int ClearFailed(string target) => DeleteTarget("failed", target);

    private int DeleteTarget(string table, string target) => WithWriteRetry(con =>
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"DELETE FROM {table} WHERE target = $t;";
        cmd.Parameters.AddWithValue("$t", target);
        return cmd.ExecuteNonQuery();
    }, CancellationToken.None);

    private sealed record BuiltRow(string AccountKey, string SheetKey, int RowKey, string Json, string Hash, string Dto)
    {
        public string? SampleLink { get; init; }
    }

    /// <summary>Link (đã làm sạch) của SP lưu được gần nhất — link mẫu để kiểm chứng phiên khi cầu dao notProduct nhảy. null =
    /// chưa từng lưu.</summary>
    public string? LastSavedLink()
    {
        EnsureInitialized();
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = 'sample_link';";
        return cmd.ExecuteScalar() as string;
    }

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
        return new BuiltRow(acct, sheet, item.RowNo, json, hash, JsonSerializer.Serialize(dto, DtoJson))
        {
            SampleLink = string.IsNullOrWhiteSpace(item.Url) ? null : HarvestPayloadCleaner.StripInvalidText(item.Url),
        };
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

    /// <summary>Trạng thái hiện tại của mục có số dòng gần nhất <paramref name="rowNo"/> (cho test / chẩn đoán; nhiều SP cùng số
    /// dòng → bản id lớn nhất).</summary>
    internal (string Status, int Attempts, string? LastError, long NextTryAtMs, long Version)? Inspect(string accountId, string sheet, int rowNo)
    {
        EnsureInitialized();
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT status, attempts, last_error, next_try_at, version FROM pending WHERE account_id = $a AND sheet = $s AND row_no = $r ORDER BY id DESC LIMIT 1;";
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
                using (var wal = con.CreateCommand())
                {
                    wal.CommandText = "PRAGMA journal_mode=WAL;";
                    wal.ExecuteNonQuery();
                }
                // Mỗi bước migration nâng user_version trong CÙNG giao dịch với thay đổi schema.
                if (version == 1) { MigrateV1ToV2(con); version = 2; }
                if (version == 2) MigrateV2ToV3(con);
                using var cmd = con.CreateCommand();
                cmd.CommandText = $"""
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
                        UNIQUE (account_id, sheet, item_id)
                    );
                    CREATE INDEX IF NOT EXISTS ix_pending_due ON pending (status, next_try_at, id);
                    CREATE INDEX IF NOT EXISTS ix_pending_row ON pending (account_id, sheet, row_no);
                    CREATE TABLE IF NOT EXISTS counters (key TEXT PRIMARY KEY, value INTEGER NOT NULL);
                    CREATE TABLE IF NOT EXISTS done (
                        target   TEXT    NOT NULL,
                        item_key TEXT    NOT NULL,
                        saved_at INTEGER NOT NULL,
                        PRIMARY KEY (target, item_key)
                    ) WITHOUT ROWID;
                    CREATE TABLE IF NOT EXISTS failed (
                        target   TEXT    NOT NULL,
                        item_key TEXT    NOT NULL,
                        reason   TEXT    NOT NULL DEFAULT '',
                        at       INTEGER NOT NULL,
                        hits        INTEGER NOT NULL DEFAULT 1,
                        last_run_id TEXT    NOT NULL DEFAULT '',
                        PRIMARY KEY (target, item_key)
                    ) WITHOUT ROWID;
                    CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                    CREATE TABLE IF NOT EXISTS probe (at INTEGER NOT NULL);
                    PRAGMA user_version = {SchemaVersion};
                    """;
                cmd.ExecuteNonQuery();
                return 0;
            }, CancellationToken.None, gate: false);
            _initialized = true;
        }
    }

    /// <summary>v1 → v2: dựng lại <c>pending</c> với khoá (account_id, sheet, item_id), GIỮ id / version / trạng thái của từng mục
    /// (worker xoá theo id + version — đổi là xoá oan). Trùng (cùng SP ở nhiều dòng — v1 cho phép) → giữ bản harvested_at mới
    /// nhất (bằng nhau → id lớn hơn) — nhưng bản đang <c>pending</c> luôn thắng bản <c>failed</c> (bản failed là bản Hub đã từ chối;
    /// giữ nó mà bỏ bản pending thì SP chờ "Đẩy lại mục lỗi" mãi). Một giao dịch: hỏng giữa chừng thì DB vẫn nguyên v1.</summary>
    private static void MigrateV1ToV2(SqliteConnection con)
    {
        using var tx = con.BeginTransaction();
        using var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            CREATE TABLE pending_v2 (
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
                UNIQUE (account_id, sheet, item_id)
            );
            INSERT INTO pending_v2 (id, account_id, sheet, row_no, item_id, raw_hash, harvested_at, json, item_dto, source,
                                    status, attempts, last_error, next_try_at, created_at, updated_at, version)
            SELECT id, account_id, sheet, row_no, item_id, raw_hash, harvested_at, json, item_dto, source,
                   status, attempts, last_error, next_try_at, created_at, updated_at, version
            FROM pending p
            WHERE NOT EXISTS (
                SELECT 1 FROM pending q
                WHERE q.account_id = p.account_id AND q.sheet = p.sheet AND q.item_id = p.item_id
                  AND ((q.status = 'pending' AND p.status <> 'pending')
                    OR ((q.status = 'pending') = (p.status = 'pending')
                        AND (q.harvested_at > p.harvested_at OR (q.harvested_at = p.harvested_at AND q.id > p.id)))));
            DROP TABLE pending;
            ALTER TABLE pending_v2 RENAME TO pending;
            CREATE INDEX ix_pending_due ON pending (status, next_try_at, id);
            CREATE INDEX ix_pending_row ON pending (account_id, sheet, row_no);
            PRAGMA user_version = 2;
            """;
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    /// <summary>v2 → v3: <c>failed</c> thêm <c>hits</c> / <c>last_run_id</c> (khoá cũ hits = 1 → cần thêm một lượt mới bị bỏ qua).
    /// Bảng chưa có (DB v2 từ migration v1) thì để CREATE TABLE phía sau tạo đủ cột.</summary>
    private static void MigrateV2ToV3(SqliteConnection con)
    {
        using var tx = con.BeginTransaction();
        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var info = con.CreateCommand())
        {
            info.Transaction = tx;
            info.CommandText = "SELECT name FROM pragma_table_info('failed');";
            using var rd = info.ExecuteReader();
            while (rd.Read()) cols.Add(rd.GetString(0));
        }
        using var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        var sql = new System.Text.StringBuilder();
        if (cols.Count > 0 && !cols.Contains("hits")) sql.Append("ALTER TABLE failed ADD COLUMN hits INTEGER NOT NULL DEFAULT 1;");
        if (cols.Count > 0 && !cols.Contains("last_run_id")) sql.Append("ALTER TABLE failed ADD COLUMN last_run_id TEXT NOT NULL DEFAULT '';");
        sql.Append("PRAGMA user_version = 3;");
        cmd.CommandText = sql.ToString();
        cmd.ExecuteNonQuery();
        tx.Commit();
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
