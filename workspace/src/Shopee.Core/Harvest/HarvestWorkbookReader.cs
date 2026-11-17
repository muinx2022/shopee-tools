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

/// <summary>Dải dòng của một lượt Harvest đọc từ MỘT bản workbook: chỉ số dòng nén (như
/// <see cref="ScrapeWorkbook.ExcelRowsToDenseRange(string, string, int, int)"/>) + các dòng hợp lệ trong dải.</summary>
public sealed record HarvestWorkbookRange(int DenseStart, int DenseEnd, ScrapeLinkResult? Links);

/// <summary>
/// Đọc workbook cho app Harvest (CHỈ Harvest dùng — không đụng các bộ đọc dùng chung của Scrape/Update):
/// <list type="bullet">
/// <item>mở file chia sẻ đọc-GHI (Excel đang mở file vẫn đọc được), chép toàn bộ byte vào RAM rồi đóng handle NGAY — không
/// giữ file trong lúc parse (vài giây với file lớn), để <c>wb.Save()</c> của app Update không trúng khe;</item>
/// <item>tôn trọng khoá sidecar <c>&lt;file&gt;.lock</c> của app Update: đang bị giữ → chờ bằng <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
/// (huỷ được) tới <see cref="LockWait"/>, quá hạn → <see cref="HarvestWorkbookLockedException"/>;</item>
/// <item>đọc link / số dòng bằng CHÍNH quy tắc của <see cref="ScrapeWorkbook"/> (overload nhận <see cref="XLWorkbook"/>) trên bản
/// trong RAM — kết quả giống hệt <see cref="ScrapeWorkbook.FetchLinks(string, string, int, int)"/>.</item>
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
        WithWorkbookAsync(workbookPath, wb => ScrapeWorkbook.TotalDataRows(wb, sheet), cancellationToken);

    public static Task<ScrapeLinkResult> FetchLinksAsync(string workbookPath, string sheet, int startRow, int endRow,
        CancellationToken cancellationToken)
    {
        if (startRow < 1) throw new ArgumentException("startRow phải >= 1.");
        if (endRow < startRow) throw new ArgumentException("endRow phải >= startRow.");
        return WithWorkbookAsync(workbookPath, wb => ScrapeWorkbook.FetchLinks(wb, sheet, startRow, endRow), cancellationToken);
    }

    /// <summary>Đầu lượt chạy: MỘT lần đọc file cho cả dải dòng nén (từ khoảng Excel) lẫn các dòng hợp lệ trong dải — dựng lại
    /// tiến độ trên đúng bản workbook đã tính dải. Dải rỗng → <see cref="HarvestWorkbookRange.Links"/> null.</summary>
    public static Task<HarvestWorkbookRange> ReadRangeAsync(string workbookPath, string sheet, int excelStart, int excelEnd,
        CancellationToken cancellationToken) =>
        WithWorkbookAsync(workbookPath, wb =>
        {
            var (start, end) = ScrapeWorkbook.ExcelRowsToDenseRange(wb, sheet, excelStart, excelEnd);
            return new HarvestWorkbookRange(start, end, end >= start ? ScrapeWorkbook.FetchLinks(wb, sheet, start, end) : null);
        }, cancellationToken);
}
