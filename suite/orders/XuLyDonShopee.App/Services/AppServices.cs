using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.App.Services;

/// <summary>
/// SỐ HÀNG CÒN TỒN trong vòng chờ đẩy, tách theo 5 loại đích: <paramref name="Orders"/> đơn lên Hub ·
/// <paramref name="Slips"/> file phiếu lên Hub · <paramref name="SheetRows"/> dòng Google Sheet ·
/// <paramref name="SoldCounts"/> lượt +1 "Đã bán" · <paramref name="ReturnCodes"/> mã trả hàng chờ lên Google
/// Sheet (kho <c>return_codes</c> — sống ĐỘC LẬP với bảng <c>orders</c> nên phải đếm riêng, tài khoản đã dọn sạch
/// đơn vẫn có thể còn cả đống mã). <see cref="HubOutboxWorker"/> tính lại sau mỗi lượt; thanh trạng thái hiện
/// <see cref="Tong"/> khi &gt; 0. Loại nào KHÔNG có đích (hook hub chưa rót / URL sheet trống) được đếm 0 để badge
/// không kẹt số vĩnh viễn.
/// </summary>
public readonly record struct OutboxPending(
    int Orders = 0, int Slips = 0, int SheetRows = 0, int SoldCounts = 0, int ReturnCodes = 0)
{
    /// <summary>Tổng hàng còn chờ đẩy (cả 5 loại).</summary>
    public int Tong => Orders + Slips + SheetRows + SoldCounts + ReturnCodes;

    /// <summary>Cộng dồn số tồn của một tài khoản nữa vào tổng của cả lượt.</summary>
    public OutboxPending Cong(OutboxPending khac)
        => new(Orders + khac.Orders, Slips + khac.Slips, SheetRows + khac.SheetRows,
            SoldCounts + khac.SoldCounts, ReturnCodes + khac.ReturnCodes);
}

/// <summary>
/// Kết quả xin KHÓA CHẠY một tài khoản (chống hai máy cùng chạy một subaccount). <paramref name="Ok"/> = false ⇒
/// máy KHÁC đang chạy tài khoản này (phiên bỏ qua lượt, KHÔNG xếp hàng chờ); <paramref name="HolderMachine"/> =
/// tên máy đang giữ (null khi hub không nói được là máy nào — vẫn là "máy khác").
/// </summary>
public sealed record OrdersLeaseResult(bool Ok, string? HolderMachine);

/// <summary>
/// Kết quả xin KHUNG cho một lượt KM. <paramref name="Trong"/> = true ⇒ khoá tài khoản đang trống (hoặc chính
/// máy này giữ) ⇒ chạy được ngay. false ⇒ hub vừa ra lệnh Dừng cho <paramref name="DangDung"/> (tên máy, để ghi
/// nhật ký cho người đọc biết đang chờ ai) và chỗ gọi phải chờ rồi hỏi lại.
/// </summary>
public sealed record KmKhungResult(bool Trong, string? DangDung);

/// <summary>Một sub-acc Đơn hàng trong DANH BẠ GỘP toàn Hub (kéo về máy mới): <paramref name="Login"/> = email
/// đăng nhập, <paramref name="Shops"/> = shop con (login + tên), kèm 3 ô đăng nhập gộp từ mọi máy (từ
/// 11/08/2026 — rỗng nghĩa là chưa máy nào nhập ô đó). KHÔNG mang cookie.
/// Kiểu RIÊNG của module (module Đơn hàng KHÔNG tham chiếu <c>Shopee.Core</c> nên không lộ DTO hub vào đây).</summary>
public sealed record OrdersDirectoryItem(
    string Login,
    IReadOnlyList<(string Login, string Name)> Shops,
    string Password = "",
    string VerifyEmail = "",
    string VerifyEmailPassword = "");

/// <summary>
/// HAI giá trị <c>Status</c> hợp lệ mà <see cref="AppServices.LenhTuHub"/> được phép trả.
/// <para><b>Vì sao là hằng RIÊNG chứ không dùng thẳng <c>OrdersCommandStatuses</c> của <c>Shopee.Core</c>:</b>
/// module Đơn hàng KHÔNG tham chiếu <c>Shopee.Core</c> (xem mọi hook ở <see cref="AppServices"/>), nên hai lớp
/// không thấy nhau được. Ràng buộc "hai bên cùng một chuỗi" giữ bằng chú thích này + một test ở
/// <c>Shopee.Suite.Tests</c> — nơi DUY NHẤT thấy được cả hai.</para>
/// </summary>
public static class KetQuaLenhTuHub
{
    /// <summary>ĐÃ NHẬN + BẮT ĐẦU (không phải "đã chạy xong") — phải khớp <c>OrdersCommandStatuses.Done</c>.</summary>
    public const string Done = "done";

    /// <summary>KHÔNG làm được, kèm lý do người đọc hiểu — phải khớp <c>OrdersCommandStatuses.Failed</c>.</summary>
    public const string Failed = "failed";
}

/// <summary>
/// Bốn hành động NGƯỜI DÙNG bấm trên hub mà máy này thi hành qua <see cref="AppServices.LenhTuHub"/>.
/// Phải khớp <c>Shopee.Core.Coordination.OrdersCommandActions</c> — cùng ràng buộc, cùng cách giữ (chú thích +
/// test ở <c>Shopee.Suite.Tests</c>) như <see cref="KetQuaLenhTuHub"/>.
/// <para><c>stop</c> KHÔNG có ở đây và cố ý không đi qua hook: nó là công cụ của tự động hoá khung xuyên máy
/// (hub tự phát để nhường khung), chỉ dừng riêng check đơn.</para>
/// </summary>
public static class HanhDongLenhTuHub
{
    /// <summary>Mở phiên CHECK ĐƠN.</summary>
    public const string Chay = "run";

    /// <summary>Dừng MỌI hoạt động của ĐÚNG MỘT tài khoản: check đơn + lượt KM/Combo/Flash đang chạy CỦA nó +
    /// huỷ lượt hoãn của nó. KHÔNG đụng tài khoản khác trên cùng máy (người dùng chốt 04/09/2026). Chuỗi vẫn là
    /// <c>stop-all</c> ("dừng tất cả CỦA acc này"), khác <c>stop</c> (chỉ dừng phiên, cho tự động hoá khung).</summary>
    public const string DungTatCa = "stop-all";

    /// <summary>Chạy vòng "KM của Shop" bằng cấu hình đã lưu.</summary>
    public const string ChayKm = "run-km";

    /// <summary>Chạy vòng "Combo Khuyến Mãi" bằng cấu hình đã lưu.</summary>
    public const string ChayCombo = "run-combo";

    /// <summary>Chạy một gói "Flash Sale Của Shop" bằng cấu hình đã lưu.</summary>
    public const string ChayFlash = "run-flash";
}

/// <summary>Dòng thống kê dùng chung từ Hub cho tab "Thống kê".</summary>
public sealed record SharedStatBreakdown(string Label, int OrderCount, long Value, double Percentage);

/// <summary>Dòng shop thống kê dùng chung từ Hub.</summary>
public sealed record SharedShopStatRow(string Shop, int OrderCount, int ItemCount, long Revenue, long Average, double TrackingRate);

/// <summary>
/// Kết quả thống kê dùng chung từ Hub — CHỈ SỐ THÔ. Hub chạy giờ UTC và không biết múi giờ/định dạng của từng
/// máy nên KHÔNG dựng chuỗi hiển thị: màn Thống kê tự định dạng tiền/ngày/tỉ lệ.
/// <paramref name="ActiveOrders"/> = số đơn không hủy (mẫu số "TB / đơn hiệu lực");
/// <paramref name="LastSyncedUtc"/> = lần đẩy đơn lên hub gần nhất trong khoảng (null = khoảng đó không có đơn).
/// </summary>
public sealed record SharedOrderStatistics(
    int TotalOrders,
    int TotalItems,
    int NeedsAction,
    int Delivered,
    int Cancelled,
    long Revenue,
    long AverageOrder,
    int ActiveOrders,
    int WithTracking,
    int WithFinalAmount,
    DateTimeOffset? LastSyncedUtc,
    IReadOnlyList<SharedStatBreakdown> StatusRows,
    IReadOnlyList<SharedShopStatRow> ShopRows,
    IReadOnlyList<SharedStatBreakdown> CarrierRows,
    IReadOnlyList<SharedStatBreakdown> PaymentRows);

/// <summary>
/// Gom Database + các repository, khởi tạo một lần và truyền vào ViewModel.
/// (Bước đầu không dùng DI container.)
/// </summary>
public class AppServices
{
    public Database Database { get; }
    public AccountRepository Accounts { get; }
    public SettingsRepository Settings { get; }

    /// <summary>Kho đơn hàng đã sync (bảng <c>orders</c>) — phiên ghi qua đây khi Sync Đơn hàng.</summary>
    public OrdersRepository Orders { get; }

    /// <summary>Kho "Kết quả chuẩn bị hàng" theo shop/ngày (bảng <c>account_shops</c> + <c>prepare_daily</c>) —
    /// phiên cầu nối ghi qua callback (lưu shop + tăng đếm mỗi đơn arrange); tab "Shops" đọc để hiển thị.</summary>
    public ResultsRepository Results { get; }

    /// <summary>Cấu hình "KM của Shop" theo từng tài khoản (bảng <c>km_config</c>).</summary>
    public KmConfigRepository KmConfig { get; }

    /// <summary>Kho banner cảnh báo lỗi địa chỉ lấy hàng (bảng <c>pickup_address_alerts</c>) — bền tới khi bấm X;
    /// đồng bộ Hub theo tài khoản.</summary>
    public PickupAddressAlertsRepository PickupAlerts { get; }

    /// <summary>Kho MÃ YÊU CẦU TRẢ HÀNG (bảng <c>return_codes</c>) — sống ĐỘC LẬP với vòng đời đơn, nên mã của đơn
    /// đã bị dọn khỏi app vẫn đẩy được lên Google Sheet.</summary>
    public ReturnCodesRepository ReturnCodes { get; }

    /// <summary>Đẩy đơn (kèm file phiếu) lên Google Sheet qua Apps Script Web App — phiên gọi sau mỗi lượt Sync.</summary>
    public GoogleSheetSyncService GsheetSync { get; }

    /// <summary>Helper CHUNG báo "đơn mới" tới Slack / Discord / Telegram — phiên gọi sau mỗi lượt Sync có đơn mới.</summary>
    public OrderNotifyService Notify { get; }

    /// <summary>
    /// HOOK đẩy một LÔ đơn đã sync của một tài khoản lên HUB đơn hàng, do shell suite RÓT (module Đơn hàng
    /// KHÔNG tham chiếu <c>Shopee.Core</c> nên không tự biết hub). Tham số: <c>accountId</c>, lô đơn, <c>ct</c>;
    /// trả <c>true</c> = hub nhận OK → phiên đánh dấu <c>hub_synced_at</c> để không đẩy lại. Mặc định
    /// <c>null</c> = TẮT (app Đơn hàng chạy độc lập / hub chưa cấu hình → không đẩy, hành vi cũ y nguyên).
    /// Phiên gọi CHẠY NỀN sau mỗi lượt Sync (<see cref="AccountSession"/>).
    /// </summary>
    public Func<long, IReadOnlyList<SyncedOrder>, CancellationToken, Task<bool>>? PushOrdersToHub { get; set; }

    /// <summary>
    /// HOOK báo sự kiện lỗi app lên Hub (Hub quyết định gửi webhook). Tham số: kind (vd.
    /// <c>khong_dat_duoc_dia_chi</c>), account, shop, detail, machine, ct. Trả true = Hub nhận OK.
    /// Null = chưa nối Hub → caller gửi Slack local (fallback độc lập).
    /// </summary>
    public Func<string, string?, string?, string?, string?, CancellationToken, Task<bool>>? ReportAppAlertToHub { get; set; }

    /// <summary>
    /// HOOK upsert banner lỗi địa chỉ lên Hub (đồng bộ đa máy). Tham số: accountLogin, shopLogin, province, ct.
    /// Trả <c>rev</c> MỚI Hub vừa cấp; <c>null</c> = chưa đẩy được (giữ cờ chờ đẩy, lượt sync sau thử lại).
    /// Hook null = chưa nối Hub.
    /// </summary>
    public Func<string, string, string?, CancellationToken, Task<long?>>? UpsertPickupAlertToHub { get; set; }

    /// <summary>
    /// HOOK dismiss banner lỗi địa chỉ trên Hub. Tham số: accountLogin, shopLogin, ct.
    /// Trả <c>rev</c> MỚI; <c>null</c> = chưa đẩy được. Hook null = chưa nối Hub.
    /// </summary>
    public Func<string, string, CancellationToken, Task<long?>>? DismissPickupAlertToHub { get; set; }

    /// <summary>
    /// HOOK kéo danh sách banner lỗi địa chỉ từ Hub cho một tài khoản (kể cả đã dismiss — để merge).
    /// Trả null = không hỏi được Hub.
    /// </summary>
    public Func<string, CancellationToken, Task<IReadOnlyList<PickupAlertHubDong>?>>? FetchPickupAlertsFromHub { get; set; }

    /// <summary>
    /// HOOK +1 "Đã bán" theo SKU (khớp TUYỆT ĐỐI, MỌI shop) trên kho sản phẩm HUB (Postgres), do shell suite RÓT
    /// (module Đơn hàng KHÔNG tham chiếu <c>Shopee.Core</c> nên không tự biết hub). Tham số: danh sách SKU — mỗi đơn
    /// vừa CHUYỂN sang "đã giao" đóng góp 1 SKU đại diện (đơn trùng SKU → +N); <c>ct</c>. Trả <c>true</c> = hub +1 OK
    /// → phiên đánh cờ <c>sold_counted_at</c> cho các đơn tương ứng. Mặc định <c>null</c> = TẮT (app Đơn hàng chạy
    /// độc lập / hub chưa cấu hình → không +1, đơn giữ CHƯA đánh cờ để lượt sync sau thử lại). Phiên gọi CHẠY NỀN
    /// sau mỗi lượt Sync (<see cref="AccountSession"/>).
    /// </summary>
    public Func<IReadOnlyList<string>, CancellationToken, Task<bool>>? IncrementSoldBySku { get; set; }

    /// <summary>
    /// HOOK đẩy một LÔ file phiếu PDF (base64) của một tài khoản lên HUB đơn hàng, do shell suite RÓT (module Đơn
    /// hàng KHÔNG tham chiếu <c>Shopee.Core</c> nên không tự biết hub). Tham số: <c>accountId</c>, lô
    /// <c>(OrderSn, FileBase64)</c> (≤5), <c>ct</c>. Trả DANH SÁCH <c>order_sn</c> hub ĐÃ LƯU thành công (đơn
    /// <c>missing</c>/lỗi KHÔNG có mặt) → phiên đánh cờ <c>hub_slip_synced_at</c> đúng các đơn đó; trả <c>null</c> =
    /// hub lỗi CẢ LÔ (offline / route chưa có) → không mark, lượt sync sau thử lại. Mặc định <c>null</c> = TẮT (app
    /// Đơn hàng chạy độc lập / hub chưa cấu hình). Phiên gọi CHẠY NỀN sau mỗi lượt Sync (<see cref="AccountSession"/>).
    /// </summary>
    public Func<long, IReadOnlyList<(string OrderSn, string FileBase64)>, CancellationToken, Task<IReadOnlyList<string>?>>? PushOrderSlipsToHub { get; set; }

    /// <summary>
    /// HOOK đẩy cấu hình ĐỒNG BỘ GOOGLE SHEET (URL Web App + tab override + link file phụ) của máy này lên HUB, do
    /// shell suite RÓT (module Đơn hàng KHÔNG tham chiếu <c>Shopee.Core</c> nên không tự biết hub). Tham số:
    /// <c>url</c>, <c>tab</c> ("" = tự động theo tháng), <c>sheet2</c> ("" = không ghi file phụ), <c>ct</c>. Trả
    /// <c>true</c> = hub nhận OK → màn Cài đặt báo "đã đồng bộ lên Hub".
    /// Mặc định <c>null</c> = TẮT (app Đơn hàng chạy độc lập / hub chưa cấu hình → chỉ lưu local như cũ).
    /// Màn Cài đặt gọi sau khi LƯU local; hub CHỈ nhận khối GSheet khi URL khác trống nên máy chưa cấu hình không
    /// xoá được cấu hình của cả fleet.
    /// </summary>
    public Func<string, string, string, CancellationToken, Task<bool>>? PushGsheetConfigToHub { get; set; }

    /// <summary>
    /// HOOK đọc thống kê ĐƠN HÀNG dùng chung từ Hub theo khoảng + shop. Tham số: <c>fromUtc</c>,
    /// <c>toUtcExclusive</c> (hai MỐC UTC màn hình tính SẴN từ ngày địa phương người dùng — hub không suy diễn múi
    /// giờ; biên <c>[from, to)</c> y hệt bộ lọc <c>created_at</c> của kho đơn local), shop, <c>ct</c>. Trả
    /// <c>null</c> = hub không phản hồi / route chưa có, để màn GIỮ số local; trả object = số liệu CHUNG toàn hệ
    /// thống, mọi client cùng nhìn một ảnh chụp. Mặc định <c>null</c> = TẮT (app chạy độc lập / chưa nối Hub).
    /// </summary>
    public Func<DateTime, DateTime, string?, CancellationToken, Task<SharedOrderStatistics?>>? QueryOrderStatistics { get; set; }

    /// <summary>
    /// HOOK hỏi "máy này ĐÃ cấu hình Hub chưa" — do shell suite RÓT (module Đơn hàng KHÔNG tham chiếu
    /// <c>Shopee.Core</c> nên không tự biết hub). Các hook đọc/đẩy Hub được rót VÔ ĐIỀU KIỆN (chúng tự trả null khi
    /// chưa nối), nên riêng chúng KHÔNG phân biệt được "chưa cấu hình Hub" với "Hub chết" — màn Thống kê cần cờ này
    /// để đừng tố "Hub không phản hồi" trên máy vốn chạy độc lập.
    /// <para>Phải đọc TƯƠI mỗi lần gọi (người dùng có thể cấu hình Hub rồi kết nối lại giữa chừng). Mặc định
    /// <c>null</c> = KHÔNG BIẾT → bên gọi coi như CÓ hub, giữ nguyên hành vi cũ.</para>
    /// </summary>
    public Func<bool>? HubDaCauHinh { get; set; }

    /// <summary>
    /// HOOK đọc SỐ ĐƠN "chuẩn bị hàng" CHUNG TOÀN HỆ THỐNG theo shop trong MỘT ngày (tab "Shops"), do shell
    /// suite RÓT (module Đơn hàng KHÔNG tham chiếu <c>Shopee.Core</c> nên không tự biết hub). Tham số: <c>day</c>
    /// (<c>yyyy-MM-dd</c> giờ địa phương), <c>ct</c>; trả map <c>shop_login → số đơn</c>. Hub đếm THẲNG từ bảng đơn
    /// nên nhiều máy cùng chạy vẫn ra con số thật (không cộng trùng bộ đếm rời của từng máy). Khóa map so khớp
    /// KHÔNG phân biệt hoa/thường — bên NHẬN tự chuẩn hóa, không phụ thuộc comparer bên rót hook.
    /// <para><b>null KHÁC map rỗng:</b> <c>null</c> = KHÔNG hỏi được hub (chưa cấu hình / offline / hub cũ chưa có
    /// route) → màn GIỮ số cục bộ + ghi chú; map RỖNG = hub bảo ngày đó chưa shop nào có đơn (số 0 là số THẬT).
    /// Mặc định <c>null</c> = TẮT (app Đơn hàng chạy độc lập) → hành vi y như trước, số của máy.</para>
    /// </summary>
    public Func<string, CancellationToken, Task<IReadOnlyDictionary<string, int>?>>? QueryPrepareStats { get; set; }

    /// <summary>
    /// HOOK XIN KHÓA CHẠY một tài khoản trên HUB — chống hai máy cùng chạy MỘT subaccount (tranh đơn "chuẩn bị
    /// hàng", đăng nhập song song một tài khoản Shopee → đá phiên/ăn captcha), do shell suite RÓT (module Đơn hàng
    /// KHÔNG tham chiếu <c>Shopee.Core</c> nên không tự biết hub). Tham số: <b>login subaccount THÔ</b> (chưa thêm
    /// tiền tố, chưa hạ chữ — quy ước khóa do phía suite lo, module không phải biết), <c>ct</c>.
    /// <para><see cref="OrdersLeaseResult.Ok"/> = false ⇒ máy khác đang giữ → phiên KHÔNG mở trình duyệt, kết thúc
    /// êm (bỏ qua lượt, KHÔNG xếp hàng chờ). Hub chưa cấu hình / offline / lỗi ⇒ trả <c>Ok = true</c> (degrade như
    /// MỘT máy: mất hub thì cũng không phối hợp được với ai, chặn sẽ làm app vô dụng khi mất mạng).</para>
    /// Mặc định <c>null</c> = TẮT (app Đơn hàng chạy độc lập / hub chưa cấu hình) → phiên chạy y như trước.
    /// </summary>
    public Func<string, CancellationToken, Task<OrdersLeaseResult>>? AcquireAccountLease { get; set; }

    /// <summary>
    /// HOOK kéo DANH BẠ sub-acc Đơn hàng GỘP TỪ MỌI MÁY trên Hub (login + shop; KHÔNG mật khẩu/cookie), do shell
    /// suite RÓT (module Đơn hàng KHÔNG tham chiếu <c>Shopee.Core</c> nên không tự biết hub). Máy MỚI dùng để tạo
    /// sẵn bản ghi tài khoản rỗng-mật-khẩu; người dùng tự nhập mật khẩu rồi đăng nhập.
    /// <para><c>null</c> trả về = KHÔNG hỏi được Hub (chưa cấu hình / offline / hub cũ chưa có route) — khác list
    /// rỗng ("Hub chưa có tài khoản nào"). Mặc định property <c>null</c> = TẮT (app chạy độc lập / hub chưa cấu hình).</para>
    /// </summary>
    public Func<CancellationToken, Task<IReadOnlyList<OrdersDirectoryItem>?>>? QueryOrdersDirectory { get; set; }

    /// <summary>
    /// HOOK NHẢ khóa chạy tài khoản — cặp với <see cref="AcquireAccountLease"/>, tham số cũng là <b>login
    /// subaccount THÔ</b>. Phiên gọi ở MỌI lối ra (xong / lỗi / hủy) và ĐÚNG MỘT lần cho mỗi lần giành được.
    /// Nhả sót là lỗi nặng: tài khoản bị coi là "đang chạy ở máy X" tới khi lease hết hạn (~5') → máy khác không
    /// chạy được. Mặc định <c>null</c> = TẮT (app chạy độc lập / hub chưa cấu hình).
    /// </summary>
    public Func<string, Task>? ReleaseAccountLease { get; set; }

    /// <summary>
    /// HOOK <b>XIN KHUNG</b> cho một lượt KM — nhờ hub dừng phiên check đơn ở MÁY KHÁC đang giữ khoá tài khoản.
    /// Tham số: <b>login subaccount THÔ</b>, <c>ct</c>. Do shell suite rót (module Đơn hàng không biết hub).
    /// <para><b>Vì sao phải có, chứ khoá xuyên máy không đủ:</b> phiên check đơn xin khoá MỘT LẦN trước vòng lặp
    /// và chỉ nhả khi dừng hẳn — kỳ nghỉ giữa hai vòng nằm TRONG vòng lặp và vẫn heartbeat. Nên máy chạy KM lúc
    /// 2h sáng không bao giờ xin được khoá nếu máy khác đang chạy check đơn cho cùng tài khoản; và
    /// <c>ReserveAccounts</c> thì TỪ CHỐI chứ không cướp. Phải có ai đó bảo máy kia dừng — đó là việc của hub.</para>
    /// <para><see cref="KmKhungResult.Trong"/> = true ⇒ khoá đang trống (hoặc chính máy này giữ) ⇒ đi tiếp.
    /// false ⇒ hub VỪA ra lệnh Dừng cho <see cref="KmKhungResult.DangDung"/>; chỗ gọi chờ rồi hỏi lại.
    /// Hub chưa cấu hình / offline / hub cũ chưa có route ⇒ trả <c>Trong = true</c> (degrade như MỘT máy — mất
    /// hub thì cũng chẳng phối hợp được với ai).</para>
    /// Mặc định <c>null</c> = TẮT ⇒ lượt KM đi thẳng như trước.
    /// </summary>
    public Func<string, CancellationToken, Task<KmKhungResult>>? XinKhungKm { get; set; }

    /// <summary>
    /// HOOK <b>TRẢ KHUNG</b> — cặp với <see cref="XinKhungKm"/>. Hub ra lệnh Chạy lại cho ĐÚNG các máy đã bị
    /// dừng vì lượt KM này.
    /// <para><b>Phải gọi ở MỌI đường ra của lượt KM</b> (xong · lỗi · huỷ), y luật "mọi đường cửa sổ biến mất
    /// phải đi qua <c>TraHoSo</c>". Quên một đường thì máy kia nằm im tới khi lượt quét quá hạn của hub bật lại
    /// (mặc định 90 phút) — không mất hẳn, nhưng là 90 phút check đơn chết không vì cái gì.</para>
    /// </summary>
    public Func<string, Task>? TraKhungKm { get; set; }

    /// <summary>
    /// HOOK hỏi ID SUẤT ĐƠN HÀNG của máy này, do shell suite RÓT (module Đơn hàng KHÔNG tham chiếu
    /// <c>Shopee.Core</c> nên không tự biết <c>MachineIdentity</c>). Dùng để ghi <b>máy chủ trì</b>
    /// (<c>KmConfig.OwnerMachine</c>) vào hồ sơ KM khi người dùng lưu form.
    /// <para>Phải đọc TƯƠI mỗi lần gọi — người dùng đổi tên máy trong Cài đặt là id hiển thị đổi theo. Mặc định
    /// <c>null</c> = app chạy độc lập / chưa có hub ⇒ chủ trì để RỖNG, và rỗng nghĩa là "không ai chủ trì" nên
    /// hành vi giữ y như hiện tại.</para>
    /// </summary>
    public Func<string>? MayNay { get; set; }

    /// <summary>
    /// HOOK tra TÊN MÁY từ id suất đơn hàng, do shell suite RÓT (module Đơn hàng KHÔNG tham chiếu
    /// <c>Shopee.Core</c> nên không đọc được danh sách máy trên hub). Trả <c>null</c>/rỗng = không tra được.
    /// <para><b>Vì sao cần</b> (phản biện vòng 3, 22/08/2026): <c>OwnerMachine</c> là
    /// <c>&lt;guid&gt;:orders</c>. Form KM và nhật ký in nguyên chuỗi đó là bắt người dùng đối chiếu GUID bằng
    /// mắt giữa các máy. Nhãn dựng bằng <c>KmLichTuDong.NhanMay</c>; không tra được thì rơi về id ngắn, KHÔNG
    /// bao giờ về id thô.</para>
    /// Mặc định <c>null</c> = TẮT (app chạy độc lập / chưa có hub) ⇒ mọi chỗ hiện id ngắn.
    /// </summary>
    public Func<string, string?>? TenMay { get; set; }

    /// <summary>
    /// HOOK báo "HỒ SƠ KM vừa đổi, đẩy lên hub đi" — gọi sau MỌI lượt ghi <c>km_config</c> (lưu form, chốt lượt
    /// KM, đánh dấu lượt thử tự động). Shell suite rót worker đồng bộ vào đây.
    /// <para>Chỉ là tiếng chuông BẨN, không mang dữ liệu: worker tự đọc lại toàn bộ bảng rồi đẩy — cùng khuôn
    /// với gương danh bạ (<c>_mirrorDirty</c>). Nhờ vậy vài lượt ghi liên tiếp gộp thành MỘT lượt đẩy, và một
    /// lượt đẩy hỏng không làm mất thay đổi nào (lượt sau chụp lại từ đầu).</para>
    /// Mặc định <c>null</c> = TẮT (app chạy độc lập) → mọi thứ y hành vi hiện tại.
    /// </summary>
    public Action? KmHoSoDaDoi { get; set; }

    /// <summary>
    /// HOOK <b>THI HÀNH MỘT LỆNH NGƯỜI DÙNG BẤM TRÊN HUB</b> — do <c>AccountsViewModel</c> tự đăng ký trong ctor
    /// và gỡ ở <c>Dispose</c> (ngược chiều mọi hook khác của lớp này: chúng do shell suite RÓT XUỐNG, còn cái
    /// này đi từ module LÊN cho shell gọi). Tham số: <b>hành động</b>
    /// (<see cref="HanhDongLenhTuHub"/>), <c>accountId</c> cục bộ, <c>email</c>; trả
    /// (<see cref="KetQuaLenhTuHub"/>, lý do — <c>null</c> = không có gì để nói).
    ///
    /// <para><b>Vì sao MỘT cửa vào qua ViewModel, chứ không gọi thẳng service:</b> bản trước luồng poll gọi
    /// <c>Sessions.Start</c> trực tiếp, tức ĐI VÒNG qua mọi cửa từ chối của app — từ hub mở được phiên check đơn
    /// ĐÈ LÊN một gói Flash Sale đang chạy (cùng hồ sơ Brave, cùng cổng cầu nối 47821; đo thật 03/09/2026).
    /// Luật ưu tiên flash + luật nhường chỗ chỉ sống trong VM, nên lệnh hub phải đi qua đúng đó.</para>
    ///
    /// <para><b>Hợp đồng luồng:</b> bên gọi là LUỒNG POLL heartbeat. Hook tự marshal về UI thread và trả kết quả
    /// ĐỒNG BỘ; kết quả nghĩa là <i>đã nhận / đã bắt đầu</i>, KHÔNG phải "đã chạy xong" — một vòng KM dài hàng
    /// giờ, chờ nó là chết nhịp heartbeat.</para>
    ///
    /// <para>Mặc định <c>null</c> = VM chưa dựng (app đang khởi động) ⇒ bên gọi ack 'failed' kèm lý do rõ.</para>
    /// </summary>
    public Func<string, long, string, (string Status, string? Error)>? LenhTuHub { get; set; }

    /// <summary>
    /// HOOK <b>"máy có đang chạy một LÁT CẮT cầu nối cho tài khoản này không"</b> — trả TÊN VIỆC
    /// (<c>"Flash Sale"</c> · <c>"KM của Shop"</c> · <c>"Combo Khuyến Mãi"</c> · <c>"Mở list shop"</c> …) nếu máy
    /// đang giữ một lát cắt VÀ lát cắt đó thuộc <c>accountId</c> được hỏi; ngược lại <c>null</c>. Do
    /// <c>AccountsViewModel</c> tự đăng ký/gỡ, cùng chiều <see cref="LenhTuHub"/> (module LÊN cho shell gọi).
    ///
    /// <para><b>Vì sao gương cần cái này:</b> lát cắt bắt đầu bằng việc DỪNG mọi phiên check đơn để mượn hồ sơ
    /// Brave, nên <c>Sessions.Get(id)?.State</c> lúc đó là "không có phiên" ⇒ gương đẩy lên hub đúng chuỗi RỖNG
    /// = "rảnh". Hub nhìn vào đó rồi hiện nút "▶ Chạy" ngay giữa lúc một gói Flash Sale đang chạy — bấm vào là
    /// ép gói nhường chỗ, ngược hẳn ý người bấm (phản biện 04/09/2026).</para>
    ///
    /// <para>Mặc định <c>null</c> = VM chưa dựng ⇒ gương gửi <c>LatCat = null</c>, tức y hành vi cũ.</para>
    /// </summary>
    public Func<long, string?>? LatCatDangChayCua { get; set; }

    /// <summary>
    /// HOOK báo "LÁT CẮT vừa bắt đầu / vừa kết thúc, đẩy gương lại đi" — tiếng chuông BẨN, không mang dữ liệu
    /// (cùng khuôn <see cref="KmHoSoDaDoi"/>). Shell suite rót worker gương vào đây; <c>AccountsViewModel</c> gọi
    /// ở đúng hai chỗ đặt/xoá cờ cầu nối.
    /// <para><b>Không có nó thì trễ tới 60s hoặc KHÔNG BAO GIỜ:</b> worker gương chỉ đẩy khi
    /// <c>Sessions.Changed</c> bắn hoặc tới nhịp nền 60s — mà một lát cắt bắt đầu lúc máy KHÔNG có phiên nào
    /// đang chạy thì không có sự kiện phiên nào bắn cả.</para>
    /// Mặc định <c>null</c> = TẮT (app chạy độc lập).
    /// </summary>
    public Action? LatCatDoi { get; set; }

    /// <summary>Nhật ký hoạt động của app (panel UI + ghi file cạnh database). Các phiên nạp log qua đây.</summary>
    public ActivityLog Log { get; }

    /// <summary>Quản lý các phiên mở trang bán hàng song song (mỗi tài khoản một phiên độc lập).
    /// App shutdown gọi <see cref="AccountSessionManager.StopAllAsync"/> để kill hết Brave.
    /// <para>Thay bằng manager giả CHỈ qua tham số <c>sessionFactory</c> của ctor — xem ctor.</para></summary>
    public AccountSessionManager Sessions { get; }

    /// <summary>
    /// Phát khi kho đơn (bảng <c>orders</c>) vừa được ghi thêm/cập nhật — phiên sync gọi
    /// <see cref="RaiseOrdersChanged"/> ngay sau <c>OrdersRepository.UpsertMany</c> để màn "Đơn hàng" đang mở
    /// tự nạp lại. CỐ Ý có thể bắn từ THREAD NỀN của phiên sync → người nghe (OrdersViewModel) PHẢI marshal về
    /// UI thread trước khi đụng ObservableCollection.
    /// </summary>
    public event Action? OrdersChanged;

    /// <summary>Phiên gọi sau khi UpsertMany đơn về DB THÀNH CÔNG để phát <see cref="OrdersChanged"/>.</summary>
    public void RaiseOrdersChanged() => OrdersChanged?.Invoke();

    /// <summary>
    /// Phát khi TẬP tài khoản (bảng <c>accounts</c>) vừa được thêm/đổi từ NGOÀI màn "Tài khoản" — vd sync shop
    /// từ BigSeller Insert dòng tài khoản mới. Màn "Tài khoản" đang mở nghe để tự <c>Reload()</c> danh sách, thấy
    /// dòng mới NGAY mà không phải đổi màn. Y như <see cref="OrdersChanged"/>: CỐ Ý có thể bắn từ THREAD NỀN →
    /// người nghe (AccountsViewModel) PHẢI marshal về UI thread trước khi đụng ObservableCollection.
    /// </summary>
    public event Action? AccountsChanged;

    /// <summary>
    /// Phát khi vừa CHUẨN BỊ HÀNG xong một đơn (số đếm của tab "Shops" vừa tăng) — kèm id tài khoản để màn
    /// "Tài khoản" chỉ nạp lại khi ĐÚNG tài khoản đang mở. Trước đây tab "Shops" chỉ đọc lại lúc đổi tài khoản
    /// / đổi ngày, nên đang chạy thì số đứng im dù CSDL đã cộng ngay. Y như <see cref="OrdersChanged"/>: CỐ Ý bắn
    /// từ THREAD NỀN của phiên → người nghe PHẢI marshal về UI thread.
    /// </summary>
    public event Action<long>? PrepareCountChanged;

    /// <summary>Phiên gọi ngay sau <c>ResultsRepository.IncrementPrepared</c> để tab "Shops" tự cập nhật.</summary>
    public void RaisePrepareCountChanged(long accountId) => PrepareCountChanged?.Invoke(accountId);

    /// <summary>
    /// Phát khi vừa ĐỌC ĐƯỢC danh sách shop của một tài khoản (ghi <c>account_shops</c>) — tab "Shops" nghe để
    /// dựng lưới NGAY. Không có sự kiện này thì màn đang mở giữ nguyên kết quả nạp lần đầu: người dùng mở app
    /// rồi chọn tài khoản TRƯỚC khi phiên kịp đọc shop sẽ thấy lưới TRỐNG mãi (chỉ hết khi bấm sang tài khoản
    /// khác rồi bấm lại). Y như <see cref="OrdersChanged"/>: CỐ Ý bắn từ THREAD NỀN → người nghe PHẢI marshal
    /// về UI thread.
    /// </summary>
    public event Action<long, bool>? ShopListChanged;

    /// <summary>Phiên gọi ngay sau <c>ResultsRepository.UpsertShops</c> để tab "Shops" dựng lưới shop.</summary>
    /// <param name="luotChayMoi">
    /// <c>true</c> (mặc định) = đọc danh sách shop vì một LƯỢT CHẠY MỚI sắp lặp qua từng shop ⇒ người nghe XOÁ
    /// dấu tick tiến độ của lượt trước. <c>false</c> = chỉ ĐỌC để dựng lưới, KHÔNG có lượt chạy nào bắt đầu ⇒
    /// giữ nguyên tiến độ.
    /// <para>Vế <c>false</c> sinh ra cho nút "Mở list shop" (18/08/2026): nó đọc danh sách y hệt vòng chạy nhưng
    /// dừng ngay ở trang chọn shop, nên nếu cũng bị coi là lượt mới thì vừa bấm một nút "chỉ xem" là mất sạch
    /// tick của cả 12 shop, không một dòng log nào giải thích.</para>
    /// </param>
    public void RaiseShopListChanged(long accountId, bool luotChayMoi = true)
        => ShopListChanged?.Invoke(accountId, luotChayMoi);

    /// <summary>
    /// Phát khi phiên BẮT ĐẦU / XONG việc check một shop — cột tiến độ của tab "Shops" dùng để chuyển vòng quay
    /// sang shop đang chạy tới và đóng dấu tick cho shop vừa xong. Tham số: id tài khoản + <b>nhãn shop</b> (ĐÚNG khóa
    /// <c>prepare_daily</c>: <c>LoginName</c>, rỗng thì <c>ShopName</c>) + đang-check hay không. Y như
    /// <see cref="OrdersChanged"/>: CỐ Ý bắn từ THREAD NỀN của phiên → người nghe PHẢI marshal về UI thread.
    /// </summary>
    public event Action<long, string, bool>? ShopCheckChanged;

    /// <summary>Phiên gọi khi vào/ra một shop để tab "Shops" vẽ vòng quay + tick đúng shop.</summary>
    public void RaiseShopCheckChanged(long accountId, string shopLabel, bool checking)
        => ShopCheckChanged?.Invoke(accountId, shopLabel, checking);

    /// <summary>
    /// Phát khi vòng KM/Combo BẮT ĐẦU / XONG việc tạo chương trình cho một shop — ba cột "KM | Combo | Flash Sale"
    /// của tab "Shops" dùng để hiện chữ "đang chạy…" trong ĐÚNG ô của loại đang chạy, rồi thay bằng số sản phẩm
    /// khi xong.
    /// <para>Tham số: id tài khoản + <b>nhãn shop</b> (ĐÚNG khóa <c>km_program_daily</c>: <c>LoginName</c>, rỗng
    /// thì <c>ShopName</c>) + <see cref="LoaiChuongTrinh"/> + đang-chạy hay không + số sản phẩm (0 lúc bắt đầu).</para>
    /// <para><b>Tách khỏi <see cref="ShopCheckChanged"/> chứ không gộp:</b> sự kiện kia chỉ mang "shop nào" —
    /// không nói được LOẠI nào, tức không biết điền vào ô nào trong ba ô. Vòng chương trình bắn CẢ HAI: sự kiện
    /// kia lo vòng quay + dấu tick (ý nghĩa "phiên đang xử shop này" đúng cho cả hai luồng), sự kiện này lo phần
    /// số. Y như <see cref="OrdersChanged"/>: CỐ Ý bắn từ THREAD NỀN của phiên → người nghe PHẢI marshal về UI.</para>
    /// </summary>
    public event Action<long, string, LoaiChuongTrinh, bool, int>? ShopProgramChanged;

    /// <summary>Phiên gọi khi vào/ra một shop trong vòng KM/Combo — xem <see cref="ShopProgramChanged"/>.</summary>
    public void RaiseShopProgramChanged(
        long accountId, string shopLabel, LoaiChuongTrinh loai, bool dangChay, int soSanPham)
        => ShopProgramChanged?.Invoke(accountId, shopLabel, loai, dangChay, soSanPham);

    /// <summary>
    /// Phát khi vừa ghi/đóng banner lỗi địa chỉ (upsert hoặc dismiss) — tab Shops nghe để nạp lại list.
    /// CỐ Ý có thể bắn từ THREAD NỀN → người nghe PHẢI marshal về UI thread.
    /// </summary>
    public event Action<long>? AddressAlertsChanged;

    /// <summary>Phiên / ViewModel gọi sau khi upsert hoặc dismiss alert địa chỉ.</summary>
    public void RaiseAddressAlertsChanged(long accountId) => AddressAlertsChanged?.Invoke(accountId);

    /// <summary>Bên ngoài (vd sync shop BigSeller) gọi sau khi Insert tài khoản THÀNH CÔNG để phát <see cref="AccountsChanged"/>.</summary>
    public void RaiseAccountsChanged() => AccountsChanged?.Invoke();

    /// <summary>
    /// SỐ HÀNG CÒN TỒN của vòng chờ đẩy (gộp MỌI tài khoản) — <see cref="HubOutboxWorker"/> ghi sau mỗi lượt,
    /// thanh trạng thái đọc để hiện "⏳ Chờ đẩy: N". Mặc định 0 (chưa có lượt nào chạy → không hiện gì).
    /// </summary>
    public OutboxPending PendingOutbox { get; private set; }

    /// <summary>
    /// Phát khi <see cref="PendingOutbox"/> ĐỔI. Y như <see cref="OrdersChanged"/>: CỐ Ý bắn từ THREAD NỀN của
    /// worker → người nghe PHẢI marshal về UI thread trước khi đụng property bind.
    /// </summary>
    public event Action? PendingOutboxChanged;

    /// <summary>Worker gọi sau mỗi lượt quét. Không đổi so với lần trước → KHÔNG phát sự kiện (khỏi làm mới UI thừa
    /// mỗi 2 phút).</summary>
    public void SetPendingOutbox(OutboxPending value)
    {
        if (value.Equals(PendingOutbox))
        {
            return;
        }
        PendingOutbox = value;
        PendingOutboxChanged?.Invoke();
    }

    /// <param name="sessionFactory">CHỈ dùng cho test: thay factory phiên bằng stub để không phóng Brave thật.
    /// Phải nhận ở CTOR chứ không phải property setter — <c>AccountsViewModel</c> đăng ký <c>Sessions.Changed</c>
    /// ngay trong ctor của nó, nên gán lại Sessions SAU đó là cắt đứt người nghe: test chạy trong một môi
    /// trường KHÔNG có sự kiện phiên, đúng tầng đã để lọt lỗi nút chết ở vòng 3.</param>
    public AppServices(string? dbPath = null, Func<long, IAccountSession>? sessionFactory = null)
    {
        Database = new Database(dbPath);
        Accounts = new AccountRepository(Database);
        // KHÔNG còn ProxyRepository: proxy runtime đã gỡ khỏi module (bảng `proxies` trong app.db vẫn giữ
        // nguyên, chỉ không còn code nào đọc).
        Settings = new SettingsRepository(Database);
        Orders = new OrdersRepository(Database);
        Results = new ResultsRepository(Database);
        KmConfig = new KmConfigRepository(Database);
        PickupAlerts = new PickupAddressAlertsRepository(Database);
        ReturnCodes = new ReturnCodesRepository(Database);

        // Migration MỘT LẦN (idempotent qua cờ settings): gộp ProxyKey cố định của tài khoản (cơ chế cũ) vào
        // pool KiotProxy CHUNG để không mất key sẵn có khi chuyển sang cấp phát key theo pool lúc chạy.
        // Chạy sau khi có Accounts + Settings; các lần mở app sau bỏ qua nhờ cờ.
        ProxyKeyPoolMigration.EnsureMigrated(Accounts, Settings);
        GsheetSync = new GoogleSheetSyncService();
        Notify = new OrderNotifyService();
        // Log đặt TRƯỚC Sessions vì các phiên sẽ nạp log qua Log khi chạy. Thư mục logs cạnh file database.
        var logDir = Path.Combine(Path.GetDirectoryName(Database.Path) ?? ".", "logs");
        Directory.CreateDirectory(logDir);
        Log = new ActivityLog(logDir);
        // Tạo sau các repository vì factory phiên đọc Accounts/Settings khi chạy.
        Sessions = sessionFactory is null ? new AccountSessionManager(this) : new AccountSessionManager(sessionFactory);
    }
}
