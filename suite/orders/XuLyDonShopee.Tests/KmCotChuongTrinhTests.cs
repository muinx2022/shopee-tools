using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// <b>Ba cột "KM | Combo | Flash Sale" ở lưới tab "Shops"</b> — số SẢN PHẨM nạp vào chương trình khuyến mãi của
/// ngày đang lọc, kèm chữ "đang chạy…" trong đúng ô của loại đang chạy.
/// <para>Ba mệnh đề dễ hỏng ÂM THẦM mà file này khoá:</para>
/// <list type="number">
/// <item><b>Khóa ghi = <c>LoginName</c> (thiếu thì <c>ShopName</c>)</b> — lấy nhầm <c>ShopName</c> (thứ dòng
/// nhật ký của vòng combo đang in ra) là mọi shop có tên hiển thị khác login ghi vào một khóa KHÔNG dòng lưới
/// nào khớp: không lỗi, không log, chỉ là ba cột trống trơn. Đo qua VÒNG CHẠY THẬT trên cầu nối
/// (<see cref="BridgeTestRig"/>), không phải qua lời gọi tay — chỗ tính khóa nằm trong vòng lặp.</item>
/// <item><b>CỘNG DỒN, không ghi đè</b> — chạy combo hai lượt trong một ngày là tạo HAI chương trình.</item>
/// <item><b><c>int?</c> chứ không <c>int</c></b> — <c>null</c> = hôm đó KHÔNG chạy loại này (ô hiện <c>—</c>),
/// <c>0</c> = đã chạy shop này mà không tạo được sản phẩm nào. Hai ca ĐO RIÊNG, không gộp.</item>
/// </list>
/// <para>Không có <c>Application.Current</c> trong test ⇒ <c>UiDispatch.Run</c> chạy thẳng trên luồng gọi, y như
/// lúc phiên bắn sự kiện về UI thread.</para>
/// </summary>
public class KmCotChuongTrinhTests
{
    private const string LoginA = "alina99.store";
    private const string TenA = "Alina Store1";   // tên hiển thị KHÁC login — đúng dữ liệu thật
    private const string LoginB = "shop9x.store";
    private const string TenB = "Shop 9X";

    private static string HomNay => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Dựng VM đang mở MỘT tài khoản có 2 shop (tên hiển thị khác login).</summary>
    private static (AppServices Services, AccountsViewModel Vm, long AccountId) NewVmWith2Shops(TempDatabase temp)
    {
        var services = new AppServices(temp.Path);
        services.Accounts.Insert(new Account { Email = "a@mail.com", Password = "p" });

        var vm = new AccountsViewModel(services);
        var accountId = vm.Accounts.First().Id;
        services.Results.UpsertShops(accountId, new[]
        {
            new ShopListItem("111", TenA, LoginA),
            new ShopListItem("222", TenB, LoginB),
        });

        vm.SelectedRow = vm.Accounts.First();
        Assert.Equal(2, vm.ResultRows.Count);
        return (services, vm, accountId);
    }

    private static ShopPrepareRow Row(AccountsViewModel vm, string login)
        => vm.ResultRows.Single(r => r.ShopLogin == login);

    // ══════════════════ Kho: cộng dồn · tách theo loại · tách theo ngày ══════════════════

    /// <summary>
    /// <b>CỘNG DỒN, KHÔNG ghi đè</b> — hai lượt cùng <c>(tài khoản, shop, ngày, loại)</c> ra TỔNG.
    /// <para>Đây là test mà phép thử phá "đổi <c>so_san_pham = so_san_pham + excluded.so_san_pham</c> thành
    /// <c>= excluded.so_san_pham</c>" phải làm ĐỎ. Ghi đè nuốt mất lượt trước, âm thầm.</para>
    /// </summary>
    [Fact]
    public void HaiLuotCungNgayCungLoai_CongDon_KhongGhiDe()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);

        services.Results.CongSoSanPhamChuongTrinh(7, LoginA, HomNay, LoaiChuongTrinh.Combo, 240);
        services.Results.CongSoSanPhamChuongTrinh(7, LoginA, HomNay, LoaiChuongTrinh.Combo, 60);

        var map = services.Results.GetChuongTrinhByDay(7, HomNay);
        Assert.Equal(300, map[(LoginA, LoaiChuongTrinh.Combo)]);
    }

    /// <summary>Khác LOẠI ⇒ hai dòng riêng, không cộng vào nhau.</summary>
    [Fact]
    public void KhacLoai_ThiHaiDongRieng()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);

        services.Results.CongSoSanPhamChuongTrinh(7, LoginA, HomNay, LoaiChuongTrinh.ChuongTrinhShop, 11);
        services.Results.CongSoSanPhamChuongTrinh(7, LoginA, HomNay, LoaiChuongTrinh.Combo, 22);
        services.Results.CongSoSanPhamChuongTrinh(7, LoginA, HomNay, LoaiChuongTrinh.FlashSale, 33);

        var map = services.Results.GetChuongTrinhByDay(7, HomNay);
        Assert.Equal(11, map[(LoginA, LoaiChuongTrinh.ChuongTrinhShop)]);
        Assert.Equal(22, map[(LoginA, LoaiChuongTrinh.Combo)]);
        Assert.Equal(33, map[(LoginA, LoaiChuongTrinh.FlashSale)]);
    }

    /// <summary>Khác NGÀY ⇒ hai dòng riêng; đọc ngày nào ra đúng số của ngày đó.</summary>
    [Fact]
    public void KhacNgay_ThiHaiDongRieng()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        var homQua = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        services.Results.CongSoSanPhamChuongTrinh(7, LoginA, homQua, LoaiChuongTrinh.Combo, 5);
        services.Results.CongSoSanPhamChuongTrinh(7, LoginA, HomNay, LoaiChuongTrinh.Combo, 9);

        Assert.Equal(5, services.Results.GetChuongTrinhByDay(7, homQua)[(LoginA, LoaiChuongTrinh.Combo)]);
        Assert.Equal(9, services.Results.GetChuongTrinhByDay(7, HomNay)[(LoginA, LoaiChuongTrinh.Combo)]);
    }

    /// <summary>
    /// Bảng <c>km_program_daily</c> tạo được trên DB <b>CŨ đã có dữ liệu</b> — không chỉ trên DB trống.
    /// <para><c>CREATE TABLE IF NOT EXISTS</c> chạy ở MỌI lượt <c>Initialize</c>, nhưng vế "DB cũ" phải đo thật:
    /// mở DB, ghi dữ liệu, ĐÓNG, mở lại rồi mới dùng bảng mới.</para>
    /// </summary>
    [Fact]
    public void BangMoi_TaoDuocTrenCaDbTrongLanDbCu()
    {
        using var temp = new TempDatabase();

        // Lượt 1 — DB trống: dùng được ngay.
        var truoc = new AppServices(temp.Path);
        truoc.Accounts.Insert(new Account { Email = "a@mail.com", Password = "p" });
        truoc.Results.CongSoSanPhamChuongTrinh(7, LoginA, HomNay, LoaiChuongTrinh.Combo, 4);

        // Lượt 2 — mở LẠI cùng file (DB "cũ" đã có dữ liệu): số cũ còn nguyên và vẫn ghi thêm được.
        var sau = new AppServices(temp.Path);
        sau.Results.CongSoSanPhamChuongTrinh(7, LoginB, HomNay, LoaiChuongTrinh.Combo, 6);

        var map = sau.Results.GetChuongTrinhByDay(7, HomNay);
        Assert.Equal(4, map[(LoginA, LoaiChuongTrinh.Combo)]);
        Assert.Equal(6, map[(LoginB, LoaiChuongTrinh.Combo)]);
    }

    // ══════════════════ Lưới: null = "—" · 0 = "0" (HAI test RIÊNG) ══════════════════

    /// <summary>
    /// Shop KHÔNG có dòng trong ngày ⇒ <c>null</c> ⇒ ô hiện <c>—</c>. Vế thứ nhất của luật <c>int?</c>.
    /// <para>Phép thử phá "đổi <c>int?</c> thành <c>int</c> (0 thay cho null)" phải làm ĐỎ chính test này.</para>
    /// </summary>
    [Fact]
    public void ShopKhongCoDong_ThiNull_VaHien_GachNgang()
    {
        using var temp = new TempDatabase();
        var (_, vm, _) = NewVmWith2Shops(temp);

        var a = Row(vm, LoginA);
        Assert.Null(a.KmSoSanPham);
        Assert.Null(a.ComboSoSanPham);
        Assert.Null(a.FlashSoSanPham);
        Assert.Equal("—", a.KmHienThi);
        Assert.Equal("—", a.ComboHienThi);
        Assert.Equal("—", a.FlashHienThi);
    }

    /// <summary>
    /// Shop CÓ dòng với số <c>0</c> (đã chạy nhưng bỏ qua vì modal rỗng / shop hỏng) ⇒ ô hiện <c>0</c>, KHÔNG
    /// phải <c>—</c>. Vế thứ hai của luật <c>int?</c> — đo RIÊNG, vì gộp với vế trên là mất đúng chỗ phân biệt.
    /// </summary>
    [Fact]
    public void ShopCoDongSo0_ThiHien0_KhongPhaiGachNgang()
    {
        using var temp = new TempDatabase();
        var (_, vm, accountId) = NewVmWith2Shops(temp);

        // Đi qua ĐÚNG callback mà phiên rót vào vòng chạy — không tự ghi DB rồi tự nạp lại.
        vm.GhiTienDoChuongTrinh(accountId, LoaiChuongTrinh.Combo, LoginA, dangChay: true, soSanPham: 0);
        vm.GhiTienDoChuongTrinh(accountId, LoaiChuongTrinh.Combo, LoginA, dangChay: false, soSanPham: 0);

        var a = Row(vm, LoginA);
        Assert.Equal(0, a.ComboSoSanPham);
        Assert.Equal("0", a.ComboHienThi);

        // …còn shop CHƯA chạy vẫn là "—": hai ca phải phân biệt được TRONG CÙNG một lưới.
        Assert.Null(Row(vm, LoginB).ComboSoSanPham);
        Assert.Equal("—", Row(vm, LoginB).ComboHienThi);
    }

    // ══════════════════ Lưới bám ĐÚNG ngày đang lọc ══════════════════

    /// <summary>Số của ba cột bám <c>ResultDayKey</c>: đổi ngày lọc ⇒ số đổi theo (cùng quy ước cột "Chuẩn bị
    /// hàng"), không phải "lượt gần nhất".</summary>
    [Fact]
    public void DoiNgayLoc_ThiSoDoiTheo()
    {
        using var temp = new TempDatabase();
        var (services, vm, accountId) = NewVmWith2Shops(temp);

        services.Results.CongSoSanPhamChuongTrinh(accountId, LoginA, HomNay, LoaiChuongTrinh.Combo, 240);
        vm.SelectedRow = null;
        vm.SelectedRow = vm.Accounts.First(); // nạp lại lưới của hôm nay
        Assert.Equal(240, Row(vm, LoginA).ComboSoSanPham);

        vm.ResultDate = DateTimeOffset.Now.AddDays(-1);
        Assert.Null(Row(vm, LoginA).ComboSoSanPham);
        Assert.Equal("—", Row(vm, LoginA).ComboHienThi);

        vm.ResultDate = DateTimeOffset.Now;
        Assert.Equal(240, Row(vm, LoginA).ComboSoSanPham);
    }

    // ══════════════════ Chữ "đang chạy…" — ĐÚNG một dòng, ĐÚNG một cột ══════════════════

    /// <summary>
    /// Bắt đầu shop A của vòng COMBO ⇒ ĐÚNG ô Combo của ĐÚNG dòng A hiện "đang chạy…"; hai ô kia của A và cả
    /// dòng B không đổi. Xong ⇒ tắt chữ đó và điền SỐ.
    /// </summary>
    [Fact]
    public void DangChayCombo_ChiOComboCuaDongA_RoiXongThiDienSo()
    {
        using var temp = new TempDatabase();
        var (_, vm, accountId) = NewVmWith2Shops(temp);

        vm.GhiTienDoChuongTrinh(accountId, LoaiChuongTrinh.Combo, LoginA, dangChay: true, soSanPham: 0);

        var a = Row(vm, LoginA);
        Assert.Equal(LoaiChuongTrinh.Combo, a.LoaiDangChay);
        Assert.True(a.ComboDangChay);
        Assert.False(a.KmDangChay);
        Assert.False(a.FlashDangChay);
        Assert.Null(Row(vm, LoginB).LoaiDangChay);

        vm.GhiTienDoChuongTrinh(accountId, LoaiChuongTrinh.Combo, LoginA, dangChay: false, soSanPham: 240);

        a = Row(vm, LoginA);
        Assert.Null(a.LoaiDangChay);
        Assert.False(a.ComboDangChay);
        Assert.Equal(240, a.ComboSoSanPham);
        Assert.Equal("240", a.ComboHienThi);
        // Ô của hai loại KIA vẫn là "—" — vòng combo không được đụng cột của vòng khác.
        Assert.Equal("—", a.KmHienThi);
        Assert.Equal("—", a.FlashHienThi);
    }

    /// <summary>Vòng KM đổ vào cột KM, không phải cột Combo — hai loại đi chung một đường sự kiện nên phải đo
    /// cả hai chiều.</summary>
    [Fact]
    public void VongKM_DoVaoCotKM_KhongPhaiCotCombo()
    {
        using var temp = new TempDatabase();
        var (_, vm, accountId) = NewVmWith2Shops(temp);

        vm.GhiTienDoChuongTrinh(accountId, LoaiChuongTrinh.ChuongTrinhShop, LoginA, dangChay: true, soSanPham: 0);
        Assert.True(Row(vm, LoginA).KmDangChay);
        Assert.False(Row(vm, LoginA).ComboDangChay);

        vm.GhiTienDoChuongTrinh(accountId, LoaiChuongTrinh.ChuongTrinhShop, LoginA, dangChay: false, soSanPham: 18);

        Assert.Equal(18, Row(vm, LoginA).KmSoSanPham);
        Assert.Null(Row(vm, LoginA).ComboSoSanPham);
    }

    /// <summary>
    /// Vòng chương trình chỉ đụng ô loại đó: đang chạy → <c>ComboDangChay</c>, không bật <c>IsChecking</c> /
    /// tick Chuẩn bị hàng. Xong → số + <c>ShowTickCombo</c>.
    /// </summary>
    [Fact]
    public void VongChuongTrinh_ChiTickOLoaiDo_KhongDungShopCheckChanged()
    {
        using var temp = new TempDatabase();
        var (_, vm, accountId) = NewVmWith2Shops(temp);

        vm.GhiTienDoChuongTrinh(accountId, LoaiChuongTrinh.Combo, LoginA, dangChay: true, soSanPham: 0);
        Assert.True(Row(vm, LoginA).ComboDangChay);
        Assert.False(Row(vm, LoginA).IsChecking);
        Assert.False(Row(vm, LoginA).ShowTick);
        Assert.False(Row(vm, LoginA).ShowTickCombo);

        vm.GhiTienDoChuongTrinh(accountId, LoaiChuongTrinh.Combo, LoginA, dangChay: false, soSanPham: 3);
        Assert.False(Row(vm, LoginA).ComboDangChay);
        Assert.False(Row(vm, LoginA).IsChecking);
        Assert.False(Row(vm, LoginA).ShowTick);
        Assert.Equal(3, Row(vm, LoginA).ComboSoSanPham);
        Assert.True(Row(vm, LoginA).ShowTickCombo);
    }

    /// <summary>Hai lượt combo trong CÙNG một ngày ⇒ ô lưới hiện TỔNG (mặt lưới của luật cộng dồn).</summary>
    [Fact]
    public void HaiLuotComboCungNgay_OLuoiHienTong()
    {
        using var temp = new TempDatabase();
        var (_, vm, accountId) = NewVmWith2Shops(temp);

        vm.GhiTienDoChuongTrinh(accountId, LoaiChuongTrinh.Combo, LoginA, dangChay: false, soSanPham: 5);
        vm.GhiTienDoChuongTrinh(accountId, LoaiChuongTrinh.Combo, LoginA, dangChay: false, soSanPham: 7);

        Assert.Equal(12, Row(vm, LoginA).ComboSoSanPham);
    }

    // ══════════════════ VÒNG CHẠY THẬT trên cầu nối — khóa ghi + lối ra sớm ══════════════════

    /// <summary>
    /// <b>§2b — KHÓA GHI phải là <c>LoginName</c>, không phải <c>ShopName</c>.</b> Đo qua vòng chạy THẬT: rig trả
    /// một shop <c>name="Alina Store1"</c> / <c>login="alina99.store"</c>, và số phải rơi vào ĐÚNG dòng lưới
    /// <c>ShopLogin = "alina99.store"</c>.
    /// <para>Phép thử phá "đổi khóa ghi sang <c>shop.ShopName</c>" làm ĐỎ chính test này. Gọi tay
    /// <c>GhiTienDoChuongTrinh</c> thì KHÔNG đo được — chỗ tính khóa nằm trong vòng lặp của
    /// <c>ChayVongTaoChuongTrinhAsync</c>, không nằm ở người gọi.</para>
    /// </summary>
    [Fact]
    public async Task VongThat_GhiTheoLoginName_ChuKhongPhaiTenHienThi()
    {
        using var temp = new TempDatabase();
        var (_, vm, accountId) = NewVmWith2Shops(temp);

        await using var rig = await BridgeTestRig.StartAsync();
        var chay = ChayComboAsync(rig, vm, accountId);

        Assert.Equal("readShopList", await TenLenhAsync(rig));
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = MotShopAlina });

        Assert.Equal("openShopDetail", await TenLenhAsync(rig));
        await rig.GuiAsync(new { action = "shopOpened" });
        Assert.Equal("chayCombo", await TenLenhAsync(rig));
        await rig.GuiAsync(new { action = "pageData", kind = "kmKetQua", token = "111", data = GoiTao(240) });

        var kq = await chay;
        Assert.Equal(240, kq.Shops.Single().SoSanPham);

        // Số phải nằm ở dòng của LOGIN. Ghi theo tên hiển thị ⇒ dòng này còn null ⇒ ĐỎ ngay tại đây.
        Assert.Equal(240, Row(vm, LoginA).ComboSoSanPham);
        Assert.Equal("240", Row(vm, LoginA).ComboHienThi);
        Assert.NotEqual(TenA, Row(vm, LoginA).ShopLogin); // bảo đảm hai chuỗi thật sự khác nhau
    }

    /// <summary>
    /// <b>LỐI RA SỚM VẪN GIỮ SỐ:</b> captcha ở shop 3/3 ⇒ số của shop 1 và shop 2 VẪN ở trong DB.
    /// <para>Đây là mặt đo của quyết định "ghi DB ngay khi xong TỪNG shop, không gom về cuối lượt": vòng có năm
    /// lối ra sớm, gom lại là mọi lối ra sớm đều mất sạch số của các shop đã chạy xong.</para>
    /// </summary>
    [Fact]
    public async Task CaptchaGiuaVong_SoCuaHaiShopDauVanConTrongDb()
    {
        using var temp = new TempDatabase();
        var (services, vm, accountId) = NewVmWith2Shops(temp);

        await using var rig = await BridgeTestRig.StartAsync();
        var chay = ChayComboAsync(rig, vm, accountId);

        Assert.Equal("readShopList", await TenLenhAsync(rig));
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = BaShop });

        for (var i = 1; i <= 2; i++)
        {
            Assert.Equal("openShopDetail", await TenLenhAsync(rig));
            await rig.GuiAsync(new { action = "shopOpened" });
            Assert.Equal("chayCombo", await TenLenhAsync(rig));
            await rig.GuiAsync(new
            {
                action = "pageData", kind = "kmKetQua", token = $"{i}00", data = GoiTao(i * 10),
            });
        }

        // Shop 3: rơi captcha ngay ở bước mở Chi tiết ⇒ vòng DỪNG cả lượt.
        Assert.Equal("openShopDetail", await TenLenhAsync(rig));
        await rig.GuiAsync(new { action = "captcha" });

        var kq = await chay;
        Assert.True(kq.Captcha);

        var map = services.Results.GetChuongTrinhByDay(accountId, HomNay);
        Assert.Equal(10, map[("shop.1", LoaiChuongTrinh.Combo)]);
        Assert.Equal(20, map[("shop.2", LoaiChuongTrinh.Combo)]);
        // Shop 3 đã được BẮT ĐẦU nên vẫn có dòng — với số 0 ("đã chạy qua, không tạo được gì"), không phải
        // vắng mặt. Đó là điều kiện để chữ 0 ở lưới có nghĩa.
        Assert.Equal(0, map[("shop.3", LoaiChuongTrinh.Combo)]);
    }

    /// <summary>
    /// Shop BỎ QUA (extension trả <c>boQua</c>, <c>soSanPham = 0</c>) vẫn GHI dòng ⇒ ô lưới hiện <c>0</c>, không
    /// phải <c>—</c>. Đo trên vòng thật vì chỗ quyết định nằm ở khối <c>finally</c> của vòng lặp.
    /// </summary>
    [Fact]
    public async Task VongThat_ShopBoQua_VanGhiDong_HienSo0()
    {
        using var temp = new TempDatabase();
        var (_, vm, accountId) = NewVmWith2Shops(temp);

        await using var rig = await BridgeTestRig.StartAsync();
        var chay = ChayComboAsync(rig, vm, accountId);

        Assert.Equal("readShopList", await TenLenhAsync(rig));
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = MotShopAlina });

        Assert.Equal("openShopDetail", await TenLenhAsync(rig));
        await rig.GuiAsync(new { action = "shopOpened" });
        Assert.Equal("chayCombo", await TenLenhAsync(rig));
        await rig.GuiAsync(new { action = "pageData", kind = "kmKetQua", token = "111", data = GoiBoQua });

        var kq = await chay;
        Assert.Equal(1, kq.SoBoQua);

        Assert.Equal(0, Row(vm, LoginA).ComboSoSanPham);
        Assert.Equal("0", Row(vm, LoginA).ComboHienThi);
    }

    // ══════════════════ Hạ tầng chung của các ca chạy vòng thật ══════════════════

    /// <summary>Gói combo mẫu — mốc bắt đầu 19-08-2026 11:00, khớp <see cref="TruocMocBatDau"/>.</summary>
    private static readonly ThamSoCombo ThamSo =
        new("Combo 19-08-2026", new DateTime(2026, 8, 19), new DateTime(2027, 2, 15), "11:00",
            Loai: 3, MuaSl: 2, Giam: 2000, GioiHan: 3);

    /// <summary>Mốc "bây giờ" CỐ ĐỊNH, TRƯỚC mốc bắt đầu của <see cref="ThamSo"/> — không dùng
    /// <c>DateTime.Now</c>, kẻo cửa "mốc bắt đầu đã trôi qua" cắt vòng ngay ở shop đầu.</summary>
    private static readonly DateTime TruocMocBatDau = new(2026, 8, 19, 9, 0, 0);

    /// <summary>Một shop có TÊN HIỂN THỊ KHÁC LOGIN (<see cref="TenA"/> / <see cref="LoginA"/>) — đúng hình dạng
    /// dữ liệu thật của §2b.</summary>
    private const string MotShopAlina =
        """[{"rowKey":"111","name":"Alina Store1","login":"alina99.store"}]""";

    private const string BaShop =
        """[{"rowKey":"100","name":"Shop 1","login":"shop.1"},""" +
        """{"rowKey":"200","name":"Shop 2","login":"shop.2"},""" +
        """{"rowKey":"300","name":"Shop 3","login":"shop.3"}]""";

    private static string GoiTao(int soSp) =>
        $$"""{"soSanPham":{{soSp}},"daTao":true,"boQua":null,"canhBao":[],"buocHong":[]}""";

    /// <summary>Gói BỎ QUA: shop không có sản phẩm nào để nạp ⇒ <c>soSanPham = 0</c> nhưng KHÔNG phải lỗi.</summary>
    private const string GoiBoQua =
        """{"soSanPham":0,"daTao":false,"boQua":"shop không có sản phẩm","canhBao":[],"buocHong":[]}""";

    /// <summary>
    /// Gói HỎNG Ở BƯỚC TẠO — đúng hình dạng mà <c>flow-km.js</c>/<c>flow-combo.js</c> trả ở lối ra hỏng của bước
    /// 7/8: <c>soSanPham</c> VẪN có số (số sp đã nạp vào FORM) nhưng <c>daTao: false</c> vì bước 9 (nút Xác nhận
    /// thanh đáy) chưa hề chạy. Trên Shopee KHÔNG có chương trình nào.
    /// </summary>
    private const string GoiHongSauKhiNapForm =
        """{"soSanPham":240,"daTao":false,"boQua":null,"canhBao":[],"buocHong":[{"ten":"8. Xác nhận modal chọn sản phẩm","loi":"nút Xác nhận vẫn disabled sau 12 nhịp"}]}""";

    /// <summary>Chạy vòng COMBO trên cầu nối thật, rót callback tiến độ đi qua ĐÚNG
    /// <c>AccountsViewModel.GhiTienDoChuongTrinh</c> (thân callback mà production rót vào phiên).</summary>
    /// <param name="onShopListRead">Callback danh sách shop — production rót
    /// <c>DoDanhSachShopVaoLuoi(…, luotChayMoi: false)</c>; bỏ trống = không rót (đo phần khác).</param>
    /// <param name="onShopChuongTrinh">Ghi đè thân callback tiến độ (ca "callback NÉM").</param>
    private static Task<KetQuaKm> ChayComboAsync(
        BridgeTestRig rig, AccountsViewModel vm, long accountId,
        Action<System.Collections.Generic.IReadOnlyList<ShopListItem>>? onShopListRead = null,
        Action<LoaiChuongTrinh, string, bool, int?>? onShopChuongTrinh = null,
        CancellationToken ct = default)
        => OrdersBridgeSession.ChayComboSauPickerAsync(
            rig.Channel, onShopListRead, rig.Log, ThamSo,
            vePicker: _ => Task.FromResult(KetQuaVePicker.VeDuoc),
            nghiGiuaShop: _ => Task.CompletedTask,
            hanChot: null,
            dongHo: () => TruocMocBatDau,
            ct: ct,
            onShopChuongTrinh: onShopChuongTrinh ?? ((loai, shopLabel, dangChay, soSanPham)
                => vm.GhiTienDoChuongTrinh(accountId, loai, shopLabel, dangChay, soSanPham)));

    private static async Task<string> TenLenhAsync(BridgeTestRig rig)
    {
        using var l = await rig.NhanLenhAsync();
        return l.RootElement.GetProperty("action").GetString() ?? "";
    }

    // ══════════════════ Sáu ca do lượt PHẢN BIỆN 27/08/2026 tìm ra ══════════════════
    //
    // Cả sáu đều LỌT qua 11/11 tiêu chí nghiệm thu của plan — tiêu chí có lỗ, không phải người chấm sai.

    /// <summary>
    /// <b>LỖI 1 — cột KHÔNG được hiện số cho shop KHÔNG tạo được chương trình.</b> Extension trả
    /// <c>soSanPham: 240</c> kèm <c>daTao: false</c> ở lối ra hỏng của bước 8: 240 là số sp đã nạp vào FORM, còn
    /// bước TẠO thật là bước 9 — trên Shopee không có chương trình nào.
    /// <para>Ô lưới phải hiện <c>0</c> (<i>đã chạy mà không tạo được gì</i>), TUYỆT ĐỐI không phải <c>240</c>.
    /// Vẫn GHI dòng, nên cũng không phải <c>—</c>.</para>
    /// <para>Phép thử phá: đổi <c>soSpShop = mot.DaTao ? mot.SoSanPham : 0</c> về <c>= mot.SoSanPham</c> ⇒ test
    /// này ĐỎ. Hai helper cũ không phủ được ô này của ma trận: <c>GoiTao</c> luôn <c>daTao: true</c>,
    /// <c>GoiBoQua</c> luôn <c>soSanPham: 0</c>.</para>
    /// </summary>
    [Fact]
    public async Task VongThat_HongOBuocTao_ODem0_ChuKhongPhaiSoSpDaNapVaoForm()
    {
        using var temp = new TempDatabase();
        var (_, vm, accountId) = NewVmWith2Shops(temp);

        await using var rig = await BridgeTestRig.StartAsync();
        var chay = ChayComboAsync(rig, vm, accountId);

        Assert.Equal("readShopList", await TenLenhAsync(rig));
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = MotShopAlina });

        Assert.Equal("openShopDetail", await TenLenhAsync(rig));
        await rig.GuiAsync(new { action = "shopOpened" });
        Assert.Equal("chayCombo", await TenLenhAsync(rig));
        await rig.GuiAsync(new
        {
            action = "pageData", kind = "kmKetQua", token = "111", data = GoiHongSauKhiNapForm,
        });

        var kq = await chay;
        // Vòng vẫn đọc đúng gói: shop này KHÔNG tạo được, và câu tổng kết đếm 0 shop tạo.
        Assert.False(kq.Shops.Single().DaTao);
        Assert.Equal(0, kq.SoTao);
        // …NHƯNG gói VẪN mang con số 240 (số sp đã nạp vào FORM). Chốt vế này TRƯỚC, kẻo một gói dựng sai hình
        // dạng làm `SoSanPham` về 0 và biến cả bài test thành thứ luôn xanh — đúng cái đã suýt lọt 27/08/2026.
        Assert.Equal(240, kq.Shops.Single().SoSanPham);

        Assert.Equal(0, Row(vm, LoginA).ComboSoSanPham);
        Assert.Equal("0", Row(vm, LoginA).ComboHienThi);
        Assert.NotEqual("240", Row(vm, LoginA).ComboHienThi);
    }

    /// <summary>
    /// <b>LỖI 2 — callback ghi số NÉM thì vòng chạy vẫn phải đi hết.</b> Ghi số là việc PHỤ; nó không có quyền
    /// giết vòng.
    /// <para>Ca thật: <c>GhiTienDoChuongTrinh</c> mở SQLite trong khối <c>finally</c> của vòng lặp. Một
    /// <c>SQLITE_BUSY</c> ném từ <c>finally</c> ⇒ thoát thẳng vòng ⇒ <b>vứt luôn <c>ketQua</c></b> ⇒ tổng kết báo
    /// "0 shop" dù 3 shop đã tạo THẬT ⇒ <c>ChotLuotKm</c> thấy <c>SoTao == 0</c> nên không ghi
    /// <c>LanChayCuoi</c> ⇒ đêm sau các shop đó nhận chương trình THỨ HAI.</para>
    /// <para>Phép thử phá: bỏ <c>try/catch</c> trong <c>BaoTienDoChuongTrinh</c> ⇒ test này ĐỎ.</para>
    /// </summary>
    [Fact]
    public async Task CallbackGhiSoNem_VongVanChayHet_VaKetQuaConDuSoShop()
    {
        using var temp = new TempDatabase();
        var (_, vm, accountId) = NewVmWith2Shops(temp);

        await using var rig = await BridgeTestRig.StartAsync();
        var soLanNem = 0;
        var chay = ChayComboAsync(rig, vm, accountId,
            onShopChuongTrinh: (_, _, _, _) =>
            {
                soLanNem++;
                throw new InvalidOperationException("SQLITE_BUSY giả lập");
            });

        Assert.Equal("readShopList", await TenLenhAsync(rig));
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = BaShop });

        for (var i = 1; i <= 3; i++)
        {
            Assert.Equal("openShopDetail", await TenLenhAsync(rig));
            await rig.GuiAsync(new { action = "shopOpened" });
            Assert.Equal("chayCombo", await TenLenhAsync(rig));
            await rig.GuiAsync(new
            {
                action = "pageData", kind = "kmKetQua", token = $"{i}00", data = GoiTao(i * 10),
            });
        }

        var kq = await chay;

        Assert.Equal(6, soLanNem);              // 3 shop × (bắt đầu + xong) — mọi lượt đều ném
        Assert.Equal(3, kq.Shops.Count);        // …mà vòng vẫn đi hết và GIỮ nguyên kết quả tích luỹ
        Assert.Equal(3, kq.SoTao);
        Assert.Equal(new[] { 10, 20, 30 }, kq.Shops.Select(s => s.SoSanPham).ToArray());
        Assert.True(rig.CoLog("Ghi tiến độ chương trình"), "phải để lại dấu vết trong nhật ký, không nuốt lặng");
    }

    /// <summary>
    /// Combo đọc list shop: <b>giữ</b> tick check đơn (ô Chuẩn bị hàng) và vẫn <c>UpsertShops</c> cho shop mới.
    /// <list type="number">
    /// <item>05:50 check đơn tick A/B. 06:05 Combo khởi động với <c>luotChayMoi: false</c> ⇒ tick Chuẩn bị hàng
    /// còn; ô Combo vẫn <c>—</c> / chưa tick Combo.</item>
    /// <item>Shop mới có dòng lưới. Xong lượt: shop mới có số + <c>ShowTickCombo</c>, không có tick check đơn.</item>
    /// </list>
    /// <para>Phép thử phá: đổi <c>luotChayMoi</c> thành <c>true</c> ⇒ vế giữ tick check đơn ĐỎ; bỏ
    /// <c>UpsertShops</c> khỏi <c>DoDanhSachShopVaoLuoi</c> ⇒ vế shop mới ĐỎ.</para>
    /// </summary>
    [Fact]
    public async Task VongChuongTrinh_GiuTickCheckDon_VaDungDongChoShopMoi()
    {
        using var temp = new TempDatabase();
        var (services, vm, accountId) = NewVmWith2Shops(temp);

        // ══ Hiện trạng lúc 06:05: vòng check đơn 05:50 đã tick hết mọi shop ══
        foreach (var login in new[] { LoginA, LoginB })
        {
            services.RaiseShopCheckChanged(accountId, login, checking: true);
            services.RaiseShopCheckChanged(accountId, login, checking: false);
        }
        Assert.True(Row(vm, LoginA).ShowTick);
        Assert.True(Row(vm, LoginB).ShowTick);

        await using var rig = await BridgeTestRig.StartAsync();
        var chay = ChayComboAsync(rig, vm, accountId,
            // ĐÚNG thân callback production: `ChayLatCatAsync(doVaoLuoiShops: true, luotChayMoi: false)`.
            onShopListRead: shops => vm.DoDanhSachShopVaoLuoi(accountId, shops, luotChayMoi: false));

        Assert.Equal("readShopList", await TenLenhAsync(rig));
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = HaiShopCuVaMotShopMoi });

        // Lệnh kế đã bay ⇒ callback danh sách shop CHẮC CHẮN đã chạy xong.
        Assert.Equal("openShopDetail", await TenLenhAsync(rig));

        // Vế 1 — tick check đơn 05:50 còn; Combo chưa xong shop nào ⇒ ô Combo chưa tick.
        Assert.True(Row(vm, LoginA).ShowTick);
        Assert.True(Row(vm, LoginB).ShowTick);
        Assert.False(Row(vm, LoginA).ShowTickCombo);
        Assert.False(Row(vm, LoginB).ShowTickCombo);
        // Vế 2 — shop MỚI đã có dòng lưới (chưa vòng check đơn nào chạm tới nó).
        Assert.Equal(3, vm.ResultRows.Count);
        Assert.Equal("—", Row(vm, LoginMoi).ComboHienThi);

        for (var i = 1; i <= 3; i++)
        {
            if (i > 1) { Assert.Equal("openShopDetail", await TenLenhAsync(rig)); }
            await rig.GuiAsync(new { action = "shopOpened" });
            Assert.Equal("chayCombo", await TenLenhAsync(rig));
            await rig.GuiAsync(new
            {
                action = "pageData", kind = "kmKetQua", token = $"{i}11", data = GoiTao(i * 7),
            });
        }

        await chay;

        // …và số của shop MỚI rơi đúng vào dòng của nó, không rơi vào hư không.
        Assert.Equal(21, Row(vm, LoginMoi).ComboSoSanPham);
        Assert.True(Row(vm, LoginMoi).ShowTickCombo);
        Assert.False(Row(vm, LoginMoi).ShowTick);
        Assert.True(Row(vm, LoginA).ShowTick);
        Assert.True(Row(vm, LoginA).ShowTickCombo);
    }

    /// <summary>
    /// <b>LỖI 5 — HUỶ giữa shop thì KHÔNG ghi dòng.</b> Bấm Dừng lúc shop đang chờ ⇒ ô lưới phải giữ <c>—</c>
    /// (<i>chưa chạy xong loại này</i>), không phải <c>0</c> (<i>đã chạy mà không tạo được sp nào</i>) — đúng
    /// ranh giới mà cả thiết kế <c>int?</c> dựng lên.
    /// <para>Vẫn phải TẮT chữ "đang chạy…", kẻo dòng đó quay mãi sau khi lượt đã dừng.</para>
    /// <para>Phép thử phá: bỏ vế <c>biCatGiuaChung</c> (luôn truyền <c>soSpShop</c>) ⇒ test này ĐỎ.</para>
    /// </summary>
    [Fact]
    public async Task HuyGiuaShop_KhongGhiDong_OLuoiVanLaGachNgang()
    {
        using var temp = new TempDatabase();
        var (services, vm, accountId) = NewVmWith2Shops(temp);

        await using var rig = await BridgeTestRig.StartAsync();
        using var cts = new CancellationTokenSource();
        var chay = ChayComboAsync(rig, vm, accountId, ct: cts.Token);

        Assert.Equal("readShopList", await TenLenhAsync(rig));
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = MotShopAlina });

        Assert.Equal("openShopDetail", await TenLenhAsync(rig));
        await rig.GuiAsync(new { action = "shopOpened" });
        Assert.Equal("chayCombo", await TenLenhAsync(rig));
        Assert.True(Row(vm, LoginA).ComboDangChay);

        // Người dùng bấm Dừng lúc shop đang chờ extension trả kết quả.
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => chay);

        // KHÔNG có dòng nào trong DB cho shop bị cắt ngang.
        var map = services.Results.GetChuongTrinhByDay(accountId, HomNay);
        Assert.False(map.ContainsKey((LoginA, LoaiChuongTrinh.Combo)));
        Assert.Null(Row(vm, LoginA).ComboSoSanPham);
        Assert.Equal("—", Row(vm, LoginA).ComboHienThi);
        // …nhưng chữ "đang chạy…" phải TẮT: lượt đã dừng, dòng không được quay mãi.
        Assert.False(Row(vm, LoginA).ComboDangChay);
    }

    /// <summary>
    /// <b>LỖI 6 — khóa <c>(shop_login, loại)</c> BỎ HOA/THƯỜNG.</b> Bản đầu dùng comparer mặc định của
    /// <c>ValueTuple</c> (so chuỗi ORDINAL), trong khi <c>ChuanHoaKhoaShop</c> và <c>MatchesShopLabel</c> đều CỐ Ý
    /// bỏ hoa/thường sau một sự cố "ra 0 một cách LẶNG".
    /// <para>Lệch hoa/thường giữa nhãn ghi và nhãn <c>account_shops</c> ⇒ ô hiện <c>—</c> cho một shop ĐÃ chạy:
    /// không lỗi, không log. Và hai dòng chỉ khác hoa/thường là CÙNG một shop ⇒ phải CỘNG, không phải "bản sau
    /// thắng".</para>
    /// <para>Phép thử phá: bỏ <c>KhoaShopLoaiComparer</c> (về comparer mặc định) ⇒ test này ĐỎ.</para>
    /// </summary>
    [Fact]
    public void KhoaLechHoaThuong_VanRaSo_VaCongDon()
    {
        using var temp = new TempDatabase();
        var (services, vm, accountId) = NewVmWith2Shops(temp);

        // Hai lượt ghi cùng một shop vật lý, nhãn lệch HOA/thường (SQLite `UNIQUE` phân biệt ⇒ hai dòng thật).
        services.Results.CongSoSanPhamChuongTrinh(accountId, "ALINA99.Store", HomNay, LoaiChuongTrinh.Combo, 240);
        services.Results.CongSoSanPhamChuongTrinh(accountId, LoginA, HomNay, LoaiChuongTrinh.Combo, 60);

        var map = services.Results.GetChuongTrinhByDay(accountId, HomNay);
        Assert.Equal(300, map[(LoginA, LoaiChuongTrinh.Combo)]);        // tra bằng nhãn thường
        Assert.Equal(300, map[("Alina99.STORE", LoaiChuongTrinh.Combo)]); // …và bằng nhãn viết kiểu khác

        vm.SelectedRow = null;
        vm.SelectedRow = vm.Accounts.First();
        Assert.Equal(300, Row(vm, LoginA).ComboSoSanPham);
    }

    private const string LoginMoi = "shop.moi";

    /// <summary>Hai shop CŨ (đã có trong <c>account_shops</c>) + một shop MỚI chưa vòng nào chạm tới.</summary>
    private const string HaiShopCuVaMotShopMoi =
        """[{"rowKey":"111","name":"Alina Store1","login":"alina99.store"},""" +
        """{"rowKey":"211","name":"Shop 9X","login":"shop9x.store"},""" +
        """{"rowKey":"311","name":"Shop Mới","login":"shop.moi"}]""";
}
