using System.Net;
using System.Net.Http;
using System.Text;
using ClosedXML.Excel;
using Shopee.Core.Coordination;
using Shopee.Core.Harvest;
using static Shopee.Core.Tests.HarvestTestData;

namespace Shopee.Core.Tests;

/// <summary>
/// GĐ6: Harvest chạy theo file Excel tự chọn (một sheet dùng chung, hoặc mỗi shop một sheet); tập "đã xong" toàn cục theo khoá SP
/// (cùng SP ở 2 sheet / 2 file chỉ lấy một lần); SP Hub đã có còn hạn thì bỏ qua; Hub lỗi thì chỉ dựa vào tập cục bộ.
/// </summary>
public sealed class HarvestPlanningTests : IDisposable
{
    private readonly string _dir = NewTempDir();

    public void Dispose() => TryDelete(_dir);

    /// <summary>File Excel đúng định dạng Harvest đọc: dòng 1 tiêu đề, link cột A, tên SP cột F.</summary>
    private string Workbook(string name, params (string Sheet, string[] Links)[] sheets)
    {
        var path = Path.Combine(_dir, name);
        using var wb = new XLWorkbook();
        foreach (var (sheet, links) in sheets)
        {
            var ws = wb.Worksheets.Add(sheet);
            ws.Cell(1, 1).Value = "Link";
            ws.Cell(1, 6).Value = "Tên";
            for (var i = 0; i < links.Length; i++)
            {
                ws.Cell(i + 2, 1).Value = links[i];
                ws.Cell(i + 2, 6).Value = "SP " + i;
            }
        }
        wb.SaveAs(path);
        return path;
    }

    private static string Link(long item) => $"https://shopee.vn/Ao-thun-i.77.{item}";

    private static HarvestedItem Saved(long item, string sheet) =>
        Item("", 1, json: SyntheticJson(item), acct: HarvestSource.AccountId, sheet: sheet) with
        {
            SourceLink = Link(item), ShopeeShopId = 77, RecordDone = true,
        };

    private static HarvestWorkbookRange RangeOf(IReadOnlyList<(string Sheet, HarvestWorkbookRange Range)> sheets, string name) =>
        sheets.Single(s => s.Sheet == name).Range;

    [Fact]
    public async Task DocFileNhieuSheet_VaFileMotSheet()
    {
        var many = Workbook("nhieu.xlsx", ("Shop A", [Link(1), Link(2)]), ("Shop B", [Link(3)]), ("Trống", []));
        var one = Workbook("mot.xlsx", ("Links", [Link(1), Link(4), Link(5)]));

        var a = await HarvestWorkbookReader.ReadAllSheetsAsync(many, CancellationToken.None);
        Assert.Equal(["Shop A", "Shop B", "Trống"], a.Select(s => s.Sheet));
        Assert.Equal([Link(1), Link(2)], RangeOf(a, "Shop A").Links!.Items.Select(i => i.Link));
        Assert.Equal([Link(3)], RangeOf(a, "Shop B").Links!.Items.Select(i => i.Link));
        Assert.Null(RangeOf(a, "Trống").Links);   // sheet không có dòng dữ liệu

        var b = await HarvestWorkbookReader.ReadAllSheetsAsync(one, CancellationToken.None);
        var only = Assert.Single(b);
        Assert.Equal("Links", only.Sheet);
        Assert.Equal(3, only.Range.Links!.Items.Count);
        var plan = HarvestPlanning.Build(only.Range, new HashSet<string>(), null, null);
        Assert.Equal((0, 3, 3), (plan.DoneRows, plan.TodoRows, only.Range.DenseEnd));
    }

    /// <summary>File tự do: chỉ các ô, không cần định dạng BigSeller. <paramref name="cells"/>[r][c] = dòng Excel r+1, cột c+1.</summary>
    private string Raw(string name, string sheet, params string[][] cells)
    {
        var path = Path.Combine(_dir, name);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheet);
        for (var r = 0; r < cells.Length; r++)
            for (var c = 0; c < cells[r].Length; c++)
                if (cells[r][c].Length > 0) ws.Cell(r + 1, c + 1).Value = cells[r][c];
        wb.SaveAs(path);
        return path;
    }

    private static async Task<HarvestWorkbookRange> Sheet(string path, string sheet) =>
        await HarvestWorkbookReader.ReadSheetAsync(path, sheet, CancellationToken.None);

    /// <summary>GĐ6: user đưa file CHỈ một cột link, không tiêu đề — dòng 1 đã là link thì không bị coi là tiêu đề; link không có
    /// https:// vẫn nhận (thêm https://); không cần tên SP.</summary>
    [Fact]
    public async Task FileChiCotLink_KhongTieuDe_DongDauLaDuLieu()
    {
        var path = Raw("links.xlsx", "Links", [Link(1)], ["shopee.vn/b-i.77.2"], [""], [Link(3)]);
        var r = await Sheet(path, "Links");
        Assert.Equal((1, 3), (r.DenseStart, r.DenseEnd));   // dòng trống Excel lược bỏ
        Assert.Equal([(1, Link(1)), (2, "https://shopee.vn/b-i.77.2"), (3, Link(3))],
            r.Links!.Items.Select(i => (i.RowNumber, i.Link)));
        var plan = HarvestPlanning.Build(r, new HashSet<string>(), null, null);
        Assert.Equal((0, 3), (plan.DoneRows, plan.TodoRows));
        Assert.Equal(3, await HarvestWorkbookReader.TotalDataRowsAsync(path, "Links", CancellationToken.None));
        Assert.Equal([Link(3)], (await HarvestWorkbookReader.FetchLinksAsync(path, "Links", 3, 3, CancellationToken.None)).Items.Select(i => i.Link));
    }

    [Fact]
    public async Task FileChiCotLink_CoTieuDe_BoDongTieuDe()
    {
        var path = Raw("links.xlsx", "Links", ["Link sản phẩm"], [Link(1)], [Link(2)]);
        var r = await Sheet(path, "Links");
        Assert.Equal([(1, Link(1)), (2, Link(2))], r.Links!.Items.Select(i => (i.RowNumber, i.Link)));
        Assert.Equal(2, r.DenseEnd);
    }

    /// <summary>Link không ở cột A (vd cột A là STT, cột B có chữ "Shopee" trong tên): cột link = cột đầu tiên có link Shopee.</summary>
    [Fact]
    public async Task TuNhanCotLink_CotDauTienCoLinkShopee()
    {
        var path = Raw("stt.xlsx", "S", ["STT", "Tên", "Link"], ["1", "Áo Shopee chính hãng", Link(1)], ["2", "", "https://shopee.vn/product/77/2"],
            ["3", "Quần", ""]);
        var r = await Sheet(path, "S");
        Assert.Equal((1, 3), (r.DenseStart, r.DenseEnd));
        Assert.Equal([(1, Link(1)), (2, "https://shopee.vn/product/77/2")], r.Links!.Items.Select(i => (i.RowNumber, i.Link)));
        Assert.Equal(1, r.Links.SkippedMissingLink);
        Assert.Equal("1", r.Links.Items[0].Values["STT"]);
        Assert.True(HarvestWorkbookReader.LooksLikeShopeeLink("s.shopee.vn/abc"));
        Assert.True(HarvestWorkbookReader.LooksLikeShopeeLink("https://shp.ee/x1"));
        Assert.False(HarvestWorkbookReader.LooksLikeShopeeLink("Áo Shopee.vn chính hãng"));
        Assert.False(HarvestWorkbookReader.LooksLikeShopeeLink("https://lazada.vn/x"));
    }

    /// <summary>Định dạng cũ (link A, tên F) mà vài dòng thiếu tên: dòng đó vẫn phải lấy (trước GĐ6 bị coi là "đã xong"); dòng thiếu
    /// link mới bị bỏ.</summary>
    [Fact]
    public async Task ThieuTenSP_VanPhaiLay_ThieuLinkMoiBo()
    {
        var path = Raw("cu.xlsx", "Shop A",
            ["Link", "", "", "", "", "Tên"],
            [Link(1), "", "", "", "", "SP 1"],
            [Link(2)],
            ["", "", "", "", "", "SP không link"],
            [Link(4), "", "", "", "", ""]);
        var r = await Sheet(path, "Shop A");
        Assert.Equal(4, r.DenseEnd);
        Assert.Equal([1, 2, 4], r.Links!.Items.Select(i => i.RowNumber));
        Assert.Equal((0, 1), (r.Links.SkippedMissingProductName, r.Links.SkippedMissingLink));
        var plan = HarvestPlanning.Build(r, new HashSet<string> { HarvestItemKey.FromIds(77, 1) }, null, null);
        Assert.Equal([(2, 2), (4, 4)], plan.Remaining);   // dòng 3 (không link) không có gì để lấy; dòng 2 + 4 thiếu tên vẫn lấy
        Assert.Equal(2, plan.TodoRows);
    }

    /// <summary>Phản biện GĐ6: cột "Ghi chú" có MỘT ô <c>shopee.vn/ref</c> và cột "Link shop" (link shop, không có id SP) đứng TRƯỚC
    /// cột link SP thật → vẫn chọn cột có nhiều link SP nhất. Hoà thì cột trái.</summary>
    [Fact]
    public async Task TuNhanCotLink_NhieuLinkSPNhat_KhongBiCotGhiChuHayLinkShopCuop()
    {
        var path = Raw("lan.xlsx", "S",
            ["Ghi chú", "Link shop", "Link SP"],
            ["shopee.vn/ref", "https://shopee.vn/shopabc", Link(1)],
            ["", "https://shopee.vn/shopabc", Link(2)],
            ["", "https://shopee.vn/shopxyz", Link(3)]);
        var r = await Sheet(path, "S");
        Assert.Equal([Link(1), Link(2), Link(3)], r.Links!.Items.Select(i => i.Link));
        Assert.Equal(("C", 3, 3), (r.Info!.LinkColumnName, r.Info.ProductRows, r.Info.DataRows));
        Assert.Null(r.Info.Warning("S"));

        var hoa = Raw("hoa.xlsx", "S", [Link(1), Link(2)], [Link(3), Link(4)]);   // 2 cột cùng số link SP → cột trái
        Assert.Equal([Link(1), Link(3)], (await Sheet(hoa, "S")).Links!.Items.Select(i => i.Link));

        // Không cột nào có id SP → cột nhiều link Shopee nhất (link rút gọn).
        var rutGon = Raw("rg.xlsx", "S", ["Tên", "Link"], ["Áo", "https://shope.ee/a1"], ["Quần", "shp.ee/b2"]);
        Assert.Equal(["https://shope.ee/a1", "https://shp.ee/b2"], (await Sheet(rutGon, "S")).Links!.Items.Select(i => i.Link));
    }

    /// <summary>Cột chọn có ít link SP / lẫn chữ không phải link → cảnh báo rõ (màn hình + log); chữ ("Link", "hết hàng", "x", tiêu đề
    /// gộp) là dòng KHÔNG có link, không phải mục phải lấy.</summary>
    [Fact]
    public async Task ChuKhongPhaiLink_BoQua_DemRieng_CoCanhBao()
    {
        var path = Raw("chu.xlsx", "Shop X",
            ["STT", "Link"],
            ["1", Link(1)], ["2", "hết hàng"], ["3", "x"], ["4", "Link"], ["5", ""], ["6", "BẢNG TỔNG HỢP THÁNG 11"]);
        var r = await Sheet(path, "Shop X");
        Assert.Equal([Link(1)], r.Links!.Items.Select(i => i.Link));
        Assert.Equal(5, r.Links.SkippedMissingLink);                  // 4 dòng chữ + 1 dòng trống: không phải mục phải lấy
        Assert.Equal((6, 1, 1, 4), (r.Info!.DataRows, r.Info.LinkRows, r.Info.ProductRows, r.Info.NotLinkRows));
        Assert.Equal("sheet Shop X: chỉ 1/6 dòng có link SP ở cột B; 4 dòng ở cột B không phải link — bỏ qua", r.Info.Warning("Shop X"));
        var plan = HarvestPlanning.Build(r, new HashSet<string>(), null, null);
        Assert.Equal((1, 5), (plan.TodoRows, plan.DoneRows));
    }

    /// <summary>Tiêu đề kiểu "Shopee.vn" / "Shopee.Link" (tên miền trơn, không path, không scheme) không phải link → dòng 1 vẫn là
    /// tiêu đề. Host Shopee gom một chỗ, gồm link rút gọn cũ <c>shope.ee</c>.</summary>
    [Fact]
    public async Task TieuDeTenMienTron_KhongPhaiLink_DanhSachHostShopee()
    {
        foreach (var header in new[] { "Shopee.vn", "Shopee.Link", "shopee.co.id" })
        {
            var path = Raw($"h-{header}.xlsx", "S", [header], [Link(1)], [Link(2)]);
            var r = await Sheet(path, "S");
            Assert.Equal([1, 2], r.Links!.Items.Select(i => i.RowNumber));
            Assert.Equal(0, r.Info!.NotLinkRows);
            Assert.False(HarvestWorkbookReader.LooksLikeShopeeLink(header));
        }
        foreach (var ok in new[] { "https://shope.ee/abc", "shope.ee/abc", "shp.ee/x", "s.shopee.vn/a", "https://shopee.co.id/x-i.1.2",
                     "www.shopee.vn", "https://shopee.vn", "SHOPEE.VN/abc" })
            Assert.True(HarvestWorkbookReader.LooksLikeShopeeLink(ok), ok);
        foreach (var host in new[] { "shopee.vn", "s.shopee.vn", "shopee.com.my", "shp.ee", "shope.ee", "SHOPE.EE", "x.shope.ee" })
            Assert.True(HarvestLinks.IsShopeeHost(host), host);
        foreach (var host in new[] { "", "notshopee.vn", "lazada.vn", "shopee", "eshope.ee" })
            Assert.False(HarvestLinks.IsShopeeHost(host), host);
    }

    /// <summary>SP lưu khi chạy sheet "Shop A" của file 1 → ở sheet "Shop B" (cùng file) và sheet "Links" (file khác) đều là "đã có",
    /// kể cả khi link viết khác dạng (/product/shop/item).</summary>
    [Fact]
    public async Task DaXongToanCuc_CungSPO2SheetVa2File_ChiLayMotLan()
    {
        var box = new HarvestOutbox(Path.Combine(_dir, "outbox.db"));
        var f1 = Workbook("f1.xlsx", ("Shop A", [Link(10), Link(11)]), ("Shop B", [$"https://shopee.vn/product/77/10?sp_atk=x", Link(12)]));
        var f2 = Workbook("f2.xlsx", ("Links", [Link(11), Link(13), Link(10)]));
        await box.SaveAsync(Saved(10, "Shop A"), CancellationToken.None);
        await box.SaveAsync(Saved(11, "Shop A"), CancellationToken.None);

        var done = box.DoneKeys(HarvestPlanning.DoneSince(DateTimeOffset.UtcNow, 30));
        var s1 = await HarvestWorkbookReader.ReadAllSheetsAsync(f1, CancellationToken.None);
        var s2 = await HarvestWorkbookReader.ReadAllSheetsAsync(f2, CancellationToken.None);

        var shopB = HarvestPlanning.Build(RangeOf(s1, "Shop B"), done, box.FailedKeys(), null);
        Assert.Equal([(2, 2)], shopB.Remaining);                 // chỉ còn dòng 2 (SP 12) — SP 10 đã lấy ở Shop A
        var links = HarvestPlanning.Build(RangeOf(s2, "Links"), done, box.FailedKeys(), null);
        Assert.Equal([(2, 2)], links.Remaining);                 // file khác: SP 11 và SP 10 đã có, chỉ còn SP 13
        Assert.Equal((2, 1), (links.DoneRows, links.TodoRows));
    }

    [Fact]
    public void HubBaoDaCo_BoQua_KhoaUrlKhongHoiHub()
    {
        var items = new List<Scrape.ScrapeLinkItem>
        {
            new(1, Link(20), []), new(2, Link(21), []), new(3, "https://shopee.vn/search?keyword=ao", []), new(4, Link(22), []),
        };
        var range = new HarvestWorkbookRange(1, 4, new Scrape.ScrapeLinkResult(items, 4, 0, 0));
        var done = new HashSet<string> { HarvestItemKey.FromIds(77, 22) };

        Assert.Equal([20L, 21L], HarvestPlanning.ItemIdsToAsk(items, done));   // url: không có itemId; SP đã có cục bộ khỏi hỏi
        var plan = HarvestPlanning.Build(range, done, null, new HashSet<long> { 21 });
        Assert.Equal([(1, 1), (3, 3)], plan.Remaining);
        var noHub = HarvestPlanning.Build(range, done, null, null);              // Hub không trả lời: chỉ dựa vào cục bộ
        Assert.Equal([(1, 3)], noHub.Remaining);
    }

    [Fact]
    public async Task HoiHub_KepSoNgay_LoiHoacChuaNoiThiKnownNull_KhongNem()
    {
        int? asked = null;
        HarvestKnownQuery ok = (ids, days, _) =>
        {
            asked = days;
            return Task.FromResult<IReadOnlyDictionary<long, DateTimeOffset>>(ids.Where(i => i % 2 == 0).ToDictionary(i => i, _ => DateTimeOffset.UtcNow));
        };
        var r = await HarvestPlanning.AskHubAsync(ok, [1, 2, 4], 0, CancellationToken.None);
        Assert.Equal(new HashSet<long> { 2, 4 }, r.Known);
        Assert.Equal(HarvestPlanning.DefaultMaxAgeDays, asked);                  // 0 → mặc định 30
        await HarvestPlanning.AskHubAsync(ok, [1], 999_999, CancellationToken.None);
        Assert.Equal(HarvestedKnownLimits.MaxAgeDays, asked);

        HarvestKnownQuery down = (_, _, _) => throw new HttpRequestException("503 pg-unavailable");
        var e = await HarvestPlanning.AskHubAsync(down, [1], 30, CancellationToken.None);
        Assert.Null(e.Known);
        Assert.Contains("503", e.Error);
        var none = await HarvestPlanning.AskHubAsync(null, [1], 30, CancellationToken.None);
        Assert.Null(none.Known);
        Assert.Empty((await HarvestPlanning.AskHubAsync(null, [], 30, CancellationToken.None)).Known!);   // không có gì để hỏi

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        HarvestKnownQuery slow = (_, _, ct) => { ct.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyDictionary<long, DateTimeOffset>>(new Dictionary<long, DateTimeOffset>()); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HarvestPlanning.AskHubAsync(slow, [1], 30, cts.Token));
    }

    // ── HubClient.GetHarvestedKnownAsync với Hub giả ──

    private sealed class FakeHub(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Bodies.Add(body);
            return respond(request, body);
        }
    }

    private static HubClient Client(FakeHub hub) =>
        new(new HubClientConfig { Enabled = true, BaseUrl = "http://hub.test/", ApiToken = "t" }, "may-1", hub);

    [Fact]
    public async Task HubClientKnown_KhuTrung_ChiaLo500_GopKetQua_5xxThiNem()
    {
        var hub = new FakeHub((req, body) =>
        {
            Assert.Equal(HubRoutes.HarvestedProductsKnown, req.RequestUri!.AbsolutePath);
            var r = System.Text.Json.JsonSerializer.Deserialize<HarvestedKnownRequest>(body,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            Assert.Equal(45, r.MaxAgeDays);
            Assert.True(r.ItemIds!.Count <= HarvestedKnownLimits.MaxItems);
            var items = string.Join(",", r.ItemIds.Where(i => i % 100 == 0).Select(i => $"{{\"itemId\":{i},\"harvestedAt\":\"2026-11-17T00:00:00Z\"}}"));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[" + items + "]}", Encoding.UTF8, "application/json") };
        });
        using var c = Client(hub);
        var ids = Enumerable.Range(1, 1200).Select(i => (long)i).Concat([100L, 0L, -5L]);
        var known = await c.GetHarvestedKnownAsync(ids, 45);
        Assert.Equal(3, hub.Bodies.Count);                                       // 1200 id khác nhau → 500 + 500 + 200
        Assert.Equal(12, known.Count);
        Assert.DoesNotContain(hub.Bodies, b => b.Contains("\"itemIds\":[0") || b.Contains("-5"));

        var down = new FakeHub((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var c2 = Client(down);
        await Assert.ThrowsAsync<HttpRequestException>(() => c2.GetHarvestedKnownAsync([1], 30));
        var lookup = await HarvestPlanning.AskHubAsync((i, d, ct) => AsReadOnly(c2.GetHarvestedKnownAsync(i, d, ct)), [1], 30, CancellationToken.None);
        Assert.Null(lookup.Known);
    }

    private static async Task<IReadOnlyDictionary<long, DateTimeOffset>> AsReadOnly(Task<Dictionary<long, DateTimeOffset>> t) => await t;

    [Fact]
    public void SheetKey_ChuanHoaDuongDan_KhacFileKhacKhoa()
    {
        var a = HarvestSource.SheetKey(@"C:\Data\Links.xlsx", "Shop A");
        Assert.Equal(a, HarvestSource.SheetKey(@"c:/data/LINKS.xlsx", "Shop A"));
        Assert.NotEqual(a, HarvestSource.SheetKey(@"C:\Data\Khac.xlsx", "Shop A"));
        Assert.NotEqual(a, HarvestSource.SheetKey(@"C:\Data\Links.xlsx", "Shop B"));
        Assert.Equal(12L, HarvestSource.ItemIdOfKey("id:77.12"));
        Assert.Null(HarvestSource.ItemIdOfKey("url:shopee.vn/x"));
        Assert.Null(HarvestSource.ItemIdOfKey(null));
    }
}
