using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

public class SanPhamParserTests
{
    [Theory]
    [InlineData("1.036 / 3.000", 1036, 3000)]
    [InlineData("987 / 1000", 987, 1000)]
    [InlineData("987/1000", 987, 1000)]
    [InlineData("1036 / 3000", 1036, 3000)]
    [InlineData("2,450 / 5,000", 2450, 5000)]
    [InlineData("Số lượng sản phẩm: 1.036 / 3.000", 1036, 3000)]
    [InlineData("Hạn mức đăng bán: 987 / 1.000 sản phẩm", 987, 1000)]
    public void TachSoVaGioiHan_DinhDangDung_BocTachChinhXac(string text, int expectedTong, int expectedGioiHan)
    {
        var (tong, limit) = SanPhamParser.TachSoVaGioiHan(text);
        Assert.Equal(expectedTong, tong);
        Assert.Equal(expectedGioiHan, limit);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Không có thông tin sản phẩm")]
    [InlineData("Tất cả sản phẩm")]
    public void TachSoVaGioiHan_KhongCoSo_TraVeNull(string? text)
    {
        var (tong, limit) = SanPhamParser.TachSoVaGioiHan(text);
        Assert.Null(tong);
        Assert.Null(limit);
    }

    [Fact]
    public void ParseKetQua_JsonDayDu_DocDungCacTruong()
    {
        var json = """{"tongSp": 1036, "gioiHanSp": 3000, "rawText": "1.036 / 3.000", "tabTatCa": true}""";
        var res = SanPhamParser.ParseKetQua(json);

        Assert.Equal(1036, res.TongSoSp);
        Assert.Equal(3000, res.GioiHanSp);
        Assert.Equal("1.036 / 3.000", res.RawText);
        Assert.True(res.TabTatCa);
    }

    [Fact]
    public void ParseKetQua_JsonChiCoRawText_TuDongTachSo()
    {
        var json = """{"rawText": "Số lượng: 987 / 1000", "tabTatCa": true}""";
        var res = SanPhamParser.ParseKetQua(json);

        Assert.Equal(987, res.TongSoSp);
        Assert.Equal(1000, res.GioiHanSp);
        Assert.True(res.TabTatCa);
    }

    [Fact]
    public void ParseKetQua_JsonRong_TraVeNull()
    {
        var res = SanPhamParser.ParseKetQua(null);
        Assert.Null(res.TongSoSp);
        Assert.Null(res.GioiHanSp);
        Assert.False(res.TabTatCa);
    }
}
