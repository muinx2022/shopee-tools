using System.Text;
using Shopee.Core.MassUpload;

namespace Shopee.Core.Tests.MassUpload;

public class AttributeDictionaryTests
{
    [Theory]
    [InlineData(100022, "Female", "Nữ")]
    [InlineData(100022, "MALE", "Nam")]
    [InlineData(101219, "Yes", "Đúng")]
    [InlineData(100185, "yes", "Có")]
    [InlineData(100037, "United States", "Mỹ")]
    [InlineData(101068, "Updating", "Đang cập nhật")]
    [InlineData(100169, "Casual", "Binh thương")] // đúng chữ (sai chính tả) của bản mẫu Shopee
    [InlineData(100169, "Vintage", "Retro")]
    [InlineData(100155, "Wedding", "lễ cưới")]
    [InlineData(100194, "Chain & Link", "Dạng chuỗi")]
    [InlineData(100121, "12 Months", "12 tháng")] // luật mẫu chung
    [InlineData(100121, "1 Month", "1 tháng")]
    public void Dich_gia_tri_mac_dinh(long attrId, string en, string vn)
    {
        Assert.True(AttributeDictionary.Default.TryTranslate(attrId, en, out var got));
        Assert.Equal(vn, got);
    }

    [Theory]
    [InlineData(100022, "Kids")]
    [InlineData(100169, "Casual, Elegant")] // nhiều phần: mapper tự tách
    [InlineData(999999, "Female")]          // khoá theo mã thuộc tính
    [InlineData(100121, "5 Years")]
    public void Chua_co_thi_false(long attrId, string en)
    {
        Assert.False(AttributeDictionary.Default.TryTranslate(attrId, en, out _));
    }

    [Fact]
    public void Nap_tu_JSON_bo_sung_va_sai_dang_thi_nem()
    {
        var json = """{"attributes":{"100245":{"values":{"Business":"Doanh nhân"}}},"patterns":[{"attrIds":[100121],"match":"^(\\d+) Years?$","replace":"$1 năm"}]}""";
        var d = AttributeDictionary.Load(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        Assert.True(d.TryTranslate(100245, "business", out var b));
        Assert.Equal("Doanh nhân", b);
        Assert.True(d.TryTranslate(100121, "5 Years", out var y));
        Assert.Equal("5 năm", y);
        Assert.False(d.TryTranslate(100000, "5 Years", out _));

        Assert.Throws<FormatException>(() =>
            AttributeDictionary.Load(new MemoryStream(Encoding.UTF8.GetBytes("""{"attributes":{"abc":{}}}"""))));
    }
}
