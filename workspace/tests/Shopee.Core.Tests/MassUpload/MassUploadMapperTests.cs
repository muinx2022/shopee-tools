using Shopee.Core.MassUpload;
using static Shopee.Core.Tests.MassUpload.MassUploadTestData;

namespace Shopee.Core.Tests.MassUpload;

public class MassUploadMapperTests
{
    private const string Gender = "ps_product_global_attribute.100022";
    private const string Origin = "ps_product_global_attribute.100037";
    private const string Material = "ps_product_global_attribute.100134";
    private const string Occasion = "ps_product_global_attribute.100155";
    private const string Style = "ps_product_global_attribute.100169";
    private const string BraceletStyle = "ps_product_global_attribute.100194";

    private static string Cell(IReadOnlyDictionary<string, MassUploadCell> row, string code) =>
        row.TryGetValue(code, out var c) ? c.Text : "";

    // ── Phân loại ──────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Mot_nhom_70_lua_chon_cat_con_20()
    {
        var l = Fixture("pdp-1tang-70model-4capdm.json");
        var p = MapOk(l, BasicSchema);
        Assert.Equal(20, p.Rows.Count);
        Assert.Equal(l.Tiers[0].Options.Take(20), p.Rows.Select(r => Cell(r, MassUploadMapper.ColTier1Option)));
        Assert.Contains(p.Warnings, w => w.Contains("70 lựa chọn → 20"));
        Assert.All(p.Rows, r => Assert.Equal("29609621870", Cell(r, MassUploadMapper.ColVariationGroup)));
    }

    [Fact]
    public void Hai_nhom_tich_vuot_50_bot_cuoi_nhom_2_truoc()
    {
        var p = MapOk(TwoTiers(10, 10), AdvancedSchema);
        Assert.Equal(50, p.Rows.Count); // 10 × 5
        Assert.Equal(Enumerable.Range(0, 5).Select(i => "Size " + i),
            p.Rows.Select(r => Cell(r, MassUploadMapper.ColTier2Option)).Distinct());
        Assert.Equal(10, p.Rows.Select(r => Cell(r, MassUploadMapper.ColTier1Option)).Distinct().Count());
        Assert.Contains(p.Warnings, w => w.Contains("10×10 = 100 tổ hợp → 10×5"));
    }

    [Fact]
    public void Hai_nhom_nhom_2_con_1_thi_bot_nhom_1_va_moi_nhom_toi_da_20()
    {
        // 25 × 3: nhóm 1 cắt còn 20 (60 tổ hợp) → nhóm 2 còn 2 (40).
        var p = MapOk(TwoTiers(25, 3), AdvancedSchema);
        Assert.Equal(40, p.Rows.Count);
        Assert.Contains(p.Warnings, w => w.Contains("25 lựa chọn → 20"));

        // 60 × 1 → 20 × 1; nhóm 2 đã còn 1 nên không bớt thêm.
        var q = MapOk(TwoTiers(60, 1), AdvancedSchema);
        Assert.Equal(20, q.Rows.Count);

        // 7 × 8 = 56 → bớt nhóm 2 về 7 → 49.
        Assert.Equal(49, MapOk(TwoTiers(7, 8), AdvancedSchema).Rows.Count);
    }

    [Fact]
    public void Ti_le_gia_giu_cum_lon_nhat_va_bo_lua_chon_khong_con_model()
    {
        var l = OneTier([10_000, 12_000, 15_000, 40_000, 45_000, 200_000, 500]);
        var p = MapOk(l, AdvancedSchema);
        var prices = p.Rows.Select(r => long.Parse(Cell(r, MassUploadMapper.ColPrice))).ToList();
        Assert.Equal([10_000, 12_000, 15_000, 40_000, 45_000], prices);
        Assert.DoesNotContain(p.Rows, r => Cell(r, MassUploadMapper.ColTier1Option) is "Màu 6" or "Màu 7");
        Assert.Contains(p.Warnings, w => w.Contains("giá đắt/rẻ") && w.Contains("bỏ 1 model"));
        Assert.Contains(p.Warnings, w => w.Contains("giá ngoài khoảng")); // 500 < 1000
        Assert.Contains(p.Warnings, w => w.Contains("bỏ 2 lựa chọn không còn model"));
        Assert.True(prices.Max() <= prices.Min() * 5);
    }

    [Fact]
    public void Cum_gia_cao_dong_hon_thi_giu_cum_cao()
    {
        var p = MapOk(OneTier([1_000, 100_000, 120_000, 150_000]), AdvancedSchema);
        Assert.Equal(["100000", "120000", "150000"], p.Rows.Select(r => Cell(r, MassUploadMapper.ColPrice)));
    }

    [Fact]
    public void Hai_nhom_thieu_to_hop_thi_bo_lua_chon_nhom_2_cho_toi_khi_ma_tran_du()
    {
        // 2×2, model (1,1) giá rác → ô (Màu 1, Size 1) trống → bỏ "Size 1" → còn 2×1 đủ.
        var l = TwoTiers(2, 2);
        l = l with { Models = l.Models.Select(m => m.TierIndex.SequenceEqual([1, 1]) ? m with { Price = 5 } : m).ToList() };
        var p = MapOk(l, AdvancedSchema);
        Assert.Equal([("Màu 0", "Size 0"), ("Màu 1", "Size 0")],
            p.Rows.Select(r => (Cell(r, MassUploadMapper.ColTier1Option), Cell(r, MassUploadMapper.ColTier2Option))));
        Assert.Contains(p.Warnings, w => w.Contains("nhóm 2: bỏ lựa chọn \"Size 1\" vì thiếu 1 tổ hợp"));
        // Ảnh nhóm 1 vẫn đi đúng chỉ số gốc.
        Assert.Equal([ImageBase + "c-0", ImageBase + "c-1"], p.Rows.Select(r => Cell(r, MassUploadMapper.ColTier1Image)));
    }

    [Fact]
    public void Hai_nhom_bo_lua_chon_thieu_nhieu_nhat_hoa_thi_bo_cai_cuoi()
    {
        // 3×3, thiếu (0,2), (1,2), (2,1): Size 2 thiếu 2 ô → bỏ trước; còn Size 1 thiếu 1 → bỏ; còn Size 0 đủ.
        var l = TwoTiers(3, 3);
        l = l with { Models = l.Models.Where(m => !(m.TierIndex[1] == 2 && m.TierIndex[0] < 2) && !(m.TierIndex[0] == 2 && m.TierIndex[1] == 1)).ToList() };
        var p = MapOk(l, AdvancedSchema);
        Assert.Equal(["Size 0"], p.Rows.Select(r => Cell(r, MassUploadMapper.ColTier2Option)).Distinct());
        Assert.Equal(3, p.Rows.Count);
        var warn = p.Warnings.Where(w => w.StartsWith("nhóm 2: bỏ lựa chọn")).ToList();
        Assert.Equal(["nhóm 2: bỏ lựa chọn \"Size 2\" vì thiếu 2 tổ hợp có model",
            "nhóm 2: bỏ lựa chọn \"Size 1\" vì thiếu 1 tổ hợp có model"], warn);
    }

    [Fact]
    public void Hai_nhom_bo_lua_chon_thieu_nhieu_nhat_truoc_du_khong_phai_cai_cuoi()
    {
        // 3×3, thiếu (0,0), (1,0), (2,2): Size 0 thiếu 2 ô, Size 2 thiếu 1 ô → bỏ Size 0 TRƯỚC (dù Size 2 đứng cuối).
        var l = TwoTiers(3, 3);
        l = l with
        {
            Models = l.Models.Where(m => !m.TierIndex.SequenceEqual([0, 0]) && !m.TierIndex.SequenceEqual([1, 0])
                                         && !m.TierIndex.SequenceEqual([2, 2])).ToList(),
        };
        var p = MapOk(l, AdvancedSchema);
        Assert.Equal(["nhóm 2: bỏ lựa chọn \"Size 0\" vì thiếu 2 tổ hợp có model",
            "nhóm 2: bỏ lựa chọn \"Size 2\" vì thiếu 1 tổ hợp có model"], p.Warnings.Where(w => w.Contains("bỏ lựa chọn \"")));
        Assert.Equal([("Màu 0", "Size 1"), ("Màu 1", "Size 1"), ("Màu 2", "Size 1")],
            p.Rows.Select(r => (Cell(r, MassUploadMapper.ColTier1Option), Cell(r, MassUploadMapper.ColTier2Option))));
    }

    [Fact]
    public void Hai_nhom_bo_nhom_2_ve_0_thi_bo_nhom_1()
    {
        // 3×2 thiếu (0,0) và (1,1): mọi Size đều thiếu → bỏ nhóm 2 sẽ về 0 → bỏ ở nhóm 1: Màu 0 và Màu 1 cùng thiếu 1,
        // hoà thì bỏ cái cuối (Màu 1) trước, rồi Màu 0 → còn Màu 2 × 2 Size, giá đúng chỉ số.
        var l = TwoTiers(3, 2);
        l = l with
        {
            Models = l.Models.Where(m => !m.TierIndex.SequenceEqual([0, 0]) && !m.TierIndex.SequenceEqual([1, 1]))
                .Select(m => m with { Price = 100_000 + m.TierIndex[0] * 1000 + m.TierIndex[1] }).ToList(),
        };
        var p = MapOk(l, AdvancedSchema);
        Assert.Equal([("Màu 2", "Size 0", "102000"), ("Màu 2", "Size 1", "102001")],
            p.Rows.Select(r => (Cell(r, MassUploadMapper.ColTier1Option), Cell(r, MassUploadMapper.ColTier2Option),
                Cell(r, MassUploadMapper.ColPrice))));
        Assert.Equal(["nhóm 1: bỏ lựa chọn \"Màu 1\" vì thiếu 1 tổ hợp có model",
            "nhóm 1: bỏ lựa chọn \"Màu 0\" vì thiếu 1 tổ hợp có model"], p.Warnings.Where(w => w.Contains("bỏ lựa chọn \"")));
        Assert.Equal(ImageBase + "c-2", Cell(p.Rows[0], MassUploadMapper.ColTier1Image));
    }

    [Fact]
    public void Cum_gia_hoa_thi_giu_cum_re_hon()
    {
        // [1000,5000] / [5000,6000] / [6000,30000] đều 2 model → giữ cụm rẻ nhất.
        var p = MapOk(OneTier([1_000, 5_000, 6_000, 30_000]), AdvancedSchema);
        Assert.Equal(["1000", "5000"], p.Rows.Select(r => Cell(r, MassUploadMapper.ColPrice)));
    }

    [Fact]
    public void Hau_to_chong_trung_khong_dung_ten_goc_da_co()
    {
        var p = MapOk(OneTier([10_000, 11_000, 12_000, 13_000], optionNames: ["A", "a", "A 2", "A"]), AdvancedSchema);
        Assert.Equal(["A", "a 3", "A 2", "A 4"], p.Rows.Select(r => Cell(r, MassUploadMapper.ColTier1Option)));
    }

    [Fact]
    public void Chong_trung_so_sau_NFC()
    {
        // "Hồng" dạng tổ hợp (NFD) và dạng dựng sẵn (NFC) là cùng một tên.
        var nfd = "Hồng".Normalize(System.Text.NormalizationForm.FormD);
        var p = MapOk(OneTier([10_000, 11_000], optionNames: ["Hồng", nfd]), AdvancedSchema);
        Assert.Equal(["Hồng", "Hồng 2"], p.Rows.Select(r => Cell(r, MassUploadMapper.ColTier1Option)));
    }

    [Theory]
    [InlineData(0, 100, 10, 10, 10, 1)]         // cân nặng 0
    [InlineData(200, -1, 10, 10, 10, 1)]        // tồn kho âm
    [InlineData(200, 1_000_000, 10, 10, 10, 1)] // tồn kho > 999.999
    [InlineData(200, 100, -1, 10, 10, 1)]       // dài âm
    [InlineData(200, 100, 10, -1, 10, 1)]       // rộng âm
    [InlineData(200, 100, 10, 10, -1, 1)]       // cao âm
    [InlineData(200, 100, 10, 10, 10, 0)]       // SP/file 0
    public void Defaults_ngoai_khoang_la_loi_cau_hinh(int weight, int stock, int length, int width, int height, int perFile)
    {
        var d = Defaults with
        {
            WeightGrams = weight, StockPerVariation = stock, LengthCm = length, WidthCm = width, HeightCm = height,
            ProductsPerFile = perFile,
        };
        Assert.Throws<ArgumentException>(() => MassUploadMapper.Map(OneTier([10_000]), d, AdvancedSchema, Resolve));
    }

    [Fact]
    public void Defaults_bien_hop_le()
    {
        var d = Defaults with { WeightGrams = 1, StockPerVariation = 999_999, LengthCm = 0, WidthCm = 0, HeightCm = 0, ProductsPerFile = 1 };
        Assert.IsType<MassUploadProduct>(MassUploadMapper.Map(OneTier([10_000]), d, AdvancedSchema, Resolve));
    }

    [Fact]
    public void Ky_tu_dieu_khien_loc_truoc_khi_do_do_dai()
    {
        // 95 ký tự thật + 30 ký tự điều khiển: đo thô ≥ 100 nhưng sau lọc < 100 → phải bù tên + thuộc tính.
        var real = LongText(94);
        Assert.Equal(95, real.Length);
        var dirty = string.Concat(real.Select((ch, i) => i < 30 ? ch + "\u0001" : ch.ToString()));
        Assert.True(dirty.Length >= 100);
        var p = MapOk(OneTier([10_000], description: dirty), AdvancedSchema);
        var desc = Cell(p.Rows[0], MassUploadMapper.ColDescription);
        Assert.StartsWith(real + "\nVòng tay bạc nữ", desc);
        Assert.DoesNotContain('\u0001', desc);
        Assert.Contains(p.Warnings, w => w.Contains("mô tả 95 ký tự"));

        // Mô tả thô 110 ký tự nhưng chỉ 40 ký tự thật; bù tên (12) vẫn < 100, không thuộc tính → Skip.
        var dirty40 = LongText(39) + new string('\u0002', 70);
        Assert.True(dirty40.Length >= 100);
        var skip = MassUploadMapper.Map(OneTier([10_000], title: "Vòng tay bạc", description: dirty40, attributes: []),
            Defaults, AdvancedSchema, Resolve);
        Assert.Contains("mô tả dưới 100", Assert.IsType<MassUploadSkip>(skip).Reason);

        // Tên thô ≥ 10 nhưng sau lọc chỉ còn 4 ký tự → Skip.
        var shortName = MassUploadMapper.Map(OneTier([10_000], title: "Ngắn\u0001\u0002\u0003\u0004\u0005\u0006"), Defaults,
            AdvancedSchema, Resolve);
        Assert.Contains("tên SP dưới 10", Assert.IsType<MassUploadSkip>(shortName).Reason);

        // Tên: ký tự điều khiển bỏ + NFC trước khi đo.
        var t = MapOk(OneTier([10_000], title: "Vòng\u0007 tay " + "bạc".Normalize(System.Text.NormalizationForm.FormD) + " nữ"), AdvancedSchema);
        Assert.Equal("Vòng tay bạc nữ", Cell(t.Rows[0], MassUploadMapper.ColName));
    }

    [Fact]
    public void Model_trung_to_hop_bi_bo_va_co_canh_bao()
    {
        var l = OneTier([10_000, 11_000]);
        l = l with { Models = [.. l.Models, new ListingModel(99, [0], 99_000), new ListingModel(98, [1], 1_000)] };
        var p = MapOk(l, AdvancedSchema);
        Assert.Equal(["10000", "11000"], p.Rows.Select(r => Cell(r, MassUploadMapper.ColPrice))); // giữ model đầu
        Assert.Contains(p.Warnings, w => w.Contains("bỏ 2 model trùng tổ hợp"));
    }

    [Fact]
    public void Moi_model_deu_gia_duoi_1000_thi_skip()
    {
        var r = MassUploadMapper.Map(OneTier([500, 900]), Defaults, AdvancedSchema, Resolve);
        Assert.Contains("không còn model", Assert.IsType<MassUploadSkip>(r).Reason);
    }

    [Fact]
    public void Trung_ten_sau_cat_them_hau_to()
    {
        var l = OneTier([10_000, 11_000, 12_000],
            optionNames: ["Màu xanh dương đậm loại A", "Màu xanh dương đậm loại B", "Đỏ"]);
        var p = MapOk(l, AdvancedSchema);
        var opts = p.Rows.Select(r => Cell(r, MassUploadMapper.ColTier1Option)).ToList();
        Assert.Equal("Màu xanh dương đậm l", opts[0]);
        Assert.Equal("Màu xanh dương đậm 2", opts[1]);
        Assert.Equal("Đỏ", opts[2]);
        Assert.All(opts, o => Assert.True(o.Length <= 20));
        Assert.Contains(p.Warnings, w => w.Contains("\"Màu xanh dương đậm loại B\" → \"Màu xanh dương đậm 2\""));
    }

    [Fact]
    public void Ten_nhom_cat_14_va_hai_nhom_khong_trung_ten()
    {
        var l = TwoTiers(2, 2) with
        {
            Tiers = [new ListingTier("Phân loại màu sắc", ["A", "B"], ["c-0", "c-1"]),
                     new ListingTier("Phân loại màu sắc khác", ["S", "M"], [null, null])],
        };
        var p = MapOk(l, AdvancedSchema);
        Assert.Equal("Phân loại màu", Cell(p.Rows[0], MassUploadMapper.ColTier1Name));
        Assert.Equal("Phân loại mà 2", Cell(p.Rows[0], MassUploadMapper.ColTier2Name));
    }

    [Fact]
    public void Anh_lua_chon_thieu_thi_bo_ca_cot()
    {
        var full = MapOk(OneTier([10_000, 11_000]), AdvancedSchema);
        Assert.Equal([ImageBase + "opt-0", ImageBase + "opt-1"], full.Rows.Select(r => Cell(r, MassUploadMapper.ColTier1Image)));

        var thieu = MapOk(OneTier([10_000, 11_000], optionImages: ["opt-0", null]), AdvancedSchema);
        Assert.All(thieu.Rows, r => Assert.False(r.ContainsKey(MassUploadMapper.ColTier1Image)));
        Assert.Contains(thieu.Warnings, w => w.Contains("ảnh lựa chọn thiếu 1/2"));
    }

    [Fact]
    public void Anh_lua_chon_thieu_o_lua_chon_da_bo_thi_van_ghi()
    {
        // Lựa chọn 3 (không ảnh) bị bỏ vì giá < 1000 → các lựa chọn còn lại đủ ảnh.
        var p = MapOk(OneTier([10_000, 11_000, 500], optionImages: ["opt-0", "opt-1", null]), AdvancedSchema);
        Assert.All(p.Rows, r => Assert.True(r.ContainsKey(MassUploadMapper.ColTier1Image)));
    }

    [Fact]
    public void SP_khong_bien_the_mot_dong_khong_co_E_den_J()
    {
        var p = MapOk(Fixture("pdp-khong-bien-the.json"), BasicSchema);
        var row = Assert.Single(p.Rows);
        Assert.Equal("2500000", Cell(row, MassUploadMapper.ColPrice));
        foreach (var code in new[] { MassUploadMapper.ColVariationGroup, MassUploadMapper.ColTier1Name,
                     MassUploadMapper.ColTier1Option, MassUploadMapper.ColTier1Image, MassUploadMapper.ColTier2Name,
                     MassUploadMapper.ColTier2Option })
            Assert.False(row.ContainsKey(code));
        Assert.Equal("47908509202", Cell(row, MassUploadMapper.ColParentSku));
    }

    // ── Tên / mô tả ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Ten_dai_cat_o_ranh_gioi_tu_ten_ngan_thi_skip()
    {
        var title = string.Join(' ', Enumerable.Repeat("Vòngtay", 25)); // 199 ký tự
        var p = MapOk(OneTier([10_000], title: title), AdvancedSchema);
        var name = Cell(p.Rows[0], MassUploadMapper.ColName);
        Assert.True(name.Length <= 120);
        Assert.All(name.Split(' '), w => Assert.Equal("Vòngtay", w)); // không có từ bị cắt dở
        Assert.Contains(p.Warnings, w => w.StartsWith("tên dài"));

        var r = MassUploadMapper.Map(OneTier([10_000], title: "Vòng  tay"), Defaults, AdvancedSchema, Resolve);
        Assert.Contains("tên SP dưới 10", Assert.IsType<MassUploadSkip>(r).Reason);
    }

    [Fact]
    public void Mo_ta_ngan_them_ten_va_thuoc_tinh_van_thieu_thi_skip()
    {
        var attrs = new[]
        {
            new ListingAttribute(100022, "Gender", "Female", 652),
            new ListingAttribute(100134, "Material", "Stainless steel premium grade", 0),
            new ListingAttribute(100155, "Occasion", "Birthday party and anniversary", 0),
        };
        var p = MapOk(OneTier([10_000], description: "Ngắn.", attributes: attrs), AdvancedSchema);
        var desc = Cell(p.Rows[0], MassUploadMapper.ColDescription);
        Assert.True(desc.Length >= 100);
        Assert.StartsWith("Ngắn.\nVòng tay bạc nữ đính đá phong cách Hàn Quốc\nGiới tính: Nữ", desc);
        Assert.Contains(p.Warnings, w => w.Contains("thêm tên + thuộc tính"));

        var r = MassUploadMapper.Map(OneTier([10_000], description: "Ngắn.", attributes: []), Defaults, AdvancedSchema, Resolve);
        Assert.Contains("mô tả dưới 100", Assert.IsType<MassUploadSkip>(r).Reason);
    }

    [Fact]
    public void Mo_ta_bu_bang_ten_cot_va_gia_tri_tieng_Viet_khong_co_thi_giu_EN()
    {
        var attrs = new[]
        {
            new ListingAttribute(100022, "Gender", "Female", 652),       // cột "Giới tính", từ điển → Nữ
            new ListingAttribute(999999, "Weird Thing", "Bar", 0),        // không cột, không từ điển → giữ EN
            new ListingAttribute(100134, "Material", "Plastic, Gold", 0), // cột "Chất liệu", dịch được 1 phần
            new ListingAttribute(100155, "Occasion", "Birthday", 0),      // đã đủ 100 ký tự trước dòng này → không thêm
        };
        const string title = "Vòng tay bạc nữ đính đá phong cách Hàn Quốc";
        var adv = MapOk(OneTier([10_000], description: "Ngắn.", attributes: attrs), AdvancedSchema);
        Assert.Equal($"Ngắn.\n{title}\nGiới tính: Nữ\nWeird Thing: Bar\nChất liệu: Plastic, Vàng",
            Cell(adv.Rows[0], MassUploadMapper.ColDescription));

        // Bản basic không có cột thuộc tính → tên EN, giá trị vẫn dịch qua từ điển.
        var basic = MapOk(OneTier([10_000], description: "Ngắn.", attributes: attrs), BasicSchema);
        Assert.Equal($"Ngắn.\n{title}\nGender: Nữ\nWeird Thing: Bar\nMaterial: Plastic, Vàng",
            Cell(basic.Rows[0], MassUploadMapper.ColDescription));
    }

    [Fact]
    public void Mo_ta_dai_cat_o_ranh_gioi_doan()
    {
        var para = MassUploadTestData.LongText(700);
        var desc = string.Join('\n', Enumerable.Repeat(para, 6)); // ~4200 ký tự
        var p = MapOk(OneTier([10_000], description: desc), AdvancedSchema);
        var got = Cell(p.Rows[0], MassUploadMapper.ColDescription);
        Assert.True(got.Length <= 3000);
        Assert.Equal(string.Join('\n', Enumerable.Repeat(para, 4)), got); // giữ trọn 4 đoạn
        Assert.Contains(p.Warnings, w => w.Contains("cắt ở ranh giới đoạn"));
    }

    // ── Ảnh ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Anh_qua_resolver_bia_8_anh_va_bang_size_dung_chung()
    {
        var l = Fixture("pdp-1tang-70model-4capdm.json"); // 9 ảnh
        var p = MapOk(l, BasicSchema);
        var row = p.Rows[0];
        Assert.Equal(ImageBase + l.Images[0], Cell(row, MassUploadMapper.ColCoverImage));
        for (var i = 1; i <= 8; i++) Assert.Equal(ImageBase + l.Images[i], Cell(row, MassUploadMapper.ColImagePrefix + i));
        Assert.Equal(ImageBase + "size-chart-shared", Cell(row, MassUploadMapper.ColSizeChartImage));
    }

    [Theory]
    [InlineData("img-1")]
    [InlineData("opt-1")]
    [InlineData("size-chart-shared")]
    public void Resolver_tra_null_bat_ky_anh_nao_thi_skip(string missingId)
    {
        var r = MassUploadMapper.Map(OneTier([10_000, 11_000]), Defaults, AdvancedSchema,
            id => id == missingId ? null : Resolve(id));
        Assert.Equal($"ảnh chưa sẵn sàng: {missingId}", Assert.IsType<MassUploadSkip>(r).Reason);
    }

    [Fact]
    public void Thieu_khoa_anh_bang_size_la_loi_cau_hinh()
    {
        Assert.Throws<ArgumentException>(() =>
            MassUploadMapper.Map(OneTier([10_000]), Defaults with { SizeChartImageKey = " " }, AdvancedSchema, Resolve));
    }

    // ── Cột cố định ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Cot_D_E_L_O_Z_va_kich_thuoc_theo_defaults()
    {
        var p = MapOk(Fixture("pdp-2tang-8model.json"), AdvancedSchema);
        Assert.Equal(8, p.Rows.Count);
        Assert.All(p.Rows, r =>
        {
            Assert.Equal("100026", Cell(r, MassUploadMapper.ColCategory));
            Assert.True(r[MassUploadMapper.ColCategory].IsNumber);
            Assert.Equal("26393229156", Cell(r, MassUploadMapper.ColParentSku));
            Assert.Equal("26393229156", Cell(r, MassUploadMapper.ColVariationGroup));
            Assert.Equal("100", Cell(r, MassUploadMapper.ColStock));
            Assert.Equal(ImageBase + "size-chart-shared", Cell(r, MassUploadMapper.ColSizeChartImage));
            Assert.Equal("200", Cell(r, MassUploadMapper.ColWeight));
            Assert.Equal("10", Cell(r, MassUploadMapper.ColLength));
            Assert.Equal("11", Cell(r, MassUploadMapper.ColWidth));
            Assert.Equal("12", Cell(r, MassUploadMapper.ColHeight));
            Assert.False(r.ContainsKey("ps_brand")); // để trống → Shopee tự điền No brand
        });
        Assert.Equal(100009, p.TopCategoryId);
    }

    // ── Ngành / thuộc tính (bản mẫu nâng cao 100009 thật) ─────────────────────────────────────────────────────────────

    [Fact]
    public void Nganh_ngoai_ban_mau_thi_skip()
    {
        var r = MassUploadMapper.Map(Fixture("pdp-1tang-70model-4capdm.json"), Defaults, AdvancedSchema, Resolve);
        Assert.Contains("không thuộc bản mẫu", Assert.IsType<MassUploadSkip>(r).Reason);
    }

    [Fact]
    public void Thuoc_tinh_2tang_8model()
    {
        var p = MapOk(Fixture("pdp-2tang-8model.json"), AdvancedSchema);
        Assert.False(p.NeedsFix);
        var row = p.Rows[0];
        Assert.Equal("Nữ", Cell(row, Gender));
        Assert.Equal("Thanh lịch", Cell(row, Style));
        Assert.Equal("Dạng chuỗi", Cell(row, BraceletStyle));
        Assert.All(p.Rows, r => Assert.Equal("Nữ", Cell(r, Gender)));
    }

    [Fact]
    public void Thuoc_tinh_nhieu_gia_tri_ngan_cham_phay_khong_cach()
    {
        var p = MapOk(Fixture("pdp-motaanh-brand-voucher.json"), AdvancedSchema);
        Assert.False(p.NeedsFix);
        var row = p.Rows[0];
        Assert.Equal("Binh thương;Thanh lịch", Cell(row, Style));
        Assert.Equal("Dạng chuỗi;Hạt Charmed", Cell(row, BraceletStyle));
        Assert.Equal("Nữ", Cell(row, Gender));
        Assert.Equal("Trung Quốc", Cell(row, Origin));
        Assert.Equal("Khác", Cell(row, Material));
        Assert.Equal("Sinh nhật", Cell(row, Occasion));
    }

    [Fact]
    public void Mandatory_danh_sach_khong_co_trong_JSON_thi_NeedsFix()
    {
        var l = Fixture("pdp-2tang-8model.json");
        var p = MapOk(l with { Attributes = l.Attributes.Where(a => a.Id != 100022).ToList() }, AdvancedSchema);
        Assert.True(p.NeedsFix);
        Assert.Contains(p.NeedsFixReasons, r => r.Contains("100022") && r.Contains("JSON không có"));
        Assert.False(p.Rows[0].ContainsKey(Gender));
        Assert.Equal(8, p.Rows.Count); // vẫn đăng
    }

    [Fact]
    public void Mandatory_danh_sach_khong_dich_duoc_thi_NeedsFix_va_liet_ke_cap_thieu()
    {
        var l = Fixture("pdp-2tang-8model.json");
        var attrs = l.Attributes.Select(a => a.Id == 100022 ? a with { Value = "Kids" } : a).ToList();
        var p = MapOk(l with { Attributes = attrs }, AdvancedSchema);
        Assert.True(p.NeedsFix);
        Assert.Contains(p.NeedsFixReasons, r => r.Contains("100022") && r.Contains("Kids"));
        Assert.Contains(new MissingAttributeValue(100022, "Gender", "Kids"), p.MissingAttributeValues);
        Assert.False(p.Rows[0].ContainsKey(Gender));
    }

    [Fact]
    public void Danh_sach_ban_dich_khong_co_trong_HiddenAttr_thi_khong_ghi()
    {
        // Từ điển dịch được ("Kids" → "Trẻ em") nhưng "Trẻ em" không có trong HiddenAttr của Giới tính → không ghi, NeedsFix.
        var dict = AttributeDictionary.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            """{"attributes":{"100022":{"values":{"Kids":"Trẻ em"}}}}""")));
        var l = OneTier([10_000], attributes: [new ListingAttribute(100022, "Gender", "Kids", 0)]);
        var p = MapOk(l, AdvancedSchema, dictionary: dict);
        Assert.False(p.Rows[0].ContainsKey(Gender));
        Assert.True(p.NeedsFix);
        Assert.Contains(new MissingAttributeValue(100022, "Gender", "Kids"), p.MissingAttributeValues);
    }

    [Fact]
    public void Danh_sach_nhan_chu_EN_neu_HiddenAttr_dung_y_vay()
    {
        // "Unisex" có trong HiddenAttr; từ điển rỗng vẫn khớp được chữ EN.
        var l = OneTier([10_000], attributes: [new ListingAttribute(100022, "Gender", "unisex", 0)]);
        var p = MapOk(l, AdvancedSchema, dictionary: AttributeDictionary.Empty);
        Assert.Equal("Unisex", Cell(p.Rows[0], Gender));
        Assert.False(p.NeedsFix);
    }

    [Fact]
    public void Custom_optional_chua_dich_thi_bo_custom_mandatory_thi_ghi_EN()
    {
        var dir = NewTempDir();
        try
        {
            // Đổi Xuất xứ (AP, kiểu tùy chỉnh) của ngành 100026 thành MANDATORY bằng shared string của ô AN12.
            var schema = TemplateSchema.Load(PatchCatProps(dir, "AP12", "AN12"));
            Assert.Equal(AttributeRequirement.Mandatory, schema.GetRequirement(100026, Origin));
            var l = OneTier([10_000], attributes:
            [
                new ListingAttribute(100022, "Gender", "Female", 652),
                new ListingAttribute(100037, "Country of Origin", "Atlantis", 77),  // EN có sẵn của Shopee, MANDATORY
                new ListingAttribute(100134, "Material", "Plastic, Gold", 0),        // tự gõ, OPTIONAL, dịch được 1 phần
                new ListingAttribute(100155, "Occasion", "Party", 1400),             // EN có sẵn, OPTIONAL, không dịch được
            ]);
            var p = MapOk(l, schema);
            Assert.Equal("Atlantis", Cell(p.Rows[0], Origin));
            Assert.Contains(p.Warnings, w => w.Contains("ghi nguyên cả chuỗi EN \"Atlantis\""));
            Assert.Contains(new MissingAttributeValue(100037, "Country of Origin", "Atlantis"), p.MissingAttributeValues);
            // Không dịch được HẾT → không tách: ghi nguyên cả chuỗi (không còn "Vàng" lẻ).
            Assert.Equal("Plastic, Gold", Cell(p.Rows[0], Material));
            Assert.Contains(p.Warnings, w => w.Contains("tự gõ") && w.Contains("\"Plastic, Gold\""));
            Assert.DoesNotContain(p.MissingAttributeValues, m => m.AttrId == 100134); // chữ tự gõ không cần từ điển
            Assert.False(p.Rows[0].ContainsKey(Occasion));
            Assert.Contains(p.Warnings, w => w.Contains("bỏ giá trị chưa có trong từ điển: \"Party\""));
            Assert.Contains(new MissingAttributeValue(100155, "Occasion", "Party"), p.MissingAttributeValues);
            Assert.False(p.NeedsFix);
        }
        finally { HarvestTestData.TryDelete(dir); }
    }

    [Fact]
    public void Gia_tri_co_dau_phay_khong_dich_het_thi_ghi_nguyen_ca_chuoi()
    {
        var l = OneTier([10_000], attributes:
        [
            new ListingAttribute(100022, "Gender", "Female", 652),
            new ListingAttribute(101067, "Manufacturer/trader name", "Công ty A; chi nhánh B", 0),
            new ListingAttribute(101068, "Manufacturer/trader address", "Số 10, Đường ABC, Hà Nội", 0),
            new ListingAttribute(100037, "Country of Origin", "Hong Kong, China", 0),
        ]);
        var p = MapOk(l, AdvancedSchema);
        Assert.Equal("Công ty A, chi nhánh B", Cell(p.Rows[0], "ps_product_global_attribute.101067")); // ";" → ","
        Assert.Equal("Số 10, Đường ABC, Hà Nội", Cell(p.Rows[0], "ps_product_global_attribute.101068"));
        Assert.Equal("Hong Kong, China", Cell(p.Rows[0], Origin)); // cột tùy chỉnh: không tách lấy "Trung Quốc" lẻ
        Assert.Contains(p.Warnings, w => w.Contains("\"Số 10, Đường ABC, Hà Nội\""));
        Assert.Empty(p.MissingAttributeValues);
    }

    [Fact]
    public void Cot_danh_sach_mot_gia_tri_uu_tien_phan_khop_duoc()
    {
        var dir = NewTempDir();
        try
        {
            // Biến Xuất xứ của 100026 thành kiểu danh sách có "Trung Quốc".
            var schema = TemplateSchema.Load(AddHiddenAttr(dir, Origin, 100026, "Trung Quốc", "Việt Nam"));
            Assert.Equal(["Trung Quốc", "Việt Nam"], schema.GetListValues(100026, Origin));
            var l = OneTier([10_000], attributes:
            [
                new ListingAttribute(100022, "Gender", "Alien, Female", 0),
                new ListingAttribute(100037, "Country of Origin", "Hong Kong, China", 35),
            ]);
            var p = MapOk(l, schema);
            Assert.Equal("Trung Quốc", Cell(p.Rows[0], Origin));
            Assert.Equal("Nữ", Cell(p.Rows[0], Gender));
            Assert.Contains(p.Warnings, w => w.Contains("100037") && w.Contains("bỏ phần chưa có") && w.Contains("Hong Kong"));
            Assert.Contains(new MissingAttributeValue(100037, "Country of Origin", "Hong Kong"), p.MissingAttributeValues);
            Assert.Contains(new MissingAttributeValue(100022, "Gender", "Alien"), p.MissingAttributeValues);
            Assert.False(p.NeedsFix);
        }
        finally { HarvestTestData.TryDelete(dir); }
    }

    [Fact]
    public void Thuoc_tinh_toi_da_5_gia_tri_va_cot_mot_gia_tri_chi_lay_1()
    {
        var l = OneTier([10_000], attributes:
        [
            new ListingAttribute(100022, "Gender", "Female", 652),
            new ListingAttribute(100169, "Style", "Elegant, Casual, Classic, Retro, Ethnic, Baroque", 0),
            new ListingAttribute(100155, "Occasion", "Birthday, Wedding", 0), // cột Dịp chỉ nhận 1 giá trị
        ]);
        var p = MapOk(l, AdvancedSchema);
        Assert.Equal("Thanh lịch;Binh thương;Cổ điển;Retro;Dân tộc", Cell(p.Rows[0], Style));
        Assert.Equal("Sinh nhật", Cell(p.Rows[0], Occasion));
        Assert.Contains(p.Warnings, w => w.Contains("6 giá trị → 5"));
    }

    [Fact]
    public void Thuoc_tinh_irrelevant_hoac_khong_co_cot_thi_bo_qua()
    {
        var l = OneTier([10_000], attributes:
        [
            new ListingAttribute(100022, "Gender", "Female", 652),
            new ListingAttribute(100024, "Body Area", "Head", 0), // AO: IRRELEVANT với 100026
            new ListingAttribute(999999, "Lạ", "X", 0),
        ]);
        var p = MapOk(l, AdvancedSchema);
        Assert.False(p.Rows[0].ContainsKey("ps_product_global_attribute.100024"));
        Assert.Empty(p.MissingAttributeValues);
    }

    [Fact]
    public void Ban_basic_khong_ghi_thuoc_tinh()
    {
        var p = MapOk(Fixture("pdp-2tang-8model.json"), BasicSchema);
        Assert.DoesNotContain(p.Rows[0].Keys, k => k.StartsWith(TemplateColumn.AttributePrefix));
        Assert.False(p.NeedsFix);
    }
}
