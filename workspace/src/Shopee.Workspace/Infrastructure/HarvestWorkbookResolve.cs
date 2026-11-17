using System.IO;
using Shopee.Core.BigSeller;
using Shopee.Core.Harvest;

namespace Shopee.Workspace.Infrastructure;

/// <summary>
/// App Harvest, lúc đăng ký job: chọn file + sheet như <see cref="SharedWorkbookCatalog.ResolveForUpdate"/> (PHẢI giữ khớp khi
/// đọc được — có test đối chiếu) nhưng KHÔNG rơi sang file gộp khi file nguồn đã chọn tạm không đọc được (Excel mở độc quyền,
/// app Update đang ghi, file hỏng / vắng): bản dùng chung đọc lỗi thì coi như "không có sheet của shop" → trả file gộp — dòng của
/// MỌI shop dưới khoá của một shop → Harvest lấy SP của shop khác gán cho shop này. Ở đây: chưa đọc được → null + lý do, job dừng.
/// Chỉ dùng file gộp khi ĐỌC ĐƯỢC danh sách sheet nguồn mà thật sự không có sheet khớp shop (hoặc chưa chọn file nguồn).
/// App Scrape / Update vẫn dùng <see cref="SharedWorkbookStore.Resolve"/>.
/// </summary>
public static class HarvestWorkbookResolve
{
    public const string SourceUnreadableMessage = "Không đọc được file workbook nguồn — đóng Excel (hoặc file đang mở độc quyền) rồi chạy lại";

    public static string SourceMissingMessage(string path) =>
        $"Không tìm thấy file workbook nguồn {path} — chọn lại file ở app Scrape";

    /// <summary>Đọc tên sheet (thay được trong test). NÉM khi không đọc được.</summary>
    internal static Func<string, IReadOnlyList<string>> ListSheets { get; set; } = HarvestWorkbookReader.ListSheetNames;

    public static SharedWorkbookTarget? Resolve(bool enabled, string configuredSourcePath, string mergedPath,
        BigSellerAccount account, BigSellerShop shop, out string? error)
    {
        error = null;
        var defaultSheet = !string.IsNullOrWhiteSpace(shop.Name) ? shop.Name.Trim() : (shop.ShopeeDataSheet?.Trim() ?? "");
        var prefix = SharedWorkbookCatalog.ProgressPrefix;
        var mergedOk = enabled && !string.IsNullOrWhiteSpace(mergedPath) && File.Exists(mergedPath);
        if (!string.IsNullOrWhiteSpace(configuredSourcePath))
        {
            if (!File.Exists(configuredSourcePath)) { error = SourceMissingMessage(configuredSourcePath); return null; }
            IReadOnlyList<string> sheets;
            try { sheets = ListSheets(configuredSourcePath); }
            catch (HarvestWorkbookLockedException ex) { error = ex.Message; return null; }
            catch { error = SourceUnreadableMessage; return null; }
            if (sheets.Count == 0) { error = SourceUnreadableMessage; return null; }
            var matched = SharedWorkbookCatalog.MatchShopSheet(sheets, shop);
            if (!string.IsNullOrWhiteSpace(matched))
                return new SharedWorkbookTarget(configuredSourcePath, matched, enabled ? prefix + (shop.Id ?? "") : matched);
            if (mergedOk)
                return new SharedWorkbookTarget(mergedPath, SharedWorkbookMerge.SheetName, prefix + (shop.Id ?? ""));
            return new SharedWorkbookTarget(configuredSourcePath, defaultSheet, enabled ? prefix + (shop.Id ?? "") : defaultSheet);
        }
        if (mergedOk)
            return new SharedWorkbookTarget(mergedPath, SharedWorkbookMerge.SheetName, prefix + (shop.Id ?? ""));
        return new SharedWorkbookTarget("", defaultSheet, defaultSheet);
    }

    /// <summary>Theo cấu hình workbook chung hiện hành (<see cref="SharedWorkbookStore.Current"/>): file nguồn user đã chọn
    /// — kể cả khi đang vắng (bản dùng chung khi đó coi như chưa chọn rồi trả file gộp).</summary>
    public static SharedWorkbookTarget? ResolveCurrent(BigSellerAccount account, BigSellerShop shop, out string? error)
    {
        var state = SharedWorkbookStore.Current;
        return Resolve(state.Enabled, state.SourceFilePath ?? "", SharedWorkbookStore.MergedPath, account, shop, out error);
    }
}
