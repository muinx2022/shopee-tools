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

    /// <summary>Tên sheet người dùng thấy. Ưu tiên sheet thực tế đã resolve; nếu đang gộp sheet thì hiện danh sách sheet đã chọn.</summary>
    public static string SheetLabel(BigSellerShop? shop, string? resolvedSheet = null)
    {
        if (!string.IsNullOrWhiteSpace(resolvedSheet)
            && !string.Equals(resolvedSheet, SharedWorkbookMerge.SheetName, StringComparison.OrdinalIgnoreCase))
        {
            return resolvedSheet;
        }
        if (SharedWorkbookStore.IsActive)
        {
            var sheets = SharedWorkbookStore.Snapshot().SelectedSheets;
            if (sheets.Count > 0) return string.Join(", ", sheets);
        }
        if (!string.IsNullOrWhiteSpace(resolvedSheet))
            return resolvedSheet;
        if (shop is null) return "—";
        if (SharedWorkbookStore.HasSharedSource && !string.IsNullOrWhiteSpace(shop.Name))
            return shop.Name;
        return !string.IsNullOrWhiteSpace(shop.ShopeeDataSheet)
            ? shop.ShopeeDataSheet
            : (!string.IsNullOrWhiteSpace(shop.Name) ? shop.Name : "—");
    }

    private static string Key(string accountId, string shopId) => (accountId ?? "") + "\n" + (shopId ?? "");
}
