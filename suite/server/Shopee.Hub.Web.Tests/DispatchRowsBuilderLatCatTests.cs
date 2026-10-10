using Shopee.Core.Coordination;
using Shopee.Hub;
using Shopee.Hub.Web.Components;

namespace Shopee.Hub.Web.Tests;

/// <summary>
/// <see cref="DispatchRowsBuilder.BuildWorkLists"/> gộp dòng lát cắt (KM/Combo/Flash/Mở-list-shop) vào bảng
/// "đang chạy" — plan <c>plans/2026-09-04-guong-mang-lat-cat-dang-chay.md</c> mục 3 bước 5, tiêu chí N5(a).
/// Trước bản vá, dòng chỉ xét theo <see cref="OrdersRunningAccount.SessionState"/>: một lát cắt đã dừng phiên
/// check đơn để mượn hồ sơ Brave nên chuỗi đó rỗng ⇒ dòng KHÔNG vào bảng, người vận hành không thấy nút ✖ Dừng.
/// </summary>
public sealed class DispatchRowsBuilderLatCatTests
{
    private const string MachineId = "m1";

    private static FleetSnapshot SnapWithOnlineMachine(string machineId = MachineId) => new()
    {
        Machines = [new MachinePresence { MachineId = machineId, Hostname = "PC-01", LastSeen = DateTimeOffset.Now }],
    };

    private static OrdersRunningAccount Row(string login, string sessionState = "", string? latCat = null, string machineId = MachineId) =>
        new(machineId, "PC-01", login, sessionState, DateTimeOffset.Now) { LatCat = latCat };

    /// <summary>(a) CA CHÍNH: state rỗng (lát cắt đã dừng phiên) + LatCat "Flash Sale" ⇒ dòng vẫn vào nhóm
    /// "đang chạy", mang đúng Login/LatCat để cột "Việc" nói tên lát cắt (xem <c>WorkLabel</c>).</summary>
    [Fact]
    public void StateRong_CoLatCat_VaoNhomRunning()
    {
        var (running, queued) = DispatchRowsBuilder.BuildWorkLists(
            SnapWithOnlineMachine(), [], [Row("a@mail.com", latCat: "Flash Sale")]);

        var item = Assert.Single(running);
        Assert.Equal("a@mail.com", item.Login);
        Assert.Equal("Flash Sale", item.LatCat);
        Assert.Empty(queued);
    }

    /// <summary>Chống ĐẾM ĐÚP: khoảnh khắc lát cắt còn chờ phiên check đơn dừng hẳn, dòng có CẢ SessionState
    /// KHÁC RỖNG lẫn LatCat khác rỗng — vẫn phải ra ĐÚNG MỘT dòng, không phải hai (một do nhánh LatCat, một do
    /// switch SessionState).</summary>
    [Fact]
    public void CoCaHaiTruong_ChiMotDongDuyNhat()
    {
        var (running, _) = DispatchRowsBuilder.BuildWorkLists(
            SnapWithOnlineMachine(), [], [Row("a@mail.com", OrdersSessionStates.Running, "Flash Sale")]);

        Assert.Single(running);
    }

    /// <summary>Máy im nhịp ≥3' bị bỏ dù đang "giữ" một lát cắt theo gương — gương đứng nguyên ở lần đẩy cuối,
    /// máy tắt giữa lát cắt sẽ để lại một dòng "đang chạy" vĩnh viễn nếu không lọc theo máy offline.</summary>
    [Fact]
    public void MayOffline_DuCoLatCat_ThiKhongVaoBangNao()
    {
        var snapKhongMay = new FleetSnapshot();   // không máy nào trong Machines ⇒ MachineOffline trả true

        var (running, queued) = DispatchRowsBuilder.BuildWorkLists(
            snapKhongMay, [], [Row("a@mail.com", latCat: "Flash Sale")]);

        Assert.Empty(running);
        Assert.Empty(queued);
    }

    /// <summary>(c) không LatCat ⇒ hành vi cũ nguyên: state "queued" vẫn vào nhóm chờ, không bị nhánh LatCat mới
    /// nuốt mất hay đổi nhóm.</summary>
    [Fact]
    public void KhongLatCat_StateQueued_HanhViCuNguyen()
    {
        var (running, queued) = DispatchRowsBuilder.BuildWorkLists(
            SnapWithOnlineMachine(), [], [Row("a@mail.com", OrdersSessionStates.Queued)]);

        Assert.Empty(running);
        var item = Assert.Single(queued);
        Assert.Equal("", item.LatCat);
    }

    /// <summary>(c) không LatCat ⇒ state "running" vẫn vào nhóm chạy như trước bản vá.</summary>
    [Fact]
    public void KhongLatCat_StateRunning_HanhViCuNguyen()
    {
        var (running, _) = DispatchRowsBuilder.BuildWorkLists(
            SnapWithOnlineMachine(), [], [Row("a@mail.com", OrdersSessionStates.Running)]);

        Assert.Single(running);
    }
}
