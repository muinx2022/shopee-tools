using Shopee.Core.Scrape;

namespace Shopee.Core.Harvest;

/// <summary>
/// Dải "Từ dòng / Đến dòng" của một sheet Harvest — số dòng Excel THẬT (như user thấy trong Excel), không phải dòng nén.
/// Từ dòng &lt; dòng dữ liệu đầu → kẹp về dòng dữ liệu đầu; Đến dòng = 0 → tới hết sheet. Màn quét (đếm) và lượt chạy cùng đi qua
/// <see cref="Restrict"/> nên số đếm và dòng được lên lịch luôn khớp nhau.
/// </summary>
public static class HarvestRowRange
{
    /// <summary>Từ dòng hợp lệ: &lt; dòng dữ liệu đầu (kể cả 0 / âm = mặc định) → dòng dữ liệu đầu.</summary>
    public static int ClampFrom(int fromRow, int firstDataRow) => Math.Max(fromRow, Math.Max(1, firstDataRow));

    /// <summary>Đến dòng hợp lệ: âm → 0 (tới hết).</summary>
    public static int ClampTo(int toRow) => Math.Max(0, toRow);

    /// <summary>Cặp đã kẹp có dùng được không: Đến dòng = 0 (tới hết) hoặc ≥ Từ dòng.</summary>
    public static bool IsValid(int fromRow, int toRow) => toRow == 0 || toRow >= fromRow;

    /// <summary>Chữ mô tả dải cho log / câu báo.</summary>
    public static string Describe(int fromRow, int toRow) => toRow == 0 ? $"từ dòng {fromRow} tới hết" : $"dòng {fromRow}–{toRow}";

    /// <summary>Thu <paramref name="range"/> (cả sheet, đọc từ <see cref="HarvestWorkbookReader"/>) về các dòng nén có số dòng Excel trong
    /// [<paramref name="fromRow"/> .. <paramref name="toRow"/>] (đã kẹp như <see cref="ClampFrom"/> / <see cref="ClampTo"/>; dòng trống
    /// giữa chừng không có dòng nén nên tự rơi ra). <see cref="HarvestWorkbookRange.Links"/> chỉ còn các dòng trong dải. Dải không trúng
    /// dòng nào → DenseEnd &lt; DenseStart (dải rỗng). Cặp không hợp lệ (Đến &lt; Từ) cũng ra dải rỗng — caller chặn từ lúc nhập.</summary>
    public static HarvestWorkbookRange Restrict(HarvestWorkbookRange range, int fromRow, int toRow)
    {
        var lo = ClampFrom(fromRow, range.FirstDataRow);
        var hi = ClampTo(toRow) == 0 ? int.MaxValue : toRow;
        var excel = ExcelRowMap(range);

        var start = range.DenseEnd + 1;
        for (var d = range.DenseStart; d <= range.DenseEnd; d++)
            if (excel(d) >= lo) { start = d; break; }
        var end = start - 1;
        for (var d = range.DenseEnd; d >= start; d--)
            if (excel(d) <= hi) { end = d; break; }

        ScrapeLinkResult? links = null;
        if (range.Links is { } all)
        {
            var items = all.Items.Where(i => i.RowNumber >= start && i.RowNumber <= end).ToList();
            var rowsInRange = Math.Max(0, end - start + 1);
            links = new ScrapeLinkResult(items, rowsInRange, 0, rowsInRange - items.Count);
        }
        return range with { DenseStart = start, DenseEnd = end, Links = links };
    }

    /// <summary>Dòng nén → số dòng Excel thật của sheet (theo <see cref="HarvestWorkbookRange.ExcelRows"/>, chụp MỘT lần — không đọc
    /// lại file) — để mọi chỗ HIỂN THỊ số dòng (log, overlay, bảng Tiến trình) khớp với số user nhập ở "Từ dòng / Đến dòng". Dòng nén
    /// ngoài bảng (vd "dòng kế" sau dòng cuối) → nối tiếp từ dòng Excel cuối; không có bảng → liền nhau từ dòng dữ liệu đầu.</summary>
    public static Func<int, int> ExcelRowMap(HarvestWorkbookRange range)
    {
        var rows = range.ExcelRows;
        var first = range.FirstDataRow;
        return dense =>
        {
            if (rows is null || rows.Count == 0 || dense < 1) return dense + first - 1;
            return dense <= rows.Count ? rows[dense - 1] : rows[^1] + (dense - rows.Count);
        };
    }
}
