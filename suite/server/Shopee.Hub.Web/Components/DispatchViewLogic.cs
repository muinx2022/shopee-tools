using Shopee.Core.Coordination;
using Shopee.Hub;
using Shopee.Hub.Web.Services;

namespace Shopee.Hub.Web.Components;

internal sealed record DispatchKpiCard(string Key, string Icon, string Tone, string Label);

/// <summary>Một dòng bảng chi tiết KPI "đang chạy" / "đang chờ": việc gì · shop nào · máy nào · từ lúc nào.
/// <c>Manual</c> = việc chạy TAY trên app client (chỉ có lease, KHÔNG có assignment) → hub không huỷ được,
/// <c>AsnId</c> rỗng. Tên shop/tài khoản đã tra sẵn ở đây để markup khỏi đụng GUID.
/// <para>Dòng phiên ĐƠN HÀNG (<c>Op</c> = <c>orders</c>) cũng nằm chung hai bảng này nhưng không phải
/// assignment: <c>AsnId</c> rỗng và huỷ bằng LỆNH tới máy (xem <c>Dispatch.OnOrdersStop</c>) → cần
/// <c>MachineId</c>/<c>Login</c> làm đích lệnh. Hai field đó RỖNG với mọi dòng BigSeller.</para>
/// <para><c>LatCat</c> (chỉ dòng Đơn hàng): tên LÁT CẮT KM/Combo/Flash/Mở-list-shop máy đang chạy cho tài khoản
/// đó — rỗng với mọi dòng khác. Có giá trị ⇒ dòng vào nhóm "đang chạy" dù phiên check đơn đã dừng, và cột "Việc"
/// hiện tên lát cắt thay cho nhãn chung "Đơn hàng" (xem <see cref="DispatchViewLogic.WorkLabel"/>).</para>
/// <para>public vì đây là kiểu THAM SỐ của <c>DispatchKpiPanel</c> (component Razor sinh ra là class public).</para></summary>
public sealed record DispatchWorkItem(
    string Key, string AsnId, string Op, string ShopName, string AcctName, string Host,
    DateTimeOffset Since, string Rows, bool Manual, string MachineId = "", string Login = "",
    string LatCat = "");

/// <summary>Trạng thái 1 nút hành động trên lưới trang Giao việc — DÙNG CHUNG cho cả tab BigSeller (ô shop × op)
/// lẫn tab Đơn hàng (dòng tài khoản). <c>Act</c> = việc nút làm khi bấm: "" = KHÔNG bấm được (render kèm thuộc
/// tính <c>disabled</c>), "run" = tạo assignment / gửi lệnh chạy, "cancel" = huỷ việc đang mở / dừng phiên.
/// <para><see cref="Note"/> = LÝ DO lượt chạy TRƯỚC của ô này hỏng (rỗng = lượt trước không hỏng). Ô chỉ hiện
/// "✘ lỗi (máy)" trong 3 phút rồi rơi về "· chờ" (xem FleetStateService.OpCell) — không có dòng này thì người
/// vận hành thấy job "chạy tí rồi dừng, không rõ vì sao". Nguồn: Snap.Interrupted (giữ 7 ngày).</para></summary>
internal sealed record OpBtn(string Label, string Act, string Css, string Title, string Note = "")
{
    public bool Disabled => Act.Length == 0;
}

internal static class DispatchViewLogic
{
    public const string OrdersOperation = AssignmentOps.Orders;

    public static readonly IReadOnlyList<DispatchKpiCard> KpiCards =
    [
        new("machines", "machines", "green", "Máy online"),
        new("running", "run", "blue", "Việc đang chạy"),
        new("queued", "clock", "amber", "Việc chờ"),
        new("interrupted", "alert", "red", "Việc gián đoạn"),
    ];

    public static bool IsKpiKey(string key) => KpiCards.Any(card => card.Key == key);

    public static int KpiCount(string key, int machines, int running, int queued, int interrupted) => key switch
    {
        "machines" => machines,
        "running" => running,
        "queued" => queued,
        "interrupted" => interrupted,
        _ => 0,
    };

    public static string KpiPanelTitle(string key, int machines, int running, int queued, int interrupted) => key switch
    {
        "machines" => $"🖥 Máy online ({machines})",
        "running" => $"▶ Việc đang chạy ({running})",
        "queued" => $"⏳ Việc chờ ({queued})",
        "interrupted" => $"⚠ Việc gián đoạn ({interrupted})",
        _ => "",
    };

    public static string SinceText(DispatchWorkItem work, bool running)
    {
        var ago = FleetStateService.Ago(work.Since);
        if (ago.Length == 0) return "—";
        return work.Op == OrdersOperation ? $"cập nhật {ago}" : $"{(running ? "chạy từ" : "xếp lúc")} {ago}";
    }

    public static (bool Close, int EmptyTicks) AdvanceEmptyKpiPanel(string selectedKey, int count, int emptyTicks)
    {
        if (selectedKey.Length == 0 || count > 0) return (false, 0);
        var next = emptyTicks + 1;
        return (next >= 2, next >= 2 ? 0 : next);
    }

    public static string OrdersStateCss(string state) => state switch
    {
        OrdersSessionStates.Running or OrdersSessionStates.Opening => "run",
        OrdersSessionStates.Queued => "queued",
        OrdersSessionStates.Stopping => "warn",
        _ => "idle",
    };

    public static string OrdersStateLabel(string state) => state switch
    {
        OrdersSessionStates.Running => "▶ Đang chạy",
        OrdersSessionStates.Opening => "▶ Đang mở",
        OrdersSessionStates.Queued => "⏱ Chờ đến lượt",
        OrdersSessionStates.Stopping => "■ Đang dừng",
        _ => "— Dừng",
    };

    /// <summary>Nhãn NGƯỜI ĐỌC của một hành động lệnh Đơn hàng. Dùng ở nút, ở dòng "⏳ đã gửi lệnh …" và ở
    /// thông báo kết quả — một nguồn, để nút và câu báo không bao giờ nói hai tên khác nhau.
    /// <para><c>stop</c> (tự động hoá khung xuyên máy) và <c>stop-all</c> (nút của người dùng) là HAI hành động
    /// khác nhau, nhưng người dùng chỉ THẤY <c>stop-all</c> trên nút — nên cả hai mang cùng NHÃN "✖ Dừng" (từ
    /// 04/09/2026 <c>stop-all</c> chỉ dừng ĐÚNG một tài khoản, không còn là "tất cả"). Xem
    /// <see cref="OrdersCommandActions.Stop"/>/<see cref="OrdersCommandActions.StopAll"/>.</para></summary>
    public static string OrdersActionLabel(string action) => action switch
    {
        OrdersCommandActions.Run => "▶ Chạy",
        OrdersCommandActions.Stop => "✖ Dừng",
        OrdersCommandActions.StopAll => "✖ Dừng",
        OrdersCommandActions.RunKm => "▶ KM",
        OrdersCommandActions.RunCombo => "▶ Combo",
        OrdersCommandActions.RunFlash => "▶ Flash",
        _ => action,
    };

    // ── LÁT CẮT đang chạy (KM · Combo · Flash Sale · Mở list shop) ───────────────
    // Gương mang thêm tên lát cắt vì lát cắt DỪNG mọi phiên check đơn để mượn hồ sơ Brave ⇒ `SessionState` rỗng.
    // Bốn hàm dưới đây là toàn bộ luật hiển thị của nó; Razor chỉ gọi, không tự suy diễn (bUnit chưa có —
    // P-20260904-13 — nên đây là tầng DUY NHẤT test chạm được).

    /// <summary>Tài khoản này có ĐANG BẬN không: còn phiên check đơn HOẶC máy đang giữ một lát cắt cho nó. Đây là
    /// cửa quyết nút chính của dòng là "✖ Dừng" hay "▶ Chạy" — thiếu vế lát cắt thì hub mời người vận hành bấm
    /// "▶ Chạy" giữa lúc gói Flash Sale đang chạy, và cú bấm đó ép chính gói ấy nhường chỗ.</summary>
    public static bool OrdersDangBan(string? sessionState, string? latCat)
        => !string.IsNullOrEmpty(sessionState) || !string.IsNullOrEmpty(latCat);

    /// <summary>Ký hiệu đứng trước tên lát cắt. So theo TÊN VIỆC client gửi lên (hằng ở
    /// <c>AccountsViewModel.ChayLatCatAsync</c>) chứ không theo mã — hai bên không thấy nhau, nên tên lạ phải rơi
    /// về "▶" thay vì mất chữ.</summary>
    public static string LatCatIcon(string? latCat) => latCat switch
    {
        null or "" => "",
        var s when s.Contains("Flash", StringComparison.OrdinalIgnoreCase) => "⚡",
        var s when s.Contains("Combo", StringComparison.OrdinalIgnoreCase) => "📦",
        var s when s.Contains("KM", StringComparison.Ordinal) => "🏷",
        _ => "▶",
    };

    /// <summary>Ô "Trạng thái" của một dòng tài khoản: lát cắt THẮNG trạng thái phiên (phiên lúc đó đã bị chính
    /// lát cắt dừng, hiện "— Dừng" ở đó là nói ngược sự thật).</summary>
    public static string OrdersTrangThaiLabel(string state, string? latCat)
        => string.IsNullOrEmpty(latCat) ? OrdersStateLabel(state) : $"{LatCatIcon(latCat)} {latCat}";

    /// <summary>Màu ô "Trạng thái" — dòng đang giữ lát cắt tô như đang chạy.</summary>
    public static string OrdersTrangThaiCss(string state, string? latCat)
        => string.IsNullOrEmpty(latCat) ? OrdersStateCss(state) : "run";

    /// <summary>Nhãn cột "Việc" của một dòng bảng KPI: dòng Đơn hàng đang giữ lát cắt nói TÊN LÁT CẮT, còn lại
    /// dùng nhãn chung của loại việc.</summary>
    public static string WorkLabel(DispatchWorkItem w)
        => w.LatCat.Length > 0 ? $"{LatCatIcon(w.LatCat)} {w.LatCat}" : FleetViewProjection.OperationLabel(w.Op);

    /// <summary>Lát cắt đang chạy TRÊN MÁY — quét MỌI tài khoản của máy đó, trả (tên lát cắt, login đang giữ);
    /// <c>null</c> = máy rảnh.
    /// <para>Một máy = MỘT lát cắt tại một thời điểm (cùng hồ sơ Brave, cùng cổng cầu nối 47821), nên ba nút
    /// ▶ KM/Combo/Flash của <b>mọi</b> dòng trên máy đó phải mờ, không riêng dòng đang chạy. Không có luật này
    /// thì người vận hành bấm rồi chờ 5–12s mới biết bị từ chối qua ack.</para></summary>
    public static (string LatCat, string Login)? LatCatCuaMay(IEnumerable<OrdersMirrorAccount> accounts)
    {
        foreach (var a in accounts)
        {
            if (!string.IsNullOrEmpty(a.LatCat)) return (a.LatCat!, a.Login);
        }
        return null;
    }

    /// <summary>Cảnh báo cho nút "▶ Chạy" của một dòng tài khoản KHÔNG đang bận (nên nút vẫn là "▶ Chạy", không
    /// phải "✖ Dừng") trong khi MÁY thì đang giữ lát cắt cho một tài khoản KHÁC (phản biện 04/09/2026, VỪA).
    /// <para>Cú bấm ấy vẫn tới <c>LenhHubChayCheckDon</c>, và cửa nhường ở đó đọc cờ TOÀN MÁY
    /// (<c>DangChayFlashHoacCauNoi</c>) — tức vẫn ép đúng lát cắt đang chạy nhường chỗ sau shop hiện tại, y hệt
    /// lỗi gốc đợt này sửa, chỉ đổi dòng bấm. Không chặn nút (đổi thành chặn là quyết định khác, cần user chốt)
    /// — chỉ nói trước hậu quả trong tooltip.</para>
    /// <c>null</c> khi máy rảnh, hoặc lát cắt đang chạy đúng là của <paramref name="login"/> (khi đó
    /// <see cref="OrdersDangBan"/> đã true và nút là "✖ Dừng" rồi, không đi tới đây).</summary>
    public static string? CanhBaoChayDeNhuong(string login, (string LatCat, string Login)? mayDangGiu)
        => mayDangGiu is { } m && !string.Equals(m.Login, login, StringComparison.OrdinalIgnoreCase)
            ? $"⚠ Máy đang chạy {m.LatCat} cho {m.Login} — bấm Chạy sẽ ép lượt đó NHƯỜNG CHỖ sau shop hiện tại."
            : null;
}
