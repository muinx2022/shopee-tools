using Shopee.Core.MassUpload;
using static Shopee.Core.Tests.MassUpload.MassUploadTestData;

namespace Shopee.Core.Tests.MassUpload;

public class TemplateSchemaTests
{
    [Fact]
    public void Ban_nang_cao_100009_dong_1_2_va_style()
    {
        var s = AdvancedSchema;
        Assert.Equal(TemplateKind.Advanced, s.Kind);
        Assert.Equal(100009, s.TopCategoryId);
        Assert.Equal("214522044", s.ShopId);
        Assert.Equal("200a70819d3c07e7ead3dd4f9d9eae3f", s.Checksum);
        Assert.Equal("xl/worksheets/sheet2.xml", s.SheetPart);
        Assert.Equal(69, s.Columns.Count); // A..BQ

        Assert.True(s.TryGetColumn("ps_category", out var a));
        Assert.Equal(("A", true, true), (a.Letter, a.Required, a.IsNumeric)); // style 5 = numFmt "0"
        Assert.True(s.TryGetColumn("ps_price", out var k));
        Assert.Equal(("K", true, false), (k.Letter, k.Required, k.IsNumeric)); // style 6 = numFmt 49 (Text)
        Assert.True(s.TryGetColumn("ps_stock", out var l));
        Assert.Equal(("L", false, true), (l.Letter, l.Required, l.IsNumeric));
        Assert.True(s.TryGetColumn("ps_brand", out var al));
        Assert.Equal("AL", al.Letter);
        Assert.True(s.TryGetColumn("et_title_reason", out var bq));
        Assert.Equal("BQ", bq.Letter);
        Assert.Equal(["ps_category", "ps_product_name", "ps_product_description", "ps_price", "ps_weight"],
            s.Columns.Where(c => c.Required).Select(c => c.Code));
    }

    [Fact]
    public void Kieu_nhap_dong_6_va_ten_cot_dong_3()
    {
        var s = AdvancedSchema;
        Assert.True(s.TryGetColumn("ps_product_global_attribute.100022", out var gender));
        Assert.Equal(("AN", "Giới tính", AttributeInputKind.List, false), (gender.Letter, gender.Title, gender.InputKind, gender.MultiValue));
        Assert.Equal(100022, gender.AttributeId);
        Assert.True(s.TryGetColumn("ps_product_global_attribute.100169", out var style));
        Assert.Equal((AttributeInputKind.Custom, true), (style.InputKind, style.MultiValue));
        Assert.True(s.TryGetColumn("ps_product_global_attribute.100037", out var origin));
        Assert.Equal((AttributeInputKind.Custom, false), (origin.InputKind, origin.MultiValue));
    }

    [Fact]
    public void HiddenCatProps_100026_chi_Gioi_tinh_la_MANDATORY()
    {
        var s = AdvancedSchema;
        Assert.True(s.HasCategory(100026));
        Assert.Equal("100026-Phụ Kiện Thời Trang/Vòng tay & Lắc tay", s.CategoryEntry(100026));
        Assert.Equal(31, s.LeafCategoryIds.Count); // HiddenCatProps!A7:A37
        var mandatory = s.Columns.Where(c => s.GetRequirement(100026, c.Code) == AttributeRequirement.Mandatory)
            .Select(c => c.Code).ToList();
        Assert.Equal(["ps_product_global_attribute.100022"], mandatory);
        Assert.Equal(AttributeRequirement.Optional, s.GetRequirement(100026, "ps_product_global_attribute.100037"));
        Assert.Equal(AttributeRequirement.Irrelevant, s.GetRequirement(100026, "ps_product_global_attribute.100024"));
        Assert.Equal(AttributeRequirement.None, s.GetRequirement(100026, "ps_price"));
        Assert.Equal(AttributeRequirement.None, s.GetRequirement(999, "ps_product_global_attribute.100022"));
        Assert.False(s.HasCategory(101847));
    }

    [Fact]
    public void HiddenAttr_Gioi_tinh_100026()
    {
        var s = AdvancedSchema;
        Assert.Equal(["Nữ", "Nam", "Unisex"], s.GetListValues(100026, "ps_product_global_attribute.100022"));
        Assert.Equal(["Đúng", "Không"], s.GetListValues(100026, "ps_product_global_attribute.101219"));
        Assert.Empty(s.GetListValues(100026, "ps_product_global_attribute.100169")); // kiểu tùy chỉnh
        Assert.Equal(["Skinny", "Tiêu chuẩn", "Rộng"], s.GetListValues(100033, "ps_product_global_attribute.100197"));
    }

    [Fact]
    public void Ban_basic()
    {
        var s = BasicSchema;
        Assert.Equal(TemplateKind.Basic, s.Kind);
        Assert.Null(s.TopCategoryId);
        Assert.Equal("214522044", s.ShopId);
        Assert.Equal(38, s.Columns.Count); // A..AL
        Assert.Empty(s.LeafCategoryIds);
        Assert.False(s.TryGetColumn("ps_brand", out _));
        Assert.True(s.TryGetColumn("ps_category", out var a));
        Assert.False(a.Required); // basic: ps_category|0|0
        Assert.Equal(["channel_id.5000", "channel_id.5001", "channel_id.5004", "channel_id.5012", "channel_id.50039",
            "channel_id.50052", "channel_id.50053"], s.Columns.Where(c => c.Code.StartsWith("channel_id.")).Select(c => c.Code));
    }

    [Fact]
    public void File_khong_phai_ban_mau_thi_nem_loi_ro()
    {
        var dir = NewTempDir();
        try
        {
            var path = Path.Combine(dir, "x.xlsx");
            using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
            {
                using (var w = new StreamWriter(zip.CreateEntry("xl/workbook.xml").Open()))
                    w.Write("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheets/></workbook>");
            }
            var ex = Assert.Throws<InvalidDataException>(() => TemplateSchema.Load(path));
            Assert.Contains("Bản đăng tải", ex.Message);
        }
        finally { HarvestTestData.TryDelete(dir); }
    }
}
