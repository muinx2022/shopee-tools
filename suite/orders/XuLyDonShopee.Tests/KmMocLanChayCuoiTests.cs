using System;
using System.Collections.Generic;
using System.Linq;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// <b>Chốt một lượt KM</b> (<c>AccountsViewModel.ChotLuotKm</c>): mốc <c>LanChayCuoi</c> — gốc tính chu kỳ
/// "sau xx ngày" của lịch tự động — và câu tổng kết đi CÙNG một hàm.
/// <para>Vì sao test qua <c>ChotLuotKm</c> chứ không test hàm ghi mốc lẻ: phản biện vòng 4 (20/08/2026) chỉ ra
/// bốn test đo hàm ghi mốc lẻ đều xanh kể cả khi KHÔNG AI GỌI nó trong <c>than</c> của <c>ChayKmAsync</c>.
/// Gộp ghi-mốc với dựng-câu vào một hàm rồi test hàm đó là cách rẻ nhất để "quên gọi" gãy ngay ở compile/test.</para>
/// <para>Luật mốc (đã lật MỘT lần — xem xmldoc <c>ChotLuotKm</c> trước khi lật lại): có shop tạo được ⇒ GHI,
/// KỂ CẢ khi lượt dừng vì hết khung 06:00. Vòng 3 từng chọn "hết khung thì không ghi"; vòng 4 chứng minh nó
/// biến lịch thành "chạy lại từ shop 1 MỖI ĐÊM" — shop đầu danh sách nhận chương trình gấp SoNgay lần ý định,
/// shop cuối vẫn đói.</para>
/// <para>Đo THẲNG trên repository thật (<c>TempDatabase</c>): thứ cần khoá là "cột <c>lan_chay_cuoi</c> trong
/// DB có bị đổi không", và đó mới là cái lịch đêm sau đọc.</para>
/// </summary>
public class KmMocLanChayCuoiTests
{
    private static readonly DateTime Luc = new(2026, 8, 19, 3, 20, 0);

    private static (TempDatabase Temp, AppServices Services, AccountsViewModel Vm, long AccountId) DungVm(string email)
    {
        var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        services.Accounts.Insert(new Account { Email = email, Password = "p" });
        var id = services.Accounts.GetAll().First(a => a.Email == email).Id;
        return (temp, services, new AccountsViewModel(services), id);
    }

    private static KetQuaKm Lot(int tao, bool hetKhung = false, int soShopChuaChay = 0)
    {
        var ds = new List<KetQuaKmMotShop>();
        for (var i = 0; i < tao; i++) { ds.Add(new KetQuaKmMotShop($"t{i}", $"Tao{i}", true, 10, null, null)); }
        return new KetQuaKm(ds, Captcha: false, Loi: null, HetKhung: hetKhung, SoShopChuaChay: soShopChuaChay);
    }

    /// <summary>Lượt chạy TRỌN và có shop tạo được ⇒ GHI mốc, câu tổng kết có thật (vế đối chứng của cả lớp).</summary>
    [StaFact]
    public void ChayTron_CoShopTaoDuoc_ThiGHI_LanChayCuoi_VaCoCauTongKet()
    {
        var (temp, services, vm, id) = DungVm("km-moc-tron@mail.com");
        using var db = temp;

        var ketLuan = vm.ChotLuotKm(id, Lot(tao: 12), Luc);

        Assert.Equal(Luc, services.KmConfig.Get(id).LanChayCuoi);
        Assert.Contains("tạo 12/12 shop", ketLuan.Nhat);
    }

    /// <summary>
    /// <b>HẾT KHUNG 06:00 mà có shop tạo được ⇒ VẪN GHI mốc.</b> Không ghi là đêm nào lịch cũng tới hạn lại và
    /// chạy lại TỪ SHOP 1 — shop đầu danh sách nhận một chương trình mỗi đêm (gấp SoNgay lần ý định người
    /// dùng), shop cuối vẫn không bao giờ tới lượt (vòng không có con trỏ tiến độ).
    /// </summary>
    [StaFact]
    public void HetKhung_CoShopTaoDuoc_ThiVAN_GHI_LanChayCuoi()
    {
        var (temp, services, vm, id) = DungVm("km-moc-hetkhung@mail.com");
        using var db = temp;

        vm.ChotLuotKm(id, Lot(tao: 3, hetKhung: true, soShopChuaChay: 9), Luc);

        Assert.Equal(Luc, services.KmConfig.Get(id).LanChayCuoi);
    }

    /// <summary>
    /// Câu tổng kết khi hết khung phải nói "sẽ vào lượt kế theo chu kỳ" — KHÔNG được hứa "để đêm sau":
    /// mốc đã ghi nên lượt kế là sau SoNgay ngày; hứa "đêm sau" là nói sai cho người đọc sáng hôm sau.
    /// </summary>
    [StaFact]
    public void HetKhung_CauTongKet_NoiChuKy_KhongHua_DemSau()
    {
        var (temp, _, vm, id) = DungVm("km-moc-cauchot@mail.com");
        using var db = temp;

        var ketLuan = vm.ChotLuotKm(id, Lot(tao: 3, hetKhung: true, soShopChuaChay: 9), Luc);

        Assert.Contains("còn 9 shop chưa chạy", ketLuan.Nhat);
        Assert.Contains("sẽ vào lượt kế theo chu kỳ", ketLuan.Nhat);
        Assert.DoesNotContain("để đêm sau", ketLuan.Nhat);
    }

    /// <summary>Cả lượt KHÔNG tạo được shop nào (danh sách rỗng) ⇒ KHÔNG ghi.</summary>
    [StaFact]
    public void KhongTaoDuocShopNao_ThiKHONG_ghi_LanChayCuoi()
    {
        var (temp, services, vm, id) = DungVm("km-moc-trang@mail.com");
        using var db = temp;

        vm.ChotLuotKm(id, Lot(tao: 0), Luc);

        Assert.Null(services.KmConfig.Get(id).LanChayCuoi);
    }

    /// <summary>
    /// Mọi shop đều BỎ QUA vì không còn sản phẩm để thêm (đã quét trọn vẹn, không lỗi, không captcha)
    /// ⇒ VẪN GHI mốc để giữ đúng nhịp chu kỳ 3 ngày/lần, không lặp lại mỗi đêm vô ích.
    /// </summary>
    [StaFact]
    public void MoiShopBoQua_KhongConSanPham_ThiVAN_GHI_LanChayCuoi()
    {
        var (temp, services, vm, id) = DungVm("km-moc-boqua@mail.com");
        using var db = temp;

        var shops = new List<KetQuaKmMotShop>
        {
            new("s1", "Shop 1", DaTao: false, SoSanPham: 0, BoQua: "modal KHÔNG còn sản phẩm", Loi: null),
            new("s2", "Shop 2", DaTao: false, SoSanPham: 0, BoQua: "modal KHÔNG còn sản phẩm", Loi: null),
        };
        var kq = new KetQuaKm(shops, Captcha: false, Loi: null, HetKhung: false, SoShopChuaChay: 0);

        vm.ChotLuotKm(id, kq, Luc);

        Assert.Equal(Luc, services.KmConfig.Get(id).LanChayCuoi);
    }

    /// <summary>Có shop HỎNG kỹ thuật mà không tạo được shop nào ⇒ KHÔNG ghi mốc, để đêm sau thử lại.</summary>
    [StaFact]
    public void CoShopHong_KhongTaoDuocShopNao_ThiKHONG_ghi_LanChayCuoi()
    {
        var (temp, services, vm, id) = DungVm("km-moc-hong@mail.com");
        using var db = temp;

        var shops = new List<KetQuaKmMotShop>
        {
            new("s1", "Shop 1", DaTao: false, SoSanPham: 0, BoQua: "modal KHÔNG còn sản phẩm", Loi: null),
            new("s2", "Shop 2", DaTao: false, SoSanPham: 0, BoQua: null, Loi: "lỗi selector"),
        };
        var kq = new KetQuaKm(shops, Captcha: false, Loi: null, HetKhung: false, SoShopChuaChay: 0);

        vm.ChotLuotKm(id, kq, Luc);

        Assert.Null(services.KmConfig.Get(id).LanChayCuoi);
    }

    /// <summary>
    /// Ca thực tế đêm 17/10/2026: đã tạo được ở một số shop đầu (<c>SoTao &gt; 0</c>) nhưng gặp
    /// <c>verify/traffic/error</c> giữa vòng (<c>Captcha: true</c>, còn shop chưa chạy) ⇒ <b>KHÔNG</b> được ghi
    /// <c>LanChayCuoi</c> / <c>ComboLanChayCuoi</c>, đồng thời lưu đúng các shop đã xong (<c>DaTao</c> / <c>BoQua</c>)
    /// vào <c>ShopKmComboDaXong</c> (không lưu shop bị Captcha).
    /// </summary>
    [StaFact]
    public void CoShopTaoDuoc_NhungBiCatBoiCaptcha_ThiKhongGhiMoc_VaGiuTienDoShopDaXong()
    {
        var (temp, services, vm, id) = DungVm("km-moc-captcha-giua-vong@mail.com");
        using var db = temp;

        var shops = new List<KetQuaKmMotShop>
        {
            new("s1", "Six6 Store", DaTao: false, SoSanPham: 0, BoQua: "modal KHÔNG còn sản phẩm", Loi: null, ShopLogin: "six6.store"),
            new("s8", "Delica Store", DaTao: true, SoSanPham: 155, BoQua: null, Loi: null, ShopLogin: "delica.store"),
            new("s9", "Minoa Store", DaTao: false, SoSanPham: 0, BoQua: null, Loi: "rơi trang verify/captcha", Captcha: true, ShopLogin: "minoa.store"),
        };
        var kq = new KetQuaKm(shops, Captcha: true, Loi: "Rơi captcha khi chạy KM ở shop Minoa Store — dừng cả lượt.", SoShopChuaChay: 7);

        vm.ChotLuotKm(id, kq, Luc);
        Assert.Null(services.KmConfig.Get(id).LanChayCuoi);
        var daXongKm = vm.ShopKmComboDaXong(id, LoaiChuongTrinh.ChuongTrinhShop);
        Assert.Contains("six6.store", daXongKm);
        Assert.Contains("delica.store", daXongKm);
        Assert.DoesNotContain("minoa.store", daXongKm);

        // Tương tự cho Combo:
        vm.ChotLuotCombo(id, kq, Luc);
        Assert.Null(services.KmConfig.Get(id).ComboLanChayCuoi);
        var daXongCombo = vm.ShopKmComboDaXong(id, LoaiChuongTrinh.Combo);
        Assert.Contains("six6.store", daXongCombo);
        Assert.Contains("delica.store", daXongCombo);
        Assert.DoesNotContain("minoa.store", daXongCombo);
    }

    /// <summary>
    /// Khi lượt chạy bị dừng giữa chừng vì <c>NhuongChoCheckDon</c>, <c>Loi != null</c>, <c>MocBatDauDaTroiQua != null</c>
    /// hoặc <c>TranThuDaCham &gt; 0</c> thì dù <c>SoTao &gt; 0</c> vẫn KHÔNG ghi mốc.
    /// </summary>
    [StaFact]
    public void CoShopTaoDuoc_NhungDungGiuaChung_ViNhuongHoacLoiHoacMocQuaHoacTran_ThiKhongGhiMoc()
    {
        var (temp, services, vm, id) = DungVm("km-moc-dung-som@mail.com");
        using var db = temp;

        var shopTao = new KetQuaKmMotShop("s1", "Shop 1", DaTao: true, SoSanPham: 10, BoQua: null, Loi: null, ShopLogin: "shop.1");

        vm.ChotLuotKm(id, new KetQuaKm([shopTao], false, null, SoShopChuaChay: 5, NhuongChoCheckDon: true), Luc);
        Assert.Null(services.KmConfig.Get(id).LanChayCuoi);

        vm.ChotLuotKm(id, new KetQuaKm([shopTao], false, "Không về được trang chọn shop", SoShopChuaChay: 5), Luc);
        Assert.Null(services.KmConfig.Get(id).LanChayCuoi);

        vm.ChotLuotKm(id, new KetQuaKm([shopTao], false, null, SoShopChuaChay: 5, MocBatDauDaTroiQua: Luc), Luc);
        Assert.Null(services.KmConfig.Get(id).LanChayCuoi);

        vm.ChotLuotCombo(id, new KetQuaKm([shopTao], false, null, SoShopChuaChay: 5, TranThuDaCham: 2), Luc);
        Assert.Null(services.KmConfig.Get(id).ComboLanChayCuoi);
    }

    /// <summary>
    /// Khi lượt kế tiếp chạy nốt các shop còn lại và hoàn tất chu kỳ (<c>NenGhiMoc == true</c>),
    /// <c>ChotLuotKm</c> và <c>ChotLuotCombo</c> ghi mốc chạy cuối và xóa sạch <c>ShopKmComboDaXong</c>.
    /// </summary>
    [StaFact]
    public void LuotKeTiepHoanTat_ThiGhiMoc_VaXoaSachTienDoShopDaXong()
    {
        var (temp, services, vm, id) = DungVm("km-moc-hoantat-luot2@mail.com");
        using var db = temp;

        // Lượt 1 bị cắt bởi Captcha sau khi xong shop 1:
        var kq1 = new KetQuaKm(
            [new KetQuaKmMotShop("s1", "Delica Store", DaTao: true, SoSanPham: 155, BoQua: null, Loi: null, ShopLogin: "delica.store")],
            Captcha: true, Loi: "Rơi captcha", SoShopChuaChay: 2);
        vm.ChotLuotKm(id, kq1, Luc);
        vm.ChotLuotCombo(id, kq1, Luc);
        Assert.Single(vm.ShopKmComboDaXong(id, LoaiChuongTrinh.ChuongTrinhShop));
        Assert.Single(vm.ShopKmComboDaXong(id, LoaiChuongTrinh.Combo));

        // Lượt 2 chạy nốt 2 shop còn lại thành công:
        var luc2 = Luc.AddHours(1);
        var kq2 = new KetQuaKm(
            [
                new KetQuaKmMotShop("s2", "Minoa Store", DaTao: false, SoSanPham: 0, BoQua: "modal KHÔNG còn sản phẩm", Loi: null, ShopLogin: "minoa.store"),
                new KetQuaKmMotShop("s3", "Babysak Store", DaTao: true, SoSanPham: 42, BoQua: null, Loi: null, ShopLogin: "babysak.store"),
            ],
            Captcha: false, Loi: null, HetKhung: false, SoShopChuaChay: 0);

        vm.ChotLuotKm(id, kq2, luc2);
        vm.ChotLuotCombo(id, kq2, luc2);

        Assert.Equal(luc2, services.KmConfig.Get(id).LanChayCuoi);
        Assert.Equal(luc2, services.KmConfig.Get(id).ComboLanChayCuoi);
        Assert.Empty(vm.ShopKmComboDaXong(id, LoaiChuongTrinh.ChuongTrinhShop));
        Assert.Empty(vm.ShopKmComboDaXong(id, LoaiChuongTrinh.Combo));
    }
}
