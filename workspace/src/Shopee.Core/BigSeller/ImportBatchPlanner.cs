namespace Shopee.Core.BigSeller;

/// <summary>
/// Tiến độ import của MỘT shop: khoá tiến độ (ProgressKey), tên shop để tick trong hộp chọn shop, tập item id của sheet
/// và tập id đã import. Thuần dữ liệu để test được; runner nạp tập id từ sheet, seed <see cref="SeedDone"/> từ tiến độ
/// đã lưu và gọi <see cref="MarkDone"/> sau mỗi lô thành công.
/// </summary>
public sealed class ImportShopState
{
    private readonly HashSet<string> _done = new(StringComparer.Ordinal);

    /// <summary>Khoá tiến độ Import của shop (ProgressKey).</summary>
    public string Key { get; }
    /// <summary>Tên shop dùng để chọn trong hộp "Import to Draft Listing".</summary>
    public string ShopName { get; }
    /// <summary>Item id của sheet shop (khoảng dòng đã đặt).</summary>
    public IReadOnlySet<string> ImportIds { get; }

    public ImportShopState(string key, string shopName, IEnumerable<string> importIds)
    {
        Key = key;
        ShopName = shopName;
        ImportIds = new HashSet<string>(importIds, StringComparer.Ordinal);
    }

    public int DoneCount => _done.Count;

    /// <summary>Đủ khi sheet có ít nhất 1 id và mọi id đã import. Sheet không có id nào thì không coi là xong.</summary>
    public bool IsComplete => ImportIds.Count > 0 && _done.Count >= ImportIds.Count;

    /// <summary>Shop còn cần SP đó: có trong sheet và chưa import.</summary>
    public bool Needs(string id) => ImportIds.Contains(id) && !_done.Contains(id);

    /// <summary>Thay tập đã import bằng phần giao của <paramref name="savedDoneIds"/> với <see cref="ImportIds"/> (id ngoài sheet bỏ qua,
    /// gọi lại không cộng dồn). Trả về số id được seed.</summary>
    public int SeedDone(IEnumerable<string> savedDoneIds)
    {
        _done.Clear();
        foreach (var id in savedDoneIds)
            if (ImportIds.Contains(id)) _done.Add(id);
        return _done.Count;
    }

    /// <summary>Đánh dấu đã import (chỉ id thuộc sheet của shop).</summary>
    public void MarkDone(IEnumerable<string> ids)
    {
        foreach (var id in ids)
            if (ImportIds.Contains(id)) _done.Add(id);
    }
}

/// <summary>
/// Phán quyết tick trên Crawl List trước khi bấm Import (1 shop / lần).
/// </summary>
public static class ImportBatchPlanner
{
    /// <summary>Các dòng ĐANG TICK trên Crawl List có đúng là lô sắp import không: số dòng tick (<paramref name="tickedRowCount"/>, kể cả dòng không nhận ra được id)
    /// phải bằng số id của lô, và tập id nhận ra được phải bằng đúng tập id của lô (không thiếu, không dư, không trùng). Lệch → KHÔNG được bấm Import.</summary>
    public static bool TickedMatchesBatch(IReadOnlyCollection<string> batchIds, IReadOnlyCollection<string> tickedIds, int tickedRowCount)
    {
        var expected = new HashSet<string>(batchIds, StringComparer.Ordinal);
        var ticked = new HashSet<string>(tickedIds, StringComparer.Ordinal);
        return tickedRowCount == expected.Count && tickedIds.Count == ticked.Count && ticked.SetEquals(expected);
    }

    /// <summary>Mirror tín hiệu checked của checkbox hàng vxe (JS <c>isChecked</c> trong ImportToStoreRunner):
    /// <paramref name="inputChecked"/>, <c>aria-checked=true</c>, hoặc class chứa <c>checkbox-checked</c> / <c>is--checked</c>
    /// (gồm cả <c>vxe-icon-checkbox-checked</c>). Không dùng nhãn ô <c>.vxe-cell--label</c> làm id.</summary>
    public static bool VxeCheckboxLooksChecked(bool inputChecked, string? classNames, string? ariaChecked = null)
    {
        if (inputChecked) return true;
        if (string.Equals(ariaChecked, "true", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.IsNullOrEmpty(classNames)) return false;
        return classNames.Contains("checkbox-checked", StringComparison.Ordinal)
            || classNames.Contains("is--checked", StringComparison.Ordinal);
    }
}
