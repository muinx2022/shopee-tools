using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// TIẾNG CHUÔNG "hồ sơ KM vừa đổi" (<c>AppServices.KmHoSoDaDoi</c>) — hook mà shell suite rót worker đồng bộ vào.
/// <para><b>Vì sao đáng test riêng, dù nó chỉ là một lời gọi:</b> mọi thứ khác của đồng bộ có thể đúng hết mà
/// tính năng vẫn hỏng nếu không ai rung chuông. Mốc <c>LanThuTuDong</c>/<c>LanChayCuoi</c> nằm im trong SQLite
/// của một máy, máy kia không biết, và đêm sau nó chạy lại đúng lượt KM vừa chạy — không lỗi, không log. Quên
/// một lời gọi là kiểu hỏng KHÔNG bao giờ lộ ra khi đọc code, vì code còn lại trông hoàn toàn đúng.</para>
/// <para>Đường LƯU FORM không có mặt ở đây: <c>MoFormKm</c> phải mở hộp thoại WPF thật mới chạy tới lượt lưu.
/// Ba đường còn lại (chốt lượt KM · lượt tự động ghi mốc thử · lượt tự động bị chặn vì cấu hình hỏng) đều gọi
/// được thẳng.</para>
/// </summary>
public class KmChuongDayHoSoTests
{
    private static readonly DateTime Luc = new(2026, 8, 19, 3, 20, 0);

    private static (TempDatabase Temp, AppServices Services, AccountsViewModel Vm, long AccountId, List<int> Chuong)
        DungVm(string email)
    {
        var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        services.Accounts.Insert(new Account { Email = email, Password = "p" });
        var id = services.Accounts.GetAll().First(a => a.Email == email).Id;

        // "Máy khác đang giữ" ⇒ lượt KM dừng ngay ở cửa khoá, KHÔNG phóng Brave nào (khuôn KmNhipTuDongTests).
        services.AcquireAccountLease = (login, ct) => Task.FromResult(new OrdersLeaseResult(false, "MAY-KHAC"));
        services.ReleaseAccountLease = login => Task.CompletedTask;

        var chuong = new List<int>();
        services.KmHoSoDaDoi = () => chuong.Add(1);

        return (temp, services, new AccountsViewModel(services), id, chuong);
    }

    private static KetQuaKm Lot(int tao)
    {
        var ds = new List<KetQuaKmMotShop>();
        for (var i = 0; i < tao; i++) { ds.Add(new KetQuaKmMotShop($"t{i}", $"Tao{i}", true, 10, null, null)); }
        return new KetQuaKm(ds, Captcha: false, Loi: null, HetKhung: false, SoShopChuaChay: 0);
    }

    /// <summary>Chốt lượt KM có ghi <c>LanChayCuoi</c> ⇒ PHẢI rung chuông (mốc này là mốc CHUNG của tài khoản,
    /// máy khác phải biết đêm nay đã chạy rồi).</summary>
    [StaFact]
    public void ChotLuotKm_CoGhiMoc_ThiRungChuong()
    {
        var (temp, services, vm, id, chuong) = DungVm("km-chuong-chot@mail.com");
        using var db = temp;

        vm.ChotLuotKm(id, Lot(tao: 3), Luc);

        Assert.Equal(Luc, services.KmConfig.Get(id).LanChayCuoi);
        Assert.Single(chuong);
    }

    /// <summary>Vế đối chứng: KHÔNG ghi mốc thì KHÔNG rung chuông — thiếu vế này thì một bản "rung chuông vô
    /// điều kiện" cũng xanh, mà bản đó bắt worker đẩy lại toàn bộ hồ sơ sau mỗi lượt KM trắng tay.</summary>
    [StaFact]
    public void ChotLuotKm_KhongGhiMoc_ThiKhongRungChuong()
    {
        var (temp, _, vm, id, chuong) = DungVm("km-chuong-trang@mail.com");
        using var db = temp;

        vm.ChotLuotKm(id, Lot(tao: 0), Luc);

        Assert.Empty(chuong);
    }

    /// <summary>Lượt tự động ghi <c>LanThuTuDong</c> TRƯỚC khi chạy ⇒ rung chuông. Đây là mốc chặn "hai máy cùng
    /// thử trong một đêm", nên nó phải lên tới hub.</summary>
    [StaFact]
    public async Task NhipTuDong_GhiMocThu_ThiRungChuong()
    {
        var (temp, services, vm, id, chuong) = DungVm("km-chuong-nhip@mail.com");
        using var db = temp;
        services.KmConfig.Save(new KmConfig(id, TuDong: true, SoNgay: 1, PhanTram: 5,
            GioBatDau: "09:30", GioKetThuc: "10:30", LanChayCuoi: null));
        chuong.Clear();   // lượt Save dựng cảnh ở trên không đi qua hook, nhưng dọn cho chắc

        var bayGio = new DateTime(2026, 8, 19, 2, 5, 0);
        await vm.NhipLichKmAsync(bayGio);

        Assert.Equal(bayGio, services.KmConfig.Get(id).LanThuTuDong);
        Assert.NotEmpty(chuong);
    }

    /// <summary>Lượt tự động bị chặn vì cấu hình hỏng cũng ghi <c>LanThuTuDong</c> ⇒ cũng phải rung chuông. Bỏ
    /// sót nhánh này thì máy khác không biết đêm nay tài khoản đó đã "dùng" lượt thử, và nó thử lại.</summary>
    [StaFact]
    public async Task NhipTuDong_CauHinhHong_VanGhiMocThu_VaVanRungChuong()
    {
        var (temp, services, vm, id, chuong) = DungVm("km-chuong-hong@mail.com");
        using var db = temp;
        // Giờ kết thúc ≤ giờ bắt đầu ⇒ cấu hình hỏng ⇒ chặn trước khi phóng trình duyệt.
        // (Trước 25/08/2026 chỗ này dựng cảnh bằng `GioBatDau = "01:00"` — mốc quá khứ so với nhịp 02:05. Từ khi
        // ngày chương trình đi trước 180 ngày thì cấu hình ấy HỢP LỆ, và ca này lặng lẽ đo nhầm nhánh.)
        services.KmConfig.Save(new KmConfig(id, TuDong: true, SoNgay: 1, PhanTram: 5,
            GioBatDau: "09:30", GioKetThuc: "09:00", LanChayCuoi: null));
        chuong.Clear();

        var bayGio = new DateTime(2026, 8, 19, 2, 5, 0);
        await vm.NhipLichKmAsync(bayGio);

        Assert.Equal(bayGio, services.KmConfig.Get(id).LanThuTuDong);
        Assert.NotEmpty(chuong);
    }

    /// <summary>Chưa tới hạn ⇒ không ghi gì, không rung chuông (vế đối chứng của hai test trên).</summary>
    [StaFact]
    public async Task NhipTuDong_ChuaToiHan_ThiKhongRungChuong()
    {
        var (temp, services, vm, id, chuong) = DungVm("km-chuong-chuahan@mail.com");
        using var db = temp;
        services.KmConfig.Save(new KmConfig(id, TuDong: true, SoNgay: 7, PhanTram: 5,
            GioBatDau: "09:30", GioKetThuc: "10:30", LanChayCuoi: null));
        chuong.Clear();

        await vm.NhipLichKmAsync(new DateTime(2026, 8, 19, 14, 0, 0));   // ngoài khung 2–6h

        Assert.Null(services.KmConfig.Get(id).LanThuTuDong);
        Assert.Empty(chuong);
    }

    /// <summary>Hook để <c>null</c> (app Đơn hàng chạy độc lập, không có shell suite rót) thì mọi đường ghi mốc
    /// vẫn chạy bình thường — không được ném <c>NullReferenceException</c> vào giữa lượt KM.</summary>
    [StaFact]
    public void HookNull_ThiVanGhiMocBinhThuong()
    {
        var (temp, services, vm, id, _) = DungVm("km-chuong-null@mail.com");
        using var db = temp;
        services.KmHoSoDaDoi = null;

        vm.ChotLuotKm(id, Lot(tao: 2), Luc);

        Assert.Equal(Luc, services.KmConfig.Get(id).LanChayCuoi);
    }
}
