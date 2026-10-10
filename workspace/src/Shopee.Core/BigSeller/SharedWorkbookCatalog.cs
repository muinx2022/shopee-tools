namespace Shopee.Core.BigSeller;

/// <summary>File và sheet một job thực sự đọc, cùng khóa tiến độ riêng của shop.</summary>
public readonly record struct SharedWorkbookTarget(string DataPath, string DataSheet, string ProgressKey);

/// <summary>
/// Khi workbook chung tắt: đọc workbook và sheet của từng shop, tiến độ theo tên sheet.
/// Khi bật: mọi shop đọc file gộp, sheet <c>Gop</c>. Tiến độ import/update khóa <c>shared:{shopId}</c>.
/// Tiến độ scrape khóa <c>acc:{accountId}:shared:{shopId}</c> — mỗi tài khoản một con trỏ, không dùng chung.
/// </summary>
public static class SharedWorkbookCatalog
{
    public const string ProgressPrefix = "shared:";

    public static SharedWorkbookTarget Resolve(bool enabled, string mergedPath, BigSellerAccount account, BigSellerShop shop)
    {
        if (!enabled)
        {
            var sheet = shop.ShopeeDataSheet ?? "";
            return new SharedWorkbookTarget(account.WorkbookPath ?? "", sheet, sheet);
        }
        return new SharedWorkbookTarget(mergedPath ?? "", SharedWorkbookMerge.SheetName, ProgressPrefix + (shop.Id ?? ""));
    }

    /// <summary>Khóa tiến độ scrape. Workbook chung: kèm id tài khoản để acc này không đọc con trỏ của acc kia.</summary>
    public static string ScrapeProgressKey(bool sharedWorkbook, string? accountId, string progressKey)
    {
        if (!sharedWorkbook) return progressKey ?? "";
        return "acc:" + (accountId ?? "") + ":" + (progressKey ?? "");
    }

    public static bool IsSharedKey(string? sheet) =>
        !string.IsNullOrEmpty(sheet)
        && (sheet.StartsWith(ProgressPrefix, StringComparison.Ordinal)
            || sheet.Contains(":" + ProgressPrefix, StringComparison.Ordinal));

    /// <summary>Id shop nằm sau <c>shared:</c>, hoặc null nếu đây là tên sheet riêng.</summary>
    public static string? ShopIdFromKey(string? sheet)
    {
        if (!IsSharedKey(sheet)) return null;
        var marker = sheet!.LastIndexOf(ProgressPrefix, StringComparison.Ordinal);
        var id = sheet[(marker + ProgressPrefix.Length)..];
        return string.IsNullOrEmpty(id) ? null : id;
    }
}
