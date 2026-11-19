using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Shopee.Core.Coordination;

namespace Shopee.Core.Tests;

/// <summary>Hàm client của kho ảnh gốc Hub (GĐ9): <see cref="HubClient.GetMissingSourceImagesAsync"/>,
/// <see cref="HubClient.PutSourceImageAsync"/>, <see cref="HubClient.GetSourceImageLinksAsync"/>,
/// <see cref="HubClient.GetSourceImageStatsAsync"/>, <see cref="HubClient.DeleteAllSourceImagesAsync"/> với Hub GIẢ.</summary>
public sealed class HubClientSourceImageTests
{
    private sealed class FakeHub(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string? Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request.Method, request.RequestUri!.PathAndQuery, body));
            return respond(request, body);
        }
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HubClient Client(FakeHub hub) =>
        new(new HubClientConfig { Enabled = true, BaseUrl = "http://hub.test/", ApiToken = "t" }, "may-1", hub);

    private static string Id(int n) => $"vn-11134207-test-{n:D6}";

    [Fact]
    public async Task Missing_KhuTrung_ChiaLo200_GopTheoThuTu()
    {
        var lo = new List<List<string>>();
        var hub = new FakeHub((_, body) =>
        {
            var ids = JsonSerializer.Deserialize<SourceImageIdsRequest>(body!, Web)!.Ids!;
            lo.Add(ids);
            return Json(JsonSerializer.Serialize(new SourceImageMissingResponse([.. ids.Where((_, i) => i % 2 == 0)]), Web));
        });
        using var c = Client(hub);
        var ids = Enumerable.Range(0, 450).Select(Id).Concat([Id(1), Id(2)]);

        var missing = await c.GetMissingSourceImagesAsync(ids);

        Assert.Equal([200, 200, 50], lo.Select(l => l.Count));
        Assert.All(hub.Calls, x => Assert.Equal(HubRoutes.SourceImagesMissing, x.Url));
        Assert.Equal(225, missing.Count);
        Assert.Equal(Id(0), missing[0]);
    }

    [Fact]
    public async Task Missing_IdSai_NemTruocKhiGoiHub()
    {
        var hub = new FakeHub((_, _) => throw new InvalidOperationException("không được gọi"));
        using var c = Client(hub);

        await Assert.ThrowsAsync<ArgumentException>(() => c.GetMissingSourceImagesAsync([Id(1), "../../x"]));
        await Assert.ThrowsAsync<ArgumentException>(() => c.PutSourceImageAsync("a/../../escaped", [1, 2, 3]));
        await Assert.ThrowsAsync<ArgumentException>(() => c.GetSourceImageLinksAsync("shop", [null!]));
        await Assert.ThrowsAsync<ArgumentException>(() => c.GetSourceImageLinksAsync("", [Id(1)]));
        await Assert.ThrowsAsync<ArgumentException>(() => c.GetSourceImageLinksAsync(new string('s', 101), [Id(1)]));
        Assert.Empty(hub.Calls);
    }

    [Fact]
    public async Task Put_200Existed_507MaLoi_KhongNem()
    {
        var lan = 0;
        var hub = new FakeHub((req, _) => ++lan == 1
            ? Json("{\"id\":\"" + Id(1) + "\",\"existed\":true,\"replaced\":false,\"size\":10,\"contentType\":\"image/jpeg\"}")
            : Json("{\"error\":\"source-quota-exceeded\"}", HttpStatusCode.InsufficientStorage));
        using var c = Client(hub);

        var ok = await c.PutSourceImageAsync(Id(1), [0xFF, 0xD8, 0xFF]);
        var day = await c.PutSourceImageAsync(Id(2), [0xFF, 0xD8, 0xFF]);

        Assert.Equal(HttpStatusCode.OK, ok.Status);
        Assert.True(ok.Body!.Existed);
        Assert.Equal((HttpMethod.Put, HubRoutes.SourceImages + "/" + Id(1)), (hub.Calls[0].Method, hub.Calls[0].Url));
        Assert.Equal(HttpStatusCode.InsufficientStorage, day.Status);
        Assert.Null(day.Body);
        Assert.Equal("source-quota-exceeded", day.ErrorCode);
    }

    [Fact]
    public async Task Links_GopLo_UrlKhongCongKhai_502_LoiHub_TraMa()
    {
        var hub = new FakeHub((_, body) =>
        {
            var r = JsonSerializer.Deserialize<SourceImageLinksRequest>(body!, Web)!;
            Assert.Equal("shop-a", r.ShopKey);
            return Json(JsonSerializer.Serialize(new SourceImageLinksResponse(
                [.. r.Ids!.Skip(1).Select(i => new SourceImageLink(i, $"https://api.example.test/api/v/{i}.jpg"))],
                [r.Ids![0]]), Web));
        });
        using var c = Client(hub);

        var (status, body, code) = await c.GetSourceImageLinksAsync("shop-a", Enumerable.Range(0, 250).Select(Id));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Null(code);
        Assert.Equal([Id(0), Id(200)], body!.Missing);
        Assert.Equal(248, body.Items.Count);
        Assert.Equal(2, hub.Calls.Count);

        using var c2 = Client(new FakeHub((_, _) => Json(JsonSerializer.Serialize(new SourceImageLinksResponse(
            [new SourceImageLink(Id(1), "https://127.0.0.1/api/v/x.jpg")], []), Web))));
        Assert.Equal((HttpStatusCode.BadGateway, "url-not-public"),
            await c2.GetSourceImageLinksAsync("s", [Id(1)]) is var r2 ? (r2.Status, r2.ErrorCode) : default);

        using var c3 = Client(new FakeHub((_, _) => Json("{\"error\":\"public-base-url-missing\"}", HttpStatusCode.ServiceUnavailable)));
        var r3 = await c3.GetSourceImageLinksAsync("s", [Id(1)]);
        Assert.Equal((HttpStatusCode.ServiceUnavailable, "public-base-url-missing"), (r3.Status, r3.ErrorCode));
        Assert.Null(r3.Body);
    }

    [Fact]
    public async Task Stats_VaDeleteAll_GuiDungRouteVaChuoiXacNhan()
    {
        var hub = new FakeHub((req, _) => req.Method == HttpMethod.Get
            ? Json("{\"diskTotalBytes\":100,\"diskFreeBytes\":40,\"sourceCount\":3,\"sourceBytes\":30,\"sourceMaxBytes\":99,"
                   + "\"mediaCount\":1,\"mediaBytes\":5,\"mediaMaxBytes\":77}")
            : Json("{\"deleted\":3,\"remaining\":0,\"freedBytes\":30,\"warning\":\"w\"}"));
        using var c = Client(hub);

        var s = await c.GetSourceImageStatsAsync();
        var d = await c.DeleteAllSourceImagesAsync();

        Assert.Equal((100L, 40L, 3L, 30L, 99L, 1L, 5L, 77L),
            (s.DiskTotalBytes, s.DiskFreeBytes, s.SourceCount, s.SourceBytes, s.SourceMaxBytes, s.MediaCount, s.MediaBytes, s.MediaMaxBytes));
        Assert.Equal((3L, 0L, 30L, "w"), (d.Deleted, d.Remaining, d.FreedBytes, d.Warning));
        Assert.Equal((HttpMethod.Get, HubRoutes.SourceImagesStats), (hub.Calls[0].Method, hub.Calls[0].Url));
        Assert.Equal((HttpMethod.Delete, HubRoutes.SourceImages), (hub.Calls[1].Method, hub.Calls[1].Url));
        Assert.Equal(SourceImageLimits.DeleteAllConfirm,
            JsonSerializer.Deserialize<SourceImageDeleteAllRequest>(hub.Calls[1].Body!, Web)!.Confirm);
    }

    [Fact]
    public async Task DeleteMot_200True_404False_IdSaiNem()
    {
        var hub = new FakeHub((req, _) => req.RequestUri!.AbsolutePath.EndsWith(Id(1))
            ? Json("{\"deleted\":true}") : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var c = Client(hub);

        Assert.True(await c.DeleteSourceImageAsync(Id(1)));
        Assert.False(await c.DeleteSourceImageAsync(Id(2)));
        await Assert.ThrowsAsync<ArgumentException>(() => c.DeleteSourceImageAsync("../../x"));
        Assert.Equal((HttpMethod.Delete, HubRoutes.SourceImages + "/" + Id(1)), (hub.Calls[0].Method, hub.Calls[0].Url));
        Assert.Equal(2, hub.Calls.Count);
    }

    [Fact]
    public async Task DeleteAll_400_Nem()
    {
        using var c = Client(new FakeHub((_, _) => Json("{\"error\":\"confirm-required\"}", HttpStatusCode.BadRequest)));
        await Assert.ThrowsAsync<HttpRequestException>(() => c.DeleteAllSourceImagesAsync());
    }
}
