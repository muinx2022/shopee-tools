using System;
using System.Linq;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// <b>Vòng 6 (22/08/2026) — hai lỗ ở CHỖ NỐI của hook <c>AppServices.MayNay</c>, phía module Đơn hàng.</b>
/// <list type="bullet">
/// <item><b>Z5</b> — lượt đọc hook ở đường MỞ FORM KM chưa có một phép đo nào: nghiệm thu vòng 5 đột biến nó
/// thành một hằng bịa và <b>cả 2552 test vẫn xanh</b>. Đây là lần thứ ba trong đợt cùng hình dạng "hàm đúng,
/// chỗ nối không ai đo" (P28 vòng 3 · X4/Y2 vòng 4–5 · nay), và chỗ nối này quyết định máy nào được đóng dấu
/// chủ trì khi người dùng sửa form — tức đêm sau máy nào chạy lượt KM thật.</item>
/// <item><b>Z1(d)</b> — <c>mayNay</c> RỖNG nghĩa là "máy này KHÔNG BIẾT MÌNH LÀ AI" (chưa cấu hình hub), khác
/// hẳn "KHÔNG AI chủ trì". Ghi rỗng đè lên là xoá chủ trì của cả fleet, mà <c>LaMayChuTri("", …)</c> trả true
/// cho MỌI máy ⇒ đêm sau cả nhóm cùng chạy lượt KM của tài khoản đó.</item>
/// </list>
/// </summary>
public class KmChoNoiMayNayTests
{
    private const string MayNay = "may-nay:orders";
    private const string MayKhac = "may-khac:orders";
    private const string Email = "km-chonoi@mail.com";

    private static readonly DateTime LucLuu = new(2026, 8, 22, 11, 0, 0);

    private static (TempDatabase Temp, AppServices Services, AccountsViewModel Vm, long AccountId)
        DungVm(string? mayNay)
    {
        var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        services.Accounts.Insert(new Account { Email = Email, Password = "p" });
        services.MayNay = mayNay is null ? null : () => mayNay;
        var id = services.Accounts.GetAll().First(a => a.Email == Email).Id;
        return (temp, services, new AccountsViewModel(services), id);
    }

    private static KmConfig Dong(long id, string ownerMachine, int phanTram = 5) => new(
        id, TuDong: true, SoNgay: 7, phanTram, GioBatDau: "02:00", GioKetThuc: "03:00", LanChayCuoi: null)
    {
        OwnerMachine = ownerMachine,
        CfgUpdatedAt = new DateTime(2026, 8, 20, 9, 0, 0),
    };

    // ══════════════════════════ Z5 — chỗ nối form ⇄ hook MayNay ══════════════════════════

    /// <summary>
    /// <b>Form "KM của Shop" phải nhận danh tính máy từ ĐÚNG hook <c>MayNay</c>.</b>
    /// <para>Đo bằng HÀNH VI người dùng nhìn thấy chứ không so chuỗi với chính hook: chủ trì trong DB đang là
    /// máy này ⇒ form phải nói "(máy này)" và nút "Máy này nhận chủ trì" phải TẮT. Đột biến lượt đọc hook thành
    /// một hằng bịa là cả hai vế lật ngược — form bảo "máy này KHÔNG chạy lượt KM tự động của tài khoản đó"
    /// trong khi chính nó là máy chủ trì.</para>
    /// </summary>
    [StaFact]
    public void MoFormKm_LayDanhTinhMayTuHookMayNay()
    {
        var (temp, services, vm, id) = DungVm(MayNay);
        using var db = temp;
        services.KmConfig.Save(Dong(id, ownerMachine: MayNay));

        var form = vm.DungFormKm(id, Email, soShop: 3);

        Assert.Equal(MayNay, form.MayNay);
        Assert.Contains("(máy này)", form.MoTaChuTri);
        Assert.False(form.CoTheNhanChuTri);   // đã là chủ trì rồi thì không "nhận" nữa
    }

    /// <summary>Vế ĐỐI CHỨNG — máy KHÁC đang chủ trì thì form phải nói đúng như thế, và nút nhận chủ trì BẬT.
    /// Thiếu vế này thì một bản "luôn coi máy này là chủ trì" cũng xanh, mà bản đó xoá sạch ý nghĩa của cửa
    /// chủ trì trên form.</summary>
    [StaFact]
    public void MoFormKm_MayKhacChuTri_ThiFormNoiDung_VaConNhanChuTriDuoc()
    {
        var (temp, services, vm, id) = DungVm(MayNay);
        using var db = temp;
        services.KmConfig.Save(Dong(id, ownerMachine: MayKhac));

        var form = vm.DungFormKm(id, Email, soShop: 3);

        Assert.Equal(MayNay, form.MayNay);
        Assert.Contains("KHÔNG chạy lượt KM tự động", form.MoTaChuTri);
        Assert.True(form.CoTheNhanChuTri);
    }

    /// <summary>Hook CHƯA RÓT (app Đơn hàng chạy độc lập / chưa cấu hình hub) ⇒ form không biết mình là ai ⇒
    /// KHÔNG nhận chủ trì được. Đây là vế mà bản đột biến "hằng bịa" cũng làm hỏng: một hằng khác rỗng biến máy
    /// không hub thành máy có danh tính.</summary>
    [StaFact]
    public void MoFormKm_ChuaCoHub_ThiFormKhongBietMinhLaAi()
    {
        var (temp, services, vm, id) = DungVm(mayNay: null);
        using var db = temp;
        services.KmConfig.Save(Dong(id, ownerMachine: MayKhac));

        var form = vm.DungFormKm(id, Email, soShop: 3);

        Assert.Equal("", form.MayNay);
        Assert.False(form.CoTheNhanChuTri);
    }

    // ══════════════════════════ Z1(d) — "không biết mình là ai" ≠ "không ai chủ trì" ══════════════════════════

    /// <summary>
    /// <b>Máy KHÔNG có hub sửa form ⇒ <c>OwnerMachine</c> đang có KHÔNG bị ghi đè thành rỗng.</b>
    /// <para>Ca thật: fleet ba máy, máy C chưa cấu hình hub (hoặc hub vừa rớt) nhưng vẫn có dòng
    /// <c>km_config</c> kéo về từ trước, ghi chủ trì là máy A. Người dùng ngồi C đổi 5% → 25% rồi bấm "Đóng".
    /// Ghi rỗng đè lên là <b>xoá chủ trì trên cả fleet</b> ngay lượt đẩy kế: <c>LaMayChuTri("", …)</c> trả true
    /// cho mọi máy ⇒ đêm sau A, B, C cùng chạy lượt KM của tài khoản đó, cùng tạo chương trình khuyến mãi
    /// THẬT.</para>
    /// </summary>
    [Fact]
    public void KhongCoHub_SuaForm_ThiGiuNguyenChuTriDangCo()
    {
        var tuoi = Dong(1, ownerMachine: MayKhac);

        var luu = KmDialogViewModel.BanLuuSauForm(
            tuoi, banNap: tuoi, tuForm: tuoi with { PhanTram = 25 }, mayNay: "", LucLuu);

        Assert.NotNull(luu);
        Assert.Equal(25, luu!.PhanTram);              // thay đổi của người dùng VẪN được lưu…
        Assert.Equal(MayKhac, luu.OwnerMachine);      // …nhưng chủ trì thì giữ nguyên
        Assert.Equal(LucLuu, luu.CfgUpdatedAt);
    }

    /// <summary>Không hub mà dòng cũng CHƯA có chủ trì ⇒ vẫn rỗng: hàm không được tự bịa ra một chủ trì.</summary>
    [Fact]
    public void KhongCoHub_DongChuaCoChuTri_ThiVanRong()
    {
        var tuoi = Dong(1, ownerMachine: "");

        var luu = KmDialogViewModel.BanLuuSauForm(
            tuoi, banNap: tuoi, tuForm: tuoi with { PhanTram = 25 }, mayNay: "", LucLuu);

        Assert.Equal("", luu!.OwnerMachine);
    }

    /// <summary>Vế ĐỐI CHỨNG — CÓ hub thì lượt sửa form vẫn ĐỔI chủ trì sang máy này (người dùng đã chốt "đổi
    /// chủ trì = sang máy đó sửa form"). Thiếu vế này thì một bản "không bao giờ đổi chủ trì" cũng xanh.</summary>
    [Fact]
    public void CoHub_SuaForm_ThiVanDoiChuTriSangMayNay()
    {
        var tuoi = Dong(1, ownerMachine: MayKhac);

        var luu = KmDialogViewModel.BanLuuSauForm(
            tuoi, banNap: tuoi, tuForm: tuoi with { PhanTram = 25 }, MayNay, LucLuu);

        Assert.Equal(MayNay, luu!.OwnerMachine);
    }
}
