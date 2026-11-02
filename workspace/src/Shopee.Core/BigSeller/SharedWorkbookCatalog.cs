using System.Text.RegularExpressions;

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
    /// Giải quyết file &amp; sheet cho luồng Scrape / Import / Update Product: CHỈ đọc file nguồn user đã chọn
    /// (<paramref name="sourcePath"/>), chọn đúng sheet của shop (<see cref="BigSellerShop.Name"/> hoặc <see cref="BigSellerShop.ShopeeDataSheet"/>).
    /// Không còn fallback sang file riêng của tài khoản hay file mặc định. File nguồn thiếu: nếu workbook gộp đang bật
    /// và file gộp còn thì trả file gộp, ngược lại trả DataPath rỗng để caller báo lỗi.
    /// </summary>
    public static SharedWorkbookTarget ResolveForUpdate(bool enabled, string sourcePath, string mergedPath, BigSellerAccount account, BigSellerShop shop)
    {
        var defaultSheet = !string.IsNullOrWhiteSpace(shop.Name) ? shop.Name.Trim() : (shop.ShopeeDataSheet?.Trim() ?? "");

        var chosenPath = sourcePath;

        if (!string.IsNullOrWhiteSpace(chosenPath) && File.Exists(chosenPath))
        {
            var matched = MatchShopSheet(chosenPath, shop);
            if (!string.IsNullOrWhiteSpace(matched))
            {
                var progressKey = enabled ? ProgressPrefix + (shop.Id ?? "") : matched;
                return new SharedWorkbookTarget(chosenPath, matched, progressKey);
            }

            if (enabled && !string.IsNullOrWhiteSpace(mergedPath) && File.Exists(mergedPath))
                return new SharedWorkbookTarget(mergedPath, SharedWorkbookMerge.SheetName, ProgressPrefix + (shop.Id ?? ""));

            var fallbackKey = enabled ? ProgressPrefix + (shop.Id ?? "") : defaultSheet;
            return new SharedWorkbookTarget(chosenPath, defaultSheet, fallbackKey);
        }

        // File nguồn thiếu: chỉ file gộp (hành động chủ động của user) mới được thay thế
        if (enabled && !string.IsNullOrWhiteSpace(mergedPath) && File.Exists(mergedPath))
            return new SharedWorkbookTarget(mergedPath, SharedWorkbookMerge.SheetName, ProgressPrefix + (shop.Id ?? ""));

        return new SharedWorkbookTarget("", defaultSheet, defaultSheet);
    }

    public static string NormalizeSheetKey(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var trimmed = s.Trim();
        trimmed = Regex.Replace(trimmed, @"\s*\.\s*", ".");
        trimmed = Regex.Replace(trimmed, @"\s+", ".");
        return trimmed.ToLowerInvariant();
    }

    public static string? MatchShopSheet(string workbookPath, BigSellerShop shop)
    {
        if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath)) return null;
        return MatchShopSheet(WorkbookSheets.ListSheetNames(workbookPath), shop);
    }

    public static string? MatchShopSheet(IReadOnlyList<string> sheets, BigSellerShop shop)
    {
        if (sheets == null || sheets.Count == 0) return null;

        // 1. Khớp chính xác (case-insensitive, trim) với shop.Name
        if (!string.IsNullOrWhiteSpace(shop.Name))
        {
            var target = shop.Name.Trim();
            var match = sheets.FirstOrDefault(x => string.Equals(x.Trim(), target, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        // 2. Khớp chuẩn hóa (NormalizeSheetKey) với shop.Name
        if (!string.IsNullOrWhiteSpace(shop.Name))
        {
            var targetNorm = NormalizeSheetKey(shop.Name);
            var match = sheets.FirstOrDefault(x => NormalizeSheetKey(x) == targetNorm);
            if (match is not null) return match;
        }

        // 3. Khớp chính xác (case-insensitive, trim) với shop.ShopeeDataSheet
        if (!string.IsNullOrWhiteSpace(shop.ShopeeDataSheet))
        {
            var target = shop.ShopeeDataSheet.Trim();
            var match = sheets.FirstOrDefault(x => string.Equals(x.Trim(), target, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        // 4. Khớp chuẩn hóa (NormalizeSheetKey) với shop.ShopeeDataSheet
        if (!string.IsNullOrWhiteSpace(shop.ShopeeDataSheet))
        {
            var targetNorm = NormalizeSheetKey(shop.ShopeeDataSheet);
            var match = sheets.FirstOrDefault(x => NormalizeSheetKey(x) == targetNorm);
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
