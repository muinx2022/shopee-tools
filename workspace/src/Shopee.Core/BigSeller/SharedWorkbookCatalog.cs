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

    /// <summary>
    /// Giải quyết file &amp; sheet cho luồng Update Product: đến shop nào thì chọn đúng sheet của shop đó
    /// (<see cref="BigSellerShop.ShopeeDataSheet"/> hoặc <see cref="BigSellerShop.Name"/>) trong file đã chọn.
    /// </summary>
    public static SharedWorkbookTarget ResolveForUpdate(bool enabled, string sourcePath, string mergedPath, BigSellerAccount account, BigSellerShop shop)
    {
        var defaultSheet = !string.IsNullOrWhiteSpace(shop.ShopeeDataSheet) ? shop.ShopeeDataSheet.Trim() : (shop.Name?.Trim() ?? "");
        if (enabled)
        {
            var progressKey = ProgressPrefix + (shop.Id ?? "");
            if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
            {
                var matched = MatchShopSheet(sourcePath, shop);
                if (!string.IsNullOrWhiteSpace(matched))
                    return new SharedWorkbookTarget(sourcePath, matched, progressKey);
                if (!string.IsNullOrWhiteSpace(mergedPath) && File.Exists(mergedPath))
                    return new SharedWorkbookTarget(mergedPath, SharedWorkbookMerge.SheetName, progressKey);
                return new SharedWorkbookTarget(sourcePath, defaultSheet, progressKey);
            }
            return new SharedWorkbookTarget(mergedPath ?? "", SharedWorkbookMerge.SheetName, progressKey);
        }

        string wbPath;
        if (!string.IsNullOrWhiteSpace(account.WorkbookPath) && File.Exists(account.WorkbookPath))
            wbPath = account.WorkbookPath;
        else if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
            wbPath = sourcePath;
        else
            wbPath = account.WorkbookPath ?? "";

        var sheet = !string.IsNullOrWhiteSpace(wbPath) && File.Exists(wbPath)
            ? (MatchShopSheet(wbPath, shop) ?? defaultSheet)
            : defaultSheet;
        return new SharedWorkbookTarget(wbPath, sheet, sheet);
    }

    private static string? MatchShopSheet(string workbookPath, BigSellerShop shop)
    {
        var sheets = WorkbookSheets.ListSheetNames(workbookPath);
        if (sheets.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(shop.ShopeeDataSheet))
        {
            var target = shop.ShopeeDataSheet.Trim();
            var match = sheets.FirstOrDefault(x => string.Equals(x.Trim(), target, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        if (!string.IsNullOrWhiteSpace(shop.Name))
        {
            var target = shop.Name.Trim();
            var match = sheets.FirstOrDefault(x => string.Equals(x.Trim(), target, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        return null;
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
