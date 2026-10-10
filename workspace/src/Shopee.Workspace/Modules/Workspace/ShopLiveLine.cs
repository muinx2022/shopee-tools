using Shopee.Core.BigSeller;
using Shopee.Workspace.Services;

namespace Shopee.Workspace.Modules.Workspace;

/// <summary>Dòng trạng thái đang chạy của đúng một shop. Shop khác đọc ra chuỗi rỗng.</summary>
public static class ShopLiveLine
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, (string Display, string Tooltip)> Lines = new(StringComparer.Ordinal);

    public static event Action? Changed;

    public static void Show(string accountId, string shopId, string text, string? tooltip = null)
    {
        lock (Gate) Lines[Key(accountId, shopId)] = (text ?? "", tooltip ?? text ?? "");
        UiThread.Post(() => Changed?.Invoke());
    }

    public static void Clear(string accountId, string shopId)
    {
        bool removed;
        lock (Gate) removed = Lines.Remove(Key(accountId, shopId));
        if (removed) UiThread.Post(() => Changed?.Invoke());
    }

    public static string For(string accountId, string shopId)
    {
        lock (Gate) return Lines.TryGetValue(Key(accountId, shopId), out var v) ? v.Display : "";
    }

    public static string TooltipFor(string accountId, string shopId)
    {
        lock (Gate) return Lines.TryGetValue(Key(accountId, shopId), out var v) ? v.Tooltip : "";
    }

    /// <summary>Tên sheet người dùng thấy. Workbook chung thì là sheet đã chọn (ví dụ hàng sẵn), không phải tên sheet gộp.</summary>
    public static string SheetLabel(BigSellerShop? shop)
    {
        if (SharedWorkbookStore.IsActive)
        {
            var sheets = SharedWorkbookStore.Snapshot().SelectedSheets;
            if (sheets.Count > 0) return string.Join(", ", sheets);
        }
        return shop is null || string.IsNullOrWhiteSpace(shop.ShopeeDataSheet) ? "—" : shop.ShopeeDataSheet;
    }

    private static string Key(string accountId, string shopId) => (accountId ?? "") + "\n" + (shopId ?? "");
}
