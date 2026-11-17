using Shopee.Core.Scrape;

namespace Shopee.Core.Harvest;

/// <summary>Kết quả dựng lại tiến độ Harvest cho một dải dòng nén (chỉ số dòng nén 1-based như
/// <see cref="ScrapeWorkbook.FetchLinks(string, string, int, int)"/> và kho tiến độ):
/// <see cref="Completed"/> = vùng không phải lấy nữa (đã lấy + bỏ qua vì lỗi vĩnh viễn + dòng không có gì để lấy),
/// <see cref="Skipped"/> = các dòng bỏ qua vì SP đã ghi lỗi vĩnh viễn (nằm trong <see cref="Completed"/>, như sổ bỏ qua của
/// kho tiến độ), <see cref="Remaining"/> = các đoạn còn phải lấy.</summary>
public sealed record HarvestDoneRebuildResult(
    List<RowRange> Completed, List<(int from, int to)> Remaining, List<int> Skipped, int DoneRows, int TodoRows);

/// <summary>
/// Tiến độ Harvest dựng lại TỪ TẬP SP ĐÃ LẤY XONG (+ tập lỗi vĩnh viễn) — không tin số dòng của lượt trước: workbook có bị chèn /
/// xoá / đổi thứ tự / gộp lại / đổi nguồn thì dòng nào có SP đã lấy vẫn là xong, dòng nào chưa thì còn phải lấy. Hàm thuần.
/// </summary>
public static class HarvestDoneRebuild
{
    /// <param name="from">Dòng nén đầu của dải (≥ 1).</param>
    /// <param name="to">Dòng nén cuối của dải (&lt; from = dải rỗng).</param>
    /// <param name="items">Các dòng HỢP LỆ trong dải (có tên SP + link — đúng <see cref="ScrapeLinkResult.Items"/>). Dòng trong
    /// dải mà không có ở đây (thiếu tên / thiếu link) giữ hành vi cũ của engine: không có gì để lấy → tính là xong.</param>
    /// <param name="done">Tập khoá SP đã lưu xong của target (<see cref="HarvestItemKey"/>).</param>
    /// <param name="failed">Tập khoá SP đã ghi lỗi VĨNH VIỄN của target (SP không tồn tại, không phải trang SP, JSON bị từ chối
    /// vì nội dung): không lên lịch lại, đưa vào sổ bỏ qua. null = không có.</param>
    public static HarvestDoneRebuildResult Build(int from, int to, IEnumerable<ScrapeLinkItem> items, IReadOnlySet<string> done,
        IReadOnlySet<string>? failed = null)
    {
        var todo = new SortedSet<int>();
        var skipped = new SortedSet<int>();
        foreach (var it in items)
        {
            if (it.RowNumber < from || it.RowNumber > to) continue;
            var key = HarvestItemKey.FromLink(it.Link);
            if (key is not null && done.Contains(key)) continue;
            if (key is not null && failed is not null && failed.Contains(key)) skipped.Add(it.RowNumber);
            else todo.Add(it.RowNumber);
        }

        var completed = new List<RowRange>();
        var remaining = new List<(int from, int to)>();
        int? doneStart = null, todoStart = null;
        for (var r = from; r <= to; r++)
        {
            if (todo.Contains(r))
            {
                if (doneStart is { } ds) { completed.Add(new RowRange { From = ds, To = r - 1 }); doneStart = null; }
                todoStart ??= r;
            }
            else
            {
                if (todoStart is { } ts) { remaining.Add((ts, r - 1)); todoStart = null; }
                doneStart ??= r;
            }
        }
        if (doneStart is { } d) completed.Add(new RowRange { From = d, To = to });
        if (todoStart is { } t) remaining.Add((t, to));
        var total = Math.Max(0, to - from + 1);
        return new HarvestDoneRebuildResult(completed, remaining, [.. skipped], total - todo.Count - skipped.Count, todo.Count);
    }
}
