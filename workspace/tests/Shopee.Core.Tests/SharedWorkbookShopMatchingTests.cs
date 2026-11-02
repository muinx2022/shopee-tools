using Shopee.Core.BigSeller;
using Shopee.Core.Infrastructure;

namespace Shopee.Core.Tests;

public sealed class SharedWorkbookShopMatchingTests
{
    private static readonly string[] Sample18Sheets =
    [
        "Linh QA",
        "shop9x.store",
        "alina99.store",
        "six6shop.store",
        "fflqam22bl",
        "pb7zqxypew",
        "wb5i4cqkgh",
        "Delica Store",
        "Hanily Store",
        "Minoa Store",
        "Bizly Store",
        "Cicily Store",
        "Sully Store",
        "mexico1.store",
        "zicazio.store",
        "babysak.store",
        "graby.store",
        "vtinho.store",
        "Donlise.store"
    ];

    [Theory]
    [InlineData("shop9x.store", "shop9x.store", "shop9x.store")]
    [InlineData("alina99.store", "alina99.store", "alina99.store")]
    [InlineData("six6shop.store", "six6shop.store", "six6shop.store")]
    [InlineData("fflqam22bl", "fflqam22bl", "fflqam22bl")]
    [InlineData("pb7zqxypew", "pb7zqxypew", "pb7zqxypew")]
    [InlineData("wb5i4cqkgh", "wb5i4cqkgh", "wb5i4cqkgh")]
    [InlineData("Delica Store", "Delica Store", "Delica Store")]
    [InlineData("Hanily Store", "Hanily Store", "Hanily Store")]
    [InlineData("Minoa Store", "Minoa Store", "Minoa Store")]
    [InlineData("Bizly Store", "Bizly Store", "Bizly Store")]
    [InlineData("Cicily Store", "Cicily Store", "Cicily Store")]
    [InlineData("Sully Store", "Sully Store", "Sully Store")]
    [InlineData("mexico1.store", "mexico1.store", "mexico1.store")]
    [InlineData("zicazio.store", "zicazio.store", "zicazio.store")]
    [InlineData("babysak.store", "babysak.store", "babysak.store")]
    [InlineData("graby.store", "graby.store", "graby.store")]
    [InlineData("vtinho.store", "vtinho.store", "vtinho.store")]
    [InlineData("Donlise.store", "Donlise.store", "Donlise.store")]
    public void MatchShopSheet_All18Shops_MatchesAccurately(string shopName, string dataSheet, string expectedSheet)
    {
        var shop = new BigSellerShop { Id = "s-" + shopName, Name = shopName, ShopeeDataSheet = dataSheet };
        var matched = SharedWorkbookCatalog.MatchShopSheet(Sample18Sheets, shop);
        Assert.Equal(expectedSheet, matched);
    }

    [Theory]
    [InlineData("Bizly Store", "bizly.store", "Bizly Store")]
    [InlineData("Cicily Store", "cicily.store", "Cicily Store")]
    [InlineData("Sully Store", "sully.store", "Sully Store")]
    [InlineData("bizly.store", "bizly.store", "Bizly Store")]
    public void MatchShopSheet_NormalizedStoreName_MatchesCorrectly(string shopName, string dataSheet, string expectedSheet)
    {
        var shop = new BigSellerShop { Id = "s-" + shopName, Name = shopName, ShopeeDataSheet = dataSheet };
        var matched = SharedWorkbookCatalog.MatchShopSheet(Sample18Sheets, shop);
        Assert.Equal(expectedSheet, matched);
    }

    [Fact]
    public void ResolveForUpdate_UuTienSourcePathHonAccountWorkbookPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "test-resolve-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var sharedFile = Path.Combine(tempDir, "shared.xlsx");
            var accountFile = Path.Combine(tempDir, "account.xlsx");
            File.WriteAllText(sharedFile, "mock");
            File.WriteAllText(accountFile, "mock");

            var account = new BigSellerAccount { Id = "acc1", WorkbookPath = accountFile };
            var shop = new BigSellerShop { Id = "shop1", Name = "Bizly Store", ShopeeDataSheet = "bizly.store" };

            // 1. Khi UseSharedSource / SourcePath tồn tại: LUÔN trỏ về sharedFile
            var targetNotEnabled = SharedWorkbookCatalog.ResolveForUpdate(enabled: false, sourcePath: sharedFile, mergedPath: "", account: account, shop: shop);
            Assert.Equal(sharedFile, targetNotEnabled.DataPath);
            Assert.Equal(targetNotEnabled.DataSheet, targetNotEnabled.ProgressKey);

            var targetEnabled = SharedWorkbookCatalog.ResolveForUpdate(enabled: true, sourcePath: sharedFile, mergedPath: "", account: account, shop: shop);
            Assert.Equal(sharedFile, targetEnabled.DataPath);
            Assert.Equal("shared:shop1", targetEnabled.ProgressKey);

            // 2. Khi sourcePath không tồn tại: KHÔNG fallback sang accountFile → DataPath rỗng
            var targetFallback = SharedWorkbookCatalog.ResolveForUpdate(enabled: false, sourcePath: "", mergedPath: "", account: account, shop: shop);
            Assert.Equal("", targetFallback.DataPath);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ResolveForUpdate_SourceThieuVaTat_DataPathRongDuAccountWorkbookPathCoThat()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "test-resolve-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var accountFile = Path.Combine(tempDir, "account.xlsx");
            File.WriteAllText(accountFile, "mock");
            var account = new BigSellerAccount { Id = "acc1", WorkbookPath = accountFile };
            var shop = new BigSellerShop { Id = "shop1", Name = "Bizly Store", ShopeeDataSheet = "bizly.store" };

            var missingSource = Path.Combine(tempDir, "khong-co.xlsx");
            foreach (var src in new[] { "", missingSource })
            {
                var t = SharedWorkbookCatalog.ResolveForUpdate(enabled: false, sourcePath: src, mergedPath: "", account: account, shop: shop);
                Assert.Equal("", t.DataPath);
                Assert.Equal("Bizly Store", t.DataSheet);
            }

            // Bật nhưng file gộp cũng không còn → vẫn rỗng, không đọc file riêng của tài khoản
            var enabledNoMerged = SharedWorkbookCatalog.ResolveForUpdate(enabled: true, sourcePath: missingSource, mergedPath: Path.Combine(tempDir, "gop.xlsx"), account: account, shop: shop);
            Assert.Equal("", enabledNoMerged.DataPath);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ResolveForUpdate_SourceThieuVaBatVaFileGopCon_TraFileGop()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "test-resolve-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var mergedFile = Path.Combine(tempDir, "gop.xlsx");
            var accountFile = Path.Combine(tempDir, "account.xlsx");
            File.WriteAllText(mergedFile, "mock");
            File.WriteAllText(accountFile, "mock");
            var account = new BigSellerAccount { Id = "acc1", WorkbookPath = accountFile };
            var shop = new BigSellerShop { Id = "shop1", Name = "Bizly Store", ShopeeDataSheet = "bizly.store" };

            var t = SharedWorkbookCatalog.ResolveForUpdate(enabled: true, sourcePath: Path.Combine(tempDir, "khong-co.xlsx"), mergedPath: mergedFile, account: account, shop: shop);
            Assert.Equal(mergedFile, t.DataPath);
            Assert.Equal(SharedWorkbookMerge.SheetName, t.DataSheet);
            Assert.Equal("shared:shop1", t.ProgressKey);

            // Tắt workbook gộp thì file gộp không được dùng thay thế
            var off = SharedWorkbookCatalog.ResolveForUpdate(enabled: false, sourcePath: "", mergedPath: mergedFile, account: account, shop: shop);
            Assert.Equal("", off.DataPath);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void VerifyDirectResellExcelFile_All18ShopsInBigSellerJson_MatchSuccessfully()
    {
        const string excelPath = @"D:\Projects\shopee-tools\data\Sản phẩm shopee resell - Linh QA - 18 shops.xlsx";
        if (!File.Exists(excelPath)) return; // Bỏ qua nếu chạy trên máy không có file

        var sheets = WorkbookSheets.ListSheetNames(excelPath);
        Assert.NotEmpty(sheets);

        var store = BigSellerStore.Shared;
        store.Load();
        var allShops = store.Accounts.SelectMany(a => a.Shops).ToList();

        if (allShops.Count >= 18)
        {
            foreach (var shop in allShops)
            {
                var matched = SharedWorkbookCatalog.MatchShopSheet(sheets, shop);
                Assert.True(!string.IsNullOrWhiteSpace(matched), $"Shop '{shop.DisplayName}' ({shop.Id}) không tìm thấy sheet tương ứng trong file Excel!");
                Assert.Contains(matched, sheets);
            }
        }
    }
}
