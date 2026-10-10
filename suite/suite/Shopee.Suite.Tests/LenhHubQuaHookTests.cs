// System.IO KHÔNG có trong danh sách implicit của SDK desktop (net8.0-windows).
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Shopee.Core.Coordination;
using Shopee.Suite.Infrastructure;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.Core.Models;

namespace Shopee.Suite.Tests;

/// <summary>
/// <b>CHỖ RẼ của <c>OrdersModuleHost.ExecuteOrdersCommand</c></b> — lệnh nào đi qua hook
/// <see cref="AppServices.LenhTuHub"/> (tức qua ViewModel, nơi giữ luật ƯU TIÊN FLASH) và lệnh nào KHÔNG.
///
/// <para><b>Vì sao phải đo ở đây:</b> bản trước gọi THẲNG <c>Sessions.Start</c> từ luồng poll heartbeat, tức đi
/// vòng qua mọi cửa từ chối của app — từ hub mở được phiên check đơn ĐÈ lên một gói Flash Sale đang chạy (cùng
/// hồ sơ Brave, cùng cổng cầu nối 47821; đo thật 03/09/2026). Mệnh đề cần khoá là <i>"năm hành động của người
/// dùng đi qua hook, <c>stop</c> thì KHÔNG"</i> — mà cả hai nhánh thật đều phóng trình duyệt, nên không bài
/// test nào chạm tới được nếu không tách chỗ rẽ ra đo riêng.</para>
///
/// <para><b><c>stop</c> phải giữ đường cũ</b>: nó là công cụ của TỰ ĐỘNG HOÁ khung xuyên máy
/// (<c>HubDatabase.KmKhung</c> phát Stop để máy đang giữ check đơn nhả khung, rồi phát Run trả lại). Cho nó đi
/// qua hook — nơi <c>stop-all</c> huỷ cả lượt đang chạy và đặt cờ "người dùng dừng tay" — là mỗi lần nhường
/// khung lại giết đúng thứ vừa xin khung.</para>
/// </summary>
public class LenhHubQuaHookTests : IDisposable
{
    private const string Login = "sub-hub@shopee.vn";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"suite_lenh_hub_{Guid.NewGuid():N}.db");
    private readonly AppServices _services;
    private readonly long _id;

    /// <summary>Mọi lượt gọi hook đã nhận — (hành động, id tài khoản, email).</summary>
    private readonly List<(string Action, long AccountId, string Email)> _daGoi = new();

    public LenhHubQuaHookTests()
    {
        _services = new AppServices(_dbPath);
        _services.Accounts.Insert(new Account { Email = Login, Password = "p" });
        _id = _services.Accounts.GetAll().First(a => a.Email == Login).Id;
    }

    private static OrdersCommandDto Lenh(string action) => new("cmd-1", Login, action);

    private void RotHook(string status = OrdersCommandStatuses.Done, string? loi = null)
        => _services.LenhTuHub = (action, id, email) =>
        {
            _daGoi.Add((action, id, email));
            return (status, loi);
        };

    // ══════════════════ 1. Hai bộ hằng ở hai lớp không thấy nhau phải KHỚP ══════════════════

    /// <summary>
    /// <c>XuLyDonShopee.App</c> KHÔNG tham chiếu <c>Shopee.Core</c>, nên hành động/trạng thái của lệnh hub tồn
    /// tại thành HAI bộ hằng cho MỘT khái niệm. Assembly này là nơi DUY NHẤT thấy được cả hai — lệch một ký tự
    /// là lệnh bay qua hook rồi rơi vào nhánh "chưa hỗ trợ", im lặng, chỉ hiện ra ở máy thật.
    /// </summary>
    [Fact]
    public void HaiBoHang_PhaiKhopTungChuoi()
    {
        Assert.Equal(OrdersCommandActions.Run, HanhDongLenhTuHub.Chay);
        Assert.Equal(OrdersCommandActions.StopAll, HanhDongLenhTuHub.DungTatCa);
        Assert.Equal(OrdersCommandActions.RunKm, HanhDongLenhTuHub.ChayKm);
        Assert.Equal(OrdersCommandActions.RunCombo, HanhDongLenhTuHub.ChayCombo);
        Assert.Equal(OrdersCommandActions.RunFlash, HanhDongLenhTuHub.ChayFlash);

        Assert.Equal(OrdersCommandStatuses.Done, KetQuaLenhTuHub.Done);
        Assert.Equal(OrdersCommandStatuses.Failed, KetQuaLenhTuHub.Failed);
    }

    // ══════════════════ 2. Năm hành động của người dùng ĐI QUA hook ══════════════════

    [Theory]
    [InlineData(OrdersCommandActions.Run)]
    [InlineData(OrdersCommandActions.StopAll)]
    [InlineData(OrdersCommandActions.RunKm)]
    [InlineData(OrdersCommandActions.RunCombo)]
    [InlineData(OrdersCommandActions.RunFlash)]
    public void LenhNguoiDung_DiQuaHook_DungHanhDongVaTaiKhoan(string action)
    {
        RotHook();

        var (status, loi) = OrdersModuleHost.ExecuteOrdersCommand(_services, Lenh(action));

        Assert.Equal(OrdersCommandStatuses.Done, status);
        Assert.Null(loi);
        Assert.Equal(new[] { (action, _id, Login) }, _daGoi);
    }

    /// <summary>Kết quả của hook đi THẲNG ra ack, không bị lớp dispatch "sửa lại cho đẹp": lý do từ chối
    /// (vd "ưu tiên flash") là dòng DUY NHẤT người vận hành đọc được trên hub.</summary>
    [Fact]
    public void KetQuaHook_DiThangRaAck()
    {
        RotHook(OrdersCommandStatuses.Failed, "Flash Sale đang chạy — ưu tiên flash, thử lại sau khi gói xong.");

        var (status, loi) = OrdersModuleHost.ExecuteOrdersCommand(_services, Lenh(OrdersCommandActions.RunKm));

        Assert.Equal(OrdersCommandStatuses.Failed, status);
        Assert.Equal("Flash Sale đang chạy — ưu tiên flash, thử lại sau khi gói xong.", loi);
    }

    /// <summary>Hook chưa rót (app đang khởi động, màn Tài khoản chưa dựng) ⇒ 'failed' KÈM LÝ DO, và tuyệt đối
    /// không rơi xuống một đường tắt nào khác.</summary>
    [Theory]
    [InlineData(OrdersCommandActions.Run)]
    [InlineData(OrdersCommandActions.StopAll)]
    [InlineData(OrdersCommandActions.RunKm)]
    [InlineData(OrdersCommandActions.RunCombo)]
    [InlineData(OrdersCommandActions.RunFlash)]
    public void HookChuaSanSang_ThiFailedKemLyDo(string action)
    {
        Assert.Null(_services.LenhTuHub);

        var (status, loi) = OrdersModuleHost.ExecuteOrdersCommand(_services, Lenh(action));

        Assert.Equal(OrdersCommandStatuses.Failed, status);
        Assert.Contains("chưa sẵn sàng", loi);
        Assert.Null(_services.Sessions.Get(_id));   // không phiên nào bị dựng ở đường tắt
    }

    // ══════════════════ 3. `stop` giữ ĐƯỜNG CŨ ══════════════════

    /// <summary>⚠ <c>stop</c> KHÔNG được đi qua hook — xem xmldoc lớp. Không có phiên nào chạy nên nó về
    /// 'done' "không có phiên nào đang chạy", đúng hành vi cũ.</summary>
    [Fact]
    public void Stop_KhongDiQuaHook()
    {
        RotHook();

        var (status, loi) = OrdersModuleHost.ExecuteOrdersCommand(_services, Lenh(OrdersCommandActions.Stop));

        Assert.Empty(_daGoi);
        Assert.Equal(OrdersCommandStatuses.Done, status);
        Assert.Equal("không có phiên nào đang chạy", loi);
    }

    [Theory]
    [InlineData(OrdersCommandActions.SyncOnce)]
    [InlineData(OrdersCommandActions.Relogin)]
    public void SyncOnceVaRelogin_GiuNguyenNhanhChuaHoTro(string action)
    {
        RotHook();

        var (status, loi) = OrdersModuleHost.ExecuteOrdersCommand(_services, Lenh(action));

        Assert.Empty(_daGoi);
        Assert.Equal(OrdersCommandStatuses.Failed, status);
        Assert.Contains(action, loi);
    }

    /// <summary>Máy này không có tài khoản đó ⇒ chặn TRƯỚC hook: hook nhận <c>accountId</c> cục bộ, gọi nó với
    /// một id không tồn tại là mở phiên cho nhầm tài khoản.</summary>
    [Fact]
    public void LoginLa_ChanTruocHook()
    {
        RotHook();

        var (status, loi) = OrdersModuleHost.ExecuteOrdersCommand(
            _services, new OrdersCommandDto("cmd-2", "ai-do@shopee.vn", OrdersCommandActions.RunFlash));

        Assert.Empty(_daGoi);
        Assert.Equal(OrdersCommandStatuses.Failed, status);
        Assert.Contains("ai-do@shopee.vn", loi);
    }

    public void Dispose()
    {
        _services.Log.Dispose();
        using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString()))
        {
            SqliteConnection.ClearPool(conn);
        }
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }
}
