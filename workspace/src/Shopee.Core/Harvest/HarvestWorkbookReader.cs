using System.Diagnostics;
using ClosedXML.Excel;
using Shopee.Core.Scrape;

namespace Shopee.Core.Harvest;

/// <summary>Workbook đang bị app Update ghi (khoá sidecar <c>&lt;file&gt;.lock</c>). Thông điệp mang tiền tố
/// <see cref="Prefix"/>: lọt tới engine thì <c>ScrapeFailurePolicy</c> nhận ra là việc của MÁY (dừng job, nhả tk nguyên vẹn,
/// không cooldown, không tính kẹt, không bỏ dòng) chứ không phải lỗi tài khoản.</summary>
public sealed class HarvestWorkbookLockedException(string path) : IOException(MessageFor(path))
{
    public const string Prefix = "Workbook đang bận: ";

    public string WorkbookPath { get; } = path;

    public static string MessageFor(string path) =>
        $"{Prefix}app Update đang ghi file workbook ({Path.GetFileName(path)}) — chờ app Update lưu xong rồi chạy lại.";

    public static bool Is(string? message) =>
        message is not null && message.StartsWith(Prefix, StringComparison.Ordinal);
}

/// <summary>Dải dòng của một sheet Harvest đọc từ MỘT bản workbook: chỉ số dòng nén (1 = dòng dữ liệu đầu, theo
/// <see cref="HarvestWorkbookReader"/>) + các dòng có link trong dải + thông tin cột link (<see cref="Info"/>, null nếu caller
/// không đọc cả sheet).</summary>
public sealed record HarvestWorkbookRange(int DenseStart, int DenseEnd, ScrapeLinkResult? Links, HarvestSheetInfo? Info = null);

/// <summary>Cột link Harvest đã chọn cho một sheet và các số đếm trên cột đó: <see cref="DataRows"/> dòng dữ liệu,
/// <see cref="LinkRows"/> dòng có link, <see cref="ProductRows"/> dòng có link SP (tách được id), <see cref="NotLinkRows"/> dòng có
/// chữ mà không phải link (bị bỏ qua).</summary>
public sealed record HarvestSheetInfo(int LinkColumn, string LinkColumnName, int DataRows, int LinkRows, int ProductRows, int NotLinkRows)
{
    /// <summary>Cảnh báo cho user (log + màn hình) khi cột link có vẻ chọn sai / file lẫn chữ: dưới một nửa số dòng có link SP, hoặc
    /// có dòng chữ không phải link. null = ổn.</summary>
    public string? Warning(string sheet)
    {
        var parts = new List<string>();
        if (DataRows > 0 && ProductRows * 2 < DataRows)
            parts.Add($"chỉ {ProductRows}/{DataRows} dòng có link SP ở cột {LinkColumnName}");
        if (NotLinkRows > 0)
            parts.Add($"{NotLinkRows} dòng ở cột {LinkColumnName} không phải link — bỏ qua");
        return parts.Count == 0 ? null : $"sheet {sheet}: {string.Join("; ", parts)}";
    }
}

/// <summary>
/// Đọc workbook cho app Harvest (CHỈ Harvest dùng — không đụng các bộ đọc dùng chung của Scrape/Update):
/// <list type="bullet">
/// <item>mở file chia sẻ đọc-GHI (Excel đang mở file vẫn đọc được), chép toàn bộ byte vào RAM rồi đóng handle NGAY — không
/// giữ file trong lúc parse (vài giây với file lớn), để <c>wb.Save()</c> của app Update không trúng khe;</item>
/// <item>tôn trọng khoá sidecar <c>&lt;file&gt;.lock</c> của app Update: đang bị giữ → chờ bằng <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
/// (huỷ được) tới <see cref="LockWait"/>, quá hạn → <see cref="HarvestWorkbookLockedException"/>;</item>
/// <item>đọc link theo luật RIÊNG của Harvest (GĐ6 — Harvest chỉ cần danh sách link; app Scrape vẫn dùng
/// <see cref="ScrapeWorkbook"/>: link cột A, tiêu đề dòng 1, bắt buộc tên SP cột F):
/// <list type="bullet">
/// <item><b>cột link</b> = cột có NHIỀU ô là link SP Shopee tách được id nhất trong <see cref="LinkColumnScanRows"/> dòng đầu (hoà →
/// cột trái); không cột nào có id SP → cột nhiều link Shopee nhất (host theo <see cref="HarvestLinks.IsShopeeHost"/>); không có →
/// cột A;</item>
/// <item><b>tiêu đề</b>: dòng 1 là tiêu đề, TRỪ KHI ô cột link ở dòng 1 đã là link (file chỉ có danh sách link, không tiêu đề);</item>
/// <item>ô cột link trông như link → phải lấy (tên SP không bắt buộc: overlay / log lấy tên từ JSON của Shopee); ô trống hoặc chữ
/// không phải link → bỏ; cột chọn có ít link SP / lẫn chữ → <see cref="HarvestSheetInfo.Warning"/>.</item>
/// </list>
/// Số dòng nén = thứ tự trong các dòng CÓ THẬT (Excel lược dòng trống) từ dòng dữ liệu đầu — mọi hàm ở đây cùng một cách đánh số
/// (dựng lại tiến độ và engine phải khớp nhau).</item>
/// </list>
/// </summary>
public static class HarvestWorkbookReader
{
    /// <summary>Thời gian giữ handle file của lần đọc gần nhất (đo / test).</summary>
    public static TimeSpan LastHandleHeld { get; private set; }

    /// <summary>Gặp khoá <c>.lock</c> của Update: chờ tối đa chừng này (thử lại mỗi <see cref="LockPoll"/>) rồi báo lỗi.</summary>
    public static TimeSpan LockWait { get; set; } = TimeSpan.FromMinutes(2);

    public static TimeSpan LockPoll { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>App Update đang giữ khoá sidecar của file (đang ghi workbook)? Dò bằng quyền ĐỌC, không chia sẻ.</summary>
    public static bool IsUpdateLocked(string workbookPath)
    {
        var lockPath = Path.GetFullPath(workbookPath) + ".lock";
        if (!File.Exists(lockPath)) return false;
        try
        {
            using var probe = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (FileNotFoundException) { return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    /// <summary>Byte của file, đọc với chia sẻ đọc-ghi rồi đóng ngay. NÉM khi không đọc được / Update đang khoá (không chờ).</summary>
    public static byte[] ReadAllShared(string workbookPath)
    {
        if (IsUpdateLocked(workbookPath)) throw new HarvestWorkbookLockedException(workbookPath);
        var sw = Stopwatch.StartNew();
        using var fs = new FileStream(workbookPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bytes = new byte[fs.Length];
        fs.ReadExactly(bytes);
        LastHandleHeld = sw.Elapsed;
        return bytes;
    }

    private static readonly System.Xml.Linq.XNamespace SheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>Tên sheet của workbook (đọc entry <c>xl/workbook.xml</c> trên bản trong RAM; entry hỏng → ClosedXML). Khác
    /// <c>WorkbookSheets</c> dùng chung (lỗi đọc → danh sách rỗng): ở đây NÉM khi không đọc được (file bị khoá độc quyền, Update
    /// đang ghi, file hỏng) — Harvest cần phân biệt "không có sheet của shop" với "chưa đọc được".</summary>
    public static IReadOnlyList<string> ListSheetNames(string workbookPath)
    {
        var bytes = ReadAllShared(workbookPath);
        var names = new List<string>();
        try
        {
            using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes, writable: false), System.IO.Compression.ZipArchiveMode.Read);
            if (zip.GetEntry("xl/workbook.xml") is { } entry)
            {
                using var s = entry.Open();
                foreach (var sheet in System.Xml.Linq.XDocument.Load(s).Descendants(SheetNs + "sheet"))
                    if (sheet.Attribute("name")?.Value is { } name && !string.IsNullOrWhiteSpace(name)) names.Add(name);
            }
        }
        catch (InvalidDataException) { names.Clear(); }
        catch (System.Xml.XmlException) { names.Clear(); }
        if (names.Count > 0) return names;
        using var wb = new XLWorkbook(new MemoryStream(bytes, writable: false));
        return wb.Worksheets.Select(ws => ws.Name).ToList();
    }

    /// <summary>Như <see cref="ReadAllShared"/> nhưng gặp khoá Update thì chờ (huỷ được) tới <see cref="LockWait"/>.</summary>
    public static async Task<byte[]> ReadAllSharedAsync(string workbookPath, CancellationToken cancellationToken)
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return ReadAllShared(workbookPath); }
            catch (HarvestWorkbookLockedException) when (waited.Elapsed < LockWait)
            {
                await Task.Delay(LockPoll, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<T> WithWorkbookAsync<T>(string workbookPath, Func<XLWorkbook, T> parse, CancellationToken cancellationToken)
    {
        var bytes = await ReadAllSharedAsync(workbookPath, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            using var wb = new XLWorkbook(new MemoryStream(bytes, writable: false));
            return parse(wb);
        }, cancellationToken).ConfigureAwait(false);
    }

    public static Task<int> TotalDataRowsAsync(string workbookPath, string sheet, CancellationToken cancellationToken) =>
        WithWorkbookAsync(workbookPath, wb => TotalDataRows(wb, sheet), cancellationToken);

    public static Task<ScrapeLinkResult> FetchLinksAsync(string workbookPath, string sheet, int startRow, int endRow,
        CancellationToken cancellationToken)
    {
        if (startRow < 1) throw new ArgumentException("startRow phải >= 1.");
        if (endRow < startRow) throw new ArgumentException("endRow phải >= startRow.");
        return WithWorkbookAsync(workbookPath, wb => FetchLinks(wb, sheet, startRow, endRow), cancellationToken);
    }

    /// <summary>Đọc MỘT lần cả file: mọi sheet (theo thứ tự trong file), mỗi sheet toàn bộ dòng dữ liệu — cho màn chọn file của app
    /// Harvest (đếm link từng sheet). Gặp khoá Update thì chờ như <see cref="ReadAllSharedAsync"/>.</summary>
    public static Task<IReadOnlyList<(string Sheet, HarvestWorkbookRange Range)>> ReadAllSheetsAsync(string workbookPath,
        CancellationToken cancellationToken) =>
        WithWorkbookAsync<IReadOnlyList<(string, HarvestWorkbookRange)>>(workbookPath,
            wb => wb.Worksheets.Select(ws => (ws.Name, ReadSheet(wb, ws.Name))).ToList(), cancellationToken);

    /// <summary>Đầu lượt chạy một sheet: MỘT lần đọc file cho cả số dòng lẫn các dòng có link — dựng lại tiến độ trên đúng bản
    /// workbook đó. Sheet không có dòng dữ liệu → <see cref="HarvestWorkbookRange.Links"/> null.</summary>
    public static Task<HarvestWorkbookRange> ReadSheetAsync(string workbookPath, string sheet, CancellationToken cancellationToken) =>
        WithWorkbookAsync(workbookPath, wb => ReadSheet(wb, sheet), cancellationToken);

    // ── Luật đọc sheet của Harvest ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Số dòng đầu (kể cả tiêu đề) quét để tìm cột chứa link Shopee.</summary>
    public const int LinkColumnScanRows = 200;

    /// <summary>Bố cục một sheet: cột link (1-based), dòng Excel dữ liệu đầu (1 = không tiêu đề, 2 = có), các dòng dữ liệu có thật
    /// theo thứ tự (vị trí trong danh sách + 1 = số dòng nén).</summary>
    internal sealed record SheetLayout(int LinkColumn, int FirstDataRow, List<IXLRow> Rows, string[] Headers, int LastColumn);

    internal static HarvestWorkbookRange ReadSheet(XLWorkbook wb, string sheet)
    {
        var ws = GetSheet(wb, sheet);
        var layout = Layout(ws);
        var total = layout.Rows.Count;
        return new HarvestWorkbookRange(1, total, total >= 1 ? FetchLinks(wb, sheet, 1, total) : null, Inspect(layout));
    }

    /// <summary>Đếm trên cột link đã chọn: dòng có link tách được id SP / dòng có link / dòng có chữ mà không phải link.</summary>
    private static HarvestSheetInfo Inspect(SheetLayout layout)
    {
        int idRows = 0, linkRows = 0, notLinkRows = 0;
        foreach (var row in layout.Rows)
        {
            var text = row.Cell(layout.LinkColumn).GetString().Trim();
            if (text.Length == 0) continue;
            if (!LooksLikeLink(text)) { notLinkRows++; continue; }
            linkRows++;
            if (IsProductLink(text)) idRows++;
        }
        return new HarvestSheetInfo(layout.LinkColumn, ColumnLetter(layout.LinkColumn), layout.Rows.Count, linkRows, idRows, notLinkRows);
    }

    internal static int TotalDataRows(XLWorkbook wb, string sheet) => Layout(GetSheet(wb, sheet)).Rows.Count;

    /// <summary>Các dòng có link trong [<paramref name="startRow"/>..<paramref name="endRow"/>] (số dòng nén). Ô cột link trống HOẶC
    /// có chữ mà không trông như link ("Link", "hết hàng", tiêu đề gộp…) → dòng không có link, đếm vào
    /// <see cref="ScrapeLinkResult.SkippedMissingLink"/> (không phải mục phải lấy); Harvest không bắt buộc tên SP
    /// (<see cref="ScrapeLinkResult.SkippedMissingProductName"/> luôn 0).</summary>
    internal static ScrapeLinkResult FetchLinks(XLWorkbook wb, string sheet, int startRow, int endRow)
    {
        var layout = Layout(GetSheet(wb, sheet));
        var items = new List<ScrapeLinkItem>();
        var skipLink = 0;
        var end = Math.Min(endRow, layout.Rows.Count);
        for (var p = Math.Max(1, startRow); p <= end; p++)
        {
            var row = layout.Rows[p - 1];
            var raw = row.Cell(layout.LinkColumn).GetString();
            if (!LooksLikeLink(raw)) { skipLink++; continue; }
            var link = NormalizeLink(raw);
            var values = new Dictionary<string, string>();
            for (var c = 1; c <= layout.LastColumn; c++) values[layout.Headers[c]] = row.Cell(c).GetString();
            items.Add(new ScrapeLinkItem(p, link, values));
        }
        return new ScrapeLinkResult(items, layout.Rows.Count, 0, skipLink);
    }

    internal static SheetLayout Layout(IXLWorksheet ws)
    {
        var used = ws.RowsUsed().OrderBy(r => r.RowNumber()).ToList();
        var lastCol = Math.Max(1, ws.LastColumnUsed()?.ColumnNumber() ?? 1);
        var linkCol = PickLinkColumn(used.Take(LinkColumnScanRows).ToList(), lastCol);
        var firstData = LooksLikeLink(ws.Cell(1, linkCol).GetString()) ? 1 : 2;
        var headers = new string[lastCol + 1];
        for (var c = 1; c <= lastCol; c++)
        {
            var h = firstData == 2 ? ws.Cell(1, c).GetString().Trim() : "";
            headers[c] = h.Length == 0 || headers.Take(c).Contains(h) ? ColumnLetter(c) : h;
        }
        return new SheetLayout(linkCol, firstData, used.Where(r => r.RowNumber() >= firstData).ToList(), headers, lastCol);
    }

    /// <summary>Cột link: cột có NHIỀU ô tách được id SP nhất (<see cref="HarvestItemKey"/> ra <c>id:</c>) trong các dòng quét — hoà thì
    /// cột bên trái (một ô "Ghi chú" lẻ, cột "Link shop" đứng trước không cướp được cột link SP). Không cột nào có id SP → cột có
    /// nhiều ô link Shopee nhất; vẫn không có → cột A.</summary>
    private static int PickLinkColumn(List<IXLRow> rows, int lastCol)
    {
        int Best(Func<string, bool> counts)
        {
            int best = 0, bestCount = 0;
            for (var c = 1; c <= lastCol; c++)
            {
                var n = rows.Count(r => counts(r.Cell(c).GetString()));
                if (n > bestCount) { best = c; bestCount = n; }
            }
            return best;
        }
        var byId = Best(IsProductLink);
        if (byId > 0) return byId;
        var byShopee = Best(LooksLikeShopeeLink);
        return byShopee > 0 ? byShopee : 1;
    }

    /// <summary>Ô là link Shopee tách được id SP (khoá <c>id:</c>).</summary>
    private static bool IsProductLink(string? value) =>
        LooksLikeLink(value) && (HarvestItemKey.FromLink(NormalizeLink(value))?.StartsWith("id:", StringComparison.Ordinal) ?? false);

    /// <summary>Ô trông như link Shopee: không khoảng trắng, host Shopee (<see cref="HarvestLinks.IsShopeeHost"/>), VÀ có scheme
    /// <c>http(s)://</c> / <c>www.</c> hoặc có path (<c>shopee.vn/…</c>). Tên miền trơn ("Shopee.vn", "Shopee.Link" — hay gặp ở tiêu
    /// đề) và chữ có "Shopee" trong tên SP không tính.</summary>
    public static bool LooksLikeShopeeLink(string? value)
    {
        var t = (value ?? "").Trim();
        if (t.Length == 0 || t.Any(char.IsWhiteSpace)) return false;
        var hasScheme = t.Contains("://", StringComparison.Ordinal);
        var withScheme = hasScheme ? t : "https://" + t;
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return false;
        if (!HarvestLinks.IsShopeeHost(uri.Host)) return false;
        return hasScheme || t.StartsWith("www.", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.Trim('/').Length > 0
               || uri.Query.Length > 1;
    }

    /// <summary>Ô cột link là một link (dữ liệu, không phải tiêu đề / ghi chú): link Shopee, hoặc chuỗi không khoảng trắng bắt đầu
    /// bằng http(s):// / www.</summary>
    private static bool LooksLikeLink(string? value)
    {
        var t = (value ?? "").Trim();
        if (t.Length == 0 || t.Any(char.IsWhiteSpace)) return false;
        return LooksLikeShopeeLink(t) || t.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("www.", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLink(string? value)
    {
        var t = (value ?? "").Trim();
        if (t.Length == 0) return "";
        if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return t;
        if (t.StartsWith("www.", StringComparison.OrdinalIgnoreCase) || LooksLikeShopeeLink(t)) return "https://" + t;
        return t;
    }

    private static IXLWorksheet GetSheet(XLWorkbook wb, string sheet) =>
        wb.TryGetWorksheet(sheet, out var ws) ? ws : throw new InvalidOperationException($"Không tìm thấy sheet \"{sheet}\".");

    private static string ColumnLetter(int index)
    {
        var s = "";
        for (var n = index; n > 0; n = (n - 1) / 26) s = (char)('A' + (n - 1) % 26) + s;
        return s;
    }
}
