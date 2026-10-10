using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.Core.Models;

namespace XuLyDonShopee.Tests;

/// <summary>
/// <b>Bắt tay "nhường khung" XUYÊN MÁY phía client</b> — mảnh trả lời câu hỏi mở đầu cả đợt: máy chạy KM nhờ hub
/// dừng check đơn ở máy khác, chờ nhả khoá, chạy, rồi cho máy kia chạy lại.
/// <para><b>Bất biến đắt nhất, và là thứ file này sinh ra để khoá:</b> <c>TraKhungKm</c> phải được gọi trên
/// <b>MỌI</b> đường ra — kể cả đường HẾT HẠN CHỜ, nơi lượt KM bỏ cuộc. Vì chỉ cần đã GỌI xin khung là hub CÓ THỂ
/// đã ra lệnh dừng máy kia; bỏ lượt mà không trả khung là để máy đó nằm im tới lượt quét quá hạn của hub (90').
/// Đây đúng họ lỗi "mượn không trả" đã cắn nhiều lần ở <c>TraHoSo</c>.</para>
/// <para>Không cần hub thật: hai hook <c>XinKhungKm</c>/<c>TraKhungKm</c> là điểm nối duy nhất, rót thẳng hàm giả.</para>
/// </summary>
// ⚠ CÙNG COLLECTION với `ComboKhuyenMaiTests`: cả hai đều SỬA hai biến static
// `AccountsViewModel.HanChoKhungXuyenMay`/`NhipHoiKhungXuyenMay` để khỏi ngồi chờ 3 phút thật. xunit chạy các
// lớp test SONG SONG theo mặc định, nên hai lớp cùng đụng một biến static là đỏ NGẪU NHIÊN — kiểu hỏng tốn
// nhất để lần ra. Đặt chung một collection là ép chúng chạy nối đuôi.
[Collection(TenCollection.KhungXuyenMay)]
public class KmKhungXuyenMayTests : IDisposable
{
    // Khôi phục từ hai HẰNG `MacDinh*` (readonly, không ai đụng), KHÔNG chụp giá trị đang dùng lúc chạy:
    // `static readonly` khởi tạo LƯỜI (`beforefieldinit`) nên lượt chụp có thể xảy ra SAU khi ctor đã rút hạn
    // xuống — lúc đó nó chụp nhầm 600ms, Dispose "khôi phục" thành 600ms, và test khoá-mặc-định tự khẳng định
    // đúng cái mình vừa đặt. Đã cắn một lần khi viết file này.
    private static readonly TimeSpan HanGoc = AccountsViewModel.MacDinhHanChoKhung;
    private static readonly TimeSpan NhipGoc = AccountsViewModel.MacDinhNhipHoiKhung;

    public KmKhungXuyenMayTests()
    {
        // Rút hạn/nhịp xuống để test không ngồi chờ 3 phút thật.
        AccountsViewModel.HanChoKhungXuyenMay = TimeSpan.FromMilliseconds(600);
        AccountsViewModel.NhipHoiKhungXuyenMay = TimeSpan.FromMilliseconds(50);
    }

    public void Dispose()
    {
        AccountsViewModel.HanChoKhungXuyenMay = HanGoc;
        AccountsViewModel.NhipHoiKhungXuyenMay = NhipGoc;
    }

    private static (TempDatabase Temp, AppServices Services, AccountsViewModel Vm, long Id) DungVm(string email)
    {
        var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        services.Accounts.Insert(new Account { Email = email, Password = "p" });
        var vm = new AccountsViewModel(services);
        vm.Reload();
        var id = services.Accounts.GetAll().First(a => a.Email == email).Id;
        vm.SelectedRow = vm.Accounts.First(a => a.Email == email);
        return (temp, services, vm, id);
    }

    /// <summary>Chạy một lát cắt KM giả (thân KHÔNG đụng trình duyệt) qua đúng khung <c>ChayLatCatAsync</c>.</summary>
    private static Task ChayLatCatKm(AccountsViewModel vm, long accountId)
        => vm.ChayLatCatAsync(
            tenViec: "KM của Shop",
            cauBatDau: "bắt đầu",
            trangThaiBatDau: "đang chạy",
            than: (phien, login, ct) => Task.FromResult(new AccountsViewModel.KetLuanLatCat("xong", "xong")),
            dongCuaSoKhiXong: true,
            accountIdChiDinh: accountId,
            xinKhungXuyenMay: true);

    /// <summary>Không có hub (hook chưa rót) ⇒ đi thẳng, KHÔNG chờ, KHÔNG log gì thêm — degrade như một máy.</summary>
    [StaFact]
    public async Task KhongCoHub_ThiDiThang_KhongCho()
    {
        var (temp, services, vm, id) = DungVm("khung-nohub@mail.com");
        using var db = temp;
        services.AcquireAccountLease = (l, ct) => Task.FromResult(new OrdersLeaseResult(true, null));
        services.ReleaseAccountLease = l => Task.CompletedTask;

        var moc = DateTime.UtcNow;
        await ChayLatCatKm(vm, id);

        Assert.True(DateTime.UtcNow - moc < TimeSpan.FromMilliseconds(500));   // không rơi vào vòng chờ
    }

    /// <summary>Khoá trống ngay ⇒ chạy luôn, và VẪN trả khung (no-op phía hub, nhưng client không được tự
    /// quyết bỏ qua — hub mới là nơi biết có nợ ai không).</summary>
    [StaFact]
    public async Task KhoaTrongNgay_ThiChayLuon_VaVanTraKhung()
    {
        var (temp, services, vm, id) = DungVm("khung-trong@mail.com");
        using var db = temp;
        var soLanTra = 0;
        services.AcquireAccountLease = (l, ct) => Task.FromResult(new OrdersLeaseResult(true, null));
        services.ReleaseAccountLease = l => Task.CompletedTask;
        services.XinKhungKm = (l, ct) => Task.FromResult(new KmKhungResult(true, null));
        services.TraKhungKm = l => { soLanTra++; return Task.CompletedTask; };

        await ChayLatCatKm(vm, id);

        Assert.Equal(1, soLanTra);
    }

    /// <summary>Máy kia đang giữ, nhả sau vài nhịp ⇒ client CHỜ rồi chạy; nhật ký nêu TÊN MÁY đang chờ.</summary>
    [StaFact]
    public async Task MayKiaNhaSauVaiNhip_ThiChoRoiChay_VaLogNeuTenMay()
    {
        var (temp, services, vm, id) = DungVm("khung-cho@mail.com");
        using var db = temp;
        var soLanHoi = 0;
        var soLanTra = 0;
        services.AcquireAccountLease = (l, ct) => Task.FromResult(new OrdersLeaseResult(true, null));
        services.ReleaseAccountLease = l => Task.CompletedTask;
        services.XinKhungKm = (l, ct) =>
        {
            soLanHoi++;
            return Task.FromResult(soLanHoi >= 3 ? new KmKhungResult(true, null) : new KmKhungResult(false, "NUC"));
        };
        services.TraKhungKm = l => { soLanTra++; return Task.CompletedTask; };

        await ChayLatCatKm(vm, id);

        Assert.Equal(3, soLanHoi);
        Assert.Equal(1, soLanTra);
        var nhat = string.Join("\n", services.Log.Snapshot("khung-cho@mail.com").Select(d => d.Message));
        Assert.Contains("NUC", nhat);
        Assert.Contains("đã nhờ hub bảo nó dừng", nhat);
        Assert.Contains("Máy kia đã dừng check đơn", nhat);
    }

    /// <summary>
    /// <b>Ca quan trọng nhất</b> — máy kia KHÔNG BAO GIỜ nhả ⇒ hết hạn, BỎ lượt KM… nhưng <b>vẫn phải TRẢ
    /// KHUNG</b>, vì hub đã ra lệnh dừng máy đó rồi.
    /// <para>Bỏ nhánh trả khung ở đường này là máy kia đứng im tới lượt quét quá hạn của hub — 90 phút check đơn
    /// chết, không vì cái gì, và không một dòng nào nói tại sao.</para>
    /// </summary>
    [StaFact]
    public async Task HetHanCho_ThiBoLuot_NHUNG_VanTraKhung()
    {
        var (temp, services, vm, id) = DungVm("khung-hethan@mail.com");
        using var db = temp;
        var soLanTra = 0;
        var daXinKhoa = 0;
        services.AcquireAccountLease = (l, ct) => { daXinKhoa++; return Task.FromResult(new OrdersLeaseResult(true, null)); };
        services.ReleaseAccountLease = l => Task.CompletedTask;
        services.XinKhungKm = (l, ct) => Task.FromResult(new KmKhungResult(false, "NUC"));
        services.TraKhungKm = l => { soLanTra++; return Task.CompletedTask; };

        await ChayLatCatKm(vm, id);

        Assert.Equal(1, soLanTra);                 // ĐÃ TRẢ, dù bỏ lượt
        Assert.Equal(0, daXinKhoa);                // và KHÔNG hề mở phiên: bỏ trước khi đụng hồ sơ/trình duyệt
        var nhat = string.Join("\n", services.Log.Snapshot("khung-hethan@mail.com").Select(d => d.Message));
        Assert.Contains("BỎ lượt KM", nhat);
        Assert.Contains("NUC", nhat);
    }

    /// <summary>Thân lệnh NÉM ⇒ vẫn trả khung. Cùng họ với <c>KmTraHoSoKhiNemTests</c>: mọi đường ra, không trừ
    /// đường nào.</summary>
    [StaFact]
    public async Task ThanNem_ThiVanTraKhung()
    {
        var (temp, services, vm, id) = DungVm("khung-nem@mail.com");
        using var db = temp;
        var soLanTra = 0;
        services.AcquireAccountLease = (l, ct) => Task.FromResult(new OrdersLeaseResult(true, null));
        services.ReleaseAccountLease = l => Task.CompletedTask;
        services.XinKhungKm = (l, ct) => Task.FromResult(new KmKhungResult(true, null));
        services.TraKhungKm = l => { soLanTra++; return Task.CompletedTask; };

        await vm.ChayLatCatAsync(
            tenViec: "KM của Shop",
            cauBatDau: "bắt đầu",
            trangThaiBatDau: "đang chạy",
            than: (phien, login, ct) => throw new InvalidOperationException("hỏng giữa chừng"),
            dongCuaSoKhiXong: true,
            accountIdChiDinh: id,
            xinKhungXuyenMay: true);

        Assert.Equal(1, soLanTra);
    }

    /// <summary>Lát cắt KHÔNG bật cờ (nút "Mở list shop") ⇒ TUYỆT ĐỐI không nhờ hub dừng máy khác. Bắt máy khác
    /// cắt vòng check đơn để phục vụ một lượt xem vài giây là quá đắt.</summary>
    [StaFact]
    public async Task KhongBatCo_ThiKhongDungMayKhac()
    {
        var (temp, services, vm, id) = DungVm("khung-tat@mail.com");
        using var db = temp;
        var soLanHoi = 0;
        var soLanTra = 0;
        services.AcquireAccountLease = (l, ct) => Task.FromResult(new OrdersLeaseResult(true, null));
        services.ReleaseAccountLease = l => Task.CompletedTask;
        services.XinKhungKm = (l, ct) => { soLanHoi++; return Task.FromResult(new KmKhungResult(true, null)); };
        services.TraKhungKm = l => { soLanTra++; return Task.CompletedTask; };

        await vm.ChayLatCatAsync(
            tenViec: "Mở list shop",
            cauBatDau: "bắt đầu",
            trangThaiBatDau: "đang chạy",
            than: (phien, login, ct) => Task.FromResult(new AccountsViewModel.KetLuanLatCat("xong", "xong")),
            accountIdChiDinh: id);

        Assert.Equal(0, soLanHoi);
        Assert.Equal(0, soLanTra);
    }

    /// <summary>Giá trị mặc định của hạn/nhịp — khoá lại vì test tự rút chúng xuống, và một lượt "dọn dẹp" sau
    /// này có thể đổi mà không ai thấy.</summary>
    /// <summary>
    /// Khoá hai HẰNG mặc định trong mã sản phẩm. Cố ý đọc <c>MacDinh*</c> (hằng <c>readonly</c>, test KHÔNG bao
    /// giờ đụng) chứ không đọc hai biến đang dùng — hai biến đó bị chính ctor của lớp này rút xuống, nên assert
    /// vào chúng là tự khẳng định thứ mình vừa đặt.
    /// </summary>
    [Fact]
    public void MacDinh_HanVaNhip_DungNhuChot()
    {
        Assert.Equal(TimeSpan.FromMinutes(3), AccountsViewModel.MacDinhHanChoKhung);
        Assert.Equal(TimeSpan.FromSeconds(10), AccountsViewModel.MacDinhNhipHoiKhung);
    }
}
