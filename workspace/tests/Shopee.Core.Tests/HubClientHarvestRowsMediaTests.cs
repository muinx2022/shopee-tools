using System.Net;
using System.Net.Http;
using System.Text;
using Shopee.Core.Coordination;

namespace Shopee.Core.Tests;

/// <summary>Hàm client đọc SP đã harvest (<see cref="HubClient.GetHarvestedByItemsAsync"/>,
/// <see cref="HubClient.GetHarvestedRowsAsync"/>) và kho ảnh công khai (<see cref="HubClient.UploadPublicMediaAsync"/>,
/// <see cref="HubClient.DeletePublicMediaAsync"/>) với Hub GIẢ.</summary>
public sealed class HubClientHarvestRowsMediaTests
{
    private sealed class FakeHub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        public List<HttpRequestMessage> Requests { get; } = [];

        /// <summary>Body request đã đọc sẵn (async) để responder đọc được mà không phải chờ chặn.</summary>
        public static readonly HttpRequestOptionsKey<string> Body = new("body");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.PathAndQuery);
            Requests.Add(request);
            if (request.Content is not null) request.Options.Set(Body, await request.Content.ReadAsStringAsync(ct));
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HubClient Client(FakeHub hub) =>
        new(new HubClientConfig { Enabled = true, BaseUrl = "http://hub.test/", ApiToken = "t" }, "may-1", hub);

    private static string Row(int rowNo, long itemId) =>
        $"{{\"rowNo\":{rowNo},\"url\":\"u{rowNo}\",\"itemId\":{itemId},\"shopId\":9,\"harvestedAt\":\"2026-11-17T00:00:00Z\","
        + $"\"linkedAt\":\"2026-11-17T00:00:00Z\",\"raw\":{{\"data\":{{\"item\":{{\"item_id\":{itemId}}}}}}}}}";

    private static string Item(long itemId) =>
        $"{{\"itemId\":{itemId},\"shopId\":9,\"harvestedAt\":\"2026-11-17T00:00:00Z\",\"raw\":{{\"data\":{{\"item\":{{\"item_id\":{itemId}}}}}}}}}";

    // ── by-items ──

    [Fact]
    public async Task GetByItems_KhuTrung_ChiaLo50_GopKetQua()
    {
        var lo = new List<List<long>>();
        var hub = new FakeHub(req =>
        {
            req.Options.TryGetValue(FakeHub.Body, out var text);
            var body = System.Text.Json.JsonSerializer.Deserialize<HarvestedByItemsRequest>(text!,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            lo.Add(body.ItemIds!);
            // Hub giả: chỉ id chẵn là "có dữ liệu".
            return Json("{\"items\":[" + string.Join(",", body.ItemIds!.Where(i => i % 2 == 0).Select(Item)) + "]}");
        });
        using var c = Client(hub);
        var ids = Enumerable.Range(1, 120).Select(i => (long)i).Concat([2L, 4L, 120L]);   // 120 id khác nhau + 3 id trùng

        var items = await c.GetHarvestedByItemsAsync(ids);

        Assert.Equal([50, 50, 20], lo.Select(l => l.Count));
        Assert.Equal(120, lo.SelectMany(l => l).Distinct().Count());
        Assert.All(hub.Urls, u => Assert.Equal(HubRoutes.HarvestedProductsByItems, u));
        Assert.Equal(Enumerable.Range(1, 60).Select(i => (long)i * 2), items.Select(i => i.ItemId));
        Assert.Equal(4, items[1].Raw.GetProperty("data").GetProperty("item").GetProperty("item_id").GetInt64());
    }

    [Fact]
    public async Task GetByItems_Rong_KhongGoiHub()
    {
        var hub = new FakeHub(_ => throw new InvalidOperationException("không được gọi"));
        using var c = Client(hub);

        Assert.Empty(await c.GetHarvestedByItemsAsync([]));
        Assert.Empty(hub.Urls);
    }

    [Fact]
    public async Task GetByItems_MotLoLoi_NemCaLoiGoi()
    {
        var n = 0;
        using var c = Client(new FakeHub(_ => ++n == 2
            ? Json("{\"error\":\"pg-unavailable\"}", HttpStatusCode.ServiceUnavailable)
            : Json("{\"items\":[]}")));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => c.GetHarvestedByItemsAsync(Enumerable.Range(1, 60).Select(i => (long)i)));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
    }

    // ── rows (chỉ tra cứu) ──

    [Fact]
    public async Task GetHarvestedRows_LapQuaCacTrang_GuiConTro_DuDongDungThuTu()
    {
        var hub = new FakeHub(req =>
        {
            var q = req.RequestUri!.Query;
            if (!q.Contains("afterRow")) return Json($"{{\"rows\":[{Row(3, 11)},{Row(3, 12)}],\"next\":{{\"afterRow\":3,\"afterItemId\":12}}}}");
            Assert.Contains("afterRow=3&afterItemId=12", q);
            return Json($"{{\"rows\":[{Row(4, 5)}],\"next\":null}}");
        });
        using var c = Client(hub);

        var rows = await c.GetHarvestedRowsAsync("tk 1", "Sheet Á", 3, 9);

        Assert.Equal([11L, 12L, 5L], rows.Select(r => r.ItemId));
        Assert.Equal(5, rows[2].Raw.GetProperty("data").GetProperty("item").GetProperty("item_id").GetInt64());
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero), rows[0].LinkedAt);
        Assert.Equal(2, hub.Urls.Count);
        Assert.StartsWith(HubRoutes.HarvestedProductsRows + "?account=tk%201&sheet=Sheet%20%C3%81&from=3&to=9", hub.Urls[0]);
    }

    [Fact]
    public async Task GetHarvestedRows_TrangChongNhau_KhuTrungTheoRowItem()
    {
        var hub = new FakeHub(req => req.RequestUri!.Query.Contains("afterRow")
            ? Json($"{{\"rows\":[{Row(3, 12)},{Row(4, 5)}],\"next\":null}}")
            : Json($"{{\"rows\":[{Row(3, 11)},{Row(3, 12)}],\"next\":{{\"afterRow\":3,\"afterItemId\":11}}}}"));
        using var c = Client(hub);

        var rows = await c.GetHarvestedRowsAsync("a", "s", 1, null);

        Assert.Equal([(3, 11L), (3, 12L), (4, 5L)], rows.Select(r => (r.RowNo, r.ItemId)));
    }

    [Fact]
    public async Task GetHarvestedRows_ConTroKhongTien_Nem_KhongLapVoHan()
    {
        var hub = new FakeHub(_ => Json($"{{\"rows\":[{Row(3, 11)}],\"next\":{{\"afterRow\":3,\"afterItemId\":11}}}}"));
        using var c = Client(hub);

        await Assert.ThrowsAsync<InvalidOperationException>(() => c.GetHarvestedRowsAsync("a", "s", 1, null));
        Assert.Equal(2, hub.Urls.Count);
    }

    [Fact]
    public async Task GetHarvestedRows_Non2xx_NemHttpRequestException()
    {
        using var c = Client(new FakeHub(_ => Json("{\"error\":\"pg-not-ready\"}", HttpStatusCode.ServiceUnavailable)));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => c.GetHarvestedRowsAsync("a", "s", 1, null));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
    }

    // ── kho ảnh công khai ──

    private static string UpBody(string? url) =>
        "{\"path\":\"/api/media/1-ab.jpg\",\"url\":" + (url is null ? "null" : "\"" + url + "\"") + ",\"expiresAt\":\"2026-11-24T00:00:00Z\"}";

    [Fact]
    public async Task UploadPublicMedia_HubTraUrlCongKhai_GiuNguyen_PutDungRoute()
    {
        var hub = new FakeHub(_ => Json(UpBody("https://api.x/api/media/1-ab.jpg")));
        using var c = Client(hub);

        var (status, body, code) = await c.UploadPublicMediaAsync([0xFF, 0xD8, 0xFF]);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Null(code);
        Assert.Equal("https://api.x/api/media/1-ab.jpg", body!.Url);
        Assert.Equal(HttpMethod.Put, hub.Requests[0].Method);
        Assert.Equal(HubRoutes.PublicMedia, hub.Urls[0]);
    }

    [Fact]
    public async Task UploadPublicMedia_HubThieuUrl_LaLoi_KhongTuGhepBaseUrl()
    {
        using var c = Client(new FakeHub(_ => Json(UpBody(null))));

        var (status, body, code) = await c.UploadPublicMediaAsync([0xFF, 0xD8, 0xFF]);

        Assert.Equal(HttpStatusCode.BadGateway, status);
        Assert.Null(body);
        Assert.Equal("url-missing", code);
    }

    [Theory]
    [InlineData("http://api.x/api/media/1-ab.jpg")]
    [InlineData("https://localhost/api/media/1-ab.jpg")]
    [InlineData("https://hub.localhost/api/media/1-ab.jpg")]
    [InlineData("https://127.0.0.1/api/media/1-ab.jpg")]
    [InlineData("https://127.8.9.1:8088/api/media/1-ab.jpg")]
    [InlineData("https://[::1]/api/media/1-ab.jpg")]
    [InlineData("https://10.0.0.5/api/media/1-ab.jpg")]
    [InlineData("https://172.16.0.1/api/media/1-ab.jpg")]
    [InlineData("https://172.31.255.1/api/media/1-ab.jpg")]
    [InlineData("https://192.168.1.10/api/media/1-ab.jpg")]
    [InlineData("/api/media/1-ab.jpg")]
    [InlineData("https://localhost./api/media/1-ab.jpg")]
    [InlineData("https://hub.localhost./api/media/1-ab.jpg")]
    [InlineData("https://0.0.0.0/api/media/1-ab.jpg")]
    [InlineData("https://0.1.2.3/api/media/1-ab.jpg")]
    [InlineData("https://100.64.0.1/api/media/1-ab.jpg")]
    [InlineData("https://100.127.255.254/api/media/1-ab.jpg")]
    [InlineData("https://169.254.169.254/api/media/1-ab.jpg")]
    [InlineData("https://[::]/api/media/1-ab.jpg")]
    [InlineData("https://[fe80::1]/api/media/1-ab.jpg")]
    [InlineData("https://[febf::1]/api/media/1-ab.jpg")]
    [InlineData("https://[fc00::1]/api/media/1-ab.jpg")]
    [InlineData("https://[fd12:3456::1]/api/media/1-ab.jpg")]
    [InlineData("https://[::ffff:192.168.1.1]/api/media/1-ab.jpg")]
    [InlineData("https://hub/api/media/1-ab.jpg")]
    [InlineData("https://hub./api/media/1-ab.jpg")]
    public async Task UploadPublicMedia_UrlKhongCongKhai_LaLoi(string url)
    {
        using var c = Client(new FakeHub(_ => Json(UpBody(url))));

        var (status, body, code) = await c.UploadPublicMediaAsync([0xFF, 0xD8, 0xFF]);

        Assert.Equal(HttpStatusCode.BadGateway, status);
        Assert.Null(body);
        Assert.Equal("url-not-public", code);
    }

    [Theory]
    [InlineData("https://api.schedra.net/api/media/1-ab.jpg")]
    [InlineData("https://172.32.0.1/api/media/1-ab.jpg")]
    [InlineData("https://11.0.0.1/api/media/1-ab.jpg")]
    [InlineData("https://100.63.255.1/api/media/1-ab.jpg")]
    [InlineData("https://100.128.0.1/api/media/1-ab.jpg")]
    [InlineData("https://169.253.0.1/api/media/1-ab.jpg")]
    [InlineData("https://[2001:db8::1]/api/media/1-ab.jpg")]
    [InlineData("https://[fec0::1]/api/media/1-ab.jpg")]
    [InlineData("https://api.schedra.net./api/media/1-ab.jpg")]
    public void IsPublicHttpsUrl_NhanUrlCongKhai(string url) => Assert.True(HubClient.IsPublicHttpsUrl(url));

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    public async Task UploadPublicMedia_2xxBodyHong_LaLoi_KhongTra2xx(string text)
    {
        using var c = Client(new FakeHub(_ => Json(text)));

        var (status, body, code) = await c.UploadPublicMediaAsync([0xFF, 0xD8, 0xFF]);

        Assert.NotEqual(HttpStatusCode.OK, status);
        Assert.Null(body);
        Assert.Equal("bad-response", code);
    }

    /// <summary>502 do client tự gán LUÔN có ErrorCode — caller dựa vào đó để không thử lại; 502 thật của Cloudflare (HTML,
    /// không mã) thì ErrorCode null → caller thử lại.</summary>
    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"path\":\"/api/media/1-ab.jpg\",\"url\":null,\"expiresAt\":\"2026-11-24T00:00:00Z\"}")]
    [InlineData("{\"path\":\"/api/media/1-ab.jpg\",\"url\":\"http://x.y/a.jpg\",\"expiresAt\":\"2026-11-24T00:00:00Z\"}")]
    public async Task UploadPublicMedia_502TuGan_LuonCoErrorCode(string body200)
    {
        using var c = Client(new FakeHub(_ => Json(body200)));

        var (status, body, code) = await c.UploadPublicMediaAsync([0xFF, 0xD8, 0xFF]);

        Assert.Equal(HttpStatusCode.BadGateway, status);
        Assert.Null(body);
        Assert.False(string.IsNullOrEmpty(code));
    }

    [Fact]
    public async Task UploadPublicMedia_502ThatCuaCloudflare_KhongCoErrorCode()
    {
        using var c = Client(new FakeHub(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        { Content = new StringContent("<html>Bad gateway</html>", Encoding.UTF8, "text/html") }));

        var (status, body, code) = await c.UploadPublicMediaAsync([0xFF, 0xD8, 0xFF]);

        Assert.Equal(HttpStatusCode.BadGateway, status);
        Assert.Null(body);
        Assert.Null(code);
    }

    [Theory]
    [InlineData(HttpStatusCode.InsufficientStorage, "media-quota-exceeded")]
    [InlineData(HttpStatusCode.InsufficientStorage, "disk-low")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "public-base-url-missing")]
    public async Task UploadPublicMedia_LoiHub_TraMaLoi_KhongNem(HttpStatusCode st, string err)
    {
        using var c = Client(new FakeHub(_ => Json("{\"error\":\"" + err + "\"}", st)));

        var (status, body, code) = await c.UploadPublicMediaAsync([0xFF, 0xD8, 0xFF]);

        Assert.Equal(st, status);
        Assert.Null(body);
        Assert.Equal(err, code);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    public async Task DeletePublicMedia_200Va404LaXong(HttpStatusCode st, bool ok)
    {
        var hub = new FakeHub(_ => new HttpResponseMessage(st));
        using var c = Client(hub);

        Assert.Equal(ok, await c.DeletePublicMediaAsync("/api/media/1-ab.jpg"));
        Assert.Equal(HttpMethod.Delete, hub.Requests[0].Method);
        Assert.Equal("/api/media/1-ab.jpg", hub.Urls[0]);
    }

    [Theory]
    [InlineData("/files/config/accounts.json")]
    [InlineData("/api/media/")]
    [InlineData("/api/media/a/b.jpg")]
    [InlineData("")]
    public async Task DeletePublicMedia_PathNgoaiKhoAnh_False_KhongGoiHub(string path)
    {
        var hub = new FakeHub(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var c = Client(hub);

        Assert.False(await c.DeletePublicMediaAsync(path));
        Assert.Empty(hub.Urls);
    }
}
