using Shopee.Core.MassUpload;
using static Shopee.Core.Tests.MassUpload.MassUploadTestData;

namespace Shopee.Core.Tests.MassUpload;

/// <summary>Thuộc tính đã là tiếng Việt (cào bằng Shopee tiếng Việt) khớp THẲNG danh sách giá trị của mẫu (sheet "Ánh xạ giá trị thuộc
/// tính", theo ngành lá của SP) — trước khi tra từ điển Anh → Việt. Mẫu thật ngành 100017 + raw thật ngành lá 100102.</summary>
public sealed class MassUploadVietnameseAttributeTests
{
    private const string Template100017 = "advanced-100017.xlsx";
    private const string VayDon = "listing-vi-vay-100017.json";                       // voan / Trung Quốc / Midi / Hằng Ngày / Mùa hè
    private const string VayNhieuGiaTri = "listing-vi-vay-100017-nhieu-gia-tri.json"; // "voan, Ren" / Việt Nam / Maxi / Mùa xuân

    private static readonly Lazy<TemplateSchema> Schema = new(() => TemplateSchema.Load(TemplatePath(Template100017)));

    /// <summary>Raw thật (SP công khai, cào bằng Shopee tiếng Việt) để cạnh file mẫu trong <c>Fixtures/mass-upload</c> — KHÔNG để ở
    /// <c>Fixtures/pdp</c> (bộ fixture đó được test golden đếm đủ số).</summary>
    private static ShopeeListing Fixture(string name) => ShopeeListingReader.Read(File.ReadAllText(TemplatePath(name)));

    private static string Code(long attrId) => TemplateColumn.AttributePrefix + attrId;

    private static IReadOnlyDictionary<string, MassUploadCell> FirstRow(ShopeeListing listing) => MapOk(listing, Schema.Value).Rows[0];

    private static string Cell(IReadOnlyDictionary<string, MassUploadCell> row, long attrId) =>
        row.TryGetValue(Code(attrId), out var c) ? c.Text : "";

    [Fact]
    public void MauDocDuocDanhSachGiaTriTheoNganhLa()
    {
        var mapped = Schema.Value.GetMappedValues(100102, Code(100037));
        Assert.Contains("Việt Nam", mapped);
        Assert.Contains("Trung Quốc", mapped);
        Assert.Contains("voan", Schema.Value.GetMappedValues(100102, Code(100134)));
        Assert.Empty(Schema.Value.GetMappedValues(999999, Code(100037)));
    }

    [Fact]
    public void ThuocTinhTiengViet_KhopThang_GhiDungChuoiCuaMau_KhongCanhBao()
    {
        var listing = Fixture(VayDon);
        var p = MapOk(listing, Schema.Value);
        var row = p.Rows[0];

        Assert.Equal("Trung Quốc", Cell(row, 100037));   // Xuất xứ
        Assert.Equal("voan", Cell(row, 100134));         // Chất liệu
        Assert.Equal("Midi", Cell(row, 100151));         // Chiều dài váy
        Assert.Equal("Hằng Ngày", Cell(row, 100155));    // Dịp
        Assert.Equal("Trơn", Cell(row, 100162));         // Mẫu
        Assert.Equal("Mùa hè", Cell(row, 100163));       // Mùa
        var kieuVay = Cell(row, 100152);                 // Kiểu váy: đúng chuỗi trong danh sách, không khớp gần đúng
        Assert.Contains(kieuVay, Schema.Value.GetMappedValues(listing.LeafCategoryId, Code(100152)));
        Assert.Equal("váy xòe", kieuVay, ignoreCase: true);
        Assert.DoesNotContain(p.Warnings, w => w.Contains("từ điển", StringComparison.Ordinal));
    }

    [Fact]
    public void NhieuGiaTri_TachDauPhay_TungPhanKhopThang()
    {
        var listing = Fixture(VayNhieuGiaTri);
        var allowed = Schema.Value.GetMappedValues(listing.LeafCategoryId, Code(100134));
        var p = MapOk(listing, Schema.Value);
        var row = p.Rows[0];

        Assert.Equal("Việt Nam", Cell(row, 100037));
        Assert.Equal("Maxi", Cell(row, 100151));
        Assert.Equal("Mùa xuân", Cell(row, 100163));
        var chatLieu = Cell(row, 100134).Split(';');
        Assert.Contains("voan", chatLieu);
        // "Ren" khớp nếu mẫu có, không thì bị bỏ kèm cảnh báo.
        if (allowed.Contains("Ren")) Assert.Equal(["voan", "Ren"], chatLieu);
        else
        {
            Assert.Equal(["voan"], chatLieu);
            Assert.Contains(p.Warnings, w => w.Contains("Ren", StringComparison.Ordinal));
        }
        // Thuộc tính tự gõ (không có trong danh sách) giữ hành vi cũ: ghi nguyên chuỗi.
        Assert.Equal("cạp chun 2cm", Cell(row, 100171));
    }

    [Fact]
    public void KhacHoaThuong_KhoangTrangThua_VanKhop_GhiChuoiCuaMau()
    {
        var listing = Fixture(VayDon);
        listing = listing with
        {
            Attributes = listing.Attributes.Select(a => a.Id switch
            {
                100037 => a with { Value = "  trung   QUỐC " },
                100163 => a with { Value = "MÙA HÈ" },
                100155 => a with { Value = "hằng ngày" },
                _ => a,
            }).ToList(),
        };

        var row = FirstRow(listing);

        Assert.Equal("Trung Quốc", Cell(row, 100037));
        Assert.Equal("Mùa hè", Cell(row, 100163));
        Assert.Equal("Hằng Ngày", Cell(row, 100155));
    }

    [Fact]
    public void KhongKhopGanDung()
    {
        var listing = Fixture(VayDon);
        var allowed = Schema.Value.GetMappedValues(listing.LeafCategoryId, Code(100152));
        // Giá trị chỉ là một phần của một giá trị trong danh sách → không tự thành giá trị dài hơn.
        var longer = allowed.First(v => v.Length > 4);
        var prefix = longer[..^2];
        Assert.DoesNotContain(allowed, v => string.Equals(v, prefix, StringComparison.OrdinalIgnoreCase));
        listing = listing with { Attributes = listing.Attributes.Select(a => a.Id == 100152 ? a with { Value = prefix } : a).ToList() };

        var p = MapOk(listing, Schema.Value);

        Assert.NotEqual(longer, Cell(p.Rows[0], 100152));
    }
}
