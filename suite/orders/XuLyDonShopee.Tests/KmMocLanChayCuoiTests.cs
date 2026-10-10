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
}
