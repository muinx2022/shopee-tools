using Shopee.Core.MassUpload;
using static Shopee.Core.Tests.MassUpload.MassUploadTestData;

namespace Shopee.Core.Tests.MassUpload;

/// <summary>Map + ghi 3 fixture ngoài ngành 100009 (bản basic): 1 ảnh / attributes null, ảnh tiền tố sg- lẫn vn-, ngành 2
/// cấp nhiều thuộc tính tự gõ (val_id 0).</summary>
public sealed class MassUploadFixtureTests : IDisposable
{
    private readonly string _dir = NewTempDir();

    public void Dispose() => HarvestTestData.TryDelete(_dir);

    private static string Cell(IReadOnlyDictionary<string, MassUploadCell> row, string code) =>
        row.TryGetValue(code, out var c) ? c.Text : "";

    private static IEnumerable<string> Col(MassUploadProduct p, string code) => p.Rows.Select(r => Cell(r, code));

    [Fact]
    public void Mot_tang_4_model_1_anh_attributes_null()
    {
        var l = Fixture("pdp-1tang-4model-1anh.json");
        Assert.Empty(l.Attributes); // JSON attributes = null
        var p = MapOk(l, BasicSchema);
        Assert.Equal(4, p.Rows.Count);
        Assert.Equal(["26000", "19500", "27000", "23000"], Col(p, MassUploadMapper.ColPrice));
        Assert.All(Col(p, MassUploadMapper.ColCategory), c => Assert.Equal("101287", c));
        Assert.Equal(ImageBase + "vn-11134207-7ras8-m1vzny7g6fun06", Cell(p.Rows[0], MassUploadMapper.ColCoverImage));
        Assert.DoesNotContain(p.Rows[0].Keys, k => k.StartsWith(MassUploadMapper.ColImagePrefix));
        Assert.Equal(l.Tiers[0].Images.Select(i => ImageBase + i), Col(p, MassUploadMapper.ColTier1Image));
        Assert.Equal("ná nhựa kẹp kèm 1day", Cell(p.Rows[0], MassUploadMapper.ColTier1Option)); // đúng 20 ký tự, không cắt
        Assert.Empty(p.Warnings);
        Assert.False(p.NeedsFix);
    }

    [Fact]
    public void Anh_tien_to_sg_lan_vn_va_lua_chon_thua_khoang_trang()
    {
        var l = Fixture("pdp-danurl-1tang-6model-anhsg.json");
        var p = MapOk(l, BasicSchema);
        Assert.Equal(6, p.Rows.Count);
        Assert.Equal(["26000", "100000", "100000", "50000", "50000", "50000"], Col(p, MassUploadMapper.ColPrice));
        Assert.All(Col(p, MassUploadMapper.ColCategory), c => Assert.Equal("100481", c));
        var row = p.Rows[0];
        Assert.Equal(ImageBase + l.Images[0], Cell(row, MassUploadMapper.ColCoverImage));
        for (var i = 1; i < 8; i++) Assert.Equal(ImageBase + l.Images[i], Cell(row, MassUploadMapper.ColImagePrefix + i));
        Assert.False(row.ContainsKey(MassUploadMapper.ColImagePrefix + 8)); // 8 ảnh = bìa + 7
        Assert.All(l.Images, i => Assert.StartsWith("sg-", i));
        var optImages = Col(p, MassUploadMapper.ColTier1Image).ToList();
        Assert.StartsWith(ImageBase + "sg-", optImages[0]);
        Assert.StartsWith(ImageBase + "vn-", optImages[3]);
        // "Cáp sạc  nhanh 100W" (2 dấu cách) → gọn khoảng trắng: chuẩn hoá, không phải cắt → không warning.
        Assert.Equal(["Cáp sạc nhanh 100W", "120W Trắng+Cáp sạc", "120W đen+Cáp sạc", "Xanh nhạt-1.5M", "Màu tím-1.5M",
            "Xanh lá nhạt-1.5M"], Col(p, MassUploadMapper.ColTier1Option));
        Assert.Empty(p.Warnings);
    }

    [Fact]
    public void Nganh_2_cap_17_thuoc_tinh_co_gia_tri_tu_go()
    {
        var l = Fixture("pdp-video-1tang-11model-2capdm.json");
        Assert.Equal(17, l.Attributes.Count);
        Assert.Equal([100204, 100236, 100238], l.Attributes.Where(a => a.ValId == 0).Select(a => a.Id));
        Assert.Equal([100534L, 100573L], l.CategoryIds);
        Assert.Equal("vn-11134207-820l4-mk0i1kts0fsw2f", l.SizeChartImageId);

        var p = MapOk(l, BasicSchema);
        Assert.Equal(11, p.Rows.Count);
        Assert.All(Col(p, MassUploadMapper.ColPrice), c => Assert.Equal("2200000", c));
        Assert.All(Col(p, MassUploadMapper.ColCategory), c => Assert.Equal("100573", c));
        Assert.Equal(ImageBase + l.Images[8], Cell(p.Rows[0], MassUploadMapper.ColImagePrefix + 8)); // 9 ảnh đủ Q..Y
        // Bảng size dùng ảnh chung, không dùng size_chart gốc của SP.
        Assert.Equal(ImageBase + "size-chart-shared", Cell(p.Rows[0], MassUploadMapper.ColSizeChartImage));
        Assert.DoesNotContain(p.Rows[0].Keys, k => k.StartsWith(TemplateColumn.AttributePrefix)); // basic: không cột thuộc tính
        Assert.Empty(p.Warnings);

        // Ngành cấp 1 100534 ≠ 100009 → bản nâng cao 100009 bỏ qua.
        var skip = MassUploadMapper.Map(l, Defaults, AdvancedSchema, Resolve);
        Assert.Contains("không thuộc bản mẫu", Assert.IsType<MassUploadSkip>(skip).Reason);
    }

    [Fact]
    public void Ghi_ca_3_vao_ban_basic_va_doc_lai()
    {
        var names = new[] { "pdp-1tang-4model-1anh.json", "pdp-danurl-1tang-6model-anhsg.json", "pdp-video-1tang-11model-2capdm.json" };
        var products = names.Select(n => MapOk(Fixture(n), BasicSchema)).ToList();
        var f = Assert.Single(MassUploadTemplateWriter.WriteFiles(TemplatePath(Basic), BasicSchema, products, _dir, "fx", 30));
        var sheet = ReadSheet(f.Path, TemplateSchema.MainSheetName);
        Assert.Equal(Enumerable.Range(7, 21), sheet.Keys.Where(r => r >= 7).OrderBy(r => r)); // 4 + 6 + 11
        Assert.Equal([(26765720617L, 7, 4), (41557430104L, 11, 6), (24395796182L, 17, 11)],
            f.Items.Select(i => (i.ItemId, i.Rows[0], i.Rows.Count)));
        Assert.Equal("101287", sheet[7]["A"].Value);
        Assert.Null(sheet[7]["A"].Type); // cột A style số → <v>
        Assert.Equal("100481", sheet[11]["A"].Value);
        Assert.Equal("100573", sheet[27]["A"].Value);
        Assert.Equal("2200000", sheet[27]["K"].Value);
        Assert.Equal("24395796182", sheet[27]["D"].Value);
        Assert.Equal("24395796182", sheet[27]["E"].Value);
    }
}
