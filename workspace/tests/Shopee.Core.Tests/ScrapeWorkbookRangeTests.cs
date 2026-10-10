using ClosedXML.Excel;
using Shopee.Core.Scrape;

namespace Shopee.Core.Tests;

public sealed class ScrapeWorkbookRangeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "scrape-range-" + Guid.NewGuid().ToString("N"));

    public ScrapeWorkbookRangeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void TuDongExcel2_LaSanPhamDau_KhongBoQua()
    {
        var path = WriteSheet(["a", "b", "c"]);

        var (start, end) = ScrapeWorkbook.ExcelRowsToDenseRange(path, "Gop", excelStart: 2, excelEnd: 0);

        Assert.Equal(1, start);
        Assert.Equal(3, end);
        var links = ScrapeWorkbook.FetchLinks(path, "Gop", start, end);
        Assert.Equal(["https://a", "https://b", "https://c"], links.Items.Select(i => i.Link).ToArray());
    }

    [Fact]
    public void DenDongExcel_CatDungSanPhamTuongUng()
    {
        var path = WriteSheet(["a", "b", "c"]);

        var (start, end) = ScrapeWorkbook.ExcelRowsToDenseRange(path, "Gop", excelStart: 2, excelEnd: 3);

        Assert.Equal((1, 2), (start, end));
    }

    [Fact]
    public void DongTrongGiua_KhongTinhVaoChiSo()
    {
        var path = Path.Combine(_dir, "trong.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Gop");
            ws.Cell(1, 1).Value = "link";
            ws.Cell(1, 6).Value = "ten";
            ws.Cell(2, 1).Value = "https://a";
            ws.Cell(2, 6).Value = "A";
            ws.Cell(4, 1).Value = "https://b";
            ws.Cell(4, 6).Value = "B";
            wb.SaveAs(path);
        }

        var all = ScrapeWorkbook.ExcelRowsToDenseRange(path, "Gop", 2, 0);
        var onlyFirst = ScrapeWorkbook.ExcelRowsToDenseRange(path, "Gop", 2, 2);
        var second = ScrapeWorkbook.ExcelRowsToDenseRange(path, "Gop", 4, 4);

        Assert.Equal((1, 2), all);
        Assert.Equal((1, 1), onlyFirst);
        Assert.Equal((2, 2), second);
    }

    private string WriteSheet(string[] links)
    {
        var path = Path.Combine(_dir, "sheet.xlsx");
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Gop");
        ws.Cell(1, 1).Value = "link";
        ws.Cell(1, 6).Value = "ten";
        for (var i = 0; i < links.Length; i++)
        {
            ws.Cell(i + 2, 1).Value = "https://" + links[i];
            ws.Cell(i + 2, 6).Value = links[i];
        }
        wb.SaveAs(path);
        return path;
    }
}
