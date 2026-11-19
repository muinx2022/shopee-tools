using Shopee.Core.MassUpload.Seller;
using Shopee.Upload.Forms;

namespace Shopee.Upload.Tests;

/// <summary>Màn chính chạy không cần WPF (luồng UI giả = chạy ngay). Không có test nào tới được Brave/Kiot thật: các ca mở Brave
/// đều dừng ở bước kiểm cấu hình của <see cref="SellerRunGate"/>, trước mọi request mạng.</summary>
public sealed class MainViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "upload-vm-tests-" + Guid.NewGuid().ToString("N"));

    public MainViewModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string FilePath => Path.Combine(_dir, "seller-accounts.json");

    private SellerAccountStore NewStore() => new(FilePath, Path.Combine(_dir, "profiles"));

    private MainViewModel NewVm(Func<string, bool>? confirm = null) => new(NewStore(), a => a(), confirm ?? (_ => true));

    [Fact]
    public void Them_SuaLuu_MoLaiThayDuDuLieu()
    {
        var vm = NewVm();
        vm.Settings.KiotKey = "SHARED-KEY-0001";
        vm.Settings.Region = "bac";
        vm.AddCommand.Execute(null);
        vm.Selected!.Label = "Shop A";
        vm.Selected.WeightGrams = "450";
        Assert.True(vm.IsDirty);

        Assert.True(vm.TrySave(), string.Join("; ", vm.Errors));
        Assert.False(vm.IsDirty);

        var again = NewVm();
        Assert.Equal("SHARED-KEY-0001", again.Settings.KiotKey);
        var a = Assert.Single(again.Accounts);
        Assert.Equal("seller-1", a.Id);
        Assert.Equal("Shop A", a.Label);
        Assert.Equal("450", a.WeightGrams);
        Assert.Equal(Path.Combine(_dir, "profiles", "seller-1"), a.ResolvedProfileDir);
        Assert.False(again.IsDirty);
    }

    [Fact]
    public void CoLoi_HienNgay_VaKhongLuu()
    {
        var vm = NewVm();
        vm.AddCommand.Execute(null);
        vm.Selected!.Proxy = ProxyChoice.Static;
        vm.Selected.StaticProxy = "khong-co-cong";

        Assert.True(vm.HasErrors);
        Assert.Contains(vm.Errors, e => e.Contains("proxy tĩnh sai"));
        Assert.False(vm.TrySave());
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void DocFileLoi_KhoaLuu_KhongGhiDe()
    {
        File.WriteAllText(FilePath, "{\"Accounts\":[{\"Id\":\"shop-a\"}]}");
        var before = File.ReadAllBytes(FilePath);

        MainViewModel vm;
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            vm = NewVm();   // Store.Load ném IOException vì file đang bị khoá
        }

        Assert.True(vm.LoadFailed);
        Assert.False(vm.SaveCommand.CanExecute(null));
        vm.AddCommand.Execute(null);
        Assert.False(vm.TrySave());
        Assert.Equal(before, File.ReadAllBytes(FilePath));
    }

    [Fact]
    public async Task ThieuKeyChung_NutMoBraveBaoLoi_KhongMoBrave()
    {
        var vm = NewVm();
        vm.AddCommand.Execute(null);   // tài khoản đi proxy chung, chưa có key chung

        await vm.OpenBraveCommand.ExecuteAsync(null);

        Assert.Null(vm.Session);
        Assert.False(vm.HasSession);
        Assert.Contains("Chưa cấu hình key Kiot chung", vm.BrowserStatus);
        Assert.Contains("ProxyConfig", vm.LogText);
        Assert.True(vm.OpenBraveCommand.CanExecute(null));   // mở lại được sau khi báo lỗi
    }

    [Fact]
    public async Task ProxyTinhSai_NutMoBraveBaoLoi_KhongMoBrave()
    {
        var vm = NewVm();
        vm.AddCommand.Execute(null);
        vm.Selected!.Proxy = ProxyChoice.Static;
        vm.Selected.StaticProxy = "1.2.3.4:abc";

        await vm.OpenBraveCommand.ExecuteAsync(null);

        Assert.Null(vm.Session);
        Assert.Contains("proxy tĩnh sai", vm.BrowserStatus);
        Assert.False(vm.OpenBraveCommand.CanExecute(null));   // lỗi Validate của tài khoản chặn luôn nút
    }

    [Fact]
    public async Task ONhapTaiKhoanSai_KhongGoiCong()
    {
        var vm = NewVm();
        vm.Settings.KiotKey = "SHARED-KEY-0001";
        vm.AddCommand.Execute(null);
        vm.Selected!.WeightGrams = "abc";

        await vm.OpenBraveCommand.ExecuteAsync(null);

        Assert.Null(vm.Session);
        Assert.Contains("tài khoản đang có lỗi", vm.BrowserStatus);
        Assert.Contains("cân nặng", vm.BrowserStatus);
        Assert.DoesNotContain("Lấy proxy", vm.LogText);
    }

    [Fact]
    public void Log_CheKeyVaMatKhau()
    {
        var vm = NewVm();
        vm.Settings.KiotKey = "SHARED-KEY-0001";
        vm.AddCommand.Execute(null);
        vm.Selected!.Proxy = ProxyChoice.Static;
        vm.Selected.StaticProxy = "1.2.3.4:8080:user:Mat-Khau-9";

        vm.Log("url ?key=SHARED-KEY-0001 pass=Mat-Khau-9");

        Assert.DoesNotContain("SHARED-KEY-0001", vm.LogText);
        Assert.DoesNotContain("Mat-Khau-9", vm.LogText);
        Assert.Contains("?key=*** pass=***", vm.LogText);   // che hẳn, không lộ 4 ký tự cuối (GĐ5b vòng 2)
    }

    [Fact]
    public void Xoa_HoiTruoc_KhongDongYThiGiuNguyen()
    {
        var answer = false;
        var vm = NewVm(_ => answer);
        vm.AddCommand.Execute(null);
        vm.AddCommand.Execute(null);

        vm.DeleteCommand.Execute(null);
        Assert.Equal(2, vm.Accounts.Count);

        answer = true;
        vm.DeleteCommand.Execute(null);
        Assert.Single(vm.Accounts);
        Assert.Equal("seller-1", vm.Selected!.Id);
    }

    [Fact]
    public void Them_KhongDungLaiMaCoThuMucProfileCu()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "profiles", "seller-1"));
        var vm = NewVm();

        vm.AddCommand.Execute(null);

        Assert.Equal("seller-2", vm.Selected!.Id);
    }

    // ─────────────── vòng 3 ───────────────

    [Fact]
    public void DocFileLoi_BannerGiuSuotPhien_SauKhiSuaO()
    {
        File.WriteAllText(FilePath, "{\"Accounts\":[]}");
        MainViewModel vm;
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None)) vm = NewVm();

        vm.Settings.KiotKey = "SHARED-KEY-0001";   // lần kiểm lại sau khi sửa ô
        vm.AddCommand.Execute(null);

        Assert.Contains(vm.Errors, e => e.Contains("KHÔNG lưu"));
    }

    [Fact]
    public void FileHong_BaoRoDaDoiTen_KhongBaoChuaCoCauHinh()
    {
        File.WriteAllText(FilePath, "{ hỏng");

        var vm = NewVm();
        vm.AddCommand.Execute(null);   // sửa ô — cảnh báo vẫn còn

        var bak = Assert.Single(Directory.GetFiles(_dir, "seller-accounts.json.corrupt-*"));
        Assert.Contains(vm.Warnings, w => w.Contains(bak) && w.Contains("tài khoản cũ"));
        Assert.DoesNotContain("Chưa có cấu hình", vm.SaveStatus);
        Assert.Contains("hỏng", vm.SaveStatus);
    }

    [Fact]
    public void XoaTaiKhoan_MaKhongBiCapLai_KeCaSauKhiMoLaiApp()
    {
        var vm = NewVm();
        vm.Settings.KiotKey = "SHARED-KEY-0001";
        vm.AddCommand.Execute(null);
        vm.Selected!.ProfileDir = Path.Combine(_dir, "custom-profile");   // profile tuỳ chỉnh: không có thư mục mặc định trên đĩa
        Assert.True(vm.TrySave(), string.Join("; ", vm.Errors));

        vm.DeleteCommand.Execute(null);
        vm.AddCommand.Execute(null);
        Assert.Equal("seller-2", vm.Selected!.Id);
        Assert.True(vm.TrySave(), string.Join("; ", vm.Errors));

        var again = NewVm();
        again.AddCommand.Execute(null);
        Assert.Equal("seller-3", again.Selected!.Id);
        Assert.Contains("\"seller-1\"", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Luu_GiuTruongLa_VaBaoKhiBoProxyRieng()
    {
        File.WriteAllText(FilePath,
            "{\"Version\":1,\"Future\":true,\"Settings\":{\"SharedProxy\":{\"KiotKey\":\"SHARED-KEY-0001\",\"Region\":\"bac\"}}," +
            "\"Accounts\":[{\"Id\":\"shop-a\",\"Label\":\"Shop A\",\"Mode\":\"Subaccount\",\"SubLogin\":\"abc\"," +
            "\"ProxyOverride\":{\"Kind\":\"Kiot\",\"KiotKey\":\"OWN-KEY-0002\",\"Region\":\"nam\",\"Extra\":5}}]}");
        var vm = NewVm();
        var a = Assert.Single(vm.Accounts);

        a.Label = "Shop A2";
        Assert.True(vm.TrySave(), string.Join("; ", vm.Errors));
        var json = File.ReadAllText(FilePath);
        Assert.Contains("\"Mode\": \"Subaccount\"", json);
        Assert.Contains("\"SubLogin\": \"abc\"", json);
        Assert.Contains("\"Extra\": 5", json);
        Assert.Contains("\"Future\": true", json);
        Assert.Contains("Shop A2", json);
        Assert.DoesNotContain("đã bỏ proxy riêng", vm.LogText);

        a.Proxy = ProxyChoice.Shared;
        Assert.True(vm.TrySave(), string.Join("; ", vm.Errors));
        Assert.Contains("đã bỏ proxy riêng của Shop A2 (shop-a)", vm.LogText);
        json = File.ReadAllText(FilePath);
        Assert.Contains("\"Mode\": \"Subaccount\"", json);
        Assert.DoesNotContain("OWN-KEY-0002", json);
    }
}
