using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using Shopee.Core.Coordination;
using Shopee.Hub;
using static Shopee.Hub.Web.Tests.HarvestedProductsHttpTests;

namespace Shopee.Hub.Web.Tests;

/// <summary>
/// GĐ7 — extension <c>shopee-harvest</c> tự đẩy Hub bằng token chung: (1) SP bấm tay KHÔNG có nguồn (account / sheet / dòng) →
/// chỉ upsert <c>harvested_products</c>, không đụng <c>harvested_product_sources</c>; (2) Hub tự cắt khối riêng tư đã biết
/// (<see cref="HarvestRawGuard"/>) dù client gửi lên. Host thật + Postgres thật (fixture của <see cref="HarvestedProductsHttpTests"/>).
/// </summary>
public sealed class HarvestedPrivacyHttpTests : IClassFixture<HarvestHubApp>
{
    private readonly HarvestHubApp _app;

    public HarvestedPrivacyHttpTests(HarvestHubApp app) => _app = app;

    private static long IdMoi() => Random.Shared.NextInt64(1_000_000_000, 9_000_000_000);

    private static string Sha(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    private static HarvestedProductItem Sp(long itemId, string raw, string hash, string? acct = null, string? sheet = null, int? rowNo = null) =>
        new(itemId, 777, $"https://shopee.vn/product/777/{itemId}", "Ao", 100352, [100017], 0, 99000, 129000, 150000, 0, 1,
            ["https://down-vn.img.susercontent.com/file/a"], null, null, raw, hash, acct, sheet, rowNo, DateTimeOffset.UtcNow);

    private static string Raw(long itemId, string dataExtra = "") =>
        "{\"error\":null,\"data\":{" + dataExtra + "\"item\":{\"item_id\":" + itemId + ",\"shop_id\":777,\"title\":\"Áo\"}}}";

    private static async Task<HarvestedProductBatchResponse> Post(HttpClient c, params HarvestedProductItem[] items)
    {
        var res = await c.PostAsJsonAsync(HubRoutes.HarvestedProductsBatch,
            new HarvestedProductBatchRequest("ext-may", DateTimeOffset.UtcNow, [.. items]));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<HarvestedProductBatchResponse>())!;
    }

    private async Task<(string Raw, string Hash, int Count)?> Doc(long itemId)
    {
        await using var conn = await _app.MoDbAsync();
        await using var cmd = new NpgsqlCommand("SELECT raw::text, raw_hash, harvest_count FROM harvested_products WHERE item_id = $1;", conn);
        cmd.Parameters.AddWithValue(itemId);
        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) return null;
        return (rd.GetString(0), rd.GetString(1), rd.GetInt32(2));
    }

    private async Task<long> SoNguon(long itemId)
    {
        await using var conn = await _app.MoDbAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM harvested_product_sources WHERE item_id = $1;", conn);
        cmd.Parameters.AddWithValue(itemId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    [PgFact]
    public async Task SpKhongNguon_ChiLuuSp_KhongGhiDongNguon_GuiLaiIdempotent()
    {
        var c = await _app.KhachCoTokenAsync();
        var id = IdMoi();
        var raw = Raw(id, "\"account\":{},");
        var r = await Post(c, Sp(id, raw, Sha(raw)));
        var kq = Assert.Single(r.Results);
        Assert.True(kq.Ok, kq.Reason);
        Assert.True(kq.Applied);
        Assert.Equal(1, (await Doc(id))!.Value.Count);
        Assert.Equal(0, await SoNguon(id));

        var lai = Assert.Single((await Post(c, Sp(id, raw, Sha(raw)))).Results);
        Assert.True(lai.Ok);
        Assert.False(lai.Applied);
        Assert.Equal(1, (await Doc(id))!.Value.Count);
        Assert.Equal(0, await SoNguon(id));

        // Cùng SP sau đó được app lấy KÈM nguồn → có liên kết; SP vẫn một dòng.
        Assert.True(Assert.Single((await Post(c, Sp(id, raw, Sha(raw), "harvest", "Shop A", 3))).Results).Ok);
        Assert.Equal(1, await SoNguon(id));
    }

    [PgFact]
    public async Task RawSach_GiuNguyenRawVaHashCuaClient()
    {
        var c = await _app.KhachCoTokenAsync();
        var id = IdMoi();
        var raw = Raw(id, "\"account\":{},\"product_shipping\":{\"shipping_fee_info\":{\"ship_from_location\":\"Hà Nội\"}},");
        Assert.True(Assert.Single((await Post(c, Sp(id, raw, "hash-cua-client"))).Results).Ok);
        Assert.Equal("hash-cua-client", (await Doc(id))!.Value.Hash);
    }

    [PgFact]
    public async Task ClientGuiKhoiRiengTu_HubTuCat_TinhLaiHash_GuiLaiKhongTangDem()
    {
        var c = await _app.KhachCoTokenAsync();
        var id = IdMoi();
        var raw = Raw(id,
            "\"account\":{\"userid\":99,\"email\":\"bimat@x.vn\"}," +
            "\"product_shipping\":{\"delivery_address\":{\"state\":\"Hà Nội\",\"latitude\":21.0601},\"shipping_fee_info\":{\"price\":3,\"ship_from_location\":\"TP. HCM\"},\"channels\":[1]}," +
            "\"shop_vouchers\":[{\"signature\":\"sig-bi-mat\",\"customer_reference_id\":\"ref-bi-mat\"}],");
        var kq = Assert.Single((await Post(c, Sp(id, raw, "hash-client"))).Results);
        Assert.True(kq.Ok, kq.Reason);

        var dong = (await Doc(id))!.Value;
        foreach (var bimat in new[] { "bimat@x.vn", "21.0601", "delivery_address", "sig-bi-mat", "ref-bi-mat", "shop_vouchers", "channels" })
            Assert.DoesNotContain(bimat, dong.Raw);
        Assert.Contains("TP. HCM", dong.Raw);   // nơi gửi hàng là thuộc tính SP — giữ
        Assert.Contains("\"account\": {}", dong.Raw);
        var cat = HarvestRawGuard.Prepare(raw, "hash-client");
        Assert.True(cat.Scrubbed);
        Assert.Equal(Sha(cat.Raw), dong.Hash);
        Assert.NotEqual("hash-client", dong.Hash);

        // Gửi lại y nguyên: cắt ra cùng chuỗi → cùng hash → không tính là bản mới.
        var lai = Assert.Single((await Post(c, Sp(id, raw, "hash-client"))).Results);
        Assert.True(lai.Ok);
        Assert.False(lai.Applied);
        Assert.Equal(1, (await Doc(id))!.Value.Count);
    }

    [PgFact]
    public async Task KhoiRiengTuNapTrongKhoaTrung_TuChoiMuc_KhongGhi()
    {
        var c = await _app.KhachCoTokenAsync();
        var id = IdMoi();
        // data trùng: bản đầu có account thật, bản sau sạch (JsonDocument tra theo tên chỉ thấy bản sau).
        var raw = "{\"error\":null,\"data\":{\"account\":{\"userid\":1}},\"data\":{\"item\":{\"item_id\":" + id + ",\"shop_id\":777}}}";
        var kq = Assert.Single((await Post(c, Sp(id, raw, "h"))).Results);
        Assert.False(kq.Ok);
        Assert.Equal("raw-private", kq.Reason);
        Assert.Null(await Doc(id));
    }

    // ── Hàm thuần (không cần Postgres) ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("{\"data\":{\"item\":{}}}", false)]
    [InlineData("{\"data\":{\"account\":{},\"item\":{}}}", false)]
    [InlineData("{\"data\":{\"product_shipping\":null,\"item\":{}}}", false)]
    [InlineData("{\"data\":{\"product_shipping\":{},\"item\":{}}}", false)]
    [InlineData("{\"data\":{\"product_shipping\":{\"shipping_fee_info\":{\"ship_from_location\":\"HN\"}},\"item\":{}}}", false)]
    [InlineData("{\"data\":{\"account\":null,\"item\":{}}}", true)]
    [InlineData("{\"data\":{\"account\":{\"u\":1},\"item\":{}}}", true)]
    [InlineData("{\"data\":{\"shop_vouchers\":[],\"item\":{}}}", true)]
    [InlineData("{\"data\":{\"product_shipping\":{\"delivery_address\":null},\"item\":{}}}", true)]
    [InlineData("{\"data\":{\"product_shipping\":[1],\"item\":{}}}", true)]
    [InlineData("{\"data\":{\"product_shipping\":{\"shipping_fee_info\":{\"ship_from_location\":\"HN\",\"price\":1}},\"item\":{}}}", true)]
    [InlineData("{\"data\":{\"item\":{}},\"data\":{\"account\":{\"u\":1}}}", true)]
    [InlineData("{\"item\":{\"account\":{\"u\":1}}}", false)]
    public void NeedsScrub_TheoTungKhoiRiengTu(string raw, bool can)
    {
        Assert.Equal(can, HarvestRawGuard.NeedsScrub(raw));
    }

    [Fact]
    public void Prepare_RawSach_TraNguyenChuoiVaHashClient()
    {
        const string raw = "{\"error\":null,\"data\":{\"account\":{},\"item\":{\"item_id\":1,\"title\":\"Áo 😀\"}}}";
        var p = HarvestRawGuard.Prepare(raw, "h");
        Assert.False(p.Scrubbed);
        Assert.Same(raw, p.Raw);
        Assert.Equal("h", p.Hash);
    }

    [Fact]
    public void Prepare_Cat_GiuThuTuKhoaVaNoiGui()
    {
        const string raw = "{\"error\":null,\"data\":{\"a\":1,\"account\":{\"u\":1},\"item\":{\"item_id\":1},\"shop_vouchers\":[1]," +
                           "\"product_shipping\":{\"x\":1,\"shipping_fee_info\":{\"y\":2,\"ship_from_location\":\"Hà Nội\"}},\"z\":2}}";
        var p = HarvestRawGuard.Prepare(raw, "h");
        Assert.True(p.Scrubbed);
        Assert.Equal("{\"error\":null,\"data\":{\"a\":1,\"account\":{},\"item\":{\"item_id\":1}," +
                     "\"product_shipping\":{\"shipping_fee_info\":{\"ship_from_location\":\"Hà Nội\"}},\"z\":2}}", p.Raw);
        Assert.Equal(Sha(p.Raw), p.Hash);
    }

    [Fact]
    public void Prepare_KhoaTrungCanCat_Nem()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            HarvestRawGuard.Prepare("{\"data\":{\"account\":{\"u\":1},\"account\":{},\"item\":{}}}", "h"));
    }
}
