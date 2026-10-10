using Shopee.Core.Coordination;

namespace Shopee.Hub;

/// <summary>
/// <b>BẮT TAY "nhường khung" giữa luồng KM và luồng check đơn — XUYÊN MÁY.</b>
/// <para><b>Vấn đề gốc:</b> khoá tài khoản <c>orders:&lt;login&gt;</c> được phiên check đơn xin MỘT LẦN trước
/// vòng lặp và chỉ nhả khi phiên dừng hẳn — kỳ nghỉ giữa hai vòng nằm TRONG vòng lặp và vẫn heartbeat. Nên máy
/// chạy KM lúc 2h sáng không tài nào xin được khoá nếu máy khác đang chạy check đơn cho cùng tài khoản, và
/// <c>ReserveAccounts</c> thì TỪ CHỐI chứ không cướp (khoá tài khoản cố ý không có cờ <c>Force</c>).</para>
/// <para><b>Cách giải, và vì sao HUB quyết chứ không phải client:</b> client chỉ nói "tôi cần khung cho login
/// này", hub tự tra ai đang giữ khoá rồi tự tạo lệnh Dừng cho đúng máy đó. Cho client tuỳ ý ra lệnh cho client
/// khác là mở một quyền rộng và khó thu lại; ở đây hub chỉ cho phép đúng một việc hẹp — dừng đúng máy đang giữ
/// đúng khoá đó — và tự ghi sổ để còn trả lại.</para>
/// <para><b>HUB GIỮ SỔ, KHÔNG PHẢI CLIENT.</b> Máy chủ trì tắt/mất điện giữa lượt KM là chuyện sẽ xảy ra; nếu
/// danh sách "đã dừng ai" nằm trong bộ nhớ của nó thì máy kia nằm im tới khi người dùng tự bấm Chạy — đúng cái
/// hỏng mà việc này sinh ra để sửa. Bảng <c>km_paused</c> sống trên hub, và
/// <see cref="QuetTraKhungQuaHan"/> là lưới cuối: quá hạn thì hub tự bật lại, không cần ai còn sống.</para>
/// </summary>
public sealed partial class HubDatabase
{
    /// <summary>Tiền tố khoá tài khoản của module Đơn hàng — phải khớp <c>OrdersModuleHost.LeaseKeyPrefix</c>
    /// phía client, vì đó là chuỗi thật nằm trong cột <c>account_leases.account_id</c>.</summary>
    private const string KhoaDonHangPrefix = "orders:";

    /// <summary>
    /// HẠN MẶC ĐỊNH của một lượt mượn khung khi client không nói rõ. 90 phút = vòng 12 shop (~50') cộng đệm.
    /// <para>Đây là <b>lưới an toàn</b>, không phải mốc nghiệp vụ: quá hạn thì hub tự trả khung cho máy bị dừng,
    /// kể cả khi máy chủ trì đã tắt. Đặt ngắn hơn thì lượt KM dài bị bật check đơn vào giữa chừng (hai luồng
    /// tranh một hồ sơ Brave); đặt dài hơn thì máy bị dừng nằm im lâu hơn cần thiết.</para>
    /// </summary>
    public TimeSpan HanMuonKhungMacDinh { get; init; } = TimeSpan.FromMinutes(90);

    private void EnsureKmKhungSchema() => ExecRaw(@"
CREATE TABLE IF NOT EXISTS km_paused(
  login TEXT NOT NULL COLLATE NOCASE, machine_id TEXT NOT NULL,
  paused_at TEXT, deadline TEXT,
  PRIMARY KEY(login, machine_id));");

    /// <summary>
    /// <b>XIN KHUNG cho một lượt KM.</b> Trả <c>Trong = true</c> nghĩa là khoá đang trống (hoặc chính máy xin
    /// đang giữ) ⇒ client cứ việc xin khoá và chạy.
    /// <para>Máy KHÁC đang giữ ⇒ tạo lệnh <c>stop</c> cho đúng máy đó (idempotent sẵn trong
    /// <see cref="CreateOrdersCommand"/>), ghi một dòng <c>km_paused</c>, và trả <c>Trong = false</c> kèm tên máy
    /// để client biết mình đang chờ ai. Client gọi lại sau vài giây; khi khoá trống thì lượt gọi kế trả
    /// <c>true</c> mà KHÔNG tạo thêm lệnh nào.</para>
    /// <para><b>Dòng <c>km_paused</c> ghi TRƯỚC khi lệnh có tác dụng</b> — cố ý. Nếu ghi sau, một lượt hub restart
    /// đúng giữa hai bước sẽ để máy kia bị dừng mà không ai nhớ phải bật lại. Ghi thừa thì vô hại:
    /// <see cref="TraKhung"/> và <see cref="QuetTraKhungQuaHan"/> đều chỉ tạo lệnh <c>run</c>, mà lệnh <c>run</c>
    /// trên một phiên đang chạy là no-op ("đã chạy sẵn", xem <c>ExecuteOrdersCommand</c>).</para>
    /// </summary>
    /// <param name="login">Tài khoản Shopee (email) cần khung.</param>
    /// <param name="machineId">Id suất đơn hàng của máy XIN — dùng để không tự dừng chính mình.</param>
    /// <param name="han">Hạn tự trả khung; <c>null</c> ⇒ <see cref="HanMuonKhungMacDinh"/>.</param>
    public KmXinKhungResponse XinKhung(string login, string machineId, TimeSpan? han = null)
    {
        login = login?.Trim() ?? "";
        machineId = machineId?.Trim() ?? "";
        if (login.Length == 0 || machineId.Length == 0)
        {
            return new KmXinKhungResponse(false, "", "thiếu login hoặc id máy");
        }

        var khoa = KhoaDonHangPrefix + login.ToLowerInvariant();
        var giu = ActiveAccountLeases()
            .FirstOrDefault(l => string.Equals(l.AccountId, khoa, StringComparison.OrdinalIgnoreCase));

        // Trống, hoặc chính ta đang giữ ⇒ đi tiếp. KHÔNG tạo lệnh, KHÔNG ghi sổ.
        if (giu is null || string.Equals(giu.MachineId, machineId, StringComparison.Ordinal))
        {
            return new KmXinKhungResponse(true, "", null);
        }

        var now = DateTimeOffset.UtcNow;
        GhiSoDaDung(login, giu.MachineId, now, now + (han ?? HanMuonKhungMacDinh));
        CreateOrdersCommand(giu.MachineId, login, OrdersCommandActions.Stop);

        var ten = giu.Hostname is { Length: > 0 } h ? h : giu.MachineId;
        return new KmXinKhungResponse(false, ten, null);
    }

    /// <summary>
    /// <b>TRẢ KHUNG sau khi lượt KM kết thúc</b> — tạo lệnh <c>run</c> cho ĐÚNG các máy mà lượt mượn này đã
    /// dừng, rồi xoá sổ. Không có dòng nào ⇒ no-op, trả 0.
    /// <para>Client phải gọi hàm này ở <b>mọi</b> đường ra của lượt KM (xong · lỗi · huỷ), y luật "mọi đường
    /// cửa sổ biến mất phải đi qua TraHoSo" phía client. Quên một đường là máy kia nằm im tới hạn quét.</para>
    /// </summary>
    /// <returns>Số máy đã được ra lệnh chạy lại.</returns>
    public int TraKhung(string login)
    {
        login = login?.Trim() ?? "";
        if (login.Length == 0) { return 0; }

        List<string> may;
        lock (_gate)
        {
            may = DocMayDaDungLocked(login);
            if (may.Count == 0) { return 0; }

            using var d = _conn.CreateCommand();
            d.CommandText = "DELETE FROM km_paused WHERE login=$l";
            d.Parameters.AddWithValue("$l", login);
            d.ExecuteNonQuery();
        }

        // Tạo lệnh NGOÀI `_gate`: CreateOrdersCommand tự khoá, gọi lồng là tự khoá chính mình.
        foreach (var m in may)
        {
            CreateOrdersCommand(m, login, OrdersCommandActions.Run);
        }
        return may.Count;
    }

    /// <summary>
    /// <b>LƯỚI CUỐI — hub tự trả khung cho mọi dòng QUÁ HẠN.</b> Chạy định kỳ, không cần máy chủ trì còn sống.
    /// <para>Đây là thứ cứu ca máy chủ trì tắt/mất điện giữa lượt KM. Thiếu nó thì máy bị dừng nằm im tới khi
    /// người dùng tự bấm Chạy — đúng hỏng mà việc này sinh ra để sửa, chỉ khác nguyên nhân.</para>
    /// </summary>
    /// <param name="bayGio">Mốc "bây giờ" (UTC) — nhận từ ngoài để test được mọi ca biên.</param>
    /// <returns>Số máy đã được ra lệnh chạy lại.</returns>
    public int QuetTraKhungQuaHan(DateTimeOffset bayGio)
    {
        var quaHan = new List<(string Login, string MachineId)>();
        lock (_gate)
        {
            using (var q = _conn.CreateCommand())
            {
                q.CommandText = "SELECT login, machine_id, deadline FROM km_paused";
                using var rd = q.ExecuteReader();
                while (rd.Read())
                {
                    if (D(rd, 2) <= bayGio) { quaHan.Add((S(rd, 0), S(rd, 1))); }
                }
            }
            if (quaHan.Count == 0) { return 0; }

            foreach (var (login, may) in quaHan)
            {
                using var d = _conn.CreateCommand();
                d.CommandText = "DELETE FROM km_paused WHERE login=$l AND machine_id=$m";
                d.Parameters.AddWithValue("$l", login);
                d.Parameters.AddWithValue("$m", may);
                d.ExecuteNonQuery();
            }
        }

        foreach (var (login, may) in quaHan)
        {
            CreateOrdersCommand(may, login, OrdersCommandActions.Run);
        }
        return quaHan.Count;
    }

    /// <summary>Các máy đang bị dừng vì lượt KM của <paramref name="login"/> — cho trang điều phối xem.</summary>
    public List<string> MayDangBiDungViKm(string login)
    {
        login = login?.Trim() ?? "";
        if (login.Length == 0) { return new List<string>(); }
        lock (_gate) { return DocMayDaDungLocked(login); }
    }

    private List<string> DocMayDaDungLocked(string login)
    {
        var list = new List<string>();
        using var c = _conn.CreateCommand();
        c.CommandText = "SELECT machine_id FROM km_paused WHERE login=$l";
        c.Parameters.AddWithValue("$l", login);
        using var rd = c.ExecuteReader();
        while (rd.Read()) { list.Add(S(rd, 0)); }
        return list;
    }

    /// <summary>Ghi sổ "máy này bị dừng vì KM của login kia". UPSERT: xin khung nhiều lượt không đẻ dòng thừa,
    /// và mỗi lượt xin gia hạn luôn deadline (lượt KM còn đang chờ khoá thì chưa nên bị quét trả).</summary>
    private void GhiSoDaDung(string login, string machineId, DateTimeOffset luc, DateTimeOffset han)
    {
        lock (_gate)
        {
            using var c = _conn.CreateCommand();
            c.CommandText = @"
INSERT INTO km_paused(login, machine_id, paused_at, deadline) VALUES($l,$m,$p,$d)
ON CONFLICT(login, machine_id) DO UPDATE SET deadline=$d;";
            c.Parameters.AddWithValue("$l", login);
            c.Parameters.AddWithValue("$m", machineId);
            c.Parameters.AddWithValue("$p", Iso(luc));
            c.Parameters.AddWithValue("$d", Iso(han));
            c.ExecuteNonQuery();
        }
    }
}
