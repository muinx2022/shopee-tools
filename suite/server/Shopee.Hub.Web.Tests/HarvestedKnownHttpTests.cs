using System.Net;
using System.Net.Http.Json;
using Npgsql;
using Shopee.Core.Coordination;
using static Shopee.Hub.Web.Tests.HarvestedProductsHttpTests;

namespace Shopee.Hub.Web.Tests;

/// <summary>
/// <b>Hỏi SP nào đã có dữ liệu còn hạn (<c>POST /api/harvested-products/known</c>)</b> — app Harvest dùng để bỏ qua SP đã có,
/// không mở trang. Host thật + Postgres thật, dùng lại fixture của <see cref="HarvestedProductsHttpTests"/>. Dữ liệu nạp qua
/// đúng route batch (mốc cũ giả bằng <c>harvestedAt</c> lùi ngày — Hub nhận mốc cũ bao lâu cũng được).
/// </summary>
public sealed class HarvestedKnownHttpTests : IClassFixture<HarvestHubApp>
{
    private readonly HarvestHubApp _app;

    public HarvestedKnownHttpTests(HarvestHubApp app) => _app = app;

    private static long IdMoi() => Random.Shared.NextInt64(1_000_000_000, 9_000_000_000);

    private static HarvestedProductItem Sp(long itemId, string hash, DateTimeOffset at) =>
        new(itemId, 777, $"https://shopee.vn/product/777/{itemId}", "Ao", 100352, [100017], 0, 99000, 129000, 150000, 1, 1,
            ["https://down-vn.img.susercontent.com/file/a"], null, null,
            "{\"error\":null,\"data\":{\"item\":{\"item_id\":" + itemId + ",\"shop_id\":777,\"title\":\"Ao\"}}}",
            hash, "harvest", "Shop A", 1, at);

    private static async Task Nap(HttpClient c, params HarvestedProductItem[] items)
    {
        var res = await c.PostAsJsonAsync(HubRoutes.HarvestedProductsBatch,
            new HarvestedProductBatchRequest("may-test", DateTimeOffset.UtcNow, [.. items]));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = (await res.Content.ReadFromJsonAsync<HarvestedProductBatchResponse>())!;
        Assert.All(body.Results, r => Assert.True(r.Ok, r.Reason));
    }

    private static async Task<HarvestedKnownResponse> Hoi(HttpClient c, IEnumerable<long> ids, int maxAgeDays)
    {
        var res = await c.PostAsJsonAsync(HubRoutes.HarvestedProductsKnown, new HarvestedKnownRequest([.. ids], maxAgeDays));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<HarvestedKnownResponse>())!;
    }

    [PgFact]
    public async Task ConHan_TraVe_IdChuaCo_KhongTra_KhongKemRaw()
    {
        var c = await _app.KhachCoTokenAsync();
        var co = IdMoi();
        var chuaCo = IdMoi();
        await Nap(c, Sp(co, "h1", DateTimeOffset.UtcNow.AddDays(-2)));

        var res = await c.PostAsJsonAsync(HubRoutes.HarvestedProductsKnown, new HarvestedKnownRequest([co, chuaCo, co], 30));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var text = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("raw", text, StringComparison.OrdinalIgnoreCase);
        var body = System.Text.Json.JsonSerializer.Deserialize<HarvestedKnownResponse>(text,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var item = Assert.Single(body.Items);
        Assert.Equal(co, item.ItemId);
        Assert.True(item.HarvestedAt > DateTimeOffset.UtcNow.AddDays(-3));
    }

    /// <summary>maxAgeDays cắt đúng: bản lấy 40 ngày trước không "còn hạn" với 30 ngày, còn hạn với 45 ngày.</summary>
    [PgFact]
    public async Task MaxAgeDays_CatTheoMocLayGanNhat()
    {
        var c = await _app.KhachCoTokenAsync();
        var cu = IdMoi();
        var moi = IdMoi();
        await Nap(c, Sp(cu, "h-cu", DateTimeOffset.UtcNow.AddDays(-40)), Sp(moi, "h-moi", DateTimeOffset.UtcNow.AddDays(-1)));

        Assert.Equal([moi], (await Hoi(c, [cu, moi], 30)).Items.Select(i => i.ItemId));
        Assert.Equal(new[] { cu, moi }.Order(), (await Hoi(c, [cu, moi], 45)).Items.Select(i => i.ItemId).Order());
        Assert.Empty((await Hoi(c, [cu], 1)).Items);
    }

    /// <summary>Lấy lại một SP với JSON Y HỆT (raw_hash trùng) phải làm mới mốc "lần lấy gần nhất" — không thì SP cũ hơn hạn bị
    /// lấy lại mãi mỗi lượt dù vừa lấy xong.</summary>
    [PgFact]
    public async Task LayLaiHashTrung_LamMoiMoc_ThanhConHan()
    {
        var c = await _app.KhachCoTokenAsync();
        var id = IdMoi();
        await Nap(c, Sp(id, "h-y-het", DateTimeOffset.UtcNow.AddDays(-40)));
        Assert.Empty((await Hoi(c, [id], 30)).Items);

        await Nap(c, Sp(id, "h-y-het", DateTimeOffset.UtcNow));

        var item = Assert.Single((await Hoi(c, [id], 30)).Items);
        Assert.True(item.HarvestedAt > DateTimeOffset.UtcNow.AddMinutes(-5), $"mốc chưa được làm mới: {item.HarvestedAt:o}");
        await using var conn = await _app.MoDbAsync();
        await using var cmd = new NpgsqlCommand("SELECT harvest_count FROM harvested_products WHERE item_id = $1;", conn);
        cmd.Parameters.AddWithValue(id);
        Assert.Equal(1, (int)(await cmd.ExecuteScalarAsync())!);   // hash trùng: không đếm là bản mới, chỉ nhích mốc
    }

    [PgFact]
    public async Task Tran500_DemCaIdTrung_501Bi400()
    {
        var c = await _app.KhachCoTokenAsync();
        var id = IdMoi();
        await Nap(c, Sp(id, "h-tran", DateTimeOffset.UtcNow));

        var du500 = Enumerable.Repeat(id, 10).Concat(Enumerable.Range(1, 490).Select(i => (long)i)).ToList();
        Assert.Equal(HarvestedKnownLimits.MaxItems, du500.Count);
        Assert.Contains((await Hoi(c, du500, 30)).Items, i => i.ItemId == id);

        var res = await c.PostAsJsonAsync(HubRoutes.HarvestedProductsKnown, new HarvestedKnownRequest([.. du500, id], 30));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("too-many-ids", await res.Content.ReadAsStringAsync());
    }

    [PgFact]
    public async Task ThamSoSai_Bi400()
    {
        var c = await _app.KhachCoTokenAsync();
        foreach (var bad in new int?[] { null, 0, -1, HarvestedKnownLimits.MaxAgeDays + 1 })
        {
            var res = await c.PostAsJsonAsync(HubRoutes.HarvestedProductsKnown, new HarvestedKnownRequest([1], bad));
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Contains("max-age-days", await res.Content.ReadAsStringAsync());
        }
        var thieu = await c.PostAsJsonAsync(HubRoutes.HarvestedProductsKnown, new HarvestedKnownRequest(null, 30));
        Assert.Equal(HttpStatusCode.BadRequest, thieu.StatusCode);
        Assert.Contains("item-ids-required", await thieu.Content.ReadAsStringAsync());
        Assert.Empty((await Hoi(c, [], 30)).Items);
    }

    [PgFact]
    public async Task KhongCoToken_Thi401()
    {
        await _app.KhachCoTokenAsync();
        var c = _app.CreateClient();
        var res = await c.PostAsJsonAsync(HubRoutes.HarvestedProductsKnown, new HarvestedKnownRequest([1], 30));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
