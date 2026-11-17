using Shopee.Core.BigSeller;
using Shopee.Core.Scrape;
using Shopee.Workspace.Infrastructure;

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
        if (!await Dialogs.ConfirmAsync($"Xóa tiến độ {RoleText.ScrapeOp} của shop \"{targetName}\" ({accountName}) để chạy lại từ đầu?", "Reset tiến độ"))
            return false;

        Reset(accountId, sheet, shop, accountName, log);
        log?.Invoke($"[{accountName}] Đã reset tiến độ {RoleText.ScrapeOp} của shop \"{targetName}\".");
        return true;
    }

    public static void Reset(string accountId, string sheet, BigSellerShop? shop, string accountName, Action<string>? log = null)
    {
        ClearKey(accountId, sheet, log);
        // App local: không có Hub ledger để xoá.
    }

    /// <summary>ĐƯỜNG DUY NHẤT để người dùng xoá tiến độ một khoá (mọi nút reset ở Workspace / Thống kê / Auto Run đi qua đây):
    /// kho tiến độ theo vai trò + (app Harvest) tập SP đã lấy của đúng target. App Scrape: y như <c>Clear</c> cũ.</summary>
    public static void ClearKey(string accountId, string sheet, Action<string>? log = null)
    {
        RoleStores.ScrapeProgress.Clear(accountId, sheet);
        ClearHarvestDone(accountId, sheet, log);
    }

    /// <summary>App Harvest: tiến độ dựng lại từ tập SP đã lấy xong mỗi lần chạy → reset phải xoá CẢ tập của đúng target
    /// (tk + khoá tiến độ), không thì lượt sau dựng lại y như cũ. Vai trò khác: không làm gì.</summary>
    internal static int ClearHarvestDone(string accountId, string sheet, Action<string>? log = null)
    {
        if (!WorkspaceAppConfig.IsHarvest) return 0;
        if (HarvestRuntime.Current is not { } rt)
        {
            log?.Invoke("Chưa mở được kho lưu dữ liệu (harvest-outbox.db) — chưa xoá được danh sách SP đã lấy.");
            return 0;
        }
        var target = Shopee.Core.Harvest.HarvestDoneTarget.Of(accountId, sheet ?? "");
        rt.Outbox.ClearFailed(target);   // reset = lấy lại từ đầu, kể cả SP từng lỗi vĩnh viễn
        return rt.Outbox.ClearDone(target);
    }

    /// <summary>App Harvest, nút "Cào lại dòng đã bỏ": xoá tập SP lỗi vĩnh viễn của đúng target để lượt sau thử lại (dựng lại
    /// sẽ không đưa chúng vào sổ bỏ qua nữa). Vai trò khác: không làm gì. Trả số khoá đã xoá.</summary>
    internal static int ClearHarvestFailed(string accountId, string sheet)
    {
        if (!WorkspaceAppConfig.IsHarvest || HarvestRuntime.Current is not { } rt) return 0;
        return rt.Outbox.ClearFailed(Shopee.Core.Harvest.HarvestDoneTarget.Of(accountId, sheet ?? ""));
    }

    /// <summary>Xoá tiến độ local khi chỉ biết sheet.</summary>
    public static void Reset(string accountId, string sheet, Action<string>? log = null)
    {
        var account = BigSellerStore.Shared.Find(accountId);
        var shops = account?.Shops.Where(s =>
            string.Equals(s.ShopeeDataSheet ?? "", sheet ?? "", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s.Name ?? "", sheet ?? "", StringComparison.OrdinalIgnoreCase)
            || string.Equals(SharedWorkbookStore.Resolve(account, s).ProgressKey, sheet ?? "", StringComparison.OrdinalIgnoreCase)).ToList() ?? [];
        if (shops.Count == 0)
        {
            ClearKey(accountId, sheet ?? "", log);
            return;
        }
        foreach (var _ in shops)
            ClearKey(accountId, sheet ?? "", log);
    }
}
