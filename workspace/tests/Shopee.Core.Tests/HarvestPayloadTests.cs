using System.Text.Json;
using System.Text.Json.Nodes;
using Shopee.Core.Harvest;
using static Shopee.Core.Tests.HarvestTestData;

namespace Shopee.Core.Tests;

/// <summary>
/// GĐ4: <see cref="HarvestPayloadCleaner"/> (riêng tư, khối phụ thuộc người xem, \u0000 / surrogate lẻ, số giữ nguyên,
/// hash ổn định) và <see cref="ShopeePdpParser"/> trên 7 fixture JSON <c>get_pc</c> thật.
/// </summary>
public sealed class HarvestPayloadTests
{
    private const string Img = ShopeePdpParser.ImageBase;

    // ── Cleaner ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Clean_Fixture_BoKhoiPhuThuocNguoiXem_GiuItemVaNoiGui(string name)
    {
        var clean = HarvestPayloadCleaner.Clean(Fixture(name));
        var data = JsonNode.Parse(clean)!["data"]!.AsObject();

        Assert.Equal("{}", data["account"]!.ToJsonString());
        Assert.False(data.ContainsKey("shop_vouchers"));
        Assert.False(data.ContainsKey("top_banner"));
        Assert.False(data.ContainsKey("coin_info"));
        Assert.False(data["product_review"]!.AsObject().ContainsKey("liked"));
        Assert.True(data["product_review"]!.AsObject().ContainsKey("rating_star"));
        var ship = data["product_shipping"]!.AsObject();
        Assert.Equal(["shipping_fee_info"], ship.Select(p => p.Key));
        Assert.Equal(["ship_from_location"], ship["shipping_fee_info"]!.AsObject().Select(p => p.Key));
        var noiGuiGoc = (string?)JsonNode.Parse(Fixture(name))!["data"]!["product_shipping"]!["shipping_fee_info"]!["ship_from_location"];
        Assert.False(string.IsNullOrEmpty(noiGuiGoc));
        Assert.Equal(noiGuiGoc, (string?)ship["shipping_fee_info"]!["ship_from_location"]);
        Assert.False(clean.Contains("delivery_address", StringComparison.Ordinal));
        // Phần SP giữ nguyên (so với JSON gốc): item, ảnh, mô tả.
        var goc = JsonNode.Parse(Fixture(name))!["data"]!;
        Assert.True(JsonNode.DeepEquals(goc["item"], data["item"]));
        Assert.True(JsonNode.DeepEquals(goc["product_images"], data["product_images"]));
        Assert.True(JsonNode.DeepEquals(goc["product_description"], data["product_description"]));
        // Làm sạch 2 lần = 1 lần.
        Assert.Equal(clean, HarvestPayloadCleaner.Clean(clean));
    }

    public static TheoryData<string> AllFixtures()
    {
        var d = new TheoryData<string>();
        foreach (var f in Fixtures) d.Add(f);
        return d;
    }

    [Fact]
    public void Clean_SoGiuNguyenVanBan_KeCaLonHon2Mu53VaSoThuc()
    {
        // Số thật trong fixture: rating_star số thực dài, promotionid 16 chữ số (trong shop_vouchers — bị bỏ, nên kiểm
        // số trong item: price 11 chữ số).
        var clean = HarvestPayloadCleaner.Clean(Fixture("pdp-motaanh-brand-voucher.json"));
        Assert.Contains("\"rating_star\":4.77022977022977", clean);
        Assert.Contains("\"price\":1830000000", clean);

        const string json = "{\"error\":null,\"data\":{\"item\":{\"item_id\":1,\"big\":123456789012345678901234567890,"
                            + "\"p53\":9007199254740993,\"f\":4.7702297702297701234,\"e\":1.5E+300,\"neg\":-0.0,\"z\":10.50}}}";
        var c = HarvestPayloadCleaner.Clean(json);
        Assert.Contains("\"big\":123456789012345678901234567890", c);
        Assert.Contains("\"p53\":9007199254740993", c);
        Assert.Contains("\"f\":4.7702297702297701234", c);
        Assert.Contains("\"e\":1.5E+300", c);
        Assert.Contains("\"neg\":-0.0", c);
        Assert.Contains("\"z\":10.50", c);
    }

    [Fact]
    public void Clean_ChuViet_KhongEscape()
    {
        var clean = HarvestPayloadCleaner.Clean(Fixture("pdp-2tang-8model.json"));
        Assert.Contains("Vòng tay DW Tennis Classic Bracelet", clean);
    }

    [Fact]
    public void Hash_CungSpChiKhacKhoiBiBo_TrungHash_KhacTen_KhacHash()
    {
        var goc = Fixture("pdp-motaanh-brand-voucher.json");
        var a = JsonNode.Parse(goc)!;
        var d = a["data"]!;
        d["shop_vouchers"] = new JsonArray(new JsonObject { ["signature"] = "khac-hoan-toan", ["promotionid"] = 1 });
        d["top_banner"] = new JsonObject { ["x"] = 1 };
        d["coin_info"] = new JsonObject { ["coin_earn_label"] = "khác" };
        d["product_review"]!["liked"] = true;
        d["account"] = new JsonObject { ["userid"] = 999, ["username"] = "ai-do" };
        var ship = d["product_shipping"]!;
        ship["delivery_address"] = new JsonObject { ["address"] = "12 Nguyễn Huệ", ["latitude"] = 10.77 };
        ship["ungrouped_channel_infos"] = new JsonArray();
        ship["shipping_fee_info"]!["price"] = new JsonObject { ["single_value"] = 1500000000 };
        ship["show_shipping_to"] = false;
        var price = d["product_price"]!;
        price["final_price_info"] = new JsonObject { ["final_price"] = 1 };
        price["final_price_vouchers"] = new JsonArray(new JsonObject { ["voucher_code"] = "XYZ" });
        d["price_breakdown"] = new JsonObject { ["voucher"] = 5 };
        d["button_group"] = new JsonObject { ["buy_now"] = "khác" };
        var doi = a.ToJsonString();

        var h1 = HarvestPayloadCleaner.Hash(HarvestPayloadCleaner.Clean(goc));
        var h2 = HarvestPayloadCleaner.Hash(HarvestPayloadCleaner.Clean(doi));
        Assert.Equal(h1, h2);
        Assert.Matches("^[0-9a-f]{64}$", h1);
        Assert.DoesNotContain("Nguyễn Huệ", HarvestPayloadCleaner.Clean(doi));
        Assert.DoesNotContain("ai-do", HarvestPayloadCleaner.Clean(doi));

        // Giá / rating / đã bán là thay đổi THẬT của SP → hash đổi.
        var giaDoi = JsonNode.Parse(doi)!;
        giaDoi["data"]!["product_price"]!["price"] = new JsonObject { ["single_value"] = 999 };
        Assert.NotEqual(h1, HarvestPayloadCleaner.Hash(HarvestPayloadCleaner.Clean(giaDoi.ToJsonString())));
        var ratingDoi = JsonNode.Parse(doi)!;
        ratingDoi["data"]!["product_review"]!["rating_star"] = 1.5;
        Assert.NotEqual(h1, HarvestPayloadCleaner.Hash(HarvestPayloadCleaner.Clean(ratingDoi.ToJsonString())));

        d["item"]!["title"] = "Tên khác";
        Assert.NotEqual(h1, HarvestPayloadCleaner.Hash(HarvestPayloadCleaner.Clean(a.ToJsonString())));
    }

    [Fact]
    public void Clean_BoNullVaSurrogateLe_GiuCapHopLe_GiuChuoiTrongNhuEscape()
    {
        // \u0000 escape, \u0000 thô, \ud83d lẻ (escape), \ude00 lẻ (escape), cặp hợp lệ escape, \\u0000 là chữ thường
        // (gạch chéo đã escape), surrogate thô lẻ.
        var json = "{\"error\":null,\"data\":{\"item\":{\"item_id\":1,"
                   + "\"title\":\"a\\u0000b\0c\\ud83dd\\ude00e\\ud83d\\ude00f\\\\u0000g\uD83Dh\","
                   + "\"k\\u0000\":1}}}";
        var clean = HarvestPayloadCleaner.Clean(json);
        var item = JsonNode.Parse(clean)!["data"]!["item"]!;
        Assert.Equal("abcde\U0001F600f\\u0000gh", (string?)item["title"]);
        Assert.True(item.AsObject().ContainsKey("k"));
        Assert.False(clean.Contains('\0'));
        Assert.DoesNotContain("\\u0000\"", clean, StringComparison.Ordinal);   // escape \u0000 trong khoá "k\u0000" đã bỏ
    }

    [Fact]
    public void StripInvalidText_BoNullVaSurrogateLe()
    {
        Assert.Equal("ab\U0001F600c", HarvestPayloadCleaner.StripInvalidText("a\0b\U0001F600\uD83Dc\uDE00"));
        Assert.Equal("", HarvestPayloadCleaner.StripInvalidText(null));
        Assert.Equal("binh thuong", HarvestPayloadCleaner.StripInvalidText("binh thuong"));
    }

    [Theory]
    [InlineData("khong phai json")]
    [InlineData("[1,2]")]
    [InlineData("{\"error\":null,\"data\":null}")]
    [InlineData("{\"error\":null,\"data\":{\"item\":null}}")]
    public void Clean_JsonHong_NemRejected(string json)
    {
        Assert.Throws<HarvestItemRejectedException>(() => HarvestPayloadCleaner.Clean(json));
    }

    /// <summary>JSON sâu 80 tầng (vượt <see cref="HarvestPayloadCleaner.MaxDepth"/> = 64, cũng là trần đọc của Hub) và
    /// khoá trùng → lỗi CỦA MỤC (<see cref="HarvestItemRejectedException"/>), không phải lỗi hạ tầng.</summary>
    [Fact]
    public void Clean_SauQua_KhoaTrung_NemRejected()
    {
        var sau = "{\"error\":null,\"data\":{\"item\":{\"item_id\":1,\"x\":" + new string('[', 80) + new string(']', 80) + "}}}";
        var ex = Assert.Throws<HarvestItemRejectedException>(() => HarvestPayloadCleaner.Clean(sau));
        Assert.IsAssignableFrom<JsonException>(ex.InnerException);

        // Khoá trùng ở gốc data (nổ lúc dựng JsonObject) và sâu bên trong item.
        Assert.Throws<HarvestItemRejectedException>(() => HarvestPayloadCleaner.Clean(
            "{\"error\":null,\"data\":{\"item\":{\"item_id\":1},\"item\":{\"item_id\":2}}}"));
        Assert.Throws<HarvestItemRejectedException>(() => HarvestPayloadCleaner.Clean(
            "{\"error\":null,\"data\":{\"item\":{\"item_id\":1,\"models\":[{\"a\":1,\"a\":2}]}}}"));

        // Sâu đúng 60 tầng (dưới trần, kể cả bộ ghi) vẫn qua.
        var vua = "{\"error\":null,\"data\":{\"item\":{\"item_id\":1,\"x\":" + new string('[', 60) + new string(']', 60) + "}}}";
        Assert.Contains(new string('[', 60), HarvestPayloadCleaner.Clean(vua));
        Assert.Throws<HarvestItemRejectedException>(() => ShopeePdpParser.Parse(sau));
        Assert.Throws<HarvestItemRejectedException>(() => ShopeePdpParser.Parse("{\"data\":{\"item\":{\"title\":\"x\"}}}"));
    }

    // ── Parser trên 7 fixture ──────────────────────────────────────────────────────────────────────────────────────

    private static ShopeePdpSummary P(string name) => ShopeePdpParser.Parse(HarvestPayloadCleaner.Clean(Fixture(name)));

    [Fact]
    public void Parse_1Tang4Model1Anh_AttributesNull()
    {
        var s = P("pdp-1tang-4model-1anh.json");
        Assert.Equal(26765720617, s.ItemId);
        Assert.Equal(1308236305, s.ShopId);
        Assert.Equal([100637L, 100725, 101287], s.CategoryIds);
        Assert.Equal(101287, s.CatId);
        Assert.Equal(0, s.BrandId);
        Assert.Equal((19500L, 27000L, 19500L), (s.PriceMin!.Value, s.PriceMax!.Value, s.PriceBeforeDiscount!.Value));
        Assert.Equal((1, 4), (s.TierCount, s.ModelCount));
        Assert.Equal([Img + "vn-11134207-7ras8-m1vzny7g6fun06"], s.ImageUrls);
        Assert.Empty(s.VideoUrls);
        Assert.Null(s.SizeChartUrl);
        Assert.Equal((22679L, 23000L), (s.Models[0].Price!.Value, s.Models[0].PriceBeforeDiscount!.Value));
        Assert.StartsWith("hot ná cao su", s.Name);
    }

    [Fact]
    public void Parse_70Model_DanhMuc4Cap()
    {
        var s = P("pdp-1tang-70model-4capdm.json");
        Assert.Equal(29609621870, s.ItemId);
        Assert.Equal(864686879, s.ShopId);
        Assert.Equal([100637L, 100725, 101277, 101847], s.CategoryIds);
        Assert.Equal(101847, s.CatId);
        Assert.Equal((49000L, 89000L, 110000L), (s.PriceMin!.Value, s.PriceMax!.Value, s.PriceBeforeDiscount!.Value));
        Assert.Equal((1, 70), (s.TierCount, s.ModelCount));
        Assert.Equal(9, s.ImageUrls.Length);
        Assert.Empty(s.VideoUrls);
        Assert.Null(s.SizeChartUrl);
    }

    [Fact]
    public void Parse_2Tang8Model_CoVideo_DuItemHasVideoFalse_SizeChartRong()
    {
        var json = HarvestPayloadCleaner.Clean(Fixture("pdp-2tang-8model.json"));
        Assert.Contains("\"item_has_video\":false", json);   // Shopee báo không có video …
        var s = ShopeePdpParser.Parse(json);
        Assert.Equal(26393229156, s.ItemId);
        Assert.Equal(1490438686, s.ShopId);
        Assert.Equal([100009L, 100026], s.CategoryIds);
        Assert.Equal(100026, s.CatId);
        Assert.Equal((219000L, 274000L, 360000L), (s.PriceMin!.Value, s.PriceMax!.Value, s.PriceBeforeDiscount!.Value));
        Assert.Equal((2, 8), (s.TierCount, s.ModelCount));
        Assert.Equal(5, s.ImageUrls.Length);
        // … nhưng product_images.video có: default_format trước, formats sau.
        Assert.Equal(
        [
            "https://mms.vod.susercontent.com/api/v4/11110105/mms/vn-11110105-6v8h1-mhh2y0micrgn8c.default.mp4",
            "https://down-zl-vn.vod.susercontent.com/api/v4/11110105/mms/vn-11110105-6v8h1-mhh2y0micrgn8c.16000081764039404.mp4",
        ], s.VideoUrls);
        Assert.Null(s.SizeChartUrl);   // size_chart = ""
        var m = s.Models[0];
        Assert.Equal(297047688529, m.ModelId);
        Assert.Equal("QT giấy lau bóng,Fullbox", m.Name);
        Assert.Equal([3, 1], m.TierIndex);
        Assert.Equal((274000L, 410000L), (m.Price!.Value, m.PriceBeforeDiscount!.Value));
    }

    [Fact]
    public void Parse_DanUrl_AnhSg_CdnVn()
    {
        var s = P("pdp-danurl-1tang-6model-anhsg.json");
        Assert.Equal(41557430104, s.ItemId);
        Assert.Equal(1551966364, s.ShopId);
        Assert.Equal([100013L, 100075, 100284, 100481], s.CategoryIds);
        Assert.Equal(100481, s.CatId);
        Assert.Equal((20000L, 79000L, 26000L), (s.PriceMin!.Value, s.PriceMax!.Value, s.PriceBeforeDiscount!.Value));
        Assert.Equal((1, 6), (s.TierCount, s.ModelCount));
        Assert.Equal(8, s.ImageUrls.Length);
        Assert.Equal(Img + "sg-11134201-7rdyt-mbt9kr3b9571b5", s.ImageUrls[0]);
        Assert.All(s.ImageUrls, u => Assert.StartsWith(Img + "sg-", u));
        Assert.Empty(s.VideoUrls);
    }

    [Fact]
    public void Parse_KhongBienThe_TangGia_0Tang_ModelGiaTruocGiam0LayGia()
    {
        var s = P("pdp-khong-bien-the.json");
        Assert.Equal(47908509202, s.ItemId);
        Assert.Equal(28599224, s.ShopId);
        Assert.Equal([100011L, 100054, 100242], s.CategoryIds);
        Assert.Equal(100242, s.CatId);
        Assert.Equal(0, s.TierCount);         // [{"name":"","options":[""]}]
        Assert.Equal(1, s.ModelCount);
        Assert.Equal((2500000L, 2500000L, 2500000L), (s.PriceMin!.Value, s.PriceMax!.Value, s.PriceBeforeDiscount!.Value));
        var m = Assert.Single(s.Models);
        Assert.Equal(405753503848, m.ModelId);
        Assert.Equal("", m.Name);
        Assert.Equal([0], m.TierIndex);
        Assert.Equal((2500000L, 2500000L), (m.Price!.Value, m.PriceBeforeDiscount!.Value));   // pbd 0 → price
        Assert.Equal(5, s.ImageUrls.Length);
        Assert.Empty(s.VideoUrls);
        Assert.Null(s.SizeChartUrl);
    }

    [Fact]
    public void Parse_MoTaAnh_BrandThat()
    {
        var s = P("pdp-motaanh-brand-voucher.json");
        Assert.Equal(41355991937, s.ItemId);
        Assert.Equal(180772792, s.ShopId);
        Assert.Equal(3653052, s.BrandId);
        Assert.Equal([100009L, 100026], s.CategoryIds);
        Assert.Equal((18300L, 18300L, 18300L), (s.PriceMin!.Value, s.PriceMax!.Value, s.PriceBeforeDiscount!.Value));
        Assert.Equal((1, 2), (s.TierCount, s.ModelCount));
        Assert.All(s.Models, m => Assert.Equal(18300L, m.PriceBeforeDiscount));
        Assert.Equal(5, s.ImageUrls.Length);
        Assert.Empty(s.VideoUrls);
        Assert.Null(s.SizeChartUrl);
    }

    [Fact]
    public void Parse_Video_DanhMuc2Cap_SizeChartCoAnh()
    {
        var s = P("pdp-video-1tang-11model-2capdm.json");
        Assert.Equal(24395796182, s.ItemId);
        Assert.Equal(1490438686, s.ShopId);
        Assert.Equal([100534L, 100573], s.CategoryIds);
        Assert.Equal(100573, s.CatId);
        Assert.Equal(0, s.BrandId);
        Assert.Equal((1499000L, 1599000L, 2200000L), (s.PriceMin!.Value, s.PriceMax!.Value, s.PriceBeforeDiscount!.Value));
        Assert.Equal((1, 11), (s.TierCount, s.ModelCount));
        Assert.Equal(9, s.ImageUrls.Length);
        Assert.Equal(2, s.VideoUrls.Length);
        Assert.EndsWith("vn-11110105-6v8h4-mhh2ryt237r838.default.mp4", s.VideoUrls[0]);
        Assert.Equal(Img + "vn-11134207-820l4-mk0i1kts0fsw2f", s.SizeChartUrl);
    }

    [Fact]
    public void Parse_CaBien_TierRongNull_BrandNull_GiaTruocGiam0()
    {
        const string json = "{\"error\":null,\"data\":{\"item\":{\"item_id\":\"7\",\"shop_id\":8,\"title\":\"x\",\"cat_id\":55,"
                            + "\"categories\":null,\"brand\":null,\"brand_id\":null,\"tier_variations\":null,\"models\":["
                            + "{\"model_id\":1,\"name\":\"\",\"price\":500000,\"price_before_discount\":0,\"extinfo\":null}],"
                            + "\"price_min\":500000,\"price_max\":500000,\"price_before_discount\":0,\"size_chart\":null,"
                            + "\"attributes\":null},\"product_images\":{\"images\":[\"https://cdn/x.jpg\",\"\",null],\"video\":null}}}";
        var s = ShopeePdpParser.Parse(json);
        Assert.Equal(7, s.ItemId);
        Assert.Equal(55, s.CatId);                 // không có categories → cat_id
        Assert.Empty(s.CategoryIds);
        Assert.Null(s.BrandId);
        Assert.Equal(0, s.TierCount);
        Assert.Equal(5, s.PriceBeforeDiscount);    // item pbd 0 → lấy từ model (0 → price)
        Assert.Equal(["https://cdn/x.jpg"], s.ImageUrls);
        Assert.Empty(s.VideoUrls);
        Assert.Empty(s.Models[0].TierIndex);
        Assert.Equal(0, ShopeePdpParser.Parse("{\"data\":{\"item\":{\"item_id\":1,\"tier_variations\":[]}}}").TierCount);
    }
}
