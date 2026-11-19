using Shopee.Core.MassUpload.Seller;
using Shopee.Upload.Forms;

namespace Shopee.Upload.Tests;

/// <summary>GĐ5b: 4 ô đăng nhập trên form ↔ <see cref="SellerAccount"/>, che mật khẩu.</summary>
public sealed class SellerLoginFormTests
{
    private static SellerAccount Sample() => new()
    {
        Id = "shop-a",
        Label = "Shop A",
        ShopeeLogin = "shop.a@gmail.com",
        ShopeePassword = " Pass word#1 ",
        VerifyEmail = "shop.a@hotmail.com",
        VerifyEmailPassword = "MailPass#99",
    };

    [Fact]
    public void RoundTrip_GiuNguyenBonO_MatKhauKhongTrim()
    {
        var a = Sample();
        var f = SellerFormMapper.ToForm(a);
        Assert.Equal("shop.a@gmail.com", f.ShopeeLogin);
        Assert.Equal(" Pass word#1 ", f.ShopeePassword);

        var back = SellerFormMapper.ToAccount(f, [], a);

        Assert.Equal(a.ShopeeLogin, back.ShopeeLogin);
        Assert.Equal(" Pass word#1 ", back.ShopeePassword);   // dấu cách là một phần mật khẩu
        Assert.Equal(a.VerifyEmail, back.VerifyEmail);
        Assert.Equal(a.VerifyEmailPassword, back.VerifyEmailPassword);
    }

    [Fact]
    public void TaiKhoanEmail_Trim_ORong_LaNull()
    {
        var f = SellerFormMapper.NewAccountForm("shop-b");
        f.ShopeeLogin = "  0901234567  ";
        f.VerifyEmail = "   ";
        var a = SellerFormMapper.ToAccount(f, []);
        Assert.Equal("0901234567", a.ShopeeLogin);
        Assert.Null(a.VerifyEmail);
        Assert.Null(a.ShopeePassword);
        Assert.Null(a.VerifyEmailPassword);
    }

    [Fact]
    public void Secrets_CoCaHaiMatKhau_KhongCoTaiKhoanEmail()
    {
        var f = SellerFormMapper.ToForm(Sample());
        var secrets = SellerFormMapper.Secrets(new SettingsForm(), [f]).ToList();

        Assert.Contains(" Pass word#1 ", secrets);
        Assert.Contains("MailPass#99", secrets);
        Assert.DoesNotContain("shop.a@gmail.com", secrets);

        var redacted = SecretText.Redact("pass=Pass word#1, mail=MailPass#99", secrets);
        Assert.DoesNotContain("Pass word#1", redacted);
        Assert.DoesNotContain("MailPass#99", redacted);
    }

    [Fact]
    public void Mask_ChiHien4KyTuCuoi_SuaMatKhauLaCoThayDoi()
    {
        var f = SellerFormMapper.ToForm(Sample());
        Assert.Equal("…s#99", f.VerifyEmailPasswordMask);
        var edited = 0;
        f.Edited += () => edited++;
        f.ShopeePassword = "Moi#2026";
        Assert.Equal("…2026", f.ShopeePasswordMask);
        Assert.True(edited > 0);
    }
}
