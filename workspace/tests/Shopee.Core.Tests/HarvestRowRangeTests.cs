using ClosedXML.Excel;
using Shopee.Core.Harvest;
using static Shopee.Core.Tests.HarvestTestData;

namespace Shopee.Core.Tests;

/// <summary>
/// GĐ7.1: dải "Từ dòng / Đến dòng" của Harvest tính bằng số dòng Excel THẬT (dòng trống giữa chừng, có / không tiêu đề), không phải
/// dòng nén; đếm và lên lịch chạy chỉ trong dải.
/// </summary>
public sealed class HarvestRowRangeTests : IDisposable
{
    private readonly string _dir = NewTempDir();

    public void Dispose() => TryDelete(_dir);

    /// <summary>Sheet một cột link: <paramref name="header"/> → dòng 1 là "Link"; link SP id 1..n đặt ở các dòng Excel cho sẵn.</summary>
    private string Sheet(bool header, params int[] excelRows)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".xlsx");
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("S");
        if (header) ws.Cell(1, 1).Value = "Link";
        for (var i = 0; i < excelRows.Length; i++) ws.Cell(excelRows[i], 1).Value = $"https://shopee.vn/sp-i.77.{excelRows[i]}";
        wb.SaveAs(path);
        return path;
    }

    private static List<int> ExcelRowsOf(HarvestWorkbookRange r) =>
        (r.Links?.Items ?? []).Select(i => r.ExcelRows![i.RowNumber - 1]).ToList();

    [Fact]
    public async Task CoTieuDe_DongTrongGiuaChung_DaiTheoSoDongExcelThat()
    {
        // Dòng 6 trống (Excel lược) → dòng nén lệch khỏi số dòng Excel từ dòng 7.
        var full = await HarvestWorkbookReader.ReadSheetAsync(Sheet(true, 2, 3, 4, 5, 7, 8, 9, 10), "S", CancellationToken.None);
        Assert.Equal(2, full.FirstDataRow);
        Assert.Equal([2, 3, 4, 5, 7, 8, 9, 10], ExcelRowsOf(full));

        var r = HarvestRowRange.Restrict(full, 4, 8);
        Assert.Equal([4, 5, 7, 8], ExcelRowsOf(r));
        Assert.Equal((3, 6), (r.DenseStart, r.DenseEnd));                     // dòng nén 3..6 = Excel 4..8
        Assert.Equal(4, r.Links!.TotalDataRows);

        Assert.Equal([7, 8, 9, 10], ExcelRowsOf(HarvestRowRange.Restrict(full, 6, 0)));   // Từ trúng dòng trống → dòng có thật sau đó; 0 = tới hết
        Assert.Equal([2, 3, 4, 5, 7, 8, 9, 10], ExcelRowsOf(HarvestRowRange.Restrict(full, 0, 0)));   // mặc định = cả sheet
        Assert.Equal([2, 3], ExcelRowsOf(HarvestRowRange.Restrict(full, 1, 3)));          // Từ 1 (tiêu đề) → kẹp về 2
    }

    [Fact]
    public async Task KhongTieuDe_DongMotLaDuLieu()
    {
        var full = await HarvestWorkbookReader.ReadSheetAsync(Sheet(false, 1, 2, 3, 4), "S", CancellationToken.None);
        Assert.Equal(1, full.FirstDataRow);
        Assert.Equal([1, 2, 3, 4], ExcelRowsOf(HarvestRowRange.Restrict(full, 0, 0)));
        Assert.Equal([1, 2], ExcelRowsOf(HarvestRowRange.Restrict(full, 1, 2)));
        Assert.Equal([3, 4], ExcelRowsOf(HarvestRowRange.Restrict(full, 3, 99)));
    }

    [Fact]
    public async Task AnhXaDongNen_SangDongExcel_CaNgoaiBang()
    {
        var map = HarvestRowRange.ExcelRowMap(
            await HarvestWorkbookReader.ReadSheetAsync(Sheet(true, 2, 3, 5, 9), "S", CancellationToken.None));
        Assert.Equal([2, 3, 5, 9, 10], new[] { 1, 2, 3, 4, 5 }.Select(map));        // dòng nén 5 (sau cuối) → nối tiếp dòng Excel cuối
        var noTable = HarvestRowRange.ExcelRowMap(new HarvestWorkbookRange(1, 3, null, FirstDataRow: 2));
        Assert.Equal(4, noTable(3));                                                 // không có bảng → liền nhau từ dòng dữ liệu đầu
    }

    [Fact]
    public async Task DaiNgoaiSheet_Rong_KhongLenLich()
    {
        var full = await HarvestWorkbookReader.ReadSheetAsync(Sheet(true, 2, 3, 4), "S", CancellationToken.None);
        var r = HarvestRowRange.Restrict(full, 50, 0);
        Assert.True(r.DenseEnd < r.DenseStart);
        Assert.Empty(r.Links!.Items);
        var plan = HarvestPlanning.Build(r, new HashSet<string>(), null, null);
        Assert.Empty(plan.Remaining);
        Assert.Equal(0, plan.TodoRows);
    }

    [Fact]
    public async Task LenLich_ChiDongTrongDai_DemTheoDai()
    {
        var full = await HarvestWorkbookReader.ReadSheetAsync(Sheet(true, 2, 3, 4, 5, 6, 7, 8), "S", CancellationToken.None);
        var done = new HashSet<string> { "id:77.4" };                               // Excel 4 đã lấy
        var r = HarvestRowRange.Restrict(full, 3, 6);
        var plan = HarvestPlanning.Build(r, done, null, null);
        // Dòng nén của Excel 3..6 = 2..5; Excel 4 (nén 3) đã có → còn nén 2, 4, 5.
        Assert.Equal(new List<(int, int)> { (2, 2), (4, 5) }, plan.Remaining);
        Assert.Equal((3, 1), (plan.TodoRows, plan.DoneRows));
        Assert.All(plan.Remaining, seg => Assert.True(seg.from >= r.DenseStart && seg.to <= r.DenseEnd));

        var whole = HarvestPlanning.Build(full, done, null, null);
        Assert.Equal(6, whole.TodoRows);                                            // cả sheet: 7 link − 1 đã có
    }

    [Theory]
    [InlineData(0, 2, 2)]
    [InlineData(1, 2, 2)]
    [InlineData(5, 2, 5)]
    [InlineData(-3, 1, 1)]
    public void KepTuDong_VeDongDuLieuDau(int input, int firstDataRow, int expected) =>
        Assert.Equal(expected, HarvestRowRange.ClampFrom(input, firstDataRow));

    [Fact]
    public void DenDong_AmVe0_HopLeKhi0HoacKhongNhoHonTu()
    {
        Assert.Equal(0, HarvestRowRange.ClampTo(-5));
        Assert.True(HarvestRowRange.IsValid(10, 0));
        Assert.True(HarvestRowRange.IsValid(10, 10));
        Assert.False(HarvestRowRange.IsValid(10, 9));
    }
}
