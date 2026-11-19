using Shopee.Core.Harvest;

namespace Shopee.Core.MassUpload.Export;

/// <summary>Một dòng nguồn có link SP tách được itemId. <see cref="ExcelRow"/> = số dòng Excel thật (như user thấy).</summary>
public sealed record ExportSourceRow(int ExcelRow, string Link, long ItemId);

/// <summary>Một dòng nguồn có link mà không tách được itemId (link shop, link rút gọn chưa mở…).</summary>
public sealed record ExportSourceLink(int ExcelRow, string Link);

/// <summary>Nguồn của một lượt xuất: các SP (đã khử trùng theo itemId, giữ dòng đầu tiên), các link không tách được id, số dòng bị
/// gộp vì trùng SP, số dòng dữ liệu trong dải.</summary>
public sealed record ExportSource(IReadOnlyList<ExportSourceRow> Items, IReadOnlyList<ExportSourceLink> NoItemId, int DuplicateRows,
    int RowsInRange);

/// <summary>
/// Đọc nguồn của lượt xuất (plan GĐ8 mục 2) theo ĐÚNG luật của app Harvest: <see cref="HarvestWorkbookReader.ReadSheetAsync"/> (cột
/// link tự dò, tiêu đề tự nhận, chia sẻ đọc-ghi với Excel đang mở, chờ khoá của app Update) → <see cref="HarvestRowRange.Restrict"/>
/// (dải "Từ dòng / Đến dòng" là số dòng Excel thật, Đến = 0 là tới hết) → <see cref="HarvestItemKey.FromLink"/> +
/// <see cref="HarvestSource.ItemIdOfKey"/> lấy itemId. Không đọc Hub ở đây.
/// </summary>
public static class ExportSourceReader
{
    public static async Task<ExportSource> ReadAsync(string workbookPath, string sheet, int fromRow, int toRow, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(workbookPath)) throw new InvalidOperationException("Chưa chọn file Excel nguồn.");
        if (!File.Exists(workbookPath)) throw new InvalidOperationException($"Không thấy file Excel nguồn: {workbookPath}");
        if (string.IsNullOrWhiteSpace(sheet)) throw new InvalidOperationException("Chưa chọn sheet.");
        if (!HarvestRowRange.IsValid(HarvestRowRange.ClampFrom(fromRow, 1), HarvestRowRange.ClampTo(toRow)))
            throw new InvalidOperationException($"Dải dòng sai: đến dòng {toRow} < từ dòng {fromRow}.");
        var range = await HarvestWorkbookReader.ReadSheetAsync(workbookPath, sheet.Trim(), ct).ConfigureAwait(false);
        return FromRange(HarvestRowRange.Restrict(range, fromRow, toRow));
    }

    /// <summary>Dải đã thu gọn → nguồn (hàm thuần).</summary>
    public static ExportSource FromRange(HarvestWorkbookRange range)
    {
        var items = new List<ExportSourceRow>();
        var noId = new List<ExportSourceLink>();
        var seen = new HashSet<long>();
        var duplicates = 0;
        foreach (var link in range.Links?.Items ?? [])
        {
            var excelRow = range.ExcelRows is { } rows && link.RowNumber - 1 < rows.Count
                ? rows[link.RowNumber - 1]
                : link.RowNumber + range.FirstDataRow - 1;
            if (HarvestSource.ItemIdOfKey(HarvestItemKey.FromLink(link.Link)) is not long id)
            {
                noId.Add(new ExportSourceLink(excelRow, link.Link));
                continue;
            }
            if (!seen.Add(id)) { duplicates++; continue; }
            items.Add(new ExportSourceRow(excelRow, link.Link, id));
        }
        return new ExportSource(items, noId, duplicates, Math.Max(0, range.DenseEnd - range.DenseStart + 1));
    }
}
