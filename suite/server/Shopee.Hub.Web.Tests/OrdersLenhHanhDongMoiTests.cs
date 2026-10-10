using Shopee.Core.Coordination;
using Shopee.Hub;

namespace Shopee.Hub.Web.Tests;

/// <summary>
/// <b>Bốn hành động lệnh Đơn hàng MỚI</b> (<c>stop-all</c> · <c>run-km</c> · <c>run-combo</c> · <c>run-flash</c>,
/// 03/09/2026) đi qua ĐÚNG đường lệnh sẵn có — không đẻ bảng mới, không đẻ đường tạo lệnh thứ hai.
///
/// <para><b>Hai bất biến phải khoá:</b>
/// <list type="number">
/// <item><b>Idempotent theo (máy, login, hành động)</b> — bấm hai lần / hai tab chỉ ra MỘT lệnh đang mở. Không
/// thế thì một cú double-click là hai lượt phóng Brave nối đuôi trên cùng một hồ sơ;</item>
/// <item><b><c>stop-all</c> KHÔNG gộp với <c>stop</c></b>. Chúng là hai việc khác hẳn nhau: <c>stop</c> do
/// <c>HubDatabase.KmKhung</c> tự phát để nhường khung xuyên máy (chỉ dừng check đơn, app tự chạy lại ngay),
/// <c>stop-all</c> là nút của người dùng (dừng cả lượt đang chạy + huỷ lượt hoãn). Gộp là mỗi lượt nhường
/// khung lại giết đúng thứ vừa xin khung — và ngược lại, cú Dừng của người dùng bị nuốt vì hub đang có một
/// lệnh <c>stop</c> tự động còn mở.</item>
/// </list></para>
/// </summary>
public sealed class OrdersLenhHanhDongMoiTests : IDisposable
{
    private readonly string _dataDir =
        Path.Combine(Path.GetTempPath(), "hub-lenh-moi-" + Guid.NewGuid().ToString("N"));

    private const string May = "may-a:orders";
    private const string Login = "sub-x@shopee.vn";

    [Theory]
    [InlineData(OrdersCommandActions.StopAll)]
    [InlineData(OrdersCommandActions.RunKm)]
    [InlineData(OrdersCommandActions.RunCombo)]
    [InlineData(OrdersCommandActions.RunFlash)]
    public void HanhDongMoi_TaoLenhVaIdempotent(string action)
    {
        using var db = new HubDatabase(_dataDir);

        var id1 = db.CreateOrdersCommand(May, Login, action);
        var id2 = db.CreateOrdersCommand(May, Login, action);

        Assert.False(string.IsNullOrWhiteSpace(id1));
        Assert.Equal(id1, id2);

        var lenh = db.OrdersCommandsOf(May);
        Assert.Single(lenh);
        Assert.Equal(action, lenh[0].Action);
        Assert.Equal(OrdersCommandStatuses.Pending, lenh[0].Status);
    }

    /// <summary>Hai hành động KHÁC nhau ⇒ hai lệnh riêng: cửa idempotent bám (máy, login, HÀNH ĐỘNG), không
    /// phải (máy, login).</summary>
    [Fact]
    public void HaiHanhDongKhacNhau_LaHaiLenhRieng()
    {
        using var db = new HubDatabase(_dataDir);

        db.CreateOrdersCommand(May, Login, OrdersCommandActions.RunKm);
        db.CreateOrdersCommand(May, Login, OrdersCommandActions.RunFlash);

        var lenh = db.OrdersCommandsOf(May);
        Assert.Equal(2, lenh.Count);
        Assert.Contains(lenh, c => c.Action == OrdersCommandActions.RunKm);
        Assert.Contains(lenh, c => c.Action == OrdersCommandActions.RunFlash);
    }

    /// <summary>⚠ <c>stop</c> (tự động hoá khung) và <c>stop-all</c> (nút người dùng) KHÔNG được gộp — xem
    /// xmldoc lớp.</summary>
    [Fact]
    public void StopVaStopAll_KhongGopVaoMotLenh()
    {
        using var db = new HubDatabase(_dataDir);

        var idStop = db.CreateOrdersCommand(May, Login, OrdersCommandActions.Stop);
        var idStopAll = db.CreateOrdersCommand(May, Login, OrdersCommandActions.StopAll);

        Assert.NotEqual(idStop, idStopAll);
        Assert.Equal(2, db.OrdersCommandsOf(May).Count);
    }

    /// <summary>Lệnh mới đi TRỌN đường xuống client: nhịp heartbeat mang đúng chuỗi hành động (client so khớp
    /// bằng chuỗi này — sai một ký tự là nó rơi vào nhánh "chưa hỗ trợ", im lặng).</summary>
    [Fact]
    public void LenhMoi_DiTrongNhipHeartbeat_GiuNguyenChuoiHanhDong()
    {
        using var db = new HubDatabase(_dataDir);
        db.CreateOrdersCommand(May, Login, OrdersCommandActions.RunFlash);

        var traLoi = db.MachineHeartbeat(new MachineHeartbeatRequest(May, "MAY-A", "1.9.0", 0));

        var lenh = Assert.Single(traLoi.OrdersCommands);
        Assert.Equal(OrdersCommandActions.RunFlash, lenh.Action);
        Assert.Equal(Login, lenh.Login);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dataDir, true); } catch { /* thư mục tạm */ }
    }
}
