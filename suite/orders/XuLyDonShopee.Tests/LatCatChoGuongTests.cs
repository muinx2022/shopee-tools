using System.Linq;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.Core.Models;

namespace XuLyDonShopee.Tests;

/// <summary>
/// <b>NGUỒN SỰ THẬT của trường <c>LatCat</c> trên gương đẩy lên hub</b> —
/// <see cref="AccountsViewModel.LatCatCua"/>: máy có đang chạy một lát cắt cầu nối (KM · Combo · Flash Sale ·
/// Mở list shop) CHO ĐÚNG tài khoản được hỏi hay không.
///
/// <para><b>Vì sao phải có trường này:</b> một lát cắt bắt đầu bằng việc DỪNG mọi phiên check đơn để mượn hồ sơ
/// Brave, nên <c>Sessions.Get(id)?.State</c> lúc đó đúng là "không có phiên" ⇒ gương báo RỖNG = rảnh. Hub nhìn
/// vào đó rồi mời bấm "▶ Chạy" ngay giữa lúc gói Flash Sale đang chạy, mà cú bấm ấy bật cờ nhường chỗ — tức ép
/// chính gói đang chạy nhường sau shop hiện tại (phản biện 04/09/2026, sổ P-20260904-12).</para>
///
/// <para><b>Hàm THUẦN chứ không đo qua VM:</b> ba trường nó đọc (<c>_bridgeRunning</c> ·
/// <c>TaiKhoanLatCatDangChay</c> · <c>TenLatCatDangChay</c>) chỉ được đặt bên trong <c>ChayLatCatAsync</c> — mà
/// hàm đó phóng Brave thật. Tách luật ra một hàm tĩnh là cách duy nhất đo được đủ ma trận.</para>
/// </summary>
public sealed class LatCatChoGuongTests
{
    private const long AccA = 11;
    private const long AccB = 22;

    /// <summary>Không lát cắt nào đang chạy ⇒ mọi tài khoản đều <c>null</c>, kể cả khi hai trường kia còn sót giá
    /// trị. Cờ cầu nối bị hạ TRƯỚC hai trường tên/tài-khoản trong <c>finally</c>, nên khe đó có thật.</summary>
    [Fact]
    public void KhongChayLatCat_ThiNull()
    {
        Assert.Null(AccountsViewModel.LatCatCua(false, null, null, AccA));
        Assert.Null(AccountsViewModel.LatCatCua(false, AccA, "Flash Sale", AccA));
    }

    /// <summary>Đang chạy lát cắt CỦA A, hỏi A ⇒ trả đúng TÊN VIỆC (chuỗi này đi thẳng lên hub làm nhãn).</summary>
    [Theory]
    [InlineData("Flash Sale")]
    [InlineData("KM của Shop")]
    [InlineData("Combo Khuyến Mãi")]
    [InlineData("Mở list shop")]
    public void ChayCuaA_HoiA_ThiTraTenViec(string ten)
        => Assert.Equal(ten, AccountsViewModel.LatCatCua(true, AccA, ten, AccA));

    /// <summary>Đang chạy lát cắt CỦA A, hỏi B ⇒ <c>null</c>. Bỏ vế này là hub hiện "✖ Dừng" ở dòng B, và cú bấm
    /// đó gửi <c>stop-all</c> cho một tài khoản chẳng chạy gì — trong khi A vẫn chạy tiếp.</summary>
    [Fact]
    public void ChayCuaA_HoiB_ThiNull()
        => Assert.Null(AccountsViewModel.LatCatCua(true, AccA, "Flash Sale", AccB));

    /// <summary>Cờ bật nhưng chưa có tên (khe giữa hai lệnh gán, hoặc tên rỗng do lỗi gọi) ⇒ <c>null</c>, không
    /// đẩy chuỗi rỗng/khoảng trắng lên hub làm nhãn ma.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TenRong_ThiNull(string? ten)
        => Assert.Null(AccountsViewModel.LatCatCua(true, AccA, ten, AccA));

    /// <summary>Cờ bật mà chưa chốt được tài khoản (<c>null</c>) ⇒ không tài khoản nào được nhận nhãn.</summary>
    [Fact]
    public void ChuaChotTaiKhoan_ThiKhongAiNhanNhan()
        => Assert.Null(AccountsViewModel.LatCatCua(true, null, "Flash Sale", AccA));

    // ══════════════════ ĐƯỜNG NỐI THẬT: VM → AppServices.LatCatDangChayCua (phản biện 04/09/2026, NẶNG) ══════════════════
    // Bốn bài trên đo hàm THUẦN — không bài nào chứng minh nó THỰC SỰ được rót vào `AppServices` lúc dựng VM.
    // Xoá dòng `_services.LatCatDangChayCua = _hookLatCat;` ở ctor thì tính năng CHẾT (gương luôn gửi null, hub
    // quay lại hành vi trước bản vá) mà không một bài nào trong repo từng đỏ — đã CHỨNG MINH bằng đột biến.

    /// <summary>Dựng VM thật (không qua bàn thử ma) ⇒ <c>services.LatCatDangChayCua</c> phải trỏ ĐÚNG hàm đọc
    /// trạng thái lát cắt hiện hành của VM đó, không phải một hook rỗng hay hook của VM khác.</summary>
    [Fact]
    public void CtorThat_RotHookVaoAppServices()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        services.Accounts.Insert(new Account { Email = "hook-a@mail.com", Password = "p" });
        var id = services.Accounts.GetAll().First(a => a.Email == "hook-a@mail.com").Id;
        using var vm = new AccountsViewModel(services);

        Assert.NotNull(services.LatCatDangChayCua);
        vm.DatTenLatCatChoTest(AccountsViewModel.TenViecFlash, id, giuCauNoi: true);

        Assert.Equal(AccountsViewModel.TenViecFlash, services.LatCatDangChayCua!(id));
    }

    /// <summary>Dispose VM ⇒ hook được GỠ khỏi <c>AppServices</c> (không hạ hờ), đúng khuôn <c>_hookLenhHub</c>
    /// đã có sẵn — một hook treo vào VM đã chết là rò bộ nhớ và gương gọi vào một VM chết.</summary>
    [Fact]
    public void DisposeThat_GoHookKhoiAppServices()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        var vm = new AccountsViewModel(services);
        Assert.NotNull(services.LatCatDangChayCua);

        vm.Dispose();

        Assert.Null(services.LatCatDangChayCua);
    }
}
