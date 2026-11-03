using System.Collections.Concurrent;
using ClosedXML.Excel;

namespace UpdateProduct;

// Tách khỏi BigSellerProductUpdateRunner.cs (đợt D — pure move): ba type "ở nhờ" của module Update —
// LaneAbortedException (tín hiệu lane phải khởi động lại), WorkbookRecord (một dòng workbook đã map)
// và WorkbookRecordCache (cache dữ liệu shop nạp MỘT lần, dùng chung mọi lane).

/// <summary>Lỗi khiến một lane Update phải DỪNG bất thường (tab đóng, listing lỗi liên tục, captcha…)
/// — ném ra để supervisor <c>RunLanesAsync</c> KHỞI ĐỘNG LẠI lane thay vì để nó "nghỉ hưu" âm thầm.
/// Trước đây các nhánh lỗi dùng <c>break</c> (return bình thường) nên supervisor tưởng "hết việc" →
/// lane mất hẳn → worker rụng dần 5→1→0.</summary>
internal sealed class LaneAbortedException(string reason) : Exception(reason);

/// <summary>Một dòng workbook đã map sang dữ liệu update (khớp theo Shopee item id).</summary>
internal sealed record WorkbookRecord(string Link, string Sku, string ProductName, string Price, int LineIndex, string Description = "");

/// <summary>
/// Cache DÙNG CHUNG dữ liệu shop (dòng workbook → <see cref="WorkbookRecord"/>, khóa = Shopee item id) cho
/// TẤT CẢ lane update. Nạp MỘT LẦN trước khi mở Brave rồi chia sẻ (immutable) → thay vì mỗi lane tự đọc
/// workbook (N lần đọc + N lần khóa file + N lần parse). Đây là "cache chung từ dòng đến dòng" trước khi
/// từng lane tìm item id trên Listing và sửa.
/// </summary>
internal sealed class WorkbookRecordCache
{
    public IReadOnlyDictionary<string, WorkbookRecord> Records { get; }

    private WorkbookRecordCache(IReadOnlyDictionary<string, WorkbookRecord> records) => Records = records;

    /// <summary>Nạp + log (dùng ở tầng điều phối, nạp 1 lần cho mọi lane).</summary>
    public static async Task<WorkbookRecordCache> LoadAsync(BigSellerWorkflowSettings settings, Action<string> log, CancellationToken ct)
    {
        var (map, emptyRewriteRows) = await LoadRecordMapAsync(settings, ct).ConfigureAwait(false);
        if (emptyRewriteRows.Count > 0)
        {
            var preview = string.Join(", ", emptyRewriteRows.Take(10));
            log($"⚠ BỎ QUA {emptyRewriteRows.Count} dòng có cột G (Tên đã sửa) TRỐNG (vd dòng {preview}) — " +
                "chạy \"Update tên SP (AI)\" để điền cột G nếu muốn update các dòng này.");
        }
        log($"📒 Workbook sheet '{settings.DataSheet}' (cache chung mọi lane): {map.Count} dòng (khớp theo Shopee item id).");
        return new WorkbookRecordCache(map);
    }

    // Đọc workbook → map item id → record. Khóa file khi đọc (chung file giữa nhiều account): serialize với
    // lúc "Update tên SP" đang GHI cột G → tránh đọc-khi-đang-ghi (IOException/đọc lệch). Thuần, không log.
    internal static async Task<(Dictionary<string, WorkbookRecord> map, List<int> emptyRewriteRows)> LoadRecordMapAsync(
        BigSellerWorkflowSettings settings, CancellationToken ct)
    {
        // HUB-MODE: dòng đã có tên-sửa lấy từ kho Hub (Postgres) — KHÔNG ánh xạ cột, KHÔNG khoá file. Đặt TRƯỚC
        // validate cột (cột chỉ áp cho workbook Excel). Excel-mode giữ nguyên toàn bộ nhánh dưới.
        if (settings.UseHubData)
            return await LoadRecordMapFromHubAsync(settings, ct).ConfigureAwait(false);

        // Cột bắt buộc: "Tên đã sửa" (tên để update) + ít nhất 1 trong (Item ID / Link) để khớp dòng.
        // 0 = "không dùng" → fail rõ ràng thay vì đẩy 0 vào ClosedXML (Cell(0) ném lỗi).
        if (settings.RewrittenNameColumn <= 0)
            throw new InvalidOperationException("Chưa map cột 'Tên đã sửa' cho shop (mục BigSeller → Ánh xạ cột).");
        if (settings.ItemIdColumn <= 0 && settings.LinkColumn <= 0)
            throw new InvalidOperationException("Cần map ít nhất 'Item ID' hoặc 'Link' để khớp dòng (mục BigSeller → Ánh xạ cột).");

        var descCol = settings.DescriptionColumn > 0 ? settings.DescriptionColumn : 8;
        var map = new Dictionary<string, WorkbookRecord>();
        var emptyRewriteRows = new List<int>();   // dòng có SP để update nhưng cột G (Tên đã sửa) còn trống
        await WorkbookSheetReader.ForEachDataRowAsync(settings, (row, r) =>
        {
            var link = WorkbookSheetReader.Cell(row, settings.LinkColumn);
            var price = WorkbookSheetReader.Cell(row, settings.PriceColumn);
            var sku = WorkbookSheetReader.Cell(row, settings.SkuColumn);
            var colE = WorkbookSheetReader.Cell(row, settings.ItemIdColumn);
            var rewritten = row.Cell(settings.RewrittenNameColumn).GetString().Trim();   // Tên đã sửa (đã validate > 0)
            var description = WorkbookSheetReader.Cell(row, descCol);

            var rowId = WorkbookSheetReader.RowId(colE, link);
            if (string.IsNullOrWhiteSpace(rowId)) return;

            // Cột G trống → BỎ QUA riêng dòng đó (không update tên gốc cột F), vẫn chạy tiếp các dòng khác.
            if (string.IsNullOrWhiteSpace(rewritten)) { emptyRewriteRows.Add(r); return; }
            map[rowId] = new WorkbookRecord(link, sku, rewritten, price, r, description);
        }, ct).ConfigureAwait(false);

        return (map, emptyRewriteRows);
    }

    /// <summary>Item id của dòng nếu dòng là MỤC TIÊU Update, ngược lại null: cùng luật với <see cref="LoadRecordMapAsync"/> (nhánh Excel) — dòng định vị
    /// được bằng Item ID / Link VÀ cột "Tên đã sửa" không trống (trống thì Update bỏ qua dòng, nên nó không bao giờ có Update Done).
    /// Cột đã được caller kiểm hợp lệ (RewrittenNameColumn &gt; 0).</summary>
    internal static string? TargetRowId(IXLRow row, BigSellerWorkflowSettings settings)
    {
        var rowId = WorkbookSheetReader.RowId(
            WorkbookSheetReader.Cell(row, settings.ItemIdColumn), WorkbookSheetReader.Cell(row, settings.LinkColumn));
        if (string.IsNullOrWhiteSpace(rowId)) return null;
        return string.IsNullOrWhiteSpace(row.Cell(settings.RewrittenNameColumn).GetString().Trim()) ? null : rowId;
    }

    /// <summary>Tập "mục tiêu Update" của từng khoá tiến độ trong <paramref name="keys"/> (khoá = tên sheet trong file nguồn), dùng cùng định nghĩa
    /// mục tiêu và ánh xạ cột của <paramref name="settings"/> như luồng Update. Mở file MỘT lần (khoá file như mọi lần đọc workbook; hub-mode: hỏi Hub
    /// theo sheet, không đụng file). Khoá <paramref name="ownKey"/> (khoá của shop đang chạy, kể cả dạng <c>shared:...</c> ở workbook chung) đọc theo
    /// <see cref="BigSellerWorkflowSettings.DataSheet"/> và vùng dòng StartRow..EndRow của shop; khoá khác đọc sheet cùng tên khoá, cả sheet (không biết
    /// vùng dòng của shop đó → tập mục tiêu có thể rộng hơn thực tế, hậu quả chỉ là import lại thêm vài SP). Khoá <c>shared:...</c> của shop KHÁC và sheet
    /// không có trong file/không đọc được thì KHÔNG đưa vào kết quả (lý do ghi qua <paramref name="log"/>). Cấu hình cột thiếu → ném lỗi (như Update).</summary>
    internal static async Task<Dictionary<string, IReadOnlySet<string>>> LoadUpdateTargetsByKeyAsync(
        BigSellerWorkflowSettings settings, string ownKey, IReadOnlyCollection<string> keys, Action<string> log, CancellationToken ct)
    {
        var result = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);
        bool IsOwn(string key) => string.Equals(key, ownKey, StringComparison.OrdinalIgnoreCase);
        // Tên sheet thật của khoá: khoá của shop đang chạy luôn là DataSheet (ở workbook chung khoá là shared:..., không phải tên sheet).
        string SheetOf(string key) => IsOwn(key) && !string.IsNullOrWhiteSpace(settings.DataSheet) ? settings.DataSheet : key;

        var readable = keys.Where(k => !string.IsNullOrWhiteSpace(k) && (IsOwn(k) || !IsSharedKey(k))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (readable.Count == 0) return result;

        if (settings.UseHubData)
        {
            foreach (var key in readable)
            {
                try
                {
                    var perKey = settings with { DataSheet = SheetOf(key), StartRow = IsOwn(key) ? settings.StartRow : 2, EndRow = IsOwn(key) ? settings.EndRow : 0 };
                    var map = await LoadRecordMapFromHubAsync(perKey, ct).ConfigureAwait(false);
                    result[key] = new HashSet<string>(map.map.Keys, StringComparer.Ordinal);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { log($"  Không đọc được mục tiêu Update của '{key}' từ Hub: {ex.Message}"); }
            }
            return result;
        }

        if (settings.RewrittenNameColumn <= 0)
            throw new InvalidOperationException("Chưa map cột 'Tên đã sửa' cho shop (mục BigSeller → Ánh xạ cột).");
        if (settings.ItemIdColumn <= 0 && settings.LinkColumn <= 0)
            throw new InvalidOperationException("Cần map ít nhất 'Item ID' hoặc 'Link' để khớp dòng (mục BigSeller → Ánh xạ cột).");

        using var _ = await WorkbookFileLockHandle.AcquireAsync(settings.WorkbookPath, ct).ConfigureAwait(false);
        using var wb = new XLWorkbook(settings.WorkbookPath);
        foreach (var key in readable)
        {
            ct.ThrowIfCancellationRequested();
            var sheetName = SheetOf(key).Trim();
            if (!wb.TryGetWorksheet(sheetName, out var ws))
            {
                log($"  Sheet '{sheetName}' (khoá '{key}') không có trong file nguồn — không biết mục tiêu Update của khoá này.");
                continue;
            }
            var start = IsOwn(key) ? Math.Max(2, settings.StartRow) : 2;
            var last = ws.LastRowUsed()?.RowNumber() ?? 0;
            var end = IsOwn(key) && settings.EndRow > 0 ? Math.Min(settings.EndRow, last) : last;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var r = start; r <= end; r++)
            {
                var id = TargetRowId(ws.Row(r), settings);
                if (id is not null) ids.Add(id);
            }
            result[key] = ids;
        }
        return result;
    }

    /// <summary>Khoá tiến độ dạng <c>shared:{shopId}</c> (workbook chung): không phải tên sheet trong file nguồn nên không đọc được mục tiêu theo khoá.</summary>
    internal static bool IsSharedKey(string key) => key.StartsWith("shared:", StringComparison.OrdinalIgnoreCase);

    // HUB-MODE của LoadRecordMapAsync: server đã LỌC đúng dòng cần update (name_rewritten non-blank AND có
    // itemId/link) trong [StartRow..EndRow] và trả field cấu trúc → dựng CÙNG map itemId→WorkbookRecord như nhánh
    // Excel (itemId ưu tiên ItemId, rỗng thì suy từ Link; LineIndex=RowNo; ProductName=NameRewritten; Price=PriceSale).
    // emptyRewriteRows rỗng: server không trả dòng "cột G trống" nên KHÔNG có gì để cảnh báo. KHÔNG khoá file.
    private static async Task<(Dictionary<string, WorkbookRecord> map, List<int> emptyRewriteRows)> LoadRecordMapFromHubAsync(
        BigSellerWorkflowSettings settings, CancellationToken ct)
    {
        var (client, start, end) = WorkbookSheetReader.BeginHubRead(settings);
        var rows = await client.GetProductRecordMapAsync(settings.AccountId, settings.DataSheet, start, end, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("⛔ Hub chưa sẵn sàng (kho sản phẩm Postgres) — thử lại sau.");

        var map = new Dictionary<string, WorkbookRecord>();
        foreach (var r in rows)
        {
            var rowId = WorkbookSheetReader.RowId(r.ItemId, r.Link);
            if (string.IsNullOrWhiteSpace(rowId)) continue;
            map[rowId] = new WorkbookRecord(r.Link, r.Sku, r.NameRewritten, r.PriceSale, r.RowNo);
        }
        return (map, new List<int>());
    }
}

/// <summary>
/// Theo dõi số lượng sản phẩm mục tiêu cần update của shop trong phiên chạy (dùng chung mọi lane, thread-safe).
/// Khi số SP đã update đạt đủ <see cref="TargetCount"/> hoặc shop chạm giới hạn đăng SP trên Shopee (<see cref="IsListingLimitReached"/>),
/// các lane dừng ngay và bỏ qua các trang Hộp nháp còn lại.
/// </summary>
internal sealed class UpdateTargetTracker
{
    private readonly ConcurrentDictionary<string, byte> _completedIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _handledIds = new(StringComparer.Ordinal);
    private readonly Action? _onListingLimitReached;
    private volatile bool _listingLimitReached;
    // Nguồn chốt giới hạn: hộp thoại (đã xác nhận wording) hay toast (đoán câu chữ). Hộp thoại thắng nếu có cả hai.
    private volatile bool _limitByDialog;
    private volatile bool _limitByToast;
    // Item id các SP KHÁC NHAU đã nhận toast nghi vượt giới hạn trong lượt này (thread-safe, mọi lane dùng chung).
    private readonly ConcurrentDictionary<string, byte> _limitToastIds = new(StringComparer.Ordinal);

    /// <summary>Số SP KHÁC NHAU cùng nhận toast nghi vượt giới hạn thì mới chốt dừng shop (1 toast khớp nhầm ở 1 SP không đủ).</summary>
    public const int ListingLimitToastThreshold = 2;

    public int TargetCount { get; }
    public int CompletedCount => _completedIds.Count;
    public int HandledCount => _handledIds.Count;
    public bool IsListingLimitReached => _listingLimitReached;
    /// <summary>True nếu giới hạn CHỈ được chốt bằng toast (chưa có hộp thoại xác nhận) — caller không coi là "xong".</summary>
    public bool IsListingLimitFromToast => _limitByToast && !_limitByDialog;
    public bool IsAllCompleted => TargetCount > 0 && _completedIds.Count >= TargetCount;
    public bool IsAllHandled => TargetCount > 0 && _handledIds.Count >= TargetCount;

    public UpdateTargetTracker(
        IReadOnlyDictionary<string, WorkbookRecord> records,
        IReadOnlyDictionary<string, string?>? updateDone = null,
        IReadOnlyDictionary<string, string?>? importDone = null,
        Action? onListingLimitReached = null)
    {
        TargetCount = records.Count;
        _onListingLimitReached = onListingLimitReached;

        if (updateDone is { Count: > 0 })
        {
            foreach (var (id, rec) in records)
            {
                if (updateDone.TryGetValue(id, out var doneName) && doneName == rec.ProductName)
                {
                    _completedIds.TryAdd(id, 0);
                    _handledIds.TryAdd(id, 0);
                }
            }
        }
    }

    public void MarkCompleted(string? itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return;
        _completedIds.TryAdd(itemId, 0);
        _handledIds.TryAdd(itemId, 0);
    }

    public void MarkFailedPermanent(string? itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return;
        _handledIds.TryAdd(itemId, 0);
    }

    /// <summary>Ghi nhận SP <paramref name="itemId"/> vừa nhận toast nghi vượt giới hạn. Chỉ khi đủ
    /// <see cref="ListingLimitToastThreshold"/> item id KHÁC NHAU mới chốt giới hạn (nguồn toast); cùng một item id lặp lại
    /// (thử lại, lane khác mở lại SP đó) chỉ tính MỘT lần. item id rỗng/null không được tính. Thread-safe.
    /// Trả (số SP khác nhau đã ghi nhận, đã chốt chưa).</summary>
    public (int Distinct, bool Decided) RecordProductLimitToast(string? itemId)
    {
        if (!string.IsNullOrEmpty(itemId)) _limitToastIds.TryAdd(itemId, 0);
        var distinct = _limitToastIds.Count;
        var decided = distinct >= ListingLimitToastThreshold;
        if (decided) MarkListingLimitReached(fromToast: true);
        return (distinct, decided);
    }

    /// <param name="fromToast">True khi chốt từ toast BigSeller (câu chữ đoán). Mặc định = từ hộp thoại đã xác nhận.</param>
    public void MarkListingLimitReached(bool fromToast = false)
    {
        // Ghi nguồn TRƯỚC khi xét "đã chốt" → lane đến sau bằng hộp thoại vẫn nâng nguồn lên "đã xác nhận".
        if (fromToast) _limitByToast = true; else _limitByDialog = true;
        if (_listingLimitReached) return;
        _listingLimitReached = true;
        try { _onListingLimitReached?.Invoke(); } catch { }
    }
}
