using System.Globalization;
using System.Linq;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// Tick ô <b>Chuẩn bị hàng</b> của tab "Shops": <c>IsChecking</c> khi đang check shop đó, <c>ShowTick</c> khi đã
/// check XONG shop đó trong lượt chạy, để trống khi chưa tới. Không đụng tick KM/Combo/Flash.
/// Sự kiện <c>AppServices.ShopCheckChanged</c> do phiên cầu nối bắn; test gọi thẳng
/// <c>RaiseShopCheckChanged</c> nên không cần trình duyệt. Chạy trên thread test (CheckAccess()==true) →
/// <c>RunOnUi</c> chạy đồng bộ, giống lúc phiên bắn về UI thread.
/// </summary>
public class ShopCheckProgressTests
{
    private const string LoginA = "alina99.store";
    private const string LoginB = "shop9x.store";

    /// <summary>Dựng VM đang mở MỘT tài khoản có 2 shop (tên hiển thị KHÁC login — đúng dữ liệu thật).</summary>
    private static (AppServices Services, AccountsViewModel Vm, long AccountId) NewVmWith2Shops(TempDatabase temp)
    {
        var services = new AppServices(temp.Path);
        services.Accounts.Insert(new Account { Email = "a@mail.com", Password = "p" });

        var vm = new AccountsViewModel(services);
        var accountId = vm.Accounts.First().Id;
        services.Results.UpsertShops(accountId, new[]
        {
            new ShopListItem("111", "Alina Store1", LoginA),
            new ShopListItem("222", "Shop 9X", LoginB),
        });

        vm.SelectedRow = vm.Accounts.First(); // nạp lưới kết quả của tài khoản này
        Assert.Equal(2, vm.ResultRows.Count);
        return (services, vm, accountId);
    }

    private static ShopPrepareRow Row(AccountsViewModel vm, string login)
        => vm.ResultRows.Single(r => r.ShopLogin == login);

    // ===== Bắt đầu check shop A → vòng quay ở A (chưa tick), B sạch cờ =====
    [Fact]
    public void BatDauCheckShopA_VongQuayOA_ChuaTick()
    {
        using var temp = new TempDatabase();
        var (services, vm, accountId) = NewVmWith2Shops(temp);

        services.RaiseShopCheckChanged(accountId, LoginA, checking: true);

        var a = Row(vm, LoginA);
        Assert.True(a.IsChecking);
        Assert.False(a.DaKiemTra);
        Assert.False(a.ShowTick); // đang check thì chữ "đang kiểm tra…" thế chỗ tick

        var b = Row(vm, LoginB);
        Assert.False(b.IsChecking);
        Assert.False(b.ShowTick);
    }

    // ===== Xong shop A → vòng quay TẮT, A nhận dấu TICK =====
    [Fact]
    public void XongShopA_TatVongQuay_AHienTick()
    {
        using var temp = new TempDatabase();
        var (services, vm, accountId) = NewVmWith2Shops(temp);

        services.RaiseShopCheckChanged(accountId, LoginA, checking: true);
        services.RaiseShopCheckChanged(accountId, LoginA, checking: false);

        var a = Row(vm, LoginA);
        Assert.False(a.IsChecking); // hết quay
        Assert.True(a.DaKiemTra);
        Assert.True(a.ShowTick);
        Assert.False(a.ShowTickKm);
        Assert.False(a.ShowTickCombo);
        Assert.False(a.ShowTickFlash);

        var b = Row(vm, LoginB);
        Assert.False(b.ShowTick); // B chưa tới → để trống
    }

    // ===== Shop kế bắt đầu → A GIỮ tick (đã kiểm tra), B quay =====
    [Fact]
    public void BatDauShopB_AGiuTick_BQuay()
    {
        using var temp = new TempDatabase();
        var (services, vm, accountId) = NewVmWith2Shops(temp);

        services.RaiseShopCheckChanged(accountId, LoginA, checking: true);
        services.RaiseShopCheckChanged(accountId, LoginA, checking: false);
        services.RaiseShopCheckChanged(accountId, LoginB, checking: true);

        var a = Row(vm, LoginA);
        Assert.False(a.IsChecking);
        Assert.True(a.ShowTick); // tick Ở LẠI shop đã kiểm tra xong

        var b = Row(vm, LoginB);
        Assert.True(b.IsChecking);
        Assert.False(b.ShowTick);
    }

    // ===== Cả 2 shop xong → CẢ HAI cùng có tick (biết đã kiểm tra hết) =====
    [Fact]
    public void XongCaHaiShop_CaHaiDeuCoTick()
    {
        using var temp = new TempDatabase();
        var (services, vm, accountId) = NewVmWith2Shops(temp);

        services.RaiseShopCheckChanged(accountId, LoginA, checking: true);
        services.RaiseShopCheckChanged(accountId, LoginA, checking: false);
        services.RaiseShopCheckChanged(accountId, LoginB, checking: true);
        services.RaiseShopCheckChanged(accountId, LoginB, checking: false);

        Assert.True(Row(vm, LoginA).ShowTick);
        Assert.True(Row(vm, LoginB).ShowTick);
    }

    // ===== Sự kiện của TÀI KHOẢN KHÁC → lưới đang mở KHÔNG đổi gì =====
    [Fact]
    public void SuKienTaiKhoanKhac_LuoiDangMoKhongDoi()
    {
        using var temp = new TempDatabase();
        var (services, vm, accountId) = NewVmWith2Shops(temp);
        services.Accounts.Insert(new Account { Email = "b@mail.com", Password = "p" });
        var otherId = services.Accounts.GetAll().Single(a => a.Email == "b@mail.com").Id;
        Assert.NotEqual(accountId, otherId);

        services.RaiseShopCheckChanged(otherId, LoginA, checking: true);
        services.RaiseShopCheckChanged(otherId, LoginA, checking: false);

        Assert.All(vm.ResultRows, r =>
        {
            Assert.False(r.IsChecking);
            Assert.False(r.DaKiemTra);
            Assert.False(r.ShowTick);
        });
    }

    // ===== BẪY CHÍNH: số đơn cập nhật (LoadResults dựng lại dòng) KHÔNG được làm mất tick/vòng quay =====
    [Fact]
    public void LoadResultsKhiSoDonCapNhat_KhongLamMatTickVaVongQuay()
    {
        using var temp = new TempDatabase();
        var (services, vm, accountId) = NewVmWith2Shops(temp);
        var today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        services.RaiseShopCheckChanged(accountId, LoginA, checking: true);

        // Đúng luồng thật: mỗi đơn arrange xong → +1 DB rồi phát PrepareCountChanged → VM gọi LoadResults().
        services.Results.IncrementPrepared(accountId, LoginA, today);
        services.RaisePrepareCountChanged(accountId);

        var a = Row(vm, LoginA);
        Assert.Equal(1, a.PreparedCount); // số đã cập nhật
        Assert.True(a.IsChecking);        // ... mà cờ tiến độ VẪN còn

        // Xong shop rồi vẫn tiếp tục có lượt nạp lại (đơn của shop kế) → tick của A vẫn còn.
        services.RaiseShopCheckChanged(accountId, LoginA, checking: false);
        services.Results.IncrementPrepared(accountId, LoginA, today);
        services.RaisePrepareCountChanged(accountId);

        a = Row(vm, LoginA);
        Assert.Equal(2, a.PreparedCount);
        Assert.False(a.IsChecking);
        Assert.True(a.ShowTick);
    }

    // ===== Nhãn phiên gửi là LOGIN, dòng lưới hiển thị TÊN shop khác login → vẫn phải khớp đúng dòng =====
    [Fact]
    public void NhanShopLaLogin_KhacTenHienThi_VanKhopDungDong()
    {
        using var temp = new TempDatabase();
        var (services, vm, accountId) = NewVmWith2Shops(temp);
        Assert.Equal("Alina Store1", Row(vm, LoginA).ShopName); // tên hiển thị KHÁC login

        services.RaiseShopCheckChanged(accountId, LoginA, checking: true);

        Assert.True(Row(vm, LoginA).IsChecking);
    }

    // ===== So khớp nhãn: bỏ khoảng trắng thừa + không phân biệt hoa/thường (cả vòng quay LẪN tick) =====
    [Fact]
    public void NhanShopLechHoaThuongVaKhoangTrang_VanKhop()
    {
        using var temp = new TempDatabase();
        var (services, vm, accountId) = NewVmWith2Shops(temp);

        services.RaiseShopCheckChanged(accountId, "  ALINA99.Store ", checking: true);

        Assert.True(Row(vm, LoginA).IsChecking);
        Assert.False(Row(vm, LoginB).IsChecking);

        services.RaiseShopCheckChanged(accountId, "  ALINA99.Store ", checking: false);

        Assert.True(Row(vm, LoginA).ShowTick); // tick khớp qua MatchesShopLabel, không so khóa thô
        Assert.False(Row(vm, LoginB).ShowTick);
    }

    // ===== Đổi sang tài khoản KHÁC (chưa chạy) → lưới của nó không dính tick của tài khoản đang chạy =====
    [Fact]
    public void DoiSangTaiKhoanKhac_KhongMangTickTheoSang()
    {
        using var temp = new TempDatabase();
        var (services, vm, accountId) = NewVmWith2Shops(temp);
        services.Accounts.Insert(new Account { Email = "b@mail.com", Password = "p" });
        vm.Reload();
        var otherId = vm.Accounts.Single(r => r.Email == "b@mail.com").Id;
        services.Results.UpsertShops(otherId, new[] { new ShopListItem("111", "Alina Store1", LoginA) });

        services.RaiseShopCheckChanged(accountId, LoginA, checking: true);
        services.RaiseShopCheckChanged(accountId, LoginA, checking: false);
        vm.SelectedRow = vm.Accounts.Single(r => r.Id == otherId);

        Assert.All(vm.ResultRows, r => Assert.False(r.ShowTick));

        // Quay lại tài khoản đang chạy → tick của NÓ hiện lại.
        vm.SelectedRow = vm.Accounts.Single(r => r.Id == accountId);
        Assert.True(Row(vm, LoginA).ShowTick);
    }

    // ===== Lượt chạy MỚI (phiên đọc lại danh sách shop) → xóa sạch tick của lượt trước =====
    [Fact]
    public void DocLaiDanhSachShop_LuotChayMoi_XoaSachTick()
    {
        using var temp = new TempDatabase();
        var (services, vm, accountId) = NewVmWith2Shops(temp);

        services.RaiseShopCheckChanged(accountId, LoginA, checking: true);
        services.RaiseShopCheckChanged(accountId, LoginA, checking: false);
        Assert.True(Row(vm, LoginA).ShowTick);

        // Phiên đọc xong /portal/shop = bắt đầu lặp qua từng shop → lượt mới, tick về trắng.
        services.RaiseShopListChanged(accountId);

        Assert.All(vm.ResultRows, r =>
        {
            Assert.False(r.DaKiemTra);
            Assert.False(r.ShowTick);
        });
    }

    /// <summary>
    /// Mặt KIA của cùng một luật: "Mở list shop" đọc danh sách y hệt nhưng DỪNG ngay ở trang chọn shop, không
    /// lặp shop nào ⇒ <c>luotChayMoi: false</c> ⇒ dấu tick tiến độ phải Ở NGUYÊN.
    /// <para>Không có vế này thì bấm một nút "chỉ xem" là mất sạch tick của cả 12 shop, âm thầm, không dòng log
    /// nào — người dùng tưởng vòng chạy vừa bị reset.</para>
    /// </summary>
    [Fact]
    public void DocLaiDanhSachShop_ChiDeXem_GiuNguyenTick()
    {
        using var temp = new TempDatabase();
        var (services, vm, accountId) = NewVmWith2Shops(temp);

        services.RaiseShopCheckChanged(accountId, LoginA, checking: true);
        services.RaiseShopCheckChanged(accountId, LoginA, checking: false);
        Assert.True(Row(vm, LoginA).ShowTick);

        // Đi qua ĐÚNG callback mà "Mở list shop" rót vào phiên cầu nối, không tự gọi
        // RaiseShopListChanged(…, false) — tự gọi là test khẳng định lại chính tham số mình vừa truyền, còn
        // callback thật thì vẫn có thể đang truyền `true`.
        vm.DoDanhSachShopVaoLuoi(accountId, new[]
        {
            new ShopListItem("111", "Alina Store1", LoginA),
            new ShopListItem("222", "Shop 9X", LoginB),
        });

        Assert.True(Row(vm, LoginA).DaKiemTra);
        Assert.True(Row(vm, LoginA).ShowTick);
    }

    // ===== Đang check thì chữ "đang kiểm tra…" thế chỗ tick ô Chuẩn bị hàng =====
    [Theory]
    [InlineData(true, false, true)]   // đã kiểm tra, hết check → tick
    [InlineData(true, true, false)]   // đã kiểm tra nhưng đang check lại → không tick
    [InlineData(false, false, false)] // chưa tới → trống
    [InlineData(false, true, false)]  // đang kiểm tra lần đầu → không tick
    public void ShowTick_ChiKhiDaKiemTraVaKhongConCheck(bool daKiemTra, bool isChecking, bool mongDoi)
    {
        var row = new ShopPrepareRow("Alina Store1", LoginA, 0)
        {
            DaKiemTra = daKiemTra,
            IsChecking = isChecking,
        };

        Assert.Equal(mongDoi, row.ShowTick);
    }

    /// <summary>Tick KM / Combo / Flash: có dòng DB (kể cả 0 sp) thì hiện; <c>null</c> hoặc đang chạy đúng loại thì không.
    /// Loại đang chạy không ẩn tick của hai cột kia.</summary>
    [Fact]
    public void ShowTickChuongTrinh_TheoSoVaDangChay()
    {
        var row = new ShopPrepareRow("Alina Store1", LoginA, 0);
        Assert.False(row.ShowTickKm);
        Assert.False(row.ShowTickCombo);
        Assert.False(row.ShowTickFlash);

        row.KmSoSanPham = 0;
        Assert.True(row.ShowTickKm);
        row.LoaiDangChay = LoaiChuongTrinh.ChuongTrinhShop;
        Assert.False(row.ShowTickKm);
        row.LoaiDangChay = LoaiChuongTrinh.Combo;
        Assert.True(row.ShowTickKm);

        row.LoaiDangChay = null;
        row.FlashSoSanPham = 0;
        Assert.True(row.ShowTickFlash);
        row.LoaiDangChay = LoaiChuongTrinh.FlashSale;
        Assert.False(row.ShowTickFlash);
        row.LoaiDangChay = null;
        row.FlashSoSanPham = 12;
        Assert.True(row.ShowTickFlash);

        row.ComboSoSanPham = 3;
        Assert.True(row.ShowTickCombo);
    }
}
