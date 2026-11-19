using Shopee.Core.MassUpload;
using static Shopee.Core.Tests.MassUpload.MassUploadTestData;

namespace Shopee.Core.Tests.MassUpload;

/// <summary>GĐ8b: phân loại khớp danh sách chuẩn của Shopee (ngành cấp 1 100017) thì ghi MÃ nhóm / mã giá trị, còn lại giữ chữ;
/// thứ tự lựa chọn (dòng, ảnh theo lựa chọn) không lệch.</summary>
public sealed class VariationStandardTests
{
    private const string Template100017 = "advanced-100017.xlsx";
    private const string VayNhieuGiaTri = "listing-vi-vay-100017-nhieu-gia-tri.json";   // Màu sắc: Đen, Trắng, Trắng kem, … Nâu tây; Size: Dài váy …

    private static readonly Lazy<TemplateSchema> Schema = new(() => TemplateSchema.Load(TemplatePath(Template100017)));

    private static ShopeeListing Raw(string name) => ShopeeListingReader.Read(File.ReadAllText(TemplatePath(name)));

    private static string Cell(IReadOnlyDictionary<string, MassUploadCell> row, string code) =>
        row.TryGetValue(code, out var c) ? c.Text : "";

    /// <summary>SP 2 nhóm ngành 100017 / lá 100102 dựng tay (ảnh riêng cho từng lựa chọn nhóm 1).</summary>
    private static ShopeeListing TwoTier(string tier1, string[] colors, string tier2, string[] sizes, long top = 100017, long leaf = 100102)
    {
        var models = new List<ListingModel>();
        for (var i = 0; i < colors.Length; i++)
            for (var j = 0; j < sizes.Length; j++) models.Add(new ListingModel(i * 100 + j, [i, j], 150_000));
        return new ShopeeListing(5005, 77, "Chân váy voan xòe dáng dài nữ phong cách Hàn Quốc", LongText(200), leaf, top, [top, leaf], true,
            [new ListingTier(tier1, colors, colors.Select((_, i) => (string?)("anh-mau-" + i)).ToList()),
             new ListingTier(tier2, sizes, sizes.Select(_ => (string?)null).ToList())],
            models, ["img-0"], [], null);
    }

    [Fact]
    public void RawThat_MauSacRaMa_NauTayGiuChu_SizeDaiVayGiuChu_AnhDungDong()
    {
        var listing = Raw(VayNhieuGiaTri);
        var plain = MassUploadMapper.Map(listing, Defaults, Schema.Value, id => ImageResolution.FromUrl(Resolve(id)), variationStandard: VariationStandard.Empty)
            as MassUploadProduct ?? throw new Xunit.Sdk.XunitException("bị bỏ");
        var p = MapOk(listing, Schema.Value);

        Assert.All(p.Rows, r => Assert.Equal("101054", Cell(r, MassUploadMapper.ColTier1Name)));
        // Size chỉ có "Dài váy …" (không giá trị nào ra mã) → tên nhóm giữ chữ.
        Assert.All(p.Rows, r => Assert.Equal("Size", Cell(r, MassUploadMapper.ColTier2Name)));
        var expected = new Dictionary<string, string>
        {
            ["Đen"] = "6258", ["Trắng"] = "6252", ["Trắng kem"] = "9973", ["Nâu tây"] = "Nâu tây",
            ["Dài váy 90cm"] = "Dài váy 90cm",   // không khớp gần đúng "90 cm"
        };
        Assert.Equal(plain.Rows.Count, p.Rows.Count);
        for (var i = 0; i < p.Rows.Count; i++)
        {
            // Cùng dòng: chữ cũ ↔ mã mới, ảnh phân loại và giá giữ nguyên.
            var oldColor = Cell(plain.Rows[i], MassUploadMapper.ColTier1Option);
            var oldSize = Cell(plain.Rows[i], MassUploadMapper.ColTier2Option);
            if (expected.TryGetValue(oldColor, out var c)) Assert.Equal(c, Cell(p.Rows[i], MassUploadMapper.ColTier1Option));
            if (expected.TryGetValue(oldSize, out var s)) Assert.Equal(s, Cell(p.Rows[i], MassUploadMapper.ColTier2Option));
            Assert.Equal(Cell(plain.Rows[i], MassUploadMapper.ColTier1Image), Cell(p.Rows[i], MassUploadMapper.ColTier1Image));
            Assert.Equal(Cell(plain.Rows[i], MassUploadMapper.ColPrice), Cell(p.Rows[i], MassUploadMapper.ColPrice));
            Assert.Equal(Cell(plain.Rows[i], MassUploadMapper.ColVariationGroup), Cell(p.Rows[i], MassUploadMapper.ColVariationGroup));
        }
        Assert.Contains(p.Rows, r => Cell(r, MassUploadMapper.ColTier1Option) == "6258");
        Assert.Contains(p.Rows, r => Cell(r, MassUploadMapper.ColTier1Option) == "Nâu tây");
        var note = Assert.Single(p.Notes!);
        Assert.Matches(@"^phân loại chuẩn: \d+/11 giá trị đổi sang mã$", note);
        Assert.DoesNotContain("phân loại chuẩn: 0/", note);
    }

    [Fact]
    public void TenNhomHoaThuongThuaKhoangTrang_SizeChuan_TrungNhieuNhomPhuGiuChu()
    {
        var listing = TwoTier("màu SẮC ", ["Đen", "Nâu tây"], "Size", ["M", "Quốc Tế: Free Size", "16", "Dài váy 90cm"]);

        var p = MapOk(listing, Schema.Value);

        Assert.All(p.Rows, r => Assert.Equal("101054", Cell(r, MassUploadMapper.ColTier1Name)));
        Assert.All(p.Rows, r => Assert.Equal("101056", Cell(r, MassUploadMapper.ColTier2Name)));
        var pairs = p.Rows.Select(r => (Cell(r, MassUploadMapper.ColTier1Option), Cell(r, MassUploadMapper.ColTier2Option),
            Cell(r, MassUploadMapper.ColTier1Image))).ToList();
        Assert.Equal(
        [
            ("6258", "10301", ImageBase + "anh-mau-0"), ("6258", "6272", ImageBase + "anh-mau-0"),
            ("6258", "16", ImageBase + "anh-mau-0"), ("6258", "Dài váy 90cm", ImageBase + "anh-mau-0"),
            ("Nâu tây", "10301", ImageBase + "anh-mau-1"), ("Nâu tây", "6272", ImageBase + "anh-mau-1"),
            ("Nâu tây", "16", ImageBase + "anh-mau-1"), ("Nâu tây", "Dài váy 90cm", ImageBase + "anh-mau-1"),
        ], pairs);
        Assert.Equal("phân loại chuẩn: 3/6 giá trị đổi sang mã", Assert.Single(p.Notes!));
    }

    [Fact]
    public void RawThat_KhongMauNaoRaMa_TenNhomGiuChu()
    {
        // SP 45721101144: "Màu sắc" + "TRẮNG (3lớp) SẴN"… — không giá trị nào khớp chuẩn → F giữ chữ, không ghi mã nhóm 101054.
        var listing = Raw("listing-vi-vay-100017.json");
        Assert.Equal(45721101144, listing.ItemId);

        var p = MapOk(listing, Schema.Value);

        Assert.All(p.Rows, r => Assert.Equal("Màu sắc", Cell(r, MassUploadMapper.ColTier1Name)));
        Assert.Contains(p.Rows, r => Cell(r, MassUploadMapper.ColTier1Option) == "TRẮNG (3lớp) SẴN");
        Assert.Equal("phân loại chuẩn: 0/10 giá trị đổi sang mã", Assert.Single(p.Notes!));
    }

    [Fact]
    public void NganhKhac100017_GiuChuHet()
    {
        var listing = TwoTier("Màu sắc", ["Đen", "Trắng"], "Size", ["M"], top: 100011, leaf: 100242);

        var p = MapOk(listing, BasicSchema);

        Assert.All(p.Rows, r => Assert.Equal("Màu sắc", Cell(r, MassUploadMapper.ColTier1Name)));
        Assert.Equal(["Đen", "Trắng"], p.Rows.Select(r => Cell(r, MassUploadMapper.ColTier1Option)));
        Assert.Equal(["M", "M"], p.Rows.Select(r => Cell(r, MassUploadMapper.ColTier2Option)));
        Assert.Empty(p.Notes!);
    }

    [Fact]
    public void NhomKhongChuan_GiuChuCaNhom()
    {
        var names = new List<string> { "Kiểu dáng", "Size", "Màu sắc" };
        var options = new List<List<string>> { new() { "Đen", "Trắng" }, new() { "M" }, new() { "Xanh ngọc bích đậm" } };

        var st = VariationStandard.Default.Apply(100017, names, options);

        // Nhóm "Màu sắc" khớp tên nhưng không giá trị nào ra mã → tên nhóm cũng giữ chữ.
        Assert.Equal(["Kiểu dáng", "101056", "Màu sắc"], names);
        Assert.Equal(["Đen", "Trắng"], options[0]);
        Assert.Equal(["10301"], options[1]);
        Assert.Equal(new VariationStandardStats(1, 4), st);
    }

    [Fact]
    public void HaiChuRaCungMa_HoacMaTrungChuKhac_GiuChu()
    {
        var names = new List<string> { "Màu sắc" };
        var options = new List<List<string>> { new() { "Đen", "ĐEN ", "Trắng", "6252", "Hồng kem" } };

        var st = VariationStandard.Default.Apply(100017, names, options);

        // "Đen" / "ĐEN " cùng ra 6258 → giữ chữ cả hai; "Trắng" ra 6252 trùng chữ "6252" của lựa chọn khác → giữ chữ.
        Assert.Equal(["Đen", "ĐEN ", "Trắng", "6252", "9972"], options[0]);
        Assert.Equal(new VariationStandardStats(1, 5), st);
    }
}
