using ClosedXML.Excel;

namespace Shopee.Core.Scrape;

/// <summary>Một dòng link để scrape, đọc từ workbook.</summary>
public sealed record ScrapeLinkItem(int RowNumber, string Link, Dictionary<string, string> Values);

/// <summary>Kết quả đọc link từ một sheet (gồm các dòng hợp lệ + thống kê dòng bỏ qua).</summary>
public sealed record ScrapeLinkResult(
    List<ScrapeLinkItem> Items, int TotalDataRows, int SkippedMissingProductName, int SkippedMissingLink);

/// <summary>
/// Đọc workbook Excel native bằng ClosedXML — THAY cho API Python (/sheets, /data). Tái hiện đúng
/// ngữ nghĩa cũ: dòng 1 = header; start/end đánh chỉ số theo dòng DỮ LIỆU (1-based, sau header);
/// link = cột A; dòng hợp lệ phải có tên sản phẩm ở cột F (cột thứ 6). Mở read-only +
/// FileShare.ReadWrite để không khóa file khi người dùng đang mở bằng Excel.
/// </summary>
public static class ScrapeWorkbook
{
    /// <summary>Tổng số dòng dữ liệu (sau header) của sheet — chỉ đếm dòng CÓ THẬT (bỏ header + bỏ
    /// dòng trống bị Excel lược), khớp <c>len(data_rows)</c> của API Python cũ.</summary>
    public static int TotalDataRows(string workbookPath, string sheet)
    {
        using var wb = Open(workbookPath);
        var ws = GetSheet(wb, sheet);
        return DataRowNumbers(ws).Count;
    }

    /// <summary>
    /// Đổi khoảng "Từ dòng / Đến dòng" (số dòng Excel, dòng 1 là header, mặc định bắt đầu ở 2)
    /// sang chỉ số sản phẩm đã nén mà <see cref="FetchLinks"/> và tiến độ scrape dùng (1 = sản phẩm đầu).
    /// <paramref name="excelEnd"/> &lt;= 0 nghĩa là đến sản phẩm cuối.
    /// Trả về (denseStart, denseEnd). denseEnd &lt; denseStart = không còn dòng nào trong khoảng.
    /// </summary>
    public static (int denseStart, int denseEnd) ExcelRowsToDenseRange(
        string workbookPath, string sheet, int excelStart, int excelEnd)
    {
        using var wb = Open(workbookPath);
        var ws = GetSheet(wb, sheet);
        var rows = DataRowNumbers(ws);
        if (rows.Count == 0) return (1, 0);

        var startExcel = Math.Max(2, excelStart);
        var startAt = rows.FindIndex(n => n >= startExcel);
        if (startAt < 0) return (rows.Count + 1, rows.Count);

        var denseStart = startAt + 1;
        if (excelEnd <= 0) return (denseStart, rows.Count);

        var last = -1;
        for (var i = 0; i < rows.Count; i++)
            if (rows[i] <= excelEnd) last = i;
        var denseEnd = last < 0 ? 0 : last + 1;
        return (denseStart, denseEnd);
    }

    private static List<int> DataRowNumbers(IXLWorksheet ws) =>
        ws.RowsUsed().Where(r => r.RowNumber() > 1).Select(r => r.RowNumber()).OrderBy(n => n).ToList();

    /// <summary>
    /// Lấy các link cần scrape trong khoảng [startRow, endRow] (chỉ số theo dòng DỮ LIỆU, 1-based).
    /// Tái hiện đúng API Python: danh sách dòng dữ liệu được NÉN (chỉ dòng có thật, bỏ header + bỏ
    /// dòng trống Excel lược bỏ); RowNumber = vị trí thứ tự trong danh sách nén (KHÔNG phải số dòng
    /// tuyệt đối trên sheet) để khớp tiến độ/resume với app gốc.
    /// </summary>
    public static ScrapeLinkResult FetchLinks(string workbookPath, string sheet, int startRow, int endRow)
    {
        if (startRow < 1) throw new ArgumentException("startRow phải >= 1.");
        if (endRow < startRow) throw new ArgumentException("endRow phải >= startRow.");

        using var wb = Open(workbookPath);
        var ws = GetSheet(wb, sheet);

        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

        // Header: ô dòng 1 hoặc dùng chữ cái cột làm tên mặc định (giống API cũ).
        var headers = new string[lastCol + 1];
        for (var c = 1; c <= lastCol; c++)
        {
            var h = ws.Cell(1, c).GetString().Trim();
            headers[c] = string.IsNullOrEmpty(h) ? ColumnLetter(c) : h;
        }

        // Danh sách NÉN các dòng dữ liệu có thật (RowNumber > 1), theo thứ tự tăng dần.
        var dense = ws.RowsUsed().Where(r => r.RowNumber() > 1).OrderBy(r => r.RowNumber()).ToList();
        var total = dense.Count;

        var items = new List<ScrapeLinkItem>();
        var skipName = 0;
        var skipLink = 0;

        // Vị trí p (1-based) trong danh sách nén = RowNumber; lấy [startRow .. min(endRow,total)].
        var end = Math.Min(endRow, total);
        for (var p = startRow; p <= end; p++)
        {
            var row = dense[p - 1];
            var values = new Dictionary<string, string>();
            for (var c = 1; c <= lastCol; c++)
                values[headers[c]] = row.Cell(c).GetString();

            // Cột F (thứ 6) = tên sản phẩm; trống thì bỏ qua.
            var productName = lastCol >= 6 ? row.Cell(6).GetString().Trim() : "";
            if (string.IsNullOrWhiteSpace(productName)) { skipName++; continue; }

            // Cột A = link.
            var link = NormalizeLink(row.Cell(1).GetString());
            if (string.IsNullOrWhiteSpace(link)) { skipLink++; continue; }

            items.Add(new ScrapeLinkItem(p, link, values));
        }

        return new ScrapeLinkResult(items, total, skipName, skipLink);
    }

    private static XLWorkbook Open(string path)
    {
        // Không khóa file: mở qua FileStream chia sẻ đọc/ghi.
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return new XLWorkbook(fs);
    }

    private static IXLWorksheet GetSheet(XLWorkbook wb, string sheet)
    {
        if (wb.TryGetWorksheet(sheet, out var ws)) return ws;
        throw new InvalidOperationException($"Không tìm thấy sheet \"{sheet}\".");
    }

    private static string NormalizeLink(string? value)
    {
        var t = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(t)) return "";
        if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return t;
        if (t.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) return "https://" + t;
        return t;
    }

    private static string ColumnLetter(int index)
    {
        var s = "";
        while (index > 0) { index = Math.DivRem(index - 1, 26, out var r); s = (char)(65 + r) + s; }
        return s;
    }
}
