using Shopee.Core.MassUpload;

namespace Shopee.Core.Tests.MassUpload;

public class ShopeeListingReaderTests
{
    [Theory]
    [InlineData("pdp-1tang-4model-1anh.json", 26765720617, 101287, 100637)]
    [InlineData("pdp-1tang-70model-4capdm.json", 29609621870, 101847, 100637)]
    [InlineData("pdp-2tang-8model.json", 26393229156, 100026, 100009)]
    [InlineData("pdp-danurl-1tang-6model-anhsg.json", 41557430104, 100481, 100013)]
    [InlineData("pdp-khong-bien-the.json", 47908509202, 100242, 100011)]
    [InlineData("pdp-motaanh-brand-voucher.json", 41355991937, 100026, 100009)]
    [InlineData("pdp-video-1tang-11model-2capdm.json", 24395796182, 100573, 100534)]
    public void Nganh_la_la_phan_tu_cuoi_cua_categories_khong_phai_cat_id(string fixture, long itemId, long leaf, long top)
    {
        var l = MassUploadTestData.Fixture(fixture);
        Assert.Equal(itemId, l.ItemId);
        Assert.Equal(leaf, l.LeafCategoryId);
        Assert.Equal(top, l.TopCategoryId);
        Assert.Equal(leaf, l.CategoryIds[^1]);
        Assert.False(string.IsNullOrWhiteSpace(l.Title));
        Assert.NotEmpty(l.Images);
        Assert.All(l.Images, i => Assert.DoesNotContain("://", i)); // id ảnh gốc, không phải URL
        Assert.NotEmpty(l.Models);
    }

    [Fact]
    public void Gia_lay_price_before_discount_neu_duong_khong_thi_price_chia_100000()
    {
        // 2tang: model [0,0] price 21900000000, before 36000000000 → 360000
        var l = MassUploadTestData.Fixture("pdp-2tang-8model.json");
        Assert.Equal(360_000, l.Models.Single(m => m.TierIndex.SequenceEqual([0, 0])).Price);
        Assert.Equal(410_000, l.Models.Single(m => m.TierIndex.SequenceEqual([0, 1])).Price);
        // motaanh: price_before_discount = 0 → price 1830000000 → 18300
        var m2 = MassUploadTestData.Fixture("pdp-motaanh-brand-voucher.json");
        Assert.All(m2.Models, m => Assert.Equal(18_300, m.Price));
    }

    [Theory]
    [InlineData("2.0E10", "0", 200_000)]          // số mũ
    [InlineData("20000000000.0", "0", 200_000)]   // số thực
    [InlineData("\"20000000000\"", "0", 200_000)] // chuỗi số
    [InlineData("0", "1.83E9", 18_300)]           // before = 0 → price dạng mũ
    [InlineData("0", "\"1830000000\"", 18_300)]   // before = 0 → price dạng chuỗi
    [InlineData("\"abc\"", "1830000000", 18_300)] // before hỏng → price
    public void Gia_dang_so_thuc_mu_chuoi_deu_doc_duoc(string before, string price, long expected)
    {
        var json = "{\"data\":{\"item\":{\"item_id\":5,\"title\":\"x\",\"models\":[{\"model_id\":1,\"price\":" + price
                   + ",\"price_before_discount\":" + before + ",\"extinfo\":{\"tier_index\":[0]}}]}}}";
        Assert.Equal(expected, ShopeeListingReader.Read(json).Models[0].Price);
    }

    [Fact]
    public void Chu_lam_sach_ky_tu_dieu_khien_va_NFC()
    {
        var nfd = "Vòng bạc".Normalize(System.Text.NormalizationForm.FormD);
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            data = new
            {
                item = new
                {
                    item_id = 5, title = nfd + "\u0001 nữ",
                    tier_variations = new[] { new { name = "Màu\u0002", options = new[] { nfd + "\u0003" } } },
                    attributes = new[] { new { id = 1, name = "Tên\u0004", value = nfd, val_id = 0 } },
                },
                product_description = new { paragraph_list = new object[] { new { type = 0, text = "A\u0005" + nfd + "\r\nB" } } },
            },
        });
        var l = ShopeeListingReader.Read(json);
        Assert.Equal("Vòng bạc nữ", l.Title);
        Assert.True(l.Title.IsNormalized(System.Text.NormalizationForm.FormC));
        Assert.Equal("Màu", l.Tiers[0].Name);
        Assert.Equal("Vòng bạc", l.Tiers[0].Options[0]);
        Assert.Equal(("Tên", "Vòng bạc"), (l.Attributes[0].Name, l.Attributes[0].Value));
        Assert.Equal("AVòng bạc\nB", l.Description);
    }

    [Fact]
    public void SP_khong_bien_the()
    {
        var l = MassUploadTestData.Fixture("pdp-khong-bien-the.json");
        Assert.False(l.HasVariations);
        Assert.Empty(l.Tiers);
        Assert.Single(l.Models);
        Assert.Equal(2_500_000, l.Models[0].Price); // before = 0 → price
        Assert.Equal(5, l.Images.Count);
        Assert.Equal("vn-11134207-81ztc-mmu8v3cozk01bc", l.Images[0]);
        Assert.Null(l.SizeChartImageId); // size_chart = ""
    }

    [Fact]
    public void Hai_tang_doc_ten_lua_chon_va_anh()
    {
        var l = MassUploadTestData.Fixture("pdp-2tang-8model.json");
        Assert.True(l.HasVariations);
        Assert.Equal(2, l.Tiers.Count);
        Assert.Equal("Màu sắc", l.Tiers[0].Name);
        Assert.Equal(["Silver", "Gold", "Rose gold", "QT giấy lau bóng"], l.Tiers[0].Options);
        Assert.All(l.Tiers[0].Images, i => Assert.False(string.IsNullOrEmpty(i)));
        Assert.Equal("Hộp", l.Tiers[1].Name);
        Assert.Equal(["Không hộp", "Fullbox"], l.Tiers[1].Options);
        Assert.All(l.Tiers[1].Images, Assert.Null); // nhóm 2 không có ảnh → đệm null cho đủ độ dài
        Assert.Equal(8, l.Models.Count);
        Assert.Contains(l.Attributes, a => a is { Id: 100022, Value: "Female", ValId: 652 });
    }

    [Fact]
    public void Mo_ta_bo_doan_anh_va_10001_la_dong_trong()
    {
        var anh = MassUploadTestData.Fixture("pdp-motaanh-brand-voucher.json");
        Assert.StartsWith("CHÀO MỪNG BẠN ĐẾN VỚI Cửa hàng chính thức HELLO GIRL!", anh.Description);
        Assert.DoesNotContain("sg-11134202", anh.Description); // id ảnh của đoạn type 1 không lọt vào
        Assert.Equal(17, anh.Description.Split('\n').Length); // 17 đoạn chữ, 5 đoạn ảnh bị bỏ

        var trong = MassUploadTestData.Fixture("pdp-1tang-70model-4capdm.json");
        Assert.StartsWith("1 X Bóng\n\nCác mặt hàng kết hợp:\n1 X Bơm", trong.Description);
    }

    [Fact]
    public void Mo_ta_fallback_rich_text_roi_description()
    {
        const string rich = """
            {"data":{"item":{"item_id":5,"title":"x","rich_text_description":{"paragraph_list":[
              {"type":0,"text":"Dòng A"},{"type":10001,"empty_paragraph_count":2},{"type":1,"img_id":"sg-1"},{"type":0,"text":"Dòng B"}]},
              "description":"bỏ qua"}}}
            """;
        Assert.Equal("Dòng A\n\n\nDòng B", ShopeeListingReader.Read(rich).Description);

        const string plain = """{"data":{"item":{"item_id":5,"title":"x","description":"Chỉ có mô tả thô"},"product_description":{"paragraph_list":[{"type":1,"img_id":"a"}]}}}""";
        Assert.Equal("Chỉ có mô tả thô", ShopeeListingReader.Read(plain).Description);
    }

    [Fact]
    public void JSON_thieu_item_nem_FormatException()
    {
        Assert.Throws<FormatException>(() => ShopeeListingReader.Read("{\"data\":{}}"));
        Assert.Throws<FormatException>(() => ShopeeListingReader.Read("{\"data\":{\"item\":{\"item_id\":0}}}"));
        Assert.Throws<FormatException>(() => ShopeeListingReader.Read("không phải json"));
    }
}
