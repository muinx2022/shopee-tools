using Shopee.Core.BigSeller;
using Shopee.Core.Scrape;

namespace Shopee.Workspace.Modules.Scrape;

/// <summary>
/// XOÁ TIẾN ĐỘ scrape của một (tk BigSeller + sheet) — một đường duy nhất cho cả nút "Xoá tiến độ" ở cửa sổ
/// Thống kê lẫn đường Reset (bấm Chạy) của <see cref="ScrapeViewModel"/>.
/// App local: chỉ xoá local, không có Hub ledger.
/// </summary>
internal static class ScrapeProgressReset
{
    /// <summary>Xoá tiến độ local khi đã biết chính xác shop.</summary>
    public static async Task<bool> ConfirmAndResetAsync(string accountId, string sheet, BigSellerShop? shop, string accountName, Action<string>? log = null)
    {
        var targetName = shop?.DisplayName ?? accountName;
        if (!await Dialogs.ConfirmAsync($"Xóa tiến độ Scrape của shop \"{targetName}\" ({accountName}) để chạy lại từ đầu?", "Reset tiến độ"))
            return false;

        Reset(accountId, sheet, shop, accountName, log);
        log?.Invoke($"[{accountName}] Đã reset tiến độ Scrape của shop \"{targetName}\".");
        return true;
    }

    public static void Reset(string accountId, string sheet, BigSellerShop? shop, string accountName, Action<string>? log = null)
    {
        ScrapeProgressStore.Shared.Clear(accountId, sheet);
        // App local: không có Hub ledger để xoá.
    }

    /// <summary>Xoá tiến độ local khi chỉ biết sheet.</summary>
    public static void Reset(string accountId, string sheet, Action<string>? log = null)
    {
        var account = BigSellerStore.Shared.Find(accountId);
        var shops = account?.Shops.Where(s =>
            string.Equals(s.ShopeeDataSheet ?? "", sheet ?? "", StringComparison.OrdinalIgnoreCase)).ToList() ?? [];
        if (shops.Count == 0)
        {
            ScrapeProgressStore.Shared.Clear(accountId, sheet ?? "");
            return;
        }
        foreach (var _ in shops)
            ScrapeProgressStore.Shared.Clear(accountId, sheet ?? "");
    }
}
