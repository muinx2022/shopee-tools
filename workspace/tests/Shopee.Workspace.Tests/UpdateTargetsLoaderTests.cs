using ClosedXML.Excel;
using UpdateProduct;

namespace Shopee.Workspace.Tests;

/// <summary>
/// <see cref="WorkbookRecordCache.LoadUpdateTargetsByKeyAsync"/>: tập "mục tiêu Update" theo khoá (tên sheet) mà reset bảng import dựa vào.
/// Phải cùng định nghĩa với luồng Update (<see cref="WorkbookRecordCache.LoadRecordMapAsync"/>): dòng định vị được bằng Item ID / Link VÀ có tên đã sửa.
/// </summary>
public sealed class UpdateTargetsLoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "upd-targets-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public UpdateTargetsLoaderTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "nguon.xlsx");
        using var wb = new XLWorkbook();
        var s1 = wb.AddWorksheet("S1");
        s1.Cell(1, 5).Value = "ItemId"; s1.Cell(1, 7).Value = "Tên đã sửa";
        Row(s1, 2, link: "", itemId: "111", rewritten: "Tên A");                                   // mục tiêu
        Row(s1, 3, link: "", itemId: "222", rewritten: "");                                        // tên đã sửa trống → KHÔNG phải mục tiêu
        Row(s1, 4, link: "https://shopee.vn/product/7/333", itemId: "", rewritten: "Tên C");       // id suy từ link → mục tiêu
        Row(s1, 5, link: "", itemId: "", rewritten: "Tên D");                                      // không định vị được → không
        Row(s1, 6, link: "", itemId: "666", rewritten: "   ");                                     // chỉ khoảng trắng → không
        Row(s1, 7, link: "", itemId: "777", rewritten: "Tên G");                                   // mục tiêu, nằm ngoài EndRow=6
        var s2 = wb.AddWorksheet("S2");
        Row(s2, 2, link: "", itemId: "444", rewritten: "Tên X");
        Row(s2, 3, link: "", itemId: "555", rewritten: "");
        wb.SaveAs(_path);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static void Row(IXLWorksheet ws, int r, string link, string itemId, string rewritten)
    {
        if (link.Length > 0) ws.Cell(r, 1).Value = link;
        if (itemId.Length > 0) ws.Cell(r, 5).Value = itemId;
        if (rewritten.Length > 0) ws.Cell(r, 7).Value = rewritten;
    }

    private BigSellerWorkflowSettings Settings(string dataSheet = "S1", int startRow = 2, int endRow = 0) => new()
    {
        WorkbookPath = _path, DataSheet = dataSheet, StartRow = startRow, EndRow = endRow,
        // Ánh xạ cột mặc định của BigSellerWorkflowSettings: Link=1, ItemId=5, Tên đã sửa=7.
    };

    private static string[] Sorted(IReadOnlySet<string> set) => set.OrderBy(x => x, StringComparer.Ordinal).ToArray();

    [Fact]
    public async Task OwnKey_TargetsEqualTheUpdateFlowRecordMap()
    {
        // Chống lệch định nghĩa: tập mục tiêu phải đúng bằng tập khoá của map mà luồng Update nạp cho cùng sheet/vùng dòng.
        var settings = Settings();
        var (map, _) = await WorkbookRecordCache.LoadRecordMapAsync(settings, CancellationToken.None);

        var targets = await WorkbookRecordCache.LoadUpdateTargetsByKeyAsync(settings, "S1", new[] { "S1" }, _ => { }, CancellationToken.None);

        Assert.Equal(new[] { "111", "333", "777" }, Sorted(targets["S1"]));
        Assert.Equal(map.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray(), Sorted(targets["S1"]));
    }

    [Fact]
    public async Task OwnKey_UsesStartEndRowOfRunningShop_OtherKeysReadWholeSheet()
    {
        var settings = Settings(startRow: 2, endRow: 6);
        var (map, _) = await WorkbookRecordCache.LoadRecordMapAsync(settings, CancellationToken.None);

        var targets = await WorkbookRecordCache.LoadUpdateTargetsByKeyAsync(settings, "S1", new[] { "S1", "S2" }, _ => { }, CancellationToken.None);

        Assert.Equal(new[] { "111", "333" }, Sorted(targets["S1"]));   // 777 ở dòng 7 > EndRow
        Assert.Equal(map.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray(), Sorted(targets["S1"]));
        Assert.Equal(new[] { "444" }, Sorted(targets["S2"]));           // khoá khác: cả sheet
    }

    [Fact]
    public async Task SharedKeys_AndSheetsMissingFromFile_AreLeftOutAndLogged()
    {
        var log = new List<string>();

        var targets = await WorkbookRecordCache.LoadUpdateTargetsByKeyAsync(
            Settings(), "S1", new[] { "S2", "shared:abc", "SHARED:xyz", "Khong-co-sheet", "" }, log.Add, CancellationToken.None);

        Assert.Equal(new[] { "S2" }, targets.Keys.ToArray());
        Assert.Contains(log, l => l.Contains("Khong-co-sheet"));
        Assert.DoesNotContain(log, l => l.Contains("shared"));   // khoá shared bị loại im lặng (store sẽ báo "bỏ qua")
    }

    [Fact]
    public async Task KeyLookup_IsCaseInsensitive()
    {
        var targets = await WorkbookRecordCache.LoadUpdateTargetsByKeyAsync(Settings(), "S1", new[] { "s2" }, _ => { }, CancellationToken.None);

        Assert.Equal(new[] { "444" }, Sorted(targets["S2"]));   // tra từ điển cũng không phân biệt hoa thường
    }

    [Fact]
    public async Task OnlySharedKeysOfOtherShops_ReturnsEmptyWithoutOpeningTheFile()
    {
        var settings = Settings() with { WorkbookPath = Path.Combine(_dir, "khong-ton-tai.xlsx") };

        var targets = await WorkbookRecordCache.LoadUpdateTargetsByKeyAsync(settings, "shared:own", new[] { "shared:abc", "shared:xyz" }, _ => { }, CancellationToken.None);

        Assert.Empty(targets);   // khoá shared: không phải của shop đang chạy → "không biết", không mở file
    }

    [Fact]
    public async Task OwnSharedKey_ReadByDataSheet_OtherShopsSharedKeyStillUnknown()
    {
        // Workbook chung: ProgressKey = "shared:s1" (không phải tên sheet), DataSheet = "S1". Khoá của chính shop đọc theo DataSheet và vùng dòng của shop.
        var settings = Settings(dataSheet: "S1", startRow: 2, endRow: 6) with { ProgressSheet = "shared:s1" };
        Assert.Equal("shared:s1", settings.ProgressKey);

        var targets = await WorkbookRecordCache.LoadUpdateTargetsByKeyAsync(
            settings, settings.ProgressKey, new[] { "shared:s1", "shared:s2" }, _ => { }, CancellationToken.None);

        Assert.Equal(new[] { "shared:s1" }, targets.Keys.ToArray());                  // shared:s2 của shop khác: không biết
        Assert.Equal(new[] { "111", "333" }, Sorted(targets["shared:s1"]));          // 777 ở dòng 7 > EndRow
    }

    [Fact]
    public async Task RewrittenNameColumnNotMapped_Throws_LikeUpdateFlow()
    {
        var settings = Settings() with { RewrittenNameColumn = 0 };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WorkbookRecordCache.LoadUpdateTargetsByKeyAsync(settings, "S1", new[] { "S1" }, _ => { }, CancellationToken.None));
    }
}
