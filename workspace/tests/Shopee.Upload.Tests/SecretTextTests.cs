using Shopee.Upload.Forms;

namespace Shopee.Upload.Tests;

public class SecretTextTests
{
    [Fact]
    public void Mask_ChiConBonKyTuCuoi()
    {
        Assert.Equal("…WXYZ", SecretText.Mask("abcdefWXYZ"));
        Assert.Equal("…WXYZ", SecretText.Mask("  abcdefWXYZ  "));
        Assert.Equal("(chưa nhập)", SecretText.Mask(""));
        Assert.Equal("(chưa nhập)", SecretText.Mask(null));
        Assert.Equal("-", SecretText.Mask("   ", empty: "-"));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("abcd")]
    [InlineData(" abcd ")]
    public void Mask_BiMatNgan_KhongHienKyTuNao(string secret)
    {
        Assert.Equal(SecretText.ShortMask, SecretText.Mask(secret));
        Assert.DoesNotContain("abcd", SecretText.Mask(secret));
    }

    [Theory]
    [InlineData("abcde", "…de")]        // 5 ký tự → tối đa nửa = 2
    [InlineData("abcdef", "…def")]      // 6 → 3
    [InlineData("abcdefg", "…efg")]     // 7 → 3
    [InlineData("abcdefgh", "…efgh")]   // 8 → 4
    [InlineData("abcdefghij", "…ghij")] // dài → vẫn 4
    public void Mask_KhongHienQuaMotNua(string secret, string expected) =>
        Assert.Equal(expected, SecretText.Mask(secret));

    [Fact]
    public void Redact_ThayMoiLanXuatHien_KhongConBiMatNguyen()
    {
        const string key = "KIOT-SECRET-123456";
        var text = $"gọi /new?key={key} lỗi; thử lại key {key}";

        var r = SecretText.Redact(text, [key]);

        Assert.DoesNotContain(key, r);
        Assert.Equal("gọi /new?key=*** lỗi; thử lại key ***", r);   // log che hẳn, không lộ 4 ký tự cuối
    }

    [Fact]
    public void Redact_BiMatDaiXuLyTruoc_BiMatQuaNganKhongDuocThay()
    {
        // Key dài chứa key ngắn: thay key ngắn trước sẽ để lại phần đầu của key dài trong log. Ngưỡng 3 ký tự như Core.
        var r = SecretText.Redact("a=AAAA-BBBB-CCCC b=BBBB-CCCC c=xyz d=ab", ["BBBB-CCCC", "AAAA-BBBB-CCCC", "xyz", "ab", null, ""]);

        Assert.Equal("a=*** b=*** c=*** d=ab", r);
        Assert.Equal(Shopee.Core.MassUpload.Seller.Login.SellerLoginText.MinScrubLength, SecretText.MinRedactLength);
    }
}
