using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// <b>Lát cắt "tự kết thúc" (KM của Shop) phải ĐÓNG CỬA SỔ + TRẢ HỒ SƠ kể cả khi thân lệnh NÉM.</b>
/// <para>Lỗi thật (phản biện 19/08/2026): khối đóng-cửa-sổ + trả-hồ-sơ nằm trên ĐƯỜNG THÀNH CÔNG của
/// <c>ChayLatCatAsync</c>. <c>than</c> ném là nhảy thẳng xuống <c>catch</c> ⇒ cửa sổ Brave còn sống ⇒
/// <c>CuaSoXemDangMo == true</c> ⇒ nhánh <c>if (!CuaSoXemDangMo) TraHoSo()</c> trong <c>finally</c> cũng bị bỏ
/// qua nốt. Lúc 2h sáng: khoá xuyên máy kẹt tới khi tắt app, check đơn KHÔNG BAO GIỜ chạy lại, Brave mở tới
/// sáng — trái đúng yêu cầu "xong KM thì chạy lại flow check đơn".</para>
/// <para><b>Vì sao phải có tiến trình THẬT:</b> trong test, <c>session.Process</c> vốn null nên
/// <c>CuaSoXemDangMo</c> luôn false và nhánh cứu ở <c>finally</c> luôn chạy — tức mọi test không có tiến trình
/// đều XANH ở CẢ HAI bản, đúng nghĩa một phép đo rỗng. Ta gán một tiến trình con thật vào
/// <c>session.Process</c> (setter <c>internal</c>) để dựng lại đúng trạng thái "cửa sổ còn sống".</para>
/// </summary>
public class KmTraHoSoKhiNemTests
{
    /// <summary>Tiến trình con vô hại, sống đủ lâu cho một lượt test (ping loopback ~30s).</summary>
    private static Process MoTienTrinhGia()
        => Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;

    private static (TempDatabase Temp, AppServices Services, AccountsViewModel Vm) DungVm()
    {
        var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        return (temp, services, new AccountsViewModel(services));
    }

    private static void ChonTaiKhoan(AppServices services, AccountsViewModel vm, string email)
    {
        services.Accounts.Insert(new Account { Email = email, Password = "p" });
        vm.Reload();
        vm.SelectedRow = vm.Accounts.First(a => a.Email == email);
    }

    /// <summary>
    /// <c>dongCuaSoKhiXong: true</c> + <c>than</c> NÉM ⇒ cửa sổ bị GIẾT và khoá xuyên máy được NHẢ.
    /// </summary>
    [StaFact]
    public async Task ThanNEM_MaLatCatTuKetThuc_ThiVanDongCuaSo_VaNhaKhoa()
    {
        var (temp, services, vm) = DungVm();
        using var db = temp;
        ChonTaiKhoan(services, vm, "km-nem@mail.com");

        var soLanNha = 0;
        services.AcquireAccountLease = (login, ct) => Task.FromResult(new OrdersLeaseResult(true, null));
        services.ReleaseAccountLease = login => { soLanNha++; return Task.CompletedTask; };

        var tienTrinh = MoTienTrinhGia();
        try
        {
            await vm.ChayLatCatAsync("KM của Shop", "bắt đầu", "đang chạy",
                (phien, _, _) =>
                {
                    // Dựng lại đúng trạng thái thật: trình duyệt ĐÃ mở, rồi vòng KM ném giữa chừng.
                    phien.Process = tienTrinh;
                    throw new InvalidOperationException("vòng KM vỡ giữa chừng");
                },
                dongCuaSoKhiXong: true);

            Assert.True(tienTrinh.WaitForExit(5000),
                "Cửa sổ Brave của lượt KM KHÔNG bị đóng khi thân lệnh ném — khoá hồ sơ + cổng cầu nối kẹt lại.");
            Assert.False(vm.CuaSoXemDangMo);
            Assert.Equal(1, soLanNha);
        }
        finally
        {
            try { if (!tienTrinh.HasExited) { tienTrinh.Kill(entireProcessTree: true); } } catch { }
            tienTrinh.Dispose();
        }
    }

    /// <summary>
    /// <b>Hành vi của "Mở list shop" KHÔNG được đổi:</b> <c>dongCuaSoKhiXong: false</c> ⇒ cửa sổ Ở LẠI cho
    /// người dùng thao tác tay, kể cả khi thân lệnh ném (chính cái cửa sổ đang kẹt mới là bằng chứng để biết
    /// kẹt ở đâu). Đây là vế đối chứng của test trên — thiếu nó thì "sửa" thành đóng-mọi-lúc cũng xanh.
    /// </summary>
    [StaFact]
    public async Task ThanNEM_MaKHONG_tu_ket_thuc_ThiCuaSo_O_LAI()
    {
        var (temp, services, vm) = DungVm();
        using var db = temp;
        ChonTaiKhoan(services, vm, "molist-nem@mail.com");

        services.AcquireAccountLease = (login, ct) => Task.FromResult(new OrdersLeaseResult(true, null));
        services.ReleaseAccountLease = login => Task.CompletedTask;

        var tienTrinh = MoTienTrinhGia();
        try
        {
            await vm.ChayLatCatAsync("Mở list shop", "bắt đầu", "đang chạy",
                (phien, _, _) =>
                {
                    phien.Process = tienTrinh;
                    throw new InvalidOperationException("hỏng giữa chừng");
                });

            Assert.False(tienTrinh.HasExited, "Nút 'Mở list shop' CỐ Ý để cửa sổ lại — không được đóng hộ.");
            Assert.True(vm.CuaSoXemDangMo);
        }
        finally
        {
            try { if (!tienTrinh.HasExited) { tienTrinh.Kill(entireProcessTree: true); } } catch { }
            tienTrinh.Dispose();
        }
    }

    /// <summary>
    /// <b>MƯỢN HỒ SƠ THẤT BẠI ⇒ TUYỆT ĐỐI KHÔNG đụng tới cửa sổ đang mở của lượt TRƯỚC.</b>
    /// <para>Lỗi do CHÍNH bản vá đợt 1 đẻ ra (phản biện vòng 2, 19/08/2026): khối đóng cửa sổ được dời vào
    /// <c>finally</c> để chạy trên MỌI đường ra — nhưng <c>finally</c> cũng chạy cho đường "mượn hồ sơ không
    /// được" (<c>return</c> sớm). Kết quả: một lượt KM tự động 2h sáng bị máy khác từ chối khoá sẽ GIẾT mất cửa
    /// sổ "Mở list shop" mà người dùng đang thao tác dở — họ chẳng làm gì sai và cũng chẳng hiểu vì sao.</para>
    /// <para>Dựng lại đúng trạng thái đó: lượt 1 ("Mở list shop", lease OK) để lại một tiến trình sống; lượt 2
    /// ("KM của Shop", <c>dongCuaSoKhiXong: true</c>, lease BỊ TỪ CHỐI) phải không đụng tới nó.</para>
    /// </summary>
    [StaFact]
    public async Task MuonHoSoTHAT_BAI_ThiKHONG_giet_cua_so_cua_luot_truoc()
    {
        var (temp, services, vm) = DungVm();
        using var db = temp;
        ChonTaiKhoan(services, vm, "km-muon-hut@mail.com");

        var choPhepKhoa = true;
        services.AcquireAccountLease = (login, ct) =>
            Task.FromResult(choPhepKhoa ? new OrdersLeaseResult(true, null) : new OrdersLeaseResult(false, "MAY-KHO-2"));
        services.ReleaseAccountLease = login => Task.CompletedTask;

        var tienTrinh = MoTienTrinhGia();
        try
        {
            // Lượt 1: "Mở list shop" — CỐ Ý để cửa sổ lại.
            await vm.ChayLatCatAsync("Mở list shop", "bắt đầu", "đang chạy",
                (phien, _, _) =>
                {
                    phien.Process = tienTrinh;
                    return Task.FromResult(AccountsViewModel.KetLuanLatCat.Chung("xong"));
                });
            Assert.True(vm.CuaSoXemDangMo, "Tiền đề của test hỏng: lượt 1 phải để lại một cửa sổ đang mở.");

            // Lượt 2: KM của Shop, nhưng máy khác đang giữ khoá ⇒ dừng ngay ở cửa.
            choPhepKhoa = false;
            var daVaoThan = false;
            await vm.ChayLatCatAsync("KM của Shop", "bắt đầu", "đang chạy",
                (phien, _, _) => { daVaoThan = true; return Task.FromResult(AccountsViewModel.KetLuanLatCat.Chung("xong")); },
                dongCuaSoKhiXong: true);

            Assert.False(daVaoThan, "Bị từ chối khoá thì KHÔNG được chạy thân lệnh (fail-closed).");
            Assert.False(tienTrinh.HasExited,
                "Lượt KM mượn hồ sơ THẤT BẠI vẫn đi giết cửa sổ 'Mở list shop' của người dùng.");
            Assert.True(vm.CuaSoXemDangMo);
        }
        finally
        {
            try { if (!tienTrinh.HasExited) { tienTrinh.Kill(entireProcessTree: true); } } catch { }
            tienTrinh.Dispose();
        }
    }
}
