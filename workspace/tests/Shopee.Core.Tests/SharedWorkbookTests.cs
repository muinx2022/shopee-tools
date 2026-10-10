using ClosedXML.Excel;
using Shopee.Core.BigSeller;

namespace Shopee.Core.Tests;

public sealed class SharedWorkbookTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "shared-wb-" + Guid.NewGuid().ToString("N"));

    public SharedWorkbookTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Build_HaiSheet_HeaderTuSheetTickTruoc_NoiDungBoDongTrong()
    {
        var src = Path.Combine(_dir, "nguon.xlsx");
        var dest = Path.Combine(_dir, "gop.xlsx");
        WriteSample(src);

        var result = SharedWorkbookMerge.Build(src, ["B", "A"], dest);

        Assert.Equal(3, result.DataRowCount);
        Assert.Contains("B: 2 cột", result.ColumnNotes);
        Assert.Contains("A: 2 cột (header lệch)", result.ColumnNotes);

        using var wb = new XLWorkbook(dest);
        var ws = wb.Worksheet("Gop");
        Assert.Equal("Link", ws.Cell(1, 1).GetString());
        Assert.Equal("Khac", ws.Cell(1, 2).GetString());
        Assert.Equal("b1", ws.Cell(2, 1).GetString());
        Assert.Equal("a1", ws.Cell(3, 1).GetString());
        Assert.Equal("Ten", ws.Cell(3, 2).GetString());
        Assert.Equal("a2", ws.Cell(4, 1).GetString());
        Assert.DoesNotContain(wb.Worksheets, s => s.Name == "A");
    }

    [Fact]
    public void Build_SheetKhongCo_KhongTaoFileDich()
    {
        var src = Path.Combine(_dir, "nguon.xlsx");
        var dest = Path.Combine(_dir, "khong-ghi.xlsx");
        WriteSample(src);

        var ex = Assert.Throws<InvalidOperationException>(() => SharedWorkbookMerge.Build(src, ["A", "KhongCo"], dest));

        Assert.Contains("KhongCo", ex.Message);
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public void Resolve_Tat_TraWorkbookVaSheetCuaShop()
    {
        var account = new BigSellerAccount { WorkbookPath = @"D:\rieng.xlsx" };
        var shop = new BigSellerShop { Id = "s1", ShopeeDataSheet = "ShopA" };

        var got = SharedWorkbookCatalog.Resolve(false, @"D:\gop.xlsx", account, shop);

        Assert.Equal(@"D:\rieng.xlsx", got.DataPath);
        Assert.Equal("ShopA", got.DataSheet);
        Assert.Equal("ShopA", got.ProgressKey);
    }

    [Fact]
    public void Resolve_Bat_TraFileGop_VaKhoaTienDoRiengTungShop()
    {
        var account = new BigSellerAccount { WorkbookPath = @"D:\rieng.xlsx" };
        var shop1 = new BigSellerShop { Id = "shop-1", ShopeeDataSheet = "A" };
        var shop2 = new BigSellerShop { Id = "shop-2", ShopeeDataSheet = "A" };

        var a = SharedWorkbookCatalog.Resolve(true, @"D:\gop.xlsx", account, shop1);
        var b = SharedWorkbookCatalog.Resolve(true, @"D:\gop.xlsx", account, shop2);

        Assert.Equal(@"D:\gop.xlsx", a.DataPath);
        Assert.Equal("Gop", a.DataSheet);
        Assert.Equal("shared:shop-1", a.ProgressKey);
        Assert.Equal("shared:shop-2", b.ProgressKey);
        Assert.NotEqual(a.ProgressKey, b.ProgressKey);

        var scrapeA = SharedWorkbookCatalog.ScrapeProgressKey(true, "acc-a", a.ProgressKey);
        var scrapeB = SharedWorkbookCatalog.ScrapeProgressKey(true, "acc-b", a.ProgressKey);
        Assert.Equal("acc:acc-a:shared:shop-1", scrapeA);
        Assert.NotEqual(scrapeA, scrapeB);
        Assert.Equal("shop-1", SharedWorkbookCatalog.ShopIdFromKey(scrapeA));
        Assert.True(SharedWorkbookCatalog.IsSharedKey(scrapeA));
        Assert.Equal("ShopA", SharedWorkbookCatalog.ScrapeProgressKey(false, "acc-a", "ShopA"));
    }

    private static void WriteSample(string path)
    {
        using var wb = new XLWorkbook();
        var a = wb.AddWorksheet("A");
        a.Cell(1, 1).Value = "Link";
        a.Cell(1, 2).Value = "Ten";
        a.Cell(2, 1).Value = "a1";
        a.Cell(2, 2).Value = "Ten";
        a.Cell(4, 1).Value = "a2";
        var b = wb.AddWorksheet("B");
        b.Cell(1, 1).Value = "Link";
        b.Cell(1, 2).Value = "Khac";
        b.Cell(2, 1).Value = "b1";
        wb.SaveAs(path);
    }
}
