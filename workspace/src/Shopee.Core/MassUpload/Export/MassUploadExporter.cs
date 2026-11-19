using System.Globalization;
using Shopee.Core.Coordination;
using Shopee.Core.MassUpload.Images;
using Shopee.Core.MassUpload.Seller;

namespace Shopee.Core.MassUpload.Export;

/// <summary>Một lượt xuất của một shop (plan GĐ8).</summary>
/// <param name="BatchId">Mã lượt — ảnh của lượt ghi theo mã này trong <c>upload.db</c> để xoá sau.</param>
/// <param name="ShopKey">Mã tài khoản seller (khoá shop của bước đệm ảnh: mỗi shop một MD5).</param>
/// <param name="ShopLabel">Tên shop (vào tên file xuất).</param>
/// <param name="FromRow">Số dòng Excel; ≤ dòng dữ liệu đầu = từ đầu.</param>
/// <param name="ToRow">Số dòng Excel; 0 = tới hết sheet.</param>
/// <param name="AllowTemplateShopChange">true = user đã xác nhận dùng mẫu có shop_id khác lần trước
/// (<see cref="TemplateShopChangedException"/>).</param>
public sealed record MassUploadExportRequest(
    string BatchId,
    string ShopKey,
    string ShopLabel,
    string WorkbookPath,
    string Sheet,
    int FromRow,
    int ToRow,
    IReadOnlyList<string> TemplatePaths,
    string SizeChartImagePath,
    SellerDefaults Defaults,
    string OutputDirectory,
    bool AllowTemplateShopChange = false,
    MissingHubImagePolicy MissingHubImages = MissingHubImagePolicy.DownloadFromCdn);

/// <summary>(GĐ9) Ảnh SP chưa có trong kho ảnh gốc Hub: tải từ CDN Shopee như cũ (mặc định), hay bỏ hẳn SP đó.</summary>
public enum MissingHubImagePolicy { DownloadFromCdn, SkipProduct }

/// <summary>(GĐ9) Kết quả hỏi link ảnh gốc trên Hub: id → URL công khai (đã đổi MD5 theo shop, không hạn) + id Hub chưa có.</summary>
public sealed record HubImageLinks(IReadOnlyDictionary<string, string> Urls, IReadOnlyCollection<string> Missing);

/// <summary>Bước của lượt xuất (thanh tiến trình).</summary>
public enum ExportStep { Prepare, ReadSource, FetchHub, Images, Write, Done }

/// <summary>Tiến độ: bước, đã xong / tổng của bước (0/0 = không đếm được), một dòng mô tả.</summary>
public sealed record ExportProgress(ExportStep Step, int Done, int Total, string Message);

/// <summary>Một ghi chú gắn với một SP của nguồn (dòng Excel + itemId + link).</summary>
public sealed record ExportItemNote(int ExcelRow, long ItemId, string Link, string Text);

/// <summary>SP thiếu mẫu của một ngành cấp 1.</summary>
public sealed record ExportMissingTemplate(long TopCategoryId, IReadOnlyList<ExportSourceRow> Items);

/// <summary>Kết quả lượt xuất.</summary>
/// <param name="Files">Đường dẫn các file đã xuất (đã chuyển vào thư mục ra).</param>
/// <param name="ExportedItems">Số SP đã ghi vào file.</param>
/// <param name="NotHarvested">Dòng có link SP mà Hub chưa có dữ liệu ("chưa cào") — không xuất.</param>
/// <param name="NoItemId">Dòng có link không tách được itemId — không xuất.</param>
/// <param name="MissingTemplates">SP không có mẫu đúng ngành — không xuất.</param>
/// <param name="ImageSkipped">SP bỏ vì ảnh (không còn ảnh dùng được, ảnh bảng size lỗi…).</param>
/// <param name="Skipped">SP bỏ vì lý do khác (tên / mô tả quá ngắn, không còn model, dữ liệu Hub hỏng…).</param>
/// <param name="Warnings">Cảnh báo cắt (tên, mô tả, giá, phân loại, ảnh…) của SP đã xuất.</param>
/// <param name="NeedsFix">SP đã xuất nhưng thiếu thuộc tính bắt buộc (Shopee để Bản nháp).</param>
/// <param name="FailedImages">Số ảnh lỗi vĩnh viễn trong lượt (ảnh gốc hỏng / không còn…).</param>
/// <param name="Notices">Ghi chú chung (vd SP vào bản basic).</param>
/// <param name="ImagesExpireAt">Hạn THẬT của lượt: ảnh SP đi đường cũ (kho tạm) sớm hết hạn nhất mà file trỏ tới, tính cả ảnh bảng
/// size — phải đẩy file lên Shopee trước mốc này. null = mọi ảnh SP là link kho ảnh gốc Hub (không hạn); khi đó chỉ còn hạn của
/// ảnh bảng size (<c>SizeChartExpiresAt</c>).</param>
/// <param name="ImagesDroppedPending">Số ảnh chưa xong khi hết thời gian chờ, đã bỏ.</param>
/// <param name="ItemNotes">Ghi chú theo SP đã xuất, không phải cảnh báo (vd "phân loại chuẩn: x/y giá trị đổi sang mã").</param>
/// <param name="HubImages">(GĐ9) Số ảnh SP / phân loại lấy link kho ảnh gốc Hub (không hạn).</param>
/// <param name="CdnImages">(GĐ9) Số ảnh SP / phân loại phải tải CDN (đường cũ: đổi MD5, kho tạm có hạn).</param>
/// <param name="CdnItems">(GĐ9) SP có ảnh phải tải CDN, kèm số ảnh.</param>
/// <param name="SizeChartExpiresAt">Hạn của ảnh bảng size trên kho tạm (luôn đi đường cũ).</param>
public sealed record MassUploadExportResult(
    string BatchId,
    IReadOnlyList<string> Files,
    int ExportedItems,
    int SourceItems,
    int DuplicateRows,
    IReadOnlyList<ExportSourceLink> NotHarvested,
    IReadOnlyList<ExportSourceLink> NoItemId,
    IReadOnlyList<ExportMissingTemplate> MissingTemplates,
    IReadOnlyList<ExportItemNote> ImageSkipped,
    IReadOnlyList<ExportItemNote> Skipped,
    IReadOnlyList<ExportItemNote> Warnings,
    IReadOnlyList<ExportItemNote> NeedsFix,
    int FailedImages,
    IReadOnlyList<string> Notices,
    bool BatchCreated,
    DateTimeOffset? ImagesExpireAt = null,
    int ImagesDroppedPending = 0,
    IReadOnlyList<ExportItemNote>? ItemNotes = null,
    int HubImages = 0,
    int CdnImages = 0,
    IReadOnlyList<ExportItemNote>? CdnItems = null,
    DateTimeOffset? SizeChartExpiresAt = null);

/// <summary>Lượt xuất dừng vì cấu hình / dữ liệu vào sai (chưa ghi file nào). Câu lỗi để hiện thẳng cho user.</summary>
public sealed class MassUploadExportException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>File mẫu của lượt có shop_id (ô D2) khác shop_id mẫu của lượt xong gần nhất của shop này — có thể chọn nhầm mẫu của shop
/// khác. Chưa làm gì; user xác nhận thì chạy lại với <see cref="MassUploadExportRequest.AllowTemplateShopChange"/> = true.</summary>
public sealed class TemplateShopChangedException(string templateShopId, string previousShopId)
    : Exception($"Mẫu này của shop_id {templateShopId}, khác lần trước {previousShopId}.")
{
    public string TemplateShopId { get; } = templateShopId;
    public string PreviousShopId { get; } = previousShopId;
}

/// <summary>Tham số của <see cref="MassUploadExporter"/> (test thu nhỏ).</summary>
public sealed class MassUploadExportOptions
{
    /// <summary>Nhịp hỏi sổ ảnh trong lúc chờ bước đệm.</summary>
    public TimeSpan ImagePollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Số ảnh SP đầu đưa vào bước đệm mỗi SP (hợp đồng GĐ5: 9 ảnh dùng + 3 dự phòng thay ảnh lỗi).</summary>
    public int ProductImagesPerItem { get; init; } = 12;

    /// <summary>Ảnh dùng lại từ lượt trước mà còn hạn trên Hub ít hơn chừng này thì đưa lên lại (Hub giữ 7 ngày → 6 ngày: file vừa
    /// xuất luôn còn gần đủ một tuần). Chỉ áp ở lần hỏi sổ ĐẦU của lượt — ảnh vừa đưa lên trong lượt được nhận bất kể hạn (Hub cấu
    /// hình hạn ngắn hơn cũng không làm lượt lặp mãi).</summary>
    public TimeSpan ImageMinRemaining { get; init; } = TimeSpan.FromDays(6);

    /// <summary>Không thêm ảnh nào xong (sẵn sàng / lỗi) trong chừng này thì thôi chờ: xuất với ảnh đã sẵn sàng, ảnh chưa xong bị bỏ.</summary>
    public TimeSpan ImageWaitIdleLimit { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>(GĐ9) Số id mỗi lần hỏi link ảnh gốc Hub (Hub nhận tối đa 200).</summary>
    public int HubLinksBatchSize { get; init; } = SourceImageLimits.MaxIdsPerCall;

    /// <summary>(GĐ9) Giãn cách giữa các lần thử lại một lô link Hub lỗi (số phần tử + 1 = số lần gọi).</summary>
    public IReadOnlyList<TimeSpan> HubLinksRetryDelays { get; init; } = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)];
}

/// <summary>
/// Lượt "Xuất xlsx" của một shop (plan GĐ8): nguồn Excel → Hub (<c>by-items</c>) → chia theo mẫu → bước đệm ảnh (đổi MD5, đưa lên kho
/// ảnh Hub, theo <c>batchId</c>) → map → ghi file. App không truy cập Shopee.
/// <list type="number">
/// <item>Kiểm cấu hình TRƯỚC mọi việc: file mẫu (<see cref="ExportTemplates.Load"/>), ảnh bảng size, giá trị mặc định, thư mục ra.
/// Sai → <see cref="MassUploadExportException"/>, chưa tạo lượt, chưa ghi gì.</item>
/// <item>Đọc nguồn (<see cref="ExportSourceReader"/>), lấy dữ liệu Hub. SP Hub chưa có = "chưa cào", không xuất.</item>
/// <item>Chia SP theo mẫu (<see cref="ExportTemplates.Route"/>); ngành không có mẫu → "thiếu mẫu ngành X", không xuất, không đưa ảnh
/// vào bước đệm.</item>
/// <item>Tạo lượt (<see cref="UploadStore.CreateBatchAsync"/>), đưa ảnh vào hàng chờ THEO LƯỢT (<see cref="UploadStore.EnqueueForBatchAsync"/>):
/// ảnh lựa chọn nhóm 1 + tối đa <see cref="MassUploadExportOptions.ProductImagesPerItem"/> ảnh SP đầu, + ảnh bảng size. Chờ
/// <see cref="ImagePrepWorker"/> làm xong (mọi ảnh cần đều sẵn sàng hoặc lỗi); worker dừng vì cấu hình hỏng → lượt dừng.</item>
/// <item>Map (<see cref="MassUploadMapper"/>), ghi (<see cref="MassUploadTemplateWriter.WriteFiles"/>) vào thư mục TẠM trong thư mục
/// ra; đủ mọi file mới chuyển sang tên thật <c>&lt;shop&gt;_&lt;ngành&gt;_&lt;ngày giờ&gt;_&lt;n&gt;.xlsx</c> (không ghi đè).</item>
/// </list>
/// Huỷ / lỗi ở bất kỳ bước nào: xoá thư mục tạm và mọi file đã chuyển của lượt — KHÔNG để lại file dở (xoá không được thì log đường
/// dẫn, ghi vào ghi chú lượt); lượt (nếu đã tạo) chốt <see cref="ExportBatchState.Cancelled"/> / <see cref="ExportBatchState.Failed"/>,
/// ảnh đã đưa lên Hub vẫn ghi theo lượt để xoá được. Hết giờ chờ HTTP (không phải user huỷ) → <see cref="MassUploadExportException"/>,
/// không bao giờ báo "đã huỷ".
/// </summary>
public sealed class MassUploadExporter
{
    private readonly UploadStore _store;
    private readonly ImagePrepWorker _worker;
    private readonly Func<IReadOnlyList<long>, CancellationToken, Task<IReadOnlyList<HarvestedItemData>>> _fetch;
    private readonly Func<string, string, int, int, CancellationToken, Task<ExportSource>> _readSource;
    private readonly Func<DateTimeOffset> _clock;
    private readonly MassUploadExportOptions _opt;
    private readonly Action<string>? _log;
    private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<HubImageLinks>>? _hubLinks;

    /// <param name="hubLinks">(GĐ9) Hỏi link ảnh gốc trên Hub cho (shopKey, các id ảnh) — app thật:
    /// <c>HubClient.GetSourceImageLinksAsync</c>. null = không dùng kho ảnh gốc (mọi ảnh đi đường cũ). Lỗi (mạng, 5xx, Hub trả
    /// sai) → cả lượt đi đường cũ kèm cảnh báo, không bỏ ảnh âm thầm.</param>
    /// <param name="fetch">Lấy dữ liệu Hub theo itemId (app thật: <see cref="HubClient.GetHarvestedByItemsAsync"/>).</param>
    /// <param name="readSource">Đọc nguồn; null = <see cref="ExportSourceReader.ReadAsync"/>.</param>
    public MassUploadExporter(UploadStore store, ImagePrepWorker worker,
        Func<IReadOnlyList<long>, CancellationToken, Task<IReadOnlyList<HarvestedItemData>>> fetch,
        Func<string, string, int, int, CancellationToken, Task<ExportSource>>? readSource = null,
        Func<DateTimeOffset>? clock = null, MassUploadExportOptions? options = null, Action<string>? log = null,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<HubImageLinks>>? hubLinks = null)
    {
        _hubLinks = hubLinks;
        _log = log;
        _store = store;
        _worker = worker;
        _fetch = fetch;
        _readSource = readSource ?? ExportSourceReader.ReadAsync;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _opt = options ?? new MassUploadExportOptions();
    }

    /// <summary>Thư mục tạm của lượt trong thư mục ra (ẩn khỏi tên file xuất: bắt đầu bằng dấu chấm).</summary>
    public static string StagingDirectory(string outputDirectory, string batchId) =>
        Path.Combine(outputDirectory, ".dang-xuat-" + batchId);

    /// <summary>Câu báo khi hết thời gian chờ HTTP (Hub / mạng) — khác với user bấm Huỷ.</summary>
    public const string TimeoutMessage = "Hub không trả lời (quá thời gian chờ) — kiểm mạng / Hub rồi xuất lại.";

    /// <summary>Câu báo khi Hub hết chỗ lưu ảnh (507 / media-quota-exceeded / disk-low).</summary>
    public const string HubFullMessage = "Hub hết chỗ — xoá ảnh tạm các lượt cũ rồi xuất lại.";

    public async Task<MassUploadExportResult> RunAsync(MassUploadExportRequest req, IProgress<ExportProgress>? progress,
        CancellationToken ct)
    {
        try { return await RunCoreAsync(req, progress, ct).ConfigureAwait(false); }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // HttpClient hết giờ ném TaskCanceledException — KHÔNG phải user huỷ.
            throw new MassUploadExportException(TimeoutMessage, ex);
        }
    }

    private async Task<MassUploadExportResult> RunCoreAsync(MassUploadExportRequest req, IProgress<ExportProgress>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        void Report(ExportStep step, int done, int total, string message) =>
            progress?.Report(new ExportProgress(step, done, total, message));

        // 1. Cấu hình
        Report(ExportStep.Prepare, 0, 0, "Kiểm file mẫu, ảnh bảng size, giá trị mặc định…");
        if (string.IsNullOrWhiteSpace(req.OutputDirectory)) throw new MassUploadExportException("Chưa chọn thư mục xuất file.");
        if (req.TemplatePaths.Count == 0) throw new MassUploadExportException("Chưa chọn file mẫu nào.");
        var (templates, templateErrors) = ExportTemplates.Load(req.TemplatePaths);
        if (templateErrors.Count > 0) throw new MassUploadExportException(string.Join(" ", templateErrors));
        if (string.IsNullOrWhiteSpace(req.SizeChartImagePath)) throw new MassUploadExportException("Chưa có ảnh bảng size.");
        SizeChartImage sizeChart;
        try { sizeChart = SizeChartImage.FromFile(req.SizeChartImagePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw new MassUploadExportException(ex.Message, ex); }
        MassUploadDefaults defaults;
        try { defaults = req.Defaults.ToMassUploadDefaults(sizeChart.Key); }
        catch (InvalidOperationException ex) { throw new MassUploadExportException(ex.Message, ex); }
        var templateShopId = templates.Select(t => t.Schema.ShopId).FirstOrDefault(id => id.Length > 0);
        if (templateShopId is not null && !req.AllowTemplateShopChange
            && await _store.GetLastTemplateShopIdAsync(req.ShopKey, ct).ConfigureAwait(false) is { } previous
            && !string.Equals(previous, templateShopId, StringComparison.Ordinal))
            throw new TemplateShopChangedException(templateShopId, previous);

        // 2. Nguồn
        Report(ExportStep.ReadSource, 0, 0, $"Đọc {Path.GetFileName(req.WorkbookPath)} › {req.Sheet}…");
        ExportSource source;
        try { source = await _readSource(req.WorkbookPath, req.Sheet, req.FromRow, req.ToRow, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new MassUploadExportException($"Không đọc được nguồn: {ex.Message}", ex);
        }
        Report(ExportStep.ReadSource, source.Items.Count, source.Items.Count,
            $"Nguồn: {source.Items.Count} SP, {source.NoItemId.Count} link không có mã SP, {source.DuplicateRows} dòng trùng.");

        // 3. Hub
        ct.ThrowIfCancellationRequested();
        Report(ExportStep.FetchHub, 0, source.Items.Count, "Lấy dữ liệu đã cào từ Hub…");
        IReadOnlyList<HarvestedItemData> harvested;
        try { harvested = await _fetch(source.Items.Select(i => i.ItemId).ToList(), ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException)
        {
            throw new MassUploadExportException($"Không lấy được dữ liệu từ Hub: {ex.Message}", ex);
        }
        var byId = new Dictionary<long, HarvestedItemData>();
        foreach (var h in harvested) byId.TryAdd(h.ItemId, h);
        var notHarvested = source.Items.Where(i => !byId.ContainsKey(i.ItemId))
            .Select(i => new ExportSourceLink(i.ExcelRow, i.Link)).ToList();
        Report(ExportStep.FetchHub, byId.Count, source.Items.Count, $"Hub có {byId.Count}/{source.Items.Count} SP, chưa cào {notHarvested.Count}.");

        // 4. Đọc listing + chia theo mẫu
        var skipped = new List<ExportItemNote>();
        var missing = new Dictionary<long, List<ExportSourceRow>>();
        var routed = new List<(ExportSourceRow Row, ShopeeListing Listing, ExportTemplate Template)>();
        foreach (var row in source.Items)
        {
            if (!byId.TryGetValue(row.ItemId, out var data)) continue;
            ShopeeListing listing;
            try { listing = ShopeeListingReader.Read(data.Raw); }
            catch (FormatException ex)
            {
                skipped.Add(new ExportItemNote(row.ExcelRow, row.ItemId, row.Link, $"dữ liệu Hub không đọc được: {ex.Message}"));
                continue;
            }
            if (ExportTemplates.Route(templates, listing.TopCategoryId) is { } template) routed.Add((row, listing, template));
            else
            {
                if (!missing.TryGetValue(listing.TopCategoryId, out var list)) missing[listing.TopCategoryId] = list = [];
                list.Add(row);
            }
        }
        var missingTemplates = missing.OrderBy(m => m.Key).Select(m => new ExportMissingTemplate(m.Key, m.Value)).ToList();
        var notices = new List<string>();
        var basicCount = routed.Count(r => r.Template.Schema.Kind == TemplateKind.Basic);
        if (basicCount > 0)
            notices.Add($"{basicCount} SP vào bản mẫu basic — basic không có cột thuộc tính → SP vào Bản nháp, phải điền thuộc tính trên Shopee.");

        if (routed.Count == 0)
        {
            Report(ExportStep.Done, 0, 0, "Không có SP nào để xuất.");
            return new MassUploadExportResult(req.BatchId, [], 0, source.Items.Count, source.DuplicateRows, notHarvested, source.NoItemId,
                missingTemplates, [], skipped, [], [], 0, notices, false);
        }

        // 5. Lượt + ảnh
        ct.ThrowIfCancellationRequested();
        await _store.CreateBatchAsync(req.BatchId, req.ShopKey, req.ShopLabel, req.OutputDirectory, CancellationToken.None, templateShopId)
            .ConfigureAwait(false);
        var staging = StagingDirectory(req.OutputDirectory, req.BatchId);
        var moved = new List<string>();
        var exported = 0;
        try
        {
            var needed = new HashSet<string>(StringComparer.Ordinal) { sizeChart.Key };
            var itemKeys = new Dictionary<long, List<string>>();
            foreach (var (_, listing, _) in routed) itemKeys[listing.ItemId] = ImageKeys(listing);

            // GĐ9: ảnh SP / phân loại có trong kho ảnh gốc Hub → ghi thẳng link Hub (không hạn, không vào hàng chờ, không theo
            // lượt). Hub báo THIẾU: chế độ CDN → đường cũ (tải CDN, đổi MD5, kho tạm theo lượt); chế độ Bỏ SP → coi ảnh đó LỖI, để
            // mapper quyết (ảnh sau lên thay / bỏ cột ảnh phân loại; chỉ bỏ SP khi mapper Skip). Hỏi theo từng lô, mỗi lô thử lại.
            var hubUrls = new Dictionary<string, string>(StringComparer.Ordinal);
            var hubMissing = new HashSet<string>(StringComparer.Ordinal);    // Hub trả lời: chưa có ảnh gốc
            var hubFailed = new HashSet<string>(StringComparer.Ordinal);     // lô Hub lỗi sau mọi lần thử
            var allKeys = itemKeys.Values.SelectMany(k => k).Distinct(StringComparer.Ordinal).ToList();
            if (_hubLinks is not null && allKeys.Count > 0)
            {
                var askable = allKeys.Where(SourceImageLimits.IsValidId).ToList();
                hubMissing.UnionWith(allKeys.Where(k => !SourceImageLimits.IsValidId(k)));   // không đúng luật mã ảnh: không hỏi
                Report(ExportStep.Images, 0, allKeys.Count, $"Hỏi Hub link {askable.Count} ảnh gốc…");
                string? lastError = null;
                foreach (var lot in askable.Chunk(Math.Max(1, _opt.HubLinksBatchSize)))
                {
                    var links = await HubLinksWithRetryAsync(req.ShopKey, lot, ct).ConfigureAwait(false);
                    if (links.Result is not { } got)
                    {
                        hubFailed.UnionWith(lot);
                        lastError = links.Error;
                        continue;
                    }
                    foreach (var id in lot)
                        if (got.Urls.TryGetValue(id, out var url) && !string.IsNullOrWhiteSpace(url)) hubUrls[id] = url;
                        else hubMissing.Add(id);
                }
                if (hubFailed.Count > 0)
                {
                    _log?.Invoke($"[xuất] lượt {req.BatchId}: Hub lỗi khi lấy link {hubFailed.Count} ảnh ({lastError})");
                    if (req.MissingHubImages == MissingHubImagePolicy.SkipProduct)
                        throw new MassUploadExportException(
                            $"Hub lỗi khi lấy link ảnh ({lastError}) — không tải CDN vì đang chọn Bỏ SP. Xuất lại sau.");
                    notices.Add($"Hub lỗi khi lấy link {hubFailed.Count} ảnh gốc ({lastError}) — các ảnh này tải từ CDN như cũ.");
                }
            }
            var skipMissing = _hubLinks is not null && req.MissingHubImages == MissingHubImagePolicy.SkipProduct;
            var cdnKeys = new HashSet<string>(StringComparer.Ordinal);
            await _store.EnqueueSizeChartForBatchAsync(req.BatchId, req.ShopKey, sizeChart, ct).ConfigureAwait(false);
            foreach (var (_, listing, _) in routed)
            {
                var fromCdn = itemKeys[listing.ItemId]
                    .Where(k => !hubUrls.ContainsKey(k) && !(skipMissing && hubMissing.Contains(k))).ToList();
                if (fromCdn.Count == 0) continue;
                cdnKeys.UnionWith(fromCdn);
                needed.UnionWith(fromCdn);
                await _store.EnqueueForBatchAsync(req.BatchId, req.ShopKey, listing.ItemId, fromCdn, ct).ConfigureAwait(false);
            }
            _worker.Start();
            _worker.Nudge();
            var (waited, timedOut) = await WaitImagesAsync(req.ShopKey, needed, Report, ct).ConfigureAwait(false);
            var failedImages = needed.Count(k => waited(k).State == ImageReadiness.Failed);
            // Hết giờ chờ: ảnh còn chờ coi như lỗi → mapper bỏ ảnh đó (luật bỏ SP thiếu ảnh của mapper giữ nguyên).
            var dropped = timedOut
                ? needed.Where(k => waited(k).State == ImageReadiness.Pending).ToHashSet(StringComparer.Ordinal)
                : [];
            var minutes = _opt.ImageWaitIdleLimit.TotalMinutes.ToString("0.#", CultureInfo.InvariantCulture);
            Func<string, ImageResolution> resolve = k =>
                hubUrls.TryGetValue(k, out var hubUrl) ? ImageResolution.Ready(hubUrl)
                : skipMissing && hubMissing.Contains(k) ? ImageResolution.Failed("chưa có trên Hub (đang chọn Bỏ SP — không tải CDN)")
                : dropped.Contains(k) ? ImageResolution.Failed($"chưa xong sau {minutes} phút chờ — bỏ")
                : waited(k);

            // 6. Map
            var imageSkipped = new List<ExportItemNote>();
            var warnings = new List<ExportItemNote>();
            var needsFix = new List<ExportItemNote>();
            var itemNotes = new List<ExportItemNote>();
            var cdnItems = new List<ExportItemNote>();
            var groups = new Dictionary<ExportTemplate, List<MassUploadProduct>>();
            var notices2 = new List<string>();
            if (dropped.Count > 0)
            {
                notices2.Add($"{dropped.Count} ảnh chưa xong sau {minutes} phút chờ — đã bỏ (bấm \"Làm lại ảnh lỗi\" / xuất lại để thử lại).");
                _log?.Invoke($"[xuất] lượt {req.BatchId}: {dropped.Count} ảnh chưa xong sau {minutes} phút — bỏ");
            }
            // Ảnh THỰC SỰ có trong file (khử trùng giữa các SP): đếm Hub / CDN và tính hạn lượt trên đúng tập này.
            var usedCdnKeys = new HashSet<string>(StringComparer.Ordinal);
            var usedHubKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (row, listing, template) in routed)
            {
                if (dropped.Count > 0 && itemKeys[listing.ItemId].Count(dropped.Contains) is var n && n > 0)
                    warnings.Add(new ExportItemNote(row.ExcelRow, row.ItemId, row.Link, $"{n} ảnh chưa xong, đã bỏ"));
                var r = MassUploadMapper.Map(listing, defaults, template.Schema, resolve);
                if (r is MassUploadSkip skip)
                {
                    var note = new ExportItemNote(row.ExcelRow, row.ItemId, row.Link, skip.Reason);
                    (skip.Reason.Contains("ảnh", StringComparison.OrdinalIgnoreCase) ? imageSkipped : skipped).Add(note);
                    continue;
                }
                var product = (MassUploadProduct)r;
                foreach (var w in product.Warnings) warnings.Add(new ExportItemNote(row.ExcelRow, row.ItemId, row.Link, w));
                foreach (var note in product.Notes ?? []) itemNotes.Add(new ExportItemNote(row.ExcelRow, row.ItemId, row.Link, note));
                if (product.NeedsFix)
                    needsFix.Add(new ExportItemNote(row.ExcelRow, row.ItemId, row.Link, string.Join("; ", product.NeedsFixReasons)));
                if (!groups.TryGetValue(template, out var list)) groups[template] = list = [];
                list.Add(product);
                var inFile = product.Rows.SelectMany(x => x.Values).Select(c => c.Text).ToHashSet(StringComparer.Ordinal);
                var itemCdn = itemKeys[listing.ItemId]
                    .Where(k => cdnKeys.Contains(k) && resolve(k).Url is { } u && inFile.Contains(u)).ToList();
                usedCdnKeys.UnionWith(itemCdn);
                usedHubKeys.UnionWith(itemKeys[listing.ItemId].Where(k => hubUrls.TryGetValue(k, out var u) && inFile.Contains(u)));
                if (itemCdn.Count > 0)
                    cdnItems.Add(new ExportItemNote(row.ExcelRow, row.ItemId, row.Link,
                        _hubLinks is null ? $"{itemCdn.Count} ảnh tải từ CDN"
                        : itemCdn.Any(hubFailed.Contains) ? $"{itemCdn.Count} ảnh tải từ CDN (Hub lỗi / chưa có trên Hub)"
                        : $"{itemCdn.Count} ảnh tải từ CDN (chưa có trên Hub)"));
            }
            notices.AddRange(notices2);
            // Hạn thật của lượt: CHỈ ảnh đi đường cũ (kho tạm) mà file trỏ tới. Link kho ảnh gốc Hub không hạn. Ảnh bảng size luôn
            // đi kho tạm: có ảnh SP đường cũ thì gộp vào hạn lượt; lượt toàn link Hub thì hạn lượt = null, chỉ báo hạn bảng size.
            var sizeChartExpiresAt = await _store.GetEarliestExpiryAsync(req.ShopKey, [sizeChart.Key], CancellationToken.None)
                .ConfigureAwait(false);
            var productExpiresAt = usedCdnKeys.Count == 0 ? null
                : await _store.GetEarliestExpiryAsync(req.ShopKey, usedCdnKeys, CancellationToken.None).ConfigureAwait(false);
            DateTimeOffset? expiresAt = productExpiresAt is not { } pe ? null
                : sizeChartExpiresAt is { } se && se < pe ? se : pe;
            var usedCdn = usedCdnKeys.Count;
            var usedHub = usedHubKeys.Count;
            if (_hubLinks is not null && cdnItems.Count > 0)
                notices.Add($"{cdnItems.Count} SP có ảnh chưa có trên Hub → tải từ CDN (kho tạm, có hạn).");

            // 7. Ghi vào thư mục tạm, đủ hết mới chuyển ra
            var stamp = _clock().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var shopName = SafeFileName(req.ShopLabel, req.ShopKey);
            var written = new List<(string Staged, string Final)>();
            var total = groups.Values.Sum(g => g.Count);
            var done = 0;
            Report(ExportStep.Write, 0, total, "Ghi file…");
            foreach (var (template, products) in groups.OrderBy(g => g.Key.GroupName, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                Directory.CreateDirectory(staging);
                var prefix = $"{shopName}_{template.GroupName}_{stamp}";
                var files = await Task.Run(() => MassUploadTemplateWriter.WriteFiles(template.Path, template.Schema, products, staging,
                    prefix, defaults.ProductsPerFile), CancellationToken.None).ConfigureAwait(false);
                for (var i = 0; i < files.Count; i++)
                    written.Add((files[i].Path,
                        Path.Combine(req.OutputDirectory, $"{prefix}_{(i + 1).ToString(CultureInfo.InvariantCulture)}.xlsx")));
                done += products.Count;
                Report(ExportStep.Write, done, total, $"Đã ghi {done}/{total} SP ({written.Count} file).");
            }
            ct.ThrowIfCancellationRequested();
            foreach (var (_, final) in written)
                if (File.Exists(final)) throw new MassUploadExportException($"Đã có file {final} — không ghi đè.");
            foreach (var (staged, final) in written)
            {
                File.Move(staged, final, overwrite: false);
                moved.Add(final);
            }
            if (!TryDeleteDirectory(staging))
                _log?.Invoke($"[xuất] không xoá được thư mục tạm (đã rỗng) {staging} — hãy xoá tay");
            exported = total;
            await _store.FinishBatchAsync(req.BatchId, ExportBatchState.Done, exported, moved.Count, null, CancellationToken.None, expiresAt,
                    sizeChartExpiresAt, usedHub, usedCdn)
                .ConfigureAwait(false);
            Report(ExportStep.Done, total, total, $"Xong: {exported} SP, {moved.Count} file.");
            return new MassUploadExportResult(req.BatchId, moved.ToList(), exported, source.Items.Count, source.DuplicateRows, notHarvested,
                source.NoItemId, missingTemplates, imageSkipped, skipped, warnings, needsFix, failedImages, notices, true, expiresAt,
                dropped.Count, itemNotes, usedHub, usedCdn, cdnItems, sizeChartExpiresAt);
        }
        catch (Exception ex)
        {
            // Không để lại file dở: bỏ thư mục tạm + mọi file của lượt đã chuyển ra.
            var leftovers = moved.Where(f => !TryDeleteFile(f)).ToList();
            if (!TryDeleteDirectory(staging)) leftovers.Add(staging);
            var leftoverNote = leftovers.Count == 0 ? ""
                : " Không xoá được: " + string.Join(", ", leftovers) + " — hãy xoá tay (file này trỏ ảnh không còn được giữ).";
            if (leftovers.Count > 0) _log?.Invoke($"[xuất] lượt {req.BatchId}:{leftoverNote}");
            var cancelled = ex is OperationCanceledException && ct.IsCancellationRequested;
            try
            {
                await _store.FinishBatchAsync(req.BatchId, cancelled ? ExportBatchState.Cancelled : ExportBatchState.Failed, 0, 0,
                    (cancelled ? "đã huỷ" : ex.Message) + leftoverNote, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception) { /* sổ lỗi không được che lỗi gốc */ }
            if (ex is MassUploadExportException && leftovers.Count > 0) throw new MassUploadExportException(ex.Message + leftoverNote, ex);
            if (ex is IOException or UnauthorizedAccessException or InvalidOperationException && ex is not MassUploadExportException)
                throw new MassUploadExportException($"Ghi file lỗi: {ex.Message}{leftoverNote}", ex);
            throw;
        }
    }

    /// <summary>Hỏi link một lô, thử lại theo <see cref="MassUploadExportOptions.HubLinksRetryDelays"/>. Hết lượt thử vẫn lỗi → (null,
    /// lý do). Huỷ của user thì ném.</summary>
    private async Task<(HubImageLinks? Result, string? Error)> HubLinksWithRetryAsync(string shopKey, IReadOnlyList<string> lot,
        CancellationToken ct)
    {
        string? error = null;
        for (var attempt = 0; ; attempt++)
        {
            try { return (await _hubLinks!(shopKey, lot, ct).ConfigureAwait(false), null); }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException
                                           or ArgumentException
                                       || (ex is OperationCanceledException && !ct.IsCancellationRequested))
            {
                error = ex is OperationCanceledException ? "quá thời gian chờ" : ex.Message;
            }
            if (attempt >= _opt.HubLinksRetryDelays.Count) return (null, error);
            await Task.Delay(_opt.HubLinksRetryDelays[attempt], ct).ConfigureAwait(false);
        }
    }

    /// <summary>Ảnh đưa vào bước đệm cho một SP: ảnh lựa chọn nhóm 1 + tối đa N ảnh SP đầu.</summary>
    private List<string> ImageKeys(ShopeeListing listing)
    {
        var keys = new List<string>();
        if (listing.Tiers.Count > 0)
            keys.AddRange(listing.Tiers[0].Images.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i!));
        keys.AddRange(listing.Images.Take(Math.Max(1, _opt.ProductImagesPerItem)));
        return keys.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Chờ tới khi mọi ảnh cần đều sẵn sàng hoặc lỗi. Lần hỏi đầu đưa lại lên Hub mọi ảnh còn hạn dưới
    /// <see cref="MassUploadExportOptions.ImageMinRemaining"/> (kể cả bảng size). Không thêm ảnh nào xong trong
    /// <see cref="MassUploadExportOptions.ImageWaitIdleLimit"/> → thôi chờ (<c>TimedOut</c> = true). Worker dừng vì cấu hình hỏng, hay Hub
    /// hết chỗ (507 / media-quota-exceeded / disk-low) → <see cref="MassUploadExportException"/>.</summary>
    private async Task<(Func<string, ImageResolution> Resolve, bool TimedOut)> WaitImagesAsync(string shopKey,
        IReadOnlyCollection<string> needed, Action<ExportStep, int, int, string> report, CancellationToken ct)
    {
        var first = true;
        var lastDone = -1;
        var idle = System.Diagnostics.Stopwatch.StartNew();
        // Lỗi gần nhất của worker có thể là của lượt TRƯỚC (Hub đã được dọn từ đó): chỉ tính lỗi mới phát sinh trong lượt này
        // (worker dựng chuỗi lỗi mới mỗi lần gặp lỗi → khác tham chiếu).
        var errorAtStart = _worker.LastError;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var resolve = await _store.GetResolverAsync(shopKey, needed, first ? _opt.ImageMinRemaining : TimeSpan.Zero, ct)
                .ConfigureAwait(false);
            first = false;
            int ready = 0, failed = 0;
            foreach (var k in needed)
                switch (resolve(k).State)
                {
                    case ImageReadiness.Ready: ready++; break;
                    case ImageReadiness.Failed: failed++; break;
                }
            var waiting = needed.Count - ready - failed;
            var why = waiting > 0 && _worker.LastError is { } err ? $" — {err}" : "";
            report(ExportStep.Images, ready + failed, needed.Count, $"Ảnh: {ready} sẵn sàng, {failed} lỗi, {waiting} đang làm{why}");
            if (waiting == 0) return (resolve, false);
            if (_worker.HaltReason is { } halt) throw new MassUploadExportException($"Bước đệm ảnh dừng: {halt}");
            if (_worker.LastError is { } last && !ReferenceEquals(last, errorAtStart) && IsHubFull(last)) throw new MassUploadExportException($"{HubFullMessage} ({last})");
            if (ready + failed > lastDone) { lastDone = ready + failed; idle.Restart(); }
            else if (idle.Elapsed >= _opt.ImageWaitIdleLimit) return (resolve, true);
            _worker.Nudge();
            await Task.Delay(_opt.ImagePollInterval, ct).ConfigureAwait(false);
        }
    }

    private static bool IsHubFull(string error) =>
        error.Contains("507", StringComparison.Ordinal) || error.Contains("media-quota-exceeded", StringComparison.Ordinal)
        || error.Contains("disk-low", StringComparison.Ordinal);

    /// <summary>Lúc mở app: lượt <c>failed</c> / <c>cancelled</c> có thư mục ra → xoá thư mục tạm <c>.dang-xuat-&lt;batchId&gt;</c> còn sót
    /// (app tắt / dọn hụt giữa chừng). Trả các câu báo (đã dọn / không dọn được — hãy xoá tay).</summary>
    public static IReadOnlyList<string> CleanupStaleStaging(IEnumerable<ExportBatchInfo> batches)
    {
        var messages = new List<string>();
        foreach (var b in batches)
        {
            if (b.State is not (ExportBatchState.Failed or ExportBatchState.Cancelled) || string.IsNullOrWhiteSpace(b.OutputDir)) continue;
            var dir = StagingDirectory(b.OutputDir, b.BatchId);
            if (!Directory.Exists(dir)) continue;
            messages.Add(TryDeleteDirectory(dir)
                ? $"Đã dọn thư mục tạm còn sót của lượt {b.BatchId}: {dir}"
                : $"Không xoá được thư mục tạm {dir} — hãy xoá tay (file trong đó trỏ ảnh không còn được giữ).");
        }
        return messages;
    }

    /// <summary>Tên shop dùng được trong tên file: bỏ ký tự cấm, khoảng trắng → <c>-</c>, tối đa 40 ký tự; rỗng → mã shop.</summary>
    public static string SafeFileName(string? label, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = (label ?? "").Trim().Select(c => invalid.Contains(c) || c == '_' || char.IsWhiteSpace(c) ? '-' : c).ToArray();
        var s = new string(chars);
        while (s.Contains("--", StringComparison.Ordinal)) s = s.Replace("--", "-", StringComparison.Ordinal);
        s = s.Trim('-', '.');
        if (s.Length > 40) s = s[..40].TrimEnd('-', '.');
        return s.Length == 0 ? fallback : s;
    }

    /// <summary>Xoá file; true = không còn file (đã xoá / vốn không có).</summary>
    private static bool TryDeleteFile(string path)
    {
        try { File.Delete(path); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>Xoá thư mục (cả nội dung); true = không còn thư mục.</summary>
    private static bool TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); return true; }
        catch (IOException) { return !Directory.Exists(path); }
        catch (UnauthorizedAccessException) { return !Directory.Exists(path); }
    }
}
