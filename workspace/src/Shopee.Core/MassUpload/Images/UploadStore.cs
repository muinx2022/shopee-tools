using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Shopee.Core.Coordination;
using Shopee.Core.Infrastructure;

namespace Shopee.Core.MassUpload.Images;

/// <summary>Loại ảnh trong <c>image_prep</c>: ảnh SP (có đếm tham chiếu, xoá được) hay ảnh bảng size dùng chung (KHÔNG BAO
/// GIỜ xoá trên Hub — để TTL tự dọn).</summary>
public enum ImagePrepKind { Product, SizeChart }

/// <summary>Số ảnh của một shop theo trạng thái (cho UI): chờ (gồm <c>hosted</c> chưa kiểm) / đã sẵn sàng / lỗi / đang xoá
/// trên Hub.</summary>
public sealed record ImagePrepStats(int Pending, int Verified, int Failed, int Deleting = 0);

/// <summary>Một ảnh <c>pending</c> / <c>hosted</c> đến hạn mà worker lấy ra xử lý. Ảnh <c>hosted</c> mang theo chỗ đã host
/// (<see cref="HubPath"/>, <see cref="Url"/>) cùng <see cref="Md5"/> / <see cref="ContentType"/> để kiểm, không up lại.
/// <see cref="SourcePath"/> = đường dẫn file gốc (giữ hoa/thường) của ảnh bảng size.</summary>
internal sealed record ImagePrepEntry(string ShopKey, string SourceKey, ImagePrepKind Kind, int Attempts,
    string State = UploadStore.StatePending, string? Md5 = null, string? HubPath = null, string? Url = null,
    string? ContentType = null, string? SourcePath = null);

/// <summary>Một ảnh đang <c>deleting</c> chờ xoá trên Hub.</summary>
internal sealed record ImageDeleteEntry(string ShopKey, string SourceKey, string? HubPath);

/// <summary>Trạng thái một lượt xuất file (GĐ8, bảng <c>export_batch</c>).</summary>
public static class ExportBatchState
{
    public const string Running = "running";
    public const string Done = "done";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
}

/// <summary>Một lượt xuất file (GĐ8) cho UI: <see cref="Images"/> = số ảnh (khoá) ghi theo lượt; <see cref="ClearedAt"/> khác
/// null = đã xoá ảnh tạm của lượt trên Hub.
/// <para><see cref="ExpiresAt"/> = hạn THẬT của lượt: mốc hết hạn sớm nhất trên Hub của các ảnh file lượt này trỏ tới (kể cả bảng
/// size); null = chưa biết (lượt chưa xong / lượt cũ) HOẶC (GĐ9) mọi ảnh SP là link kho ảnh gốc Hub (không hạn) — khi đó
/// <see cref="HubImages"/> &gt; 0 và hạn còn lại chỉ là <see cref="SizeChartExpiresAt"/>. <see cref="TemplateShopId"/> = shop_id
/// (ô D2) của file mẫu đã dùng. <see cref="HubImages"/> / <see cref="CdnImages"/> = số ảnh SP lấy link Hub / phải tải CDN (đường
/// cũ, kho tạm có hạn). <see cref="HubSourcesDeletedAt"/> = lúc kho ảnh gốc trên Hub bị xoá sạch SAU khi lượt này xuất (link ảnh
/// SP của file hỏng nếu chưa đẩy).</para></summary>
public sealed record ExportBatchInfo(string BatchId, string ShopKey, string ShopLabel, DateTimeOffset CreatedAt, string State,
    int Products, int Files, string? OutputDir, string? Note, DateTimeOffset? ClearedAt, int Images, DateTimeOffset? ExpiresAt = null,
    string? TemplateShopId = null, DateTimeOffset? SizeChartExpiresAt = null, int HubImages = 0, int CdnImages = 0,
    DateTimeOffset? HubSourcesDeletedAt = null);

/// <summary>Kết quả <see cref="UploadStore.ClearBatchAsync"/>: <see cref="Deleted"/> ảnh đã xoá xong (trên Hub, hoặc chưa từng lên
/// Hub); <see cref="SharedKept"/> ảnh giữ lại vì lượt khác chưa xoá còn dùng; <see cref="SizeChartKept"/> ảnh bảng size dùng chung
/// (không bao giờ xoá chủ động — Hub tự xoá khi hết hạn); <see cref="Failed"/> ảnh xoá trên Hub hụt (giữ <c>deleting</c>, bấm lại
/// hoặc worker dọn sau).</summary>
public sealed record BatchClearResult(int Deleted, int SharedKept, int SizeChartKept, int Failed)
{
    public bool Complete => Failed == 0;
}

/// <summary>
/// Sổ cục bộ (SQLite) của app đăng SP — <c>&lt;SuitePaths.Root&gt;\upload\upload.db</c>. Hiện có bước đệm MD5 ảnh (plan GĐ2
/// bước 4); GĐ5 thêm bảng sổ đăng bằng migration theo <c>PRAGMA user_version</c>.
/// <para><b>Hợp đồng cho lượt đăng (GĐ5):</b></para>
/// <list type="number">
/// <item>Chọn nguồn xong → <see cref="EnqueueAsync"/> MỌI ảnh của từng SP (ảnh SP, ảnh lựa chọn) + <see cref="EnqueueSizeChartAsync"/>
/// với <see cref="SizeChartImage.FromFile"/> — TRƯỚC khi resolve.</item>
/// <item>Dựng file → <see cref="GetResolverAsync"/> với đúng các khoá các SP định đăng → đưa cho <see cref="MassUploadMapper"/>.</item>
/// <item>SP bị Skip vĩnh viễn / người dùng bỏ → <see cref="ForgetItemAsync"/> (gỡ tham chiếu, không gọi Hub; ảnh hết tham
/// chiếu được worker dọn).</item>
/// <item>SP đã lên Bản nháp (Shopee đã kéo ảnh) → <see cref="ReleaseItemAsync"/> (gỡ tham chiếu + xoá ngay trên Hub).</item>
/// </list>
/// <para>Bảng <c>image_prep</c> — khoá (shop_key, source_key): <c>source_key</c> = id ảnh gốc trên CDN Shopee (ảnh SP) hoặc khoá
/// <see cref="SizeChartImage"/>. <c>state</c>:</para>
/// <list type="bullet">
/// <item><c>pending</c> — chờ <see cref="ImagePrepWorker"/> (đến hạn khi <c>next_attempt_at</c> ≤ bây giờ).</item>
/// <item><c>hosted</c> — đã up lên Hub, đã ghi bền <c>md5</c> / <c>content_type</c> / <c>hub_path</c> / <c>url</c> /
/// <c>expires_at</c>, NHƯNG chưa kiểm. Worker lấy lại để kiểm, KHÔNG up lại. Resolver coi là chờ.</item>
/// <item><c>verified</c> — đã host trên Hub và tải lại không token khớp MD5.</item>
/// <item><c>failed</c> — lỗi gắn với CHÍNH ảnh (ảnh gốc 404, định dạng không hỗ trợ, quá cỡ, Hub 413/415, kiểm lệch lặp lại tới
/// trần) kèm <c>last_error</c>. <see cref="RetryFailedAsync"/> đưa về <c>pending</c>.</item>
/// <item><c>deleting</c> — ảnh SP hết tham chiếu, đang chờ xoá trên Hub (<c>updated_at</c> = mốc đánh dấu). Release xoá ngay;
/// Forget / Release hụt / huỷ giữa chừng thì worker dọn lại sau <see cref="ImagePrepWorkerOptions.DeletingRetryAfter"/>.
/// Resolver không trả, worker xử lý ảnh không lấy.</item>
/// </list>
/// <para>Bảng <c>image_ref</c> — (shop_key, source_key, item_id): SP nào đang dùng ảnh nào. Ảnh bảng size không có tham chiếu
/// và không bao giờ bị xoá.</para>
/// <para>Đồng thời theo khuôn <c>HarvestOutbox</c>: WAL + <c>busy_timeout</c>, ghi trong process qua một cổng tuần tự,
/// BUSY/LOCKED thử lại tới <see cref="BusyBudget"/>. Mốc thời gian lưu dạng Unix ms.</para>
/// </summary>
public sealed class UploadStore
{
    /// <summary>Phiên bản schema (<c>PRAGMA user_version</c>). DB có version LỚN hơn → mở là ném (bản app cũ không ghi bừa).
    /// v1: <c>image_prep</c> + <c>image_ref</c>. Cột <c>content_type</c> / <c>source_path</c>, trạng thái <c>hosted</c> / <c>deleting</c> thêm thẳng
    /// vào v1 vì v1 CHƯA PHÁT HÀNH (chưa có DB thật nào) — không cần migration. Từ bản phát hành đầu, mọi thay đổi schema phải
    /// nâng version + migration.
    /// <para>v2 (GĐ8): <c>export_batch</c> + <c>batch_image</c> — ảnh theo lượt xuất (xem <see cref="EnqueueForBatchAsync"/>,
    /// <see cref="ClearBatchAsync"/>). DB v1 nâng lên v2 trong MỘT giao dịch, dữ liệu v1 giữ nguyên.</para>
    /// <para>v3 (GĐ8 vòng 3): <c>export_batch.expires_at</c> (hạn thật của lượt) + <c>export_batch.template_shop_id</c>.</para>
    /// <para>v4 (GĐ9): <c>export_batch.size_chart_expires_at</c>, <c>hub_images</c>, <c>cdn_images</c>.</para>
    /// <para>v5 (GĐ9 vòng 2): <c>export_batch.hub_sources_deleted_at</c>.</para></summary>
    public const int SchemaVersion = 5;

    /// <summary>Ảnh <c>verified</c> còn hạn ít hơn chừng này thì resolver coi như chưa sẵn sàng (đưa về <c>pending</c>).</summary>
    public static readonly TimeSpan DefaultMinRemaining = TimeSpan.FromDays(1);

    public static readonly TimeSpan DefaultBusyBudget = TimeSpan.FromSeconds(10);

    internal const string StatePending = "pending";
    internal const string StateHosted = "hosted";
    internal const string StateVerified = "verified";
    internal const string StateFailed = "failed";
    internal const string StateDeleting = "deleting";

    private readonly string _dbPath;
    private readonly string _connString;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string>? _log;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _initLock = new();
    private bool _initialized;

    public TimeSpan BusyBudget { get; }

    public string DbPath => _dbPath;

    /// <summary>Đường dẫn mặc định: <c>&lt;SuitePaths.Root&gt;\upload\upload.db</c> (tạo thư mục nếu chưa có).</summary>
    public static string DefaultDbPath() => Path.Combine(SuitePaths.ModuleDir("upload"), "upload.db");

    /// <param name="clock">Đồng hồ (test tiêm giờ giả); null = giờ hệ thống.</param>
    /// <param name="log">Log một dòng cho tình huống đáng biết (vd xoá ảnh Hub hụt).</param>
    public UploadStore(string dbPath, Func<DateTimeOffset>? clock = null, TimeSpan? busyBudget = null, Action<string>? log = null)
    {
        _dbPath = Path.GetFullPath(dbPath);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _log = log;
        BusyBudget = busyBudget ?? DefaultBusyBudget;
        _connString = new SqliteConnectionStringBuilder { DataSource = _dbPath, Pooling = false, DefaultTimeout = 1 }.ToString();
    }

    internal DateTimeOffset Now => _clock();

    // ── Hàng đợi ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Ghi các ảnh SP <paramref name="itemId"/> đang dùng (tham chiếu) và đưa ảnh CHƯA có trong sổ của shop vào
    /// <c>pending</c>. Ảnh đã có (bất kể trạng thái) giữ nguyên — gọi lại bao nhiêu lần cũng vậy. Khoá rỗng / trùng bị bỏ.</summary>
    public Task EnqueueAsync(string shopKey, long itemId, IEnumerable<string> sourceKeys, CancellationToken ct = default) =>
        EnqueueCoreAsync(null, shopKey, itemId, sourceKeys, ct);

    /// <summary>(GĐ8) Như <see cref="EnqueueAsync"/> và ghi các ảnh vào lượt <paramref name="batchId"/> (<c>batch_image</c>) trong
    /// CÙNG giao dịch — ảnh vào hàng chờ là đã thuộc lượt, huỷ giữa chừng vẫn xoá được theo lượt. Lượt chưa tạo
    /// (<see cref="CreateBatchAsync"/>) hoặc đã xoá ảnh → <see cref="InvalidOperationException"/>, không ghi gì.</summary>
    public Task EnqueueForBatchAsync(string batchId, string shopKey, long itemId, IEnumerable<string> sourceKeys,
        CancellationToken ct = default)
    {
        RequireKey(batchId, nameof(batchId));
        return EnqueueCoreAsync(batchId, shopKey, itemId, sourceKeys, ct);
    }

    private Task EnqueueCoreAsync(string? batchId, string shopKey, long itemId, IEnumerable<string> sourceKeys, CancellationToken ct)
    {
        RequireKey(shopKey, nameof(shopKey));
        var keys = sourceKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).ToList();
        return Task.Run(() =>
        {
            if (keys.Count == 0) return;
            WithWriteRetry(con =>
            {
                using var tx = con.BeginTransaction();
                if (batchId is not null) RequireOpenBatch(con, tx, batchId, shopKey);
                var now = NowMs();
                foreach (var key in keys)
                {
                    using var prep = con.CreateCommand();
                    prep.Transaction = tx;
                    prep.CommandText = """
                        INSERT INTO image_prep (shop_key, source_key, kind, state, attempts, next_attempt_at, updated_at)
                        VALUES ($shop, $key, 'product', 'pending', 0, 0, $now)
                        ON CONFLICT (shop_key, source_key) DO NOTHING;
                        INSERT INTO image_ref (shop_key, source_key, item_id) VALUES ($shop, $key, $item)
                        ON CONFLICT (shop_key, source_key, item_id) DO NOTHING;
                        """ + (batchId is null ? "" : BatchImageInsertSql);
                    prep.Parameters.AddWithValue("$shop", shopKey);
                    prep.Parameters.AddWithValue("$key", key);
                    prep.Parameters.AddWithValue("$item", itemId);
                    prep.Parameters.AddWithValue("$now", now);
                    if (batchId is not null) prep.Parameters.AddWithValue("$batch", batchId);
                    prep.ExecuteNonQuery();
                }
                tx.Commit();
                return 0;
            }, ct);
        }, ct);
    }

    /// <summary>Đưa ảnh bảng size dùng chung của shop vào hàng chờ (kind = sizechart, không có tham chiếu, không bao giờ xoá).
    /// Khoá = <see cref="SizeChartImage.Key"/>, đường dẫn đọc file = <see cref="SizeChartImage.FilePath"/> (giữ hoa/thường, ghi
    /// đè mỗi lần gọi). Đã có thì giữ trạng thái; riêng <c>failed</c> thì đặt lại <c>pending</c>. Dòng đang là ảnh SP trùng khoá
    /// thì chuyển sang sizechart (để không bao giờ bị xoá).</summary>
    public Task EnqueueSizeChartAsync(string shopKey, SizeChartImage image, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        return EnqueueSizeChartAsync(shopKey, image.Key, image.FilePath, ct);
    }

    /// <summary>Như <see cref="EnqueueSizeChartAsync(string, SizeChartImage, CancellationToken)"/> với khoá + đường dẫn tách rời.</summary>
    public Task EnqueueSizeChartAsync(string shopKey, string sizeChartKey, string filePath, CancellationToken ct = default) =>
        EnqueueSizeChartCoreAsync(null, shopKey, sizeChartKey, filePath, ct);

    /// <summary>(GĐ8) Như <see cref="EnqueueSizeChartAsync(string, SizeChartImage, CancellationToken)"/> và ghi ảnh vào lượt
    /// <paramref name="batchId"/> trong cùng giao dịch (để danh sách lượt đếm đủ ảnh). Ảnh bảng size vẫn KHÔNG BAO GIỜ bị
    /// <see cref="ClearBatchAsync"/> xoá.</summary>
    public Task EnqueueSizeChartForBatchAsync(string batchId, string shopKey, SizeChartImage image, CancellationToken ct = default)
    {
        RequireKey(batchId, nameof(batchId));
        ArgumentNullException.ThrowIfNull(image);
        return EnqueueSizeChartCoreAsync(batchId, shopKey, image.Key, image.FilePath, ct);
    }

    private Task EnqueueSizeChartCoreAsync(string? batchId, string shopKey, string sizeChartKey, string filePath, CancellationToken ct)
    {
        RequireKey(shopKey, nameof(shopKey));
        RequireKey(sizeChartKey, nameof(sizeChartKey));
        RequireKey(filePath, nameof(filePath));
        return Task.Run(() => WithWriteRetry(con =>
        {
            using var tx = con.BeginTransaction();
            if (batchId is not null) RequireOpenBatch(con, tx, batchId, shopKey);
            using var cmd = con.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO image_prep (shop_key, source_key, kind, state, attempts, next_attempt_at, updated_at, source_path)
                VALUES ($shop, $key, 'sizechart', 'pending', 0, 0, $now, $path)
                ON CONFLICT (shop_key, source_key) DO UPDATE SET
                    kind = 'sizechart', source_path = excluded.source_path,
                    state = CASE WHEN image_prep.state = 'failed' THEN 'pending' ELSE image_prep.state END,
                    attempts = CASE WHEN image_prep.state = 'failed' THEN 0 ELSE image_prep.attempts END,
                    next_attempt_at = CASE WHEN image_prep.state = 'failed' THEN 0 ELSE image_prep.next_attempt_at END,
                    last_error = CASE WHEN image_prep.state = 'failed' THEN NULL ELSE image_prep.last_error END,
                    updated_at = excluded.updated_at;
                """ + (batchId is null ? "" : BatchImageInsertSql);
            cmd.Parameters.AddWithValue("$shop", shopKey);
            cmd.Parameters.AddWithValue("$key", sizeChartKey);
            cmd.Parameters.AddWithValue("$path", filePath);
            cmd.Parameters.AddWithValue("$now", NowMs());
            if (batchId is not null) cmd.Parameters.AddWithValue("$batch", batchId);
            var n = cmd.ExecuteNonQuery();
            tx.Commit();
            return n;
        }, ct), ct);
    }

    /// <summary>Đưa mọi ảnh <c>failed</c> của shop về <c>pending</c> (xoá số lần thử, lý do) — nút "Làm lại ảnh lỗi". Trả số
    /// ảnh đã đưa về.</summary>
    public Task<int> RetryFailedAsync(string shopKey, CancellationToken ct = default)
    {
        RequireKey(shopKey, nameof(shopKey));
        return Task.Run(() => WithWriteRetry(con =>
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = """
                UPDATE image_prep SET state = 'pending', attempts = 0, next_attempt_at = 0, last_error = NULL,
                    hub_path = NULL, url = NULL, expires_at = NULL, updated_at = $now
                WHERE shop_key = $shop AND state = 'failed';
                """;
            cmd.Parameters.AddWithValue("$shop", shopKey);
            cmd.Parameters.AddWithValue("$now", NowMs());
            return cmd.ExecuteNonQuery();
        }, ct), ct);
    }

    /// <summary>Bản chụp tình trạng ảnh của shop cho <see cref="MassUploadMapper"/>: <see cref="ImageReadiness.Ready"/> (URL) khi
    /// <c>verified</c> còn hạn ≥ <paramref name="minRemaining"/> (mặc định <see cref="DefaultMinRemaining"/>);
    /// <see cref="ImageReadiness.Failed"/> (lý do) khi <c>failed</c>; còn lại (chưa có trong sổ, <c>pending</c>, <c>hosted</c>,
    /// <c>deleting</c>, sắp hết hạn) → <see cref="ImageReadiness.Pending"/>.
    /// <para>Chỉ các ảnh trong <paramref name="neededKeys"/> mà <c>verified</c> sắp hết hạn mới bị đưa về <c>pending</c> NGAY lúc
    /// chụp (worker làm lại, SP lùi sang lượt sau); ảnh khác của shop không bị đụng. Ảnh cũ trên Hub KHÔNG bị xoá (có thể đang
    /// nằm trong file đã tải lên) — TTL tự dọn.</para></summary>
    public Task<Func<string, ImageResolution>> GetResolverAsync(string shopKey, IEnumerable<string> neededKeys,
        TimeSpan? minRemaining = null, CancellationToken ct = default)
    {
        RequireKey(shopKey, nameof(shopKey));
        var needed = (neededKeys ?? []).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).ToList();
        var min = minRemaining ?? DefaultMinRemaining;
        return Task.Run(() => WithWriteRetry<Func<string, ImageResolution>>(con =>
        {
            var now = NowMs();
            var cutoff = now + (long)min.TotalMilliseconds;
            using var tx = con.BeginTransaction();
            var stale = 0;
            foreach (var key in needed)
            {
                using var cmd = con.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    UPDATE image_prep SET state = 'pending', url = NULL, hub_path = NULL, expires_at = NULL, attempts = 0,
                        next_attempt_at = 0, last_error = NULL, updated_at = $now
                    WHERE shop_key = $shop AND source_key = $key AND state = 'verified'
                        AND (expires_at IS NULL OR expires_at < $cutoff);
                    """;
                cmd.Parameters.AddWithValue("$shop", shopKey);
                cmd.Parameters.AddWithValue("$key", key);
                cmd.Parameters.AddWithValue("$cutoff", cutoff);
                cmd.Parameters.AddWithValue("$now", now);
                stale += cmd.ExecuteNonQuery();
            }
            if (stale > 0) _log?.Invoke($"[ảnh] shop {shopKey}: {stale} ảnh sắp hết hạn trên Hub → làm lại");
            var map = new Dictionary<string, ImageResolution>(StringComparer.Ordinal);
            using (var sel = con.CreateCommand())
            {
                sel.Transaction = tx;
                sel.CommandText = "SELECT source_key, state, url, expires_at, last_error FROM image_prep WHERE shop_key = $shop;";
                sel.Parameters.AddWithValue("$shop", shopKey);
                using var rd = sel.ExecuteReader();
                while (rd.Read())
                {
                    var state = rd.GetString(1);
                    map[rd.GetString(0)] = state switch
                    {
                        StateVerified when !rd.IsDBNull(2) && !rd.IsDBNull(3) && rd.GetInt64(3) >= cutoff
                            => ImageResolution.Ready(rd.GetString(2)),
                        StateFailed => ImageResolution.Failed(rd.IsDBNull(4) ? "lỗi không rõ" : rd.GetString(4)),
                        _ => ImageResolution.Pending,
                    };
                }
            }
            tx.Commit();
            return key => key is not null && map.TryGetValue(key, out var r) ? r : ImageResolution.Pending;
        }, ct), ct);
    }

    /// <summary>SP <paramref name="itemId"/> đã lên Bản nháp (Shopee đã kéo ảnh — caller tự bảo đảm): gỡ tham chiếu của SP và
    /// xoá NGAY trên Hub các ảnh SP của CHÍNH SP này không còn SP nào dùng (không quét ảnh khác của shop):
    /// <list type="bullet">
    /// <item><c>pending</c> / <c>failed</c> → xoá dòng (chưa có gì trên Hub).</item>
    /// <item><c>verified</c> / <c>hosted</c> → <c>deleting</c> → <see cref="HubClient.DeletePublicMediaAsync"/> (404 = đã xoá).
    /// Xoá được: còn tham chiếu mới (SP khác vừa Enqueue) → <c>pending</c> (ảnh trên Hub đã mất), không → xoá dòng. Xoá hụt:
    /// còn tham chiếu → <c>hosted</c> (worker kiểm lại), không → giữ <c>deleting</c>, worker dọn lại sau.</item>
    /// </list>
    /// Huỷ <paramref name="ct"/> giữa lúc xoá → ảnh nằm ở <c>deleting</c>, worker dọn sau (không kẹt). Ảnh bảng size KHÔNG BAO
    /// GIỜ bị đụng. Trả số ảnh đã xoá xong trên Hub.</summary>
    public async Task<int> ReleaseItemAsync(string shopKey, long itemId, HubClient hub, CancellationToken ct = default)
    {
        RequireKey(shopKey, nameof(shopKey));
        ArgumentNullException.ThrowIfNull(hub);
        var doomed = await Task.Run(() => Unref(shopKey, itemId, NowMs(), ct), ct).ConfigureAwait(false);
        var deleted = 0;
        foreach (var d in doomed)
            if (await FinishDeleteAsync(d, hub, ct).ConfigureAwait(false)) deleted++;
        return deleted;
    }

    /// <summary>Gỡ tham chiếu của SP <paramref name="itemId"/> (SP bị Skip / người dùng bỏ) KHÔNG gọi Hub: ảnh SP hết tham
    /// chiếu đang <c>pending</c>/<c>failed</c> → xoá dòng; <c>verified</c>/<c>hosted</c> → <c>deleting</c> đến hạn dọn ngay
    /// (worker xoá trên Hub ở lượt kế).</summary>
    public Task ForgetItemAsync(string shopKey, long itemId, CancellationToken ct = default)
    {
        RequireKey(shopKey, nameof(shopKey));
        return Task.Run(() => { Unref(shopKey, itemId, 0, ct); }, ct);
    }

    /// <summary>Số ảnh của shop theo trạng thái (<c>hosted</c> tính vào chờ).</summary>
    public Task<ImagePrepStats> GetStatsAsync(string shopKey, CancellationToken ct = default) => Task.Run(() =>
    {
        EnsureInitialized();
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT state, count(*) FROM image_prep WHERE shop_key = $shop GROUP BY state;";
        cmd.Parameters.AddWithValue("$shop", shopKey ?? "");
        int p = 0, v = 0, f = 0, d = 0;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var n = rd.GetInt32(1);
            switch (rd.GetString(0))
            {
                case StatePending: case StateHosted: p += n; break;
                case StateVerified: v = n; break;
                case StateFailed: f = n; break;
                case StateDeleting: d = n; break;
            }
        }
        return new ImagePrepStats(p, v, f, d);
    }, ct);

    // ── Lượt xuất file (GĐ8) ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Phần SQL thêm một ảnh vào lượt (cần <c>$batch</c>, <c>$shop</c>, <c>$key</c>).</summary>
    private const string BatchImageInsertSql = """

        INSERT INTO batch_image (batch_id, shop_key, source_key) VALUES ($batch, $shop, $key)
        ON CONFLICT (batch_id, shop_key, source_key) DO NOTHING;
        """;

    /// <summary>Tạo lượt <paramref name="batchId"/> (trạng thái <see cref="ExportBatchState.Running"/>). Trùng mã → ném.
    /// <paramref name="templateShopId"/> = shop_id (ô D2) của file mẫu dùng cho lượt.</summary>
    public Task CreateBatchAsync(string batchId, string shopKey, string shopLabel, string? outputDir, CancellationToken ct = default,
        string? templateShopId = null)
    {
        RequireKey(batchId, nameof(batchId));
        RequireKey(shopKey, nameof(shopKey));
        return Task.Run(() => WithWriteRetry(con =>
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = """
                INSERT INTO export_batch (batch_id, shop_key, shop_label, created_at, state, output_dir, template_shop_id)
                VALUES ($batch, $shop, $label, $now, 'running', $dir, $tshop);
                """;
            cmd.Parameters.AddWithValue("$tshop", (object?)templateShopId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$batch", batchId);
            cmd.Parameters.AddWithValue("$shop", shopKey);
            cmd.Parameters.AddWithValue("$label", shopLabel ?? "");
            cmd.Parameters.AddWithValue("$now", NowMs());
            cmd.Parameters.AddWithValue("$dir", (object?)outputDir ?? DBNull.Value);
            return cmd.ExecuteNonQuery();
        }, ct), ct);
    }

    /// <summary>Chốt lượt: trạng thái (<see cref="ExportBatchState"/>), số SP / số file đã xuất, ghi chú (lý do huỷ / lỗi), hạn thật
    /// của lượt (<see cref="ExportBatchInfo.ExpiresAt"/>).</summary>
    public Task FinishBatchAsync(string batchId, string state, int products, int files, string? note, CancellationToken ct = default,
        DateTimeOffset? expiresAt = null, DateTimeOffset? sizeChartExpiresAt = null, int hubImages = 0, int cdnImages = 0)
    {
        RequireKey(batchId, nameof(batchId));
        return Task.Run(() => WithWriteRetry(con =>
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = """
                UPDATE export_batch SET state = $state, products = $products, files = $files, note = $note, expires_at = $exp,
                    size_chart_expires_at = $sexp, hub_images = $hub, cdn_images = $cdn
                WHERE batch_id = $batch;
                """;
            cmd.Parameters.AddWithValue("$sexp", sizeChartExpiresAt is { } se ? se.ToUnixTimeMilliseconds() : DBNull.Value);
            cmd.Parameters.AddWithValue("$hub", hubImages);
            cmd.Parameters.AddWithValue("$cdn", cdnImages);
            cmd.Parameters.AddWithValue("$exp", expiresAt is { } e ? e.ToUnixTimeMilliseconds() : DBNull.Value);
            cmd.Parameters.AddWithValue("$batch", batchId);
            cmd.Parameters.AddWithValue("$state", state);
            cmd.Parameters.AddWithValue("$products", products);
            cmd.Parameters.AddWithValue("$files", files);
            cmd.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
            return cmd.ExecuteNonQuery();
        }, ct), ct);
    }

    /// <summary>Lượt còn <see cref="ExportBatchState.Running"/> lúc mở app = lượt trước bị tắt giữa chừng → đánh
    /// <see cref="ExportBatchState.Failed"/> (ảnh của nó vẫn xoá được). Gọi MỘT lần lúc khởi động (app chặn chạy 2 bản). Trả số lượt.</summary>
    public Task<int> MarkInterruptedBatchesAsync(CancellationToken ct = default) => Task.Run(() => WithWriteRetry(con =>
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            UPDATE export_batch SET state = 'failed', note = coalesce(note, 'app tắt giữa lượt xuất') WHERE state = 'running';
            """;
        return cmd.ExecuteNonQuery();
    }, ct), ct);

    /// <summary>Các lượt, mới nhất trước, tối đa <paramref name="limit"/>.</summary>
    public Task<IReadOnlyList<ExportBatchInfo>> ListBatchesAsync(int limit = 200, CancellationToken ct = default) => Task.Run(() =>
    {
        EnsureInitialized();
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT b.batch_id, b.shop_key, b.shop_label, b.created_at, b.state, b.products, b.files, b.output_dir, b.note, b.cleared_at,
                (SELECT count(*) FROM batch_image i WHERE i.batch_id = b.batch_id), b.expires_at, b.template_shop_id,
                b.size_chart_expires_at, b.hub_images, b.cdn_images, b.hub_sources_deleted_at
            FROM export_batch b ORDER BY b.created_at DESC, b.batch_id DESC LIMIT $lim;
            """;
        cmd.Parameters.AddWithValue("$lim", Math.Max(1, limit));
        var list = new List<ExportBatchInfo>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            string? S(int i) => rd.IsDBNull(i) ? null : rd.GetString(i);
            list.Add(new ExportBatchInfo(rd.GetString(0), rd.GetString(1), rd.GetString(2),
                DateTimeOffset.FromUnixTimeMilliseconds(rd.GetInt64(3)), rd.GetString(4), rd.GetInt32(5), rd.GetInt32(6), S(7), S(8),
                rd.IsDBNull(9) ? null : DateTimeOffset.FromUnixTimeMilliseconds(rd.GetInt64(9)), rd.GetInt32(10),
                rd.IsDBNull(11) ? null : DateTimeOffset.FromUnixTimeMilliseconds(rd.GetInt64(11)), S(12),
                rd.IsDBNull(13) ? null : DateTimeOffset.FromUnixTimeMilliseconds(rd.GetInt64(13)), rd.GetInt32(14), rd.GetInt32(15),
                rd.IsDBNull(16) ? null : DateTimeOffset.FromUnixTimeMilliseconds(rd.GetInt64(16))));
        }
        return (IReadOnlyList<ExportBatchInfo>)list;
    }, ct);

    /// <summary>(GĐ9) Số lượt có ảnh SP là link kho ảnh gốc Hub mà chưa bị đánh "kho gốc đã xoá" — lượt sẽ hỏng ảnh nếu xoá sạch kho
    /// gốc trước khi đẩy file.</summary>
    public Task<int> CountBatchesUsingHubSourcesAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        EnsureInitialized();
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM export_batch WHERE hub_images > 0 AND hub_sources_deleted_at IS NULL;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }, ct);

    /// <summary>(GĐ9) Kho ảnh gốc trên Hub vừa bị xoá sạch: đánh mọi lượt có link Hub (chưa đánh) với mốc <paramref name="at"/>. Trả số
    /// lượt.</summary>
    public Task<int> MarkHubSourcesDeletedAsync(DateTimeOffset at, CancellationToken ct = default) => Task.Run(() => WithWriteRetry(con =>
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            UPDATE export_batch SET hub_sources_deleted_at = $at WHERE hub_images > 0 AND hub_sources_deleted_at IS NULL;
            """;
        cmd.Parameters.AddWithValue("$at", at.ToUnixTimeMilliseconds());
        return cmd.ExecuteNonQuery();
    }, ct), ct);

    /// <summary>shop_id của file mẫu lượt XONG gần nhất của shop (để cảnh báo khi đổi sang mẫu của shop khác); null = chưa có.</summary>
    public Task<string?> GetLastTemplateShopIdAsync(string shopKey, CancellationToken ct = default) => Task.Run(() =>
    {
        EnsureInitialized();
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT template_shop_id FROM export_batch WHERE shop_key = $shop AND state = 'done' AND template_shop_id IS NOT NULL
                AND template_shop_id <> '' ORDER BY created_at DESC, batch_id DESC LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$shop", shopKey ?? "");
        return cmd.ExecuteScalar() as string;
    }, ct);

    /// <summary>Mốc hết hạn SỚM NHẤT trên Hub của các ảnh <c>verified</c> trong <paramref name="keys"/> (của shop); null = không có
    /// ảnh nào. Dùng làm hạn thật của lượt.</summary>
    public Task<DateTimeOffset?> GetEarliestExpiryAsync(string shopKey, IEnumerable<string> keys, CancellationToken ct = default)
    {
        var list = keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).ToList();
        return Task.Run(() =>
        {
            EnsureInitialized();
            using var con = Open();
            long? min = null;
            foreach (var key in list)
            {
                using var cmd = con.CreateCommand();
                cmd.CommandText = """
                    SELECT expires_at FROM image_prep WHERE shop_key = $shop AND source_key = $key AND state = 'verified'
                        AND expires_at IS NOT NULL;
                    """;
                cmd.Parameters.AddWithValue("$shop", shopKey);
                cmd.Parameters.AddWithValue("$key", key);
                if (cmd.ExecuteScalar() is long v && (min is null || v < min)) min = v;
            }
            return min is { } m ? DateTimeOffset.FromUnixTimeMilliseconds(m) : (DateTimeOffset?)null;
        }, ct);
    }

    /// <summary>Nút "Xoá ảnh tạm của lượt" (GĐ8) — user bấm SAU khi đã đẩy file lên Shopee (Shopee đã kéo ảnh về kho của họ).
    /// Với mỗi ảnh SP của lượt:
    /// <list type="bullet">
    /// <item>một lượt KHÁC đang chạy, hoặc đã xong mà chưa xoá ảnh, cũng có ảnh này → GIỮ (đếm tham chiếu theo lượt). Lượt
    /// <c>cancelled</c> / <c>failed</c> không xuất file nào nên KHÔNG giữ ảnh cho ai;</item>
    /// <item>không lượt nào khác dùng → bỏ mọi tham chiếu <c>image_ref</c> của ảnh; <c>pending</c>/<c>failed</c> → xoá dòng (chưa
    /// có gì trên Hub); <c>verified</c>/<c>hosted</c>/<c>deleting</c> → <c>deleting</c> rồi xoá trên Hub NGAY như
    /// <see cref="ReleaseItemAsync"/> (404 = đã xoá; hụt → giữ <c>deleting</c>, bấm lại hoặc worker dọn sau).</item>
    /// </list>
    /// Ảnh bảng size dùng chung KHÔNG BAO GIỜ bị xoá (luật GĐ2b — Hub tự xoá khi hết hạn). Xoá hết không hụt → lượt được đánh
    /// <c>cleared_at</c> (không còn tính là "đang dùng" ảnh nào). Bấm lại một lượt đã xoá là vô hại. Lượt không có → ném.</summary>
    /// <remarks>Ở GĐ8 MỌI tham chiếu ảnh đều đến từ lượt xuất (<see cref="EnqueueForBatchAsync"/>), nên "không lượt nào khác dùng"
    /// = không ai dùng. Nếu sau này luồng đăng (GĐ5) dùng chung sổ, phải xét lại bước bỏ <c>image_ref</c>.</remarks>
    public async Task<BatchClearResult> ClearBatchAsync(string batchId, HubClient hub, CancellationToken ct = default)
    {
        RequireKey(batchId, nameof(batchId));
        ArgumentNullException.ThrowIfNull(hub);
        var (doomed, removed, shared, sizeCharts) = await Task.Run(() => UnrefBatch(batchId, NowMs(), ct), ct).ConfigureAwait(false);
        var deleted = 0;
        foreach (var d in doomed)
            if (await FinishDeleteAsync(d, hub, ct).ConfigureAwait(false)) deleted++;
        var failed = doomed.Count - deleted;
        if (failed == 0)
            await Task.Run(() => WithWriteRetry(con =>
            {
                using var cmd = con.CreateCommand();
                cmd.CommandText = "UPDATE export_batch SET cleared_at = coalesce(cleared_at, $now) WHERE batch_id = $batch;";
                cmd.Parameters.AddWithValue("$batch", batchId);
                cmd.Parameters.AddWithValue("$now", NowMs());
                return cmd.ExecuteNonQuery();
            }, CancellationToken.None)).ConfigureAwait(false);
        _log?.Invoke($"[ảnh] lượt {batchId}: xoá {deleted + removed}, giữ {shared} (lượt khác dùng), bảng size {sizeCharts}, hụt {failed}");
        return new BatchClearResult(deleted + removed, shared, sizeCharts, failed);
    }

    /// <summary>Một giao dịch của <see cref="ClearBatchAsync"/>: chia ảnh của lượt thành giữ / xoá dòng / chờ xoá trên Hub.</summary>
    private (List<ImageDeleteEntry> Doomed, int Removed, int Shared, int SizeCharts) UnrefBatch(string batchId, long markMs,
        CancellationToken ct) => WithWriteRetry(con =>
    {
        using var tx = con.BeginTransaction();
        using (var chk = con.CreateCommand())
        {
            chk.Transaction = tx;
            chk.CommandText = "SELECT count(*) FROM export_batch WHERE batch_id = $batch;";
            chk.Parameters.AddWithValue("$batch", batchId);
            if (Convert.ToInt64(chk.ExecuteScalar()) == 0) throw new InvalidOperationException($"Không có lượt {batchId}");
        }
        var images = new List<(string Shop, string Key, string? Kind, string? State, string? HubPath)>();
        using (var sel = con.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = """
                SELECT i.shop_key, i.source_key, p.kind, p.state, p.hub_path FROM batch_image i
                LEFT JOIN image_prep p ON p.shop_key = i.shop_key AND p.source_key = i.source_key
                WHERE i.batch_id = $batch;
                """;
            sel.Parameters.AddWithValue("$batch", batchId);
            using var rd = sel.ExecuteReader();
            while (rd.Read())
                images.Add((rd.GetString(0), rd.GetString(1), rd.IsDBNull(2) ? null : rd.GetString(2),
                    rd.IsDBNull(3) ? null : rd.GetString(3), rd.IsDBNull(4) ? null : rd.GetString(4)));
        }
        var doomed = new List<ImageDeleteEntry>();
        int removed = 0, shared = 0, sizeCharts = 0;
        foreach (var (shop, key, kind, state, hubPath) in images)
        {
            if (kind is null) continue;   // không còn trong sổ (lượt khác đã xoá) = không còn gì để xoá, không đếm lại
            if (kind == "sizechart") { sizeCharts++; continue; }
            using (var other = con.CreateCommand())
            {
                other.Transaction = tx;
                other.CommandText = """
                    SELECT EXISTS (SELECT 1 FROM batch_image o JOIN export_batch e ON e.batch_id = o.batch_id
                        WHERE o.shop_key = $shop AND o.source_key = $key AND o.batch_id <> $batch AND e.cleared_at IS NULL
                            AND e.state IN ('running', 'done'));
                    """;
                other.Parameters.AddWithValue("$shop", shop);
                other.Parameters.AddWithValue("$key", key);
                other.Parameters.AddWithValue("$batch", batchId);
                if (Convert.ToInt64(other.ExecuteScalar()) != 0) { shared++; continue; }
            }
            using var cmd = con.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                DELETE FROM image_ref WHERE shop_key = $shop AND source_key = $key;
                DELETE FROM image_prep WHERE shop_key = $shop AND source_key = $key AND kind = 'product' AND state IN ('pending', 'failed');
                UPDATE image_prep SET state = 'deleting', updated_at = $mark
                WHERE shop_key = $shop AND source_key = $key AND kind = 'product' AND state IN ('verified', 'hosted');
                """;
            cmd.Parameters.AddWithValue("$shop", shop);
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$mark", markMs);
            cmd.ExecuteNonQuery();
            if (state is StatePending or StateFailed) removed++;
            else doomed.Add(new ImageDeleteEntry(shop, key, hubPath));
        }
        tx.Commit();
        return (doomed, removed, shared, sizeCharts);
    }, ct);

    /// <summary>Lượt phải có và chưa xoá ảnh, cùng shop — không thì ném (không ghi ảnh vào lượt sai).</summary>
    private static void RequireOpenBatch(SqliteConnection con, SqliteTransaction tx, string batchId, string shopKey)
    {
        using var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT shop_key, cleared_at FROM export_batch WHERE batch_id = $batch;";
        cmd.Parameters.AddWithValue("$batch", batchId);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) throw new InvalidOperationException($"Chưa tạo lượt {batchId}");
        if (!rd.IsDBNull(1)) throw new InvalidOperationException($"Lượt {batchId} đã xoá ảnh tạm — không thêm ảnh được");
        if (!string.Equals(rd.GetString(0), shopKey, StringComparison.Ordinal))
            throw new InvalidOperationException($"Lượt {batchId} thuộc shop khác");
    }

    /// <summary>v4 → v5 (GĐ9 vòng 2): mốc kho ảnh gốc Hub bị xoá sạch sau khi lượt xuất.</summary>
    private static void MigrateToV5(SqliteConnection con)
    {
        using var tx = con.BeginTransaction();
        using var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            ALTER TABLE export_batch ADD COLUMN hub_sources_deleted_at INTEGER;
            PRAGMA user_version = 5;
            """;
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    /// <summary>v3 → v4 (GĐ9): hạn riêng của ảnh bảng size + số ảnh lấy link Hub / tải CDN của lượt.</summary>
    private static void MigrateToV4(SqliteConnection con)
    {
        using var tx = con.BeginTransaction();
        using var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            ALTER TABLE export_batch ADD COLUMN size_chart_expires_at INTEGER;
            ALTER TABLE export_batch ADD COLUMN hub_images INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE export_batch ADD COLUMN cdn_images INTEGER NOT NULL DEFAULT 0;
            PRAGMA user_version = 4;
            """;
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    /// <summary>v2 → v3: hạn thật của lượt + shop_id của mẫu, nâng <c>user_version</c> trong cùng giao dịch.</summary>
    private static void MigrateToV3(SqliteConnection con)
    {
        using var tx = con.BeginTransaction();
        using var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            ALTER TABLE export_batch ADD COLUMN expires_at INTEGER;
            ALTER TABLE export_batch ADD COLUMN template_shop_id TEXT;
            PRAGMA user_version = 3;
            """;
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    /// <summary>v1 → v2 (GĐ8): bảng lượt xuất + ảnh theo lượt, nâng <c>user_version</c> trong cùng giao dịch.</summary>
    private static void MigrateToV2(SqliteConnection con)
    {
        using var tx = con.BeginTransaction();
        using var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS export_batch (
                batch_id   TEXT    NOT NULL PRIMARY KEY,
                shop_key   TEXT    NOT NULL,
                shop_label TEXT    NOT NULL DEFAULT '',
                created_at INTEGER NOT NULL,
                state      TEXT    NOT NULL DEFAULT 'running',
                products   INTEGER NOT NULL DEFAULT 0,
                files      INTEGER NOT NULL DEFAULT 0,
                output_dir TEXT,
                note       TEXT,
                cleared_at INTEGER
            );
            CREATE TABLE IF NOT EXISTS batch_image (
                batch_id   TEXT NOT NULL,
                shop_key   TEXT NOT NULL,
                source_key TEXT NOT NULL,
                PRIMARY KEY (batch_id, shop_key, source_key)
            );
            CREATE INDEX IF NOT EXISTS ix_batch_image_key ON batch_image (shop_key, source_key);
            PRAGMA user_version = 2;
            """;
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    // ── Xoá ảnh trên Hub (Release + worker dọn) ─────────────────────────────────────────────────────────────────────

    /// <summary>Một giao dịch: lấy các khoá SP đang dùng, xoá tham chiếu; trong các khoá đó, ảnh SP hết tham chiếu:
    /// pending/failed → xoá dòng, verified/hosted → <c>deleting</c> với <c>updated_at</c> = <paramref name="markMs"/>. Trả các
    /// dòng <c>deleting</c> thuộc khoá của SP.</summary>
    private List<ImageDeleteEntry> Unref(string shopKey, long itemId, long markMs, CancellationToken ct) => WithWriteRetry(con =>
    {
        using var tx = con.BeginTransaction();
        var keys = new List<string>();
        using (var sel = con.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = "SELECT source_key FROM image_ref WHERE shop_key = $shop AND item_id = $item;";
            sel.Parameters.AddWithValue("$shop", shopKey);
            sel.Parameters.AddWithValue("$item", itemId);
            using var rd = sel.ExecuteReader();
            while (rd.Read()) keys.Add(rd.GetString(0));
        }
        using (var del = con.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM image_ref WHERE shop_key = $shop AND item_id = $item;";
            del.Parameters.AddWithValue("$shop", shopKey);
            del.Parameters.AddWithValue("$item", itemId);
            del.ExecuteNonQuery();
        }
        var doomed = new List<ImageDeleteEntry>();
        foreach (var key in keys)
        {
            using var cmd = con.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                DELETE FROM image_prep WHERE shop_key = $shop AND source_key = $key AND kind = 'product'
                    AND state IN ('pending', 'failed')
                    AND NOT EXISTS (SELECT 1 FROM image_ref r WHERE r.shop_key = $shop AND r.source_key = $key);
                UPDATE image_prep SET state = 'deleting', updated_at = $mark
                WHERE shop_key = $shop AND source_key = $key AND kind = 'product' AND state IN ('verified', 'hosted')
                    AND NOT EXISTS (SELECT 1 FROM image_ref r WHERE r.shop_key = $shop AND r.source_key = $key);
                SELECT hub_path FROM image_prep WHERE shop_key = $shop AND source_key = $key AND state = 'deleting';
                """;
            cmd.Parameters.AddWithValue("$shop", shopKey);
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$mark", markMs);
            using var rd = cmd.ExecuteReader();
            do
            {
                while (rd.Read()) doomed.Add(new ImageDeleteEntry(shopKey, key, rd.IsDBNull(0) ? null : rd.GetString(0)));
            } while (rd.NextResult());
        }
        tx.Commit();
        return doomed;
    }, ct);

    /// <summary>Các dòng <c>deleting</c> đánh dấu từ trước <paramref name="olderThan"/> (worker dọn lại).</summary>
    internal List<ImageDeleteEntry> TakeStaleDeleting(TimeSpan olderThan, int max)
    {
        EnsureInitialized();
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT shop_key, source_key, hub_path FROM image_prep WHERE state = 'deleting' AND updated_at <= $before
            ORDER BY updated_at LIMIT $lim;
            """;
        cmd.Parameters.AddWithValue("$before", NowMs() - (long)olderThan.TotalMilliseconds);
        cmd.Parameters.AddWithValue("$lim", max);
        var list = new List<ImageDeleteEntry>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) list.Add(new ImageDeleteEntry(rd.GetString(0), rd.GetString(1), rd.IsDBNull(2) ? null : rd.GetString(2)));
        return list;
    }

    /// <summary>Xoá một ảnh <c>deleting</c> trên Hub rồi chốt dòng (xem <see cref="ReleaseItemAsync"/>). Trả true nếu đã xoá
    /// xong trên Hub (404 tính là xong). Huỷ <paramref name="ct"/> → ném, dòng giữ <c>deleting</c>.</summary>
    internal async Task<bool> FinishDeleteAsync(ImageDeleteEntry d, HubClient hub, CancellationToken ct)
    {
        // Không có path (dữ liệu hỏng) = không có gì để xoá trên Hub → coi như xong.
        var ok = string.IsNullOrEmpty(d.HubPath) || await hub.DeletePublicMediaAsync(d.HubPath, ct).ConfigureAwait(false);
        _log?.Invoke(ok
            ? $"[ảnh] shop {d.ShopKey}: đã xoá ảnh {d.SourceKey} trên Hub ({d.HubPath ?? "không có path"})"
            : $"[ảnh] shop {d.ShopKey}: xoá ảnh {d.SourceKey} trên Hub hụt — worker dọn lại sau");
        await Task.Run(() => WithWriteRetry(con =>
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = ok
                ? """
                  UPDATE image_prep SET state = 'pending', url = NULL, hub_path = NULL, expires_at = NULL, attempts = 0,
                      next_attempt_at = 0, last_error = NULL, updated_at = $now
                  WHERE shop_key = $shop AND source_key = $key AND state = 'deleting'
                      AND EXISTS (SELECT 1 FROM image_ref r WHERE r.shop_key = $shop AND r.source_key = $key);
                  DELETE FROM image_prep WHERE shop_key = $shop AND source_key = $key AND state = 'deleting';
                  """
                : """
                  UPDATE image_prep SET state = CASE WHEN EXISTS (SELECT 1 FROM image_ref r WHERE r.shop_key = $shop
                          AND r.source_key = $key) THEN 'hosted' ELSE 'deleting' END,
                      next_attempt_at = 0, updated_at = $now
                  WHERE shop_key = $shop AND source_key = $key AND state = 'deleting';
                  """;
            cmd.Parameters.AddWithValue("$shop", d.ShopKey);
            cmd.Parameters.AddWithValue("$key", d.SourceKey);
            cmd.Parameters.AddWithValue("$now", NowMs());
            return cmd.ExecuteNonQuery();
        }, CancellationToken.None)).ConfigureAwait(false);
        return ok;
    }

    // ── Cho ImagePrepWorker ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Tối đa <paramref name="max"/> ảnh <c>pending</c> / <c>hosted</c> đến hạn, bảng size trước, rồi theo mốc hẹn /
    /// lúc cập nhật (ảnh vừa gặp lỗi hệ thống được <see cref="Touch"/> nên xuống cuối — không ảnh nào chặn hàng).</summary>
    internal List<ImagePrepEntry> TakeDue(int max)
    {
        EnsureInitialized();
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT shop_key, source_key, kind, attempts, state, md5, hub_path, url, content_type, source_path FROM image_prep
            WHERE state IN ('pending', 'hosted') AND next_attempt_at <= $now
            ORDER BY CASE kind WHEN 'sizechart' THEN 0 ELSE 1 END, next_attempt_at, updated_at, shop_key, source_key
            LIMIT $lim;
            """;
        cmd.Parameters.AddWithValue("$now", NowMs());
        cmd.Parameters.AddWithValue("$lim", max);
        var list = new List<ImagePrepEntry>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            string? S(int i) => rd.IsDBNull(i) ? null : rd.GetString(i);
            list.Add(new ImagePrepEntry(rd.GetString(0), rd.GetString(1),
                rd.GetString(2) == "sizechart" ? ImagePrepKind.SizeChart : ImagePrepKind.Product, rd.GetInt32(3),
                rd.GetString(4), S(5), S(6), S(7), S(8), S(9)));
        }
        return list;
    }

    /// <summary><c>pending</c> → <c>hosted</c> NGAY sau PUT thành công (ghi bền trước khi kiểm). Trả entry ở trạng thái mới,
    /// hoặc null nếu dòng không còn <c>pending</c> (Release xoá / lượt khác đã host) — worker phải xoá ảnh vừa up.</summary>
    internal ImagePrepEntry? MarkHosted(ImagePrepEntry e, string md5, string contentType, string hubPath, string url,
        DateTimeOffset expiresAt)
    {
        var n = UpdateFrom(e, StatePending, """
            UPDATE image_prep SET state = 'hosted', md5 = $md5, content_type = $type, hub_path = $path, url = $url,
                expires_at = $exp, updated_at = $now
            WHERE shop_key = $shop AND source_key = $key AND state = $from;
            """, p =>
        {
            p.AddWithValue("$md5", md5);
            p.AddWithValue("$type", contentType);
            p.AddWithValue("$path", hubPath);
            p.AddWithValue("$url", url);
            p.AddWithValue("$exp", expiresAt.ToUnixTimeMilliseconds());
        });
        return n == 0 ? null : e with { State = StateHosted, Md5 = md5, ContentType = contentType, HubPath = hubPath, Url = url };
    }

    /// <summary><c>hosted</c> → <c>verified</c> (kiểm đạt). Trả false nếu dòng không còn <c>hosted</c> (Release / Forget đã
    /// chuyển sang <c>deleting</c> — phần xoá trên Hub do họ / worker dọn lo).</summary>
    internal bool MarkVerified(ImagePrepEntry e) => UpdateFrom(e, StateHosted, """
        UPDATE image_prep SET state = 'verified', last_error = NULL, next_attempt_at = 0, updated_at = $now
        WHERE shop_key = $shop AND source_key = $key AND state = $from;
        """, _ => { }) > 0;

    /// <summary>→ <c>failed</c> ngay (lỗi vĩnh viễn của CHÍNH ảnh), chỉ khi dòng còn ở trạng thái của entry. Quên chỗ host
    /// (caller đã xoá ảnh trên Hub nếu có).</summary>
    internal void MarkFailed(ImagePrepEntry e, string reason) => UpdateFrom(e, e.State, """
        UPDATE image_prep SET state = 'failed', attempts = attempts + 1, last_error = $err, hub_path = NULL, url = NULL,
            expires_at = NULL, updated_at = $now
        WHERE shop_key = $shop AND source_key = $key AND state = $from;
        """, p => p.AddWithValue("$err", reason));

    /// <summary>Kiểm lệch (lỗi gắn với ảnh): về <c>pending</c>, quên chỗ host (caller đã xoá trên Hub), tăng <c>attempts</c>,
    /// hẹn lại sau <paramref name="delay"/>; đủ <paramref name="maxAttempts"/> → <c>failed</c>. Trả true nếu đã <c>failed</c>.</summary>
    internal bool Defer(ImagePrepEntry e, TimeSpan delay, string error, int maxAttempts)
    {
        var failed = e.Attempts + 1 >= maxAttempts;
        UpdateFrom(e, e.State, $"""
            UPDATE image_prep SET attempts = attempts + 1, last_error = $err, next_attempt_at = $next,
                state = {(failed ? "'failed'" : "'pending'")}, hub_path = NULL, url = NULL, expires_at = NULL, updated_at = $now
            WHERE shop_key = $shop AND source_key = $key AND state = $from;
            """, p =>
        {
            p.AddWithValue("$err", failed ? $"thử {maxAttempts} lần vẫn lỗi: {error}" : error);
            p.AddWithValue("$next", NowMs() + (long)delay.TotalMilliseconds);
        });
        return failed;
    }

    /// <summary><c>hosted</c> mà ảnh trên Hub đã mất (404 — hết hạn / bị dọn): về <c>pending</c> để up lại, KHÔNG tăng số lần
    /// thử (không phải lỗi của ảnh).</summary>
    internal void ReturnToPending(ImagePrepEntry e, string reason) => UpdateFrom(e, e.State, """
        UPDATE image_prep SET state = 'pending', hub_path = NULL, url = NULL, expires_at = NULL, last_error = $err,
            next_attempt_at = 0, updated_at = $now
        WHERE shop_key = $shop AND source_key = $key AND state = $from;
        """, p => p.AddWithValue("$err", reason));

    /// <summary>Lỗi hệ thống (Hub / mạng): GIỮ trạng thái + số lần thử, chỉ đẩy <c>updated_at</c> để ảnh xuống cuối hàng.</summary>
    internal void Touch(ImagePrepEntry e) => UpdateFrom(e, e.State,
        "UPDATE image_prep SET updated_at = $now WHERE shop_key = $shop AND source_key = $key AND state = $from;", _ => { });

    /// <summary>Đọc một dòng (cho test / chẩn đoán).</summary>
    internal (string State, string Kind, int Attempts, string? Md5, string? HubPath, string? Url, long? ExpiresAtMs,
        long NextAttemptAtMs, string? LastError)? Inspect(string shopKey, string sourceKey)
    {
        EnsureInitialized();
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT state, kind, attempts, md5, hub_path, url, expires_at, next_attempt_at, last_error
            FROM image_prep WHERE shop_key = $shop AND source_key = $key;
            """;
        cmd.Parameters.AddWithValue("$shop", shopKey);
        cmd.Parameters.AddWithValue("$key", sourceKey);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;
        string? S(int i) => rd.IsDBNull(i) ? null : rd.GetString(i);
        return (rd.GetString(0), rd.GetString(1), rd.GetInt32(2), S(3), S(4), S(5), rd.IsDBNull(6) ? null : rd.GetInt64(6),
            rd.GetInt64(7), S(8));
    }

    /// <summary>Số tham chiếu của một ảnh (cho test).</summary>
    internal int RefCount(string shopKey, string sourceKey)
    {
        EnsureInitialized();
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM image_ref WHERE shop_key = $shop AND source_key = $key;";
        cmd.Parameters.AddWithValue("$shop", shopKey);
        cmd.Parameters.AddWithValue("$key", sourceKey);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>Cập nhật một dòng; câu SQL tự lọc <c>state = $from</c> để không đè trạng thái Release / resolver vừa đổi.</summary>
    private int UpdateFrom(ImagePrepEntry e, string from, string sql, Action<SqliteParameterCollection> bind) =>
        WithWriteRetry(con =>
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$from", from);
            cmd.Parameters.AddWithValue("$shop", e.ShopKey);
            cmd.Parameters.AddWithValue("$key", e.SourceKey);
            cmd.Parameters.AddWithValue("$now", NowMs());
            bind(cmd.Parameters);
            return cmd.ExecuteNonQuery();
        }, CancellationToken.None);

    // ── Hạ tầng SQLite ─────────────────────────────────────────────────────────────────────────────────────────────

    private static void RequireKey(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("khoá rỗng", name);
    }

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
                        $"upload.db {_dbPath} có schema v{version}, bản app này chỉ hiểu tới v{SchemaVersion} — cập nhật app rồi chạy lại");
                using (var wal = con.CreateCommand())
                {
                    wal.CommandText = "PRAGMA journal_mode=WAL;";
                    wal.ExecuteNonQuery();
                }
                // Mỗi bước migration nâng user_version trong CÙNG giao dịch với thay đổi schema.
                using (var cmd = con.CreateCommand())
                {
                    cmd.CommandText = $"""
                    CREATE TABLE IF NOT EXISTS image_prep (
                        shop_key        TEXT    NOT NULL,
                        source_key      TEXT    NOT NULL,
                        kind            TEXT    NOT NULL DEFAULT 'product',
                        state           TEXT    NOT NULL DEFAULT 'pending',
                        md5             TEXT,
                        content_type    TEXT,
                        source_path     TEXT,
                        hub_path        TEXT,
                        url             TEXT,
                        expires_at      INTEGER,
                        attempts        INTEGER NOT NULL DEFAULT 0,
                        next_attempt_at INTEGER NOT NULL DEFAULT 0,
                        last_error      TEXT,
                        updated_at      INTEGER NOT NULL,
                        PRIMARY KEY (shop_key, source_key)
                    );
                    CREATE INDEX IF NOT EXISTS ix_image_prep_due ON image_prep (state, next_attempt_at);
                    CREATE TABLE IF NOT EXISTS image_ref (
                        shop_key   TEXT    NOT NULL,
                        source_key TEXT    NOT NULL,
                        item_id    INTEGER NOT NULL,
                        PRIMARY KEY (shop_key, source_key, item_id)
                    );
                    CREATE INDEX IF NOT EXISTS ix_image_ref_item ON image_ref (shop_key, item_id);
                    {(version < 1 ? "PRAGMA user_version = 1;" : "")}
                    """;
                    cmd.ExecuteNonQuery();
                }
                if (version < 2) MigrateToV2(con);
                if (version < 3) MigrateToV3(con);
                if (version < 4) MigrateToV4(con);
                if (version < 5) MigrateToV5(con);
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
        pragma.CommandText = "PRAGMA busy_timeout=1000; PRAGMA synchronous=FULL;";
        pragma.ExecuteNonQuery();
        return con;
    }

    /// <summary>Chạy một thao tác GHI: qua cổng tuần tự trong process, thử lại SQLITE_BUSY/LOCKED tới hết
    /// <see cref="BusyBudget"/>; hết ngân sách → ném. Lỗi khác ném ngay.</summary>
    private T WithWriteRetry<T>(Func<SqliteConnection, T> work, CancellationToken ct, bool gate = true)
    {
        if (gate) EnsureInitialized();
        var sw = Stopwatch.StartNew();
        if (gate && !_writeGate.Wait(BusyBudget, ct))
            throw new TimeoutException($"upload.db bận quá {BusyBudget.TotalSeconds:0} s (chờ lượt ghi trong app)");
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

    private long NowMs() => _clock().ToUnixTimeMilliseconds();
}
