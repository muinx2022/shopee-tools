using Shopee.Core.MassUpload.Seller;
using Shopee.Upload.Forms;

namespace Shopee.Upload.Tests;

/// <summary>GĐ5d: ô proxy tĩnh ở mục "Proxy chung" — xem trước, "Đang dùng: …", lưu / đọc lại, che mật khẩu.</summary>
public sealed class SharedStaticProxyFormTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "upload-shared-static-tests-" + Guid.NewGuid().ToString("N"));

    public SharedStaticProxyFormTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private MainViewModel NewVm() =>
        new(new SellerAccountStore(Path.Combine(_dir, "seller-accounts.json"), Path.Combine(_dir, "profiles")), a => a(), _ => true);

    [Fact]
    public void DangDung_TheoThuTuKiotRoiTinh_GhiChuKhiBiBoQua()
    {
        var f = new SettingsForm();
        Assert.Equal("Chưa có proxy chung", f.ProxyInUse);
        Assert.Null(f.StaticIgnoredNote);

        f.StaticProxy = "193.233.210.148\t8080";
        Assert.Equal("Đang dùng: proxy tĩnh", f.ProxyInUse);
        Assert.Equal("Đọc được: http://193.233.210.148:8080", f.StaticProxyPreview);
        Assert.Null(f.StaticIgnoredNote);

        f.KiotKey = "KEY-0001";
        Assert.Equal("Đang dùng: Kiot", f.ProxyInUse);
        Assert.Equal("Proxy tĩnh đang bị bỏ qua vì đã có key Kiot.", f.StaticIgnoredNote);

        f.KiotKey = "";
        f.StaticProxy = "1.2.3.4 8080 chi-co-user";
        Assert.StartsWith("Chưa có proxy chung", f.ProxyInUse);
        Assert.StartsWith("Sai:", f.StaticProxyPreview);
    }

    [Fact]
    public void XemTruoc_KhongInMatKhau()
    {
        var f = new SettingsForm { StaticProxy = "193.233.210.148\t8080\tuser1\tMatKhau#Proxy9" };
        Assert.Contains("(có user/pass — kiểm lại nếu bạn không có user/pass)", f.StaticProxyPreview);
        Assert.DoesNotContain("MatKhau#Proxy9", f.StaticProxyPreview);
        Assert.DoesNotContain("user1", f.StaticProxyPreview);
    }

    [Fact]
    public void LuuRoiMoLai_GiuProxyTinhChung_TrimHaiDauKhiLuu()
    {
        var vm = NewVm();
        vm.Settings.StaticProxy = "\r\n193.233.210.148\t8080\r\n";
        Assert.True(vm.IsDirty);
        vm.AddCommand.Execute(null);
        Assert.True(vm.TrySave(), string.Join("; ", vm.Errors));

        var again = NewVm();
        Assert.Equal("193.233.210.148\t8080", again.Settings.StaticProxy);
        Assert.Equal("Đang dùng: proxy tĩnh", again.Settings.ProxyInUse);
    }

    [Theory]
    [InlineData("193.233.210.148:8080:user1:MatKhau#Proxy9")]
    [InlineData("193.233.210.148\t8080\tuser1\tMatKhau#Proxy9")]
    [InlineData("193.233.210.148 8080 MatKhau#Proxy9")]   // sai dạng (3 đoạn) — vẫn phải che
    public void Secrets_CheMatKhauProxyTinhChung_LogKhongLo(string raw)
    {
        var vm = NewVm();
        vm.Settings.StaticProxy = raw;

        Assert.Contains("MatKhau#Proxy9", SellerFormMapper.Secrets(vm.Settings, vm.Accounts));
        vm.Log("thử proxy MatKhau#Proxy9 lỗi");
        Assert.DoesNotContain("MatKhau#Proxy9", vm.LogText);
        Assert.DoesNotContain(vm.Errors.Concat(vm.Warnings), x => x.Contains("MatKhau#Proxy9"));
    }

    [Fact]
    public void HaiShopDiProxyTinhChung_CanhBaoTrenMan_VanLuu()
    {
        var vm = NewVm();
        vm.Settings.StaticProxy = "193.233.210.148\t8080";
        vm.AddCommand.Execute(null);
        vm.AddCommand.Execute(null);

        Assert.Contains(vm.Warnings, w => w.StartsWith("2 shop dùng chung 1 IP tĩnh"));
        Assert.False(vm.HasErrors, string.Join("; ", vm.Errors));
        Assert.True(vm.TrySave());
    }
}
