namespace Shopee.Core.Coordination;

// ── DTO truyền tin giữa client và Hub (dùng chung cho cả 2 phía) ───────────────

/// <summary>Acc Shopee đang được một máy DÙNG (giữ qua hub để chống dùng trùng xuyên máy).</summary>
public sealed class AccountLease
{
    public string AccountId { get; set; } = "";
    public string MachineId { get; set; } = "";
    public string Hostname { get; set; } = "";
    public DateTimeOffset HeartbeatAt { get; set; }
}

/// <summary>Một máy đang/đã online (nhịp sống) — cho bảng trạng thái.</summary>
public sealed class MachinePresence
{
    public string MachineId { get; set; } = "";
    public string Hostname { get; set; } = "";
    public DateTimeOffset LastSeen { get; set; }
    public string? AppVersion { get; set; }
    /// <summary>Trần cửa sổ Brave máy này tự báo lên (0 = chưa báo/không rõ). Hub dùng để chia quỹ khi giao việc.</summary>
    public int MaxBrave { get; set; }
    /// <summary>Dòng trạng thái lệnh update app cho máy này (⏳ đã ra lệnh / 🔄 đang khởi động lại / ✓ đã lên bản…).
    /// Rỗng = chưa từng ra lệnh. Field mới: client cũ parse /fleet bỏ qua field lạ → an toàn.</summary>
    public string UpdateStatus { get; set; } = "";
    /// <summary>Thời điểm operator ra lệnh update (còn hiệu lực). null = không có lệnh đang chờ.</summary>
    public DateTimeOffset? UpdateRequestedAt { get; set; }
    /// <summary>Chế độ app của máy: "Full" | "Workspace" | "Shopee" (rỗng = client cũ chưa báo → Hub suy "Workspace").</summary>
    public string Mode { get; set; } = "";
    /// <summary>Loại SUẤT làm việc: <see cref="MachineSlots.Workspace"/> | <see cref="MachineSlots.Orders"/>.</summary>
    public string Kind { get; set; } = "";
    /// <summary>Id PC THẬT (gộp 2 suất của cùng một máy). Suất workspace: = <see cref="MachineId"/>.</summary>
    public string HostId { get; set; } = "";

    /// <summary>Tổng hàng còn TỒN trong vòng chờ đẩy của module Đơn hàng trên máy này (đơn + phiếu + dòng
    /// GSheet + lượt "Đã bán" + mã trả hàng). <c>null</c> = máy KHÔNG báo — bản client cũ, hoặc suất/chế độ
    /// không có module Đơn hàng (suất workspace luôn null). Phân biệt null với 0 là CỐ Ý: 0 = "đã đẩy hết",
    /// null = "không biết" → bảng /machines hiện "—" thay vì con số bịa.</summary>
    public int? OutboxPending { get; set; }
}

/// <summary>Mục manifest của một file dùng chung trên Hub.</summary>
public sealed class FileManifestEntry
{
    public string Name { get; set; } = "";
    public int Version { get; set; }
    public string Hash { get; set; } = "";
    public long Size { get; set; }
    public DateTimeOffset Mtime { get; set; }
}

/// <summary>Ảnh chụp toàn cảnh cho bảng trạng thái (1 lần gọi /fleet).</summary>
public sealed class FleetSnapshot
{
    public List<LeaseRecord> Leases { get; set; } = [];
    public List<AccountLease> AccountLeases { get; set; } = [];
    public List<WorkLedgerRecord> Ledger { get; set; } = [];
    public List<MachinePresence> Machines { get; set; } = [];

    // ── Giao việc (Hub đẩy việc cho client) ──
    public List<MachineRoleInfo> Roles { get; set; } = [];
    public List<Assignment> Assignments { get; set; } = [];
    /// <summary>Việc GIÁN ĐOẠN (failed/canceled, chưa xong) — operator bấm ▶ Tiếp tục. Field mới: client cũ bỏ qua.</summary>
    public List<Assignment> Interrupted { get; set; } = [];
}

// ── Giao việc (Hub chủ động giao việc cho client) ─────────────────────────────

/// <summary>Vai trò máy = loại việc máy nhận tự động từ Hub. Lưu chuỗi để DTO/JSON gọn.</summary>
public static class MachineRoles
{
    public const string Off = "off";
    public const string Scrape = AssignmentOps.Scrape;
    public const string Import = AssignmentOps.Import;
    public const string Update = AssignmentOps.Update;
    public const string All = "all";

    /// <summary>Op "search" KHÔNG có vai trò tự động — luôn giao TAY (ghim máy) từ bảng điều phối Search.</summary>
    public const string Search = AssignmentOps.Search;

    /// <summary>Vai trò phụ trách op này (rewrite gộp vào nhóm Update).</summary>
    public static string ForOp(string op) => op switch
    {
        AssignmentOps.Scrape => Scrape,
        AssignmentOps.Import => Import,
        AssignmentOps.Update or AssignmentOps.Rewrite => Update,
        _ => op,
    };

    /// <summary>Máy vai trò <paramref name="role"/> có nhận op <paramref name="op"/> không (All nhận hết).</summary>
    public static bool Handles(string role, string op) =>
        role == All || (role != Off && role == ForOp(op));
}

/// <summary>Vai trò đã gán cho 1 máy (Hub lưu; client đọc để biết mình nhận loại việc nào).</summary>
public sealed class MachineRoleInfo
{
    public string MachineId { get; set; } = "";
    public string Role { get; set; } = MachineRoles.Off;
}

/// <summary>Một việc Hub giao: (tài khoản BigSeller + shop + op) → máy đích hoặc theo vai trò.</summary>
public sealed class Assignment
{
    public string Id { get; set; } = "";
    public string BigsellerId { get; set; } = "";
    public string ShopId { get; set; } = "";
    public string Sheet { get; set; } = "";
    public string Op { get; set; } = "";                 // xem AssignmentOps: scrape | import | update | rewrite | search
    /// <summary>Ghim cứng vào 1 máy; null = để Hub định tuyến theo vai trò.</summary>
    public string? TargetMachineId { get; set; }
    /// <summary>Dữ liệu kèm theo việc (JSON). Hiện chỉ op "search" dùng: <see cref="SearchJobPayload"/>
    /// (danh sách link của khối + số acc khóa + lane + khu vực). Các op khác để rỗng.</summary>
    public string Payload { get; set; } = "";
    public bool Pinned { get; set; }
    public string Status { get; set; } = AssignmentStatus.Queued;   // xem AssignmentStatus
    /// <summary>Operator đã bỏ khỏi danh sách gián đoạn (hub-side; client không dùng) — ẩn khỏi ▶ Tiếp tục; status giữ nguyên.</summary>
    public bool Dismissed { get; set; }
    public string ClaimedByMachineId { get; set; } = "";
    public string ClaimedByHostname { get; set; } = "";
    public string LastError { get; set; } = "";
    /// <summary>Khoảng dòng Hub đặt cho client chạy (ghi đè cấu hình client lượt này). 0 = dùng cấu hình client.</summary>
    public int StartRow { get; set; }
    public int EndRow { get; set; }
    /// <summary>Tham số chạy Hub đặt cho lượt này (ghi đè cấu hình client). 0 = dùng cấu hình client.</summary>
    public int Processes { get; set; }
    public int FrameSize { get; set; }
    public int ReloadSeconds { get; set; }
    /// <summary>Khoảng nghỉ giữa 2 link của Scrape, tính bằng GIÂY (chỉ op scrape đọc). 0 = dùng cấu hình client
    /// (mặc định 120–240s — xem <see cref="Shopee.Core.Scrape.ScrapeRestWindow"/>). Client CŨ không biết 2 field
    /// này thì bỏ qua an toàn: JSON thừa field → deserialize bỏ qua, và cấu hình máy vẫn quyết định khoảng nghỉ.</summary>
    public int RestMinSec { get; set; }
    public int RestMaxSec { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public string CoordId => $"{BigsellerId}__{ShopId}__{Op}";
}

public sealed record SetRoleRequest(string MachineId, string Role);
// Processes/FrameSize/ReloadSeconds đặt SAU Payload (không phải "trước Payload") vì các call-site hiện có
// truyền StartRow/EndRow/Payload THEO VỊ TRÍ (Fleet.razor, FleetViewModel, SearchBoardService…) — chèn param
// int vào giữa sẽ nuốt nhầm đối số Payload (string) → vỡ build. Sau Payload thì mọi call-site cũ giữ nguyên.
// RestMinSec/RestMaxSec thêm ở CUỐI (cùng lý do như 3 param trên: mọi call-site cũ truyền positional).
public sealed record CreateAssignmentRequest(
    string BigsellerId, string ShopId, string Sheet, string Op, string? TargetMachineId, bool Pinned,
    int StartRow = 0, int EndRow = 0, string Payload = "",
    int Processes = 0, int FrameSize = 0, int ReloadSeconds = 0,
    int RestMinSec = 0, int RestMaxSec = 0);

/// <summary>Dữ liệu việc Import Hub giao (ghi vào <see cref="Assignment.Payload"/> cho op "import"):
/// cờ import từ tab "Đã nhận" (Claimed) thay vì danh sách crawl. Payload rỗng = client dùng cấu hình của nó.</summary>
public sealed class ImportJobPayload
{
    public bool FromClaimedTab { get; set; }
}

/// <summary>Dữ liệu việc Search Hub giao cho 1 client: chạy đúng khối link này, khóa tối đa
/// <see cref="AccountsPerClient"/> tài khoản Shopee (qua account-lease) để máy khác không đụng.</summary>
public sealed class SearchJobPayload
{
    public List<string> Links { get; set; } = [];
    public int AccountsPerClient { get; set; }
    public int Lanes { get; set; } = 3;
    public string? Region { get; set; }
    public string? SourceFile { get; set; }
}

// ── Gom kết quả Search về Hub (client đẩy sản phẩm cào được → Hub gộp) ─────────
/// <summary>1 sản phẩm client gửi lên Hub: <see cref="ItemId"/> để dedup (khoá chính), <see cref="Json"/>
/// là toàn bộ ProductResult serialize (Hub CHỈ lưu blob, không cần biết model engine).</summary>
public sealed record SearchProductItem(long ItemId, string Json);
public sealed record SearchProductsPushRequest(string MachineId, string SourceFile, List<SearchProductItem> Products);

// ── Log tập trung (client gửi 1 dòng lên Hub → tab Log xem log nhiều máy) ──────
public sealed record AppendLogRequest(string MachineId, string Hostname, string Level, string Text);
public sealed class LogEntry
{
    public long Id { get; set; }
    public string MachineId { get; set; } = "";
    public string Hostname { get; set; } = "";
    public DateTimeOffset Ts { get; set; }
    public string Level { get; set; } = "info";   // info | ok | warn | error
    public string Text { get; set; } = "";
}

// ── Client báo acc Shopee dính captcha/lỗi về Hub (Hub xem + quyết giữ/xóa) ────
/// <summary>Client báo 1 acc Shopee bị captcha/lỗi. Status: "captcha" (vừa dính, đang tự sửa) | "failed"
/// (client không sửa được → Hub quyết). Sửa được thì client gọi clear (gỡ báo).</summary>
public sealed record AccountErrorRequest(
    string AccountId, string MachineId, string Hostname, string Reason, string? CaptchaUrl, string Status);
public sealed class AccountError
{
    public string AccountId { get; set; } = "";
    public string MachineId { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? CaptchaUrl { get; set; }
    public string Status { get; set; } = "captcha";
    public DateTimeOffset ReportedAt { get; set; }
}
public sealed record ClearAccountErrorRequest(string AccountId);
public sealed record ClaimAssignmentsRequest(string MachineId, string Role, int Max);
public sealed record AssignmentStatusRequest(string Id, string MachineId, string Status, string? Error);
public sealed record CancelAssignmentRequest(string Id);
/// <summary>Operator bấm ▶ Tiếp tục 1 việc đã dừng/huỷ → Hub đưa về 'queued' (giữ nguyên tham số).</summary>
public sealed record ResumeAssignmentRequest(string Id);
/// <summary>Client khởi động lại → xin nhận lại việc đang dở của CHÍNH máy mình (nhả lease chết + việc về 'queued').</summary>
public sealed record ResumeMineRequest(string MachineId);
/// <summary>Kết quả POST /assignments/resume: Error null = tiếp tục OK; ngược lại là lý do từ chối (tiếng Việt).</summary>
public sealed record ResumeAssignmentResponse(string? Error);
/// <summary>Kết quả POST /assignments/resume-mine: số việc máy này được đưa lại về 'queued'.</summary>
public sealed record ResumeMineResponse(int Requeued);
/// <summary>Hub đặt TAY trạng thái sổ hoàn thành cho 1 (shop+op): completed = ✓ xong; stopped = ■ dừng;
/// idle = chưa chạy (XOÁ bản ghi + tiến độ dòng → scrape giao lại + chạy lại từ đầu).</summary>
public sealed record SetLedgerStatusRequest(
    string Key, string BigsellerId, string ShopId, string Sheet, string Op, string Status);

/// <summary>Client bấm "Cào lại dòng đã bỏ" → Hub bỏ các dòng trong sổ bỏ-qua khỏi vùng phủ + xoá sổ + đưa
/// status về stopped. Mang đủ (BigsellerId, ShopId, Sheet, Op) như <see cref="SetLedgerStatusRequest"/> dù hub
/// chỉ tra theo <paramref name="Key"/>: giữ 2 request cùng hình dạng để đọc log Hub còn biết là việc nào.
/// <para><paramref name="Rows"/> = sổ dòng bỏ-qua ĐANG CÓ Ở CLIENT: hub union với sổ của mình rồi mới khoét.
/// Thiếu nó thì mọi dòng bỏ TRƯỚC khi hub có cột skipped (không backfill) làm nút thành no-op im lặng —
/// hub trả OK mà không khoét gì, client xoá sổ local, lượt fold sau phủ lại → mất dấu vĩnh viễn.</para></summary>
public sealed record ReopenSkippedRequest(
    string Key, string BigsellerId, string ShopId, string Sheet, string Op, List<int>? Rows = null);

// ── Request / Response ────────────────────────────────────────────────────────

public sealed record LeaseAcquireRequest(
    string Key, string BigsellerId, string ShopId, string Sheet, string Op,
    string MachineId, string Hostname, bool Force);

public sealed record LeaseAcquireResponse(bool Granted, string? BlockedByHostname);

public sealed record LeaseHeartbeatRequest(string Key, string MachineId);

public sealed record LeaseReleaseRequest(string Key, string MachineId);

public sealed record AccountReserveRequest(List<string> AccountIds, string MachineId, string Hostname);

public sealed record AccountReserveResponse(List<string> Granted, List<string> Blocked);

public sealed record AccountReleaseRequest(List<string> AccountIds, string MachineId);

/// <summary>Client ghi "nhà" (home machine) = máy này cho các tk đã được lease cấp (affinity tk↔máy, Scrape).</summary>
public sealed record SetAccountHomeRequest(List<string> AccountIds, string MachineId, string Hostname);
/// <summary>Một dòng "nhà" tk: tk <see cref="AccountId"/> thuộc máy <see cref="MachineId"/>. <see cref="Binding"/>
/// = máy nhà CÒN online (last_seen trong ngưỡng HomeTakeoverAfter) ⇒ máy khác phải tránh; false = tk tự do.</summary>
public sealed record AccountHomeItem(string AccountId, string MachineId, string Hostname, bool Binding);

/// <summary>Nhịp sống 1 SUẤT làm việc. <paramref name="MachineId"/> = id SUẤT (suất workspace GIỮ NGUYÊN id máy,
/// suất đơn hàng có hậu tố — xem <see cref="MachineSlots"/>). 3 field Mode/Kind/HostId có MẶC ĐỊNH RỖNG để client
/// CŨ (không gửi) vẫn hợp lệ; Hub suy ra qua <c>MachineSlots.Normalize*</c>.
/// <para><paramref name="OutboxPending"/> = tổng hàng còn chờ đẩy của module Đơn hàng trên máy (xem
/// <see cref="MachinePresence.OutboxPending"/>). MẶC ĐỊNH <c>null</c> ⇒ tương thích NGƯỢC cả hai chiều: client cũ
/// không gửi thì hub mới đọc ra null ("không biết"); client mới gửi thêm field thì hub cũ bỏ qua field lạ khi
/// deserialize (System.Text.Json mặc định) — không phá thứ tự deploy nào.</para></summary>
public sealed record MachineHeartbeatRequest(
    string MachineId, string Hostname, string? AppVersion, int MaxBrave = 0,
    string Mode = "", string Kind = "", string HostId = "", int? OutboxPending = null);
/// <summary>Phản hồi heartbeat: kênh Hub đẩy lệnh xuống client. <see cref="UpdateRequestedAt"/> null/rỗng = không có
/// lệnh; có giá trị = chuỗi ISO lúc operator ra lệnh update, client dùng làm ID dedup (chỉ update 1 lần/lệnh).
/// Là class để sau này thêm lệnh khác chỉ cần thêm field (client cũ bỏ qua field lạ).</summary>
public sealed class MachineHeartbeatResponse
{
    public string? UpdateRequestedAt { get; set; }

    /// <summary>Lệnh Hub giao cho SUẤT ĐƠN HÀNG này (▶ Chạy / ✖ Dừng một tài khoản). Rỗng = không có lệnh.
    /// Field MỚI: client cũ bỏ qua field lạ khi parse → hub deploy trước, client release sau vẫn an toàn.</summary>
    public List<OrdersCommandDto> OrdersCommands { get; set; } = [];
}
/// <summary>Client báo tiến trình/kết quả tự-update app về Hub. Status: "checking" | "restarting" | "already-latest"
/// | "unsupported" | "failed: &lt;lý do&gt;" — Hub map thành dòng trạng thái + clear cờ khi terminal.</summary>
public sealed record UpdateAckRequest(string MachineId, string Status);
/// <summary>Client báo Hub "tôi rời đi" (bấm Ngắt kết nối) → Hub xoá khỏi danh sách máy ngay.</summary>
public sealed record MachineLeaveRequest(string MachineId);

// ── GƯƠNG danh bạ tài khoản Đơn hàng (client đẩy lên; Hub KHÔNG sở hữu) ───────
// Tài khoản Đơn hàng (Email/Password/Cookie/ProxyKey/PickupAddress…) nằm trong CSDL cục bộ của TỪNG máy. Hub chỉ
// giữ một BẢN GƯƠNG để trang điều phối biết "máy này có những tài khoản nào" mà ra lệnh — acc vẫn thuộc máy.
//
// ĐỔI 11/08/2026 — gương MANG THÊM 3 Ô ĐĂNG NHẬP: Password, VerifyEmail, VerifyEmailPassword.
//   Vì sao: máy MỚI kéo danh bạ về chỉ tạo được bản ghi RỖNG mật khẩu ⇒ phải gõ tay 3 ô cho từng tài khoản trên
//   từng máy. Đường BigSeller vốn đã đồng bộ Password + EmailPassword y hệt (FileStoreConfigService
//   .UpdateSharedAccountFields) nên tư thế bảo mật của hệ thống không đổi về CHẤT.
//   Lưu DẠNG THƯỜNG (đúng mức đang lưu ở client và ở đường BigSeller). Đường truyền là HTTPS + X-Api-Token.
//   COOKIE VẪN TUYỆT ĐỐI KHÔNG ĐẨY: cookie là phiên đăng nhập sống, dùng lại được ngay không cần verify —
//   rủi ro khác hẳn mật khẩu, và không giải quyết được vấn đề "máy mới phải gõ tay" nào cả.

/// <summary>Một shop con của tài khoản Đơn hàng trong gương. <see cref="Login"/> = <c>shop_login</c> phía client —
/// CŨNG là <c>shops.username</c> trên hub (client đẩy đơn theo khoá này) nên hub tra được số đơn của shop;
/// <see cref="Name"/> chỉ để hiển thị.</summary>
public sealed record OrdersShopItem(string Login, string Name);

/// <summary>Một tài khoản Đơn hàng trên MỘT máy (gương). KHOÁ là <see cref="Login"/> (email đăng nhập), KHÔNG
/// phải Id cục bộ: mỗi máy tự tạo bản ghi tài khoản nên Id của CÙNG một tài khoản LỆCH giữa các máy.
/// <see cref="SessionState"/> nhận giá trị trong <see cref="OrdersSessionStates"/>.</summary>
public sealed record OrdersAccountItem(
    string Login,
    string SessionState,
    List<OrdersShopItem> Shops,
    bool VerifyFailed,
    DateTimeOffset? LastSyncAt)
{
    // BA Ô ĐĂNG NHẬP — để trong THÂN record (property init) chứ KHÔNG thêm tham số positional có giá trị mặc
    // định: System.Text.Json bỏ qua field thiếu trong JSON và giữ nguyên giá trị khởi tạo ở đây, nên hub CŨ
    // (chưa biết field) lẫn client CŨ (không gửi field) đều parse bình thường — deploy hai phía lệch nhau vô hại.
    // Rỗng nghĩa là "máy này chưa có" — hub KHÔNG được lấy nó xoá giá trị đang giữ (xem UpsertOrdersAccounts).

    /// <summary>Mật khẩu đăng nhập tài khoản phụ. Rỗng = máy này chưa nhập.</summary>
    public string Password { get; init; } = "";

    /// <summary>Hòm thư nhận mail xác minh của Shopee. Rỗng = máy này chưa nhập.</summary>
    public string VerifyEmail { get; init; } = "";

    /// <summary>Mật khẩu hòm thư xác minh. Rỗng = máy này chưa nhập.</summary>
    public string VerifyEmailPassword { get; init; } = "";

    /// <summary>Tên LÁT CẮT (KM của Shop · Combo Khuyến Mãi · Flash Sale · Mở list shop …) mà máy đang chạy cho
    /// login này; <c>null</c> = không có.
    /// <para>Trường RIÊNG chứ không nhét vào <see cref="SessionState"/>: lát cắt bắt đầu bằng việc DỪNG mọi phiên
    /// check đơn để mượn hồ sơ Brave ⇒ <see cref="SessionState"/> lúc đó ĐÚNG là rỗng, và hub cần thấy cả hai sự
    /// thật (không còn phiên check đơn, nhưng máy vẫn bận vì tài khoản này).</para>
    /// <para>Nullable + trong THÂN record, KHÔNG <c>[Required]</c> — client cũ không gửi field này thì hub nhận
    /// <c>null</c> và hành xử y bản trước; hub cũ nhận field lạ thì System.Text.Json bỏ qua (mặc định
    /// <c>UnmappedMemberHandling.Skip</c>). Cùng luật với ba ô đăng nhập ở trên.</para></summary>
    public string? LatCat { get; init; }
}

/// <summary>Client đẩy TOÀN BỘ danh bạ tài khoản Đơn hàng của CHÍNH MÁY MÌNH lên hub. <see cref="MachineId"/> =
/// id SUẤT ĐƠN HÀNG (<c>&lt;id-máy&gt;:orders</c>, xem <see cref="MachineSlots"/>). Hợp đồng: hub THAY TOÀN BỘ
/// danh bạ của máy đó (client là nguồn sự thật cho danh sách của chính nó), KHÔNG đụng máy khác.</summary>
public sealed record OrdersAccountsPushRequest(string MachineId, string Hostname, List<OrdersAccountItem> Accounts);

/// <summary>Một sub-acc Đơn hàng trong DANH BẠ GỘP toàn Hub (mọi máy, distinct theo login) — máy MỚI kéo về để
/// tạo sẵn bản ghi tài khoản. Khoá là <see cref="Login"/> (email đăng nhập). Từ 11/08/2026 CÓ mang 3 ô đăng
/// nhập (xem khối chú thích trên <see cref="OrdersAccountsPushRequest"/>); vẫn KHÔNG bao giờ mang cookie.
/// Ô rỗng = chưa máy nào trong hệ thống nhập ô đó.</summary>
public sealed record OrdersDirectoryAccount(string Login, List<OrdersShopItem> Shops)
{
    /// <summary>Mật khẩu đăng nhập tài khoản phụ (rỗng = chưa máy nào nhập).</summary>
    public string Password { get; init; } = "";

    /// <summary>Hòm thư nhận mail xác minh (rỗng = chưa máy nào nhập).</summary>
    public string VerifyEmail { get; init; } = "";

    /// <summary>Mật khẩu hòm thư xác minh (rỗng = chưa máy nào nhập).</summary>
    public string VerifyEmailPassword { get; init; } = "";
}

/// <summary>Trạng thái phiên tài khoản Đơn hàng trong gương (chuỗi để DTO/JSON gọn, y khuôn
/// <see cref="MachineRoles"/>). Rỗng = không có phiên / đã dừng / lỗi.</summary>
public static class OrdersSessionStates
{
    public const string Idle = "";
    public const string Queued = "queued";
    public const string Opening = "opening";
    public const string Running = "running";
    public const string Stopping = "stopping";
}

// ── HỒ SƠ KM của Shop (đồng bộ XUYÊN MÁY — khoá là LOGIN, không phải máy) ─────
// Cấu hình "KM của Shop" vốn nằm trong SQLite cục bộ TỪNG MÁY (bảng km_config), nên bật "chạy tự động" ở máy A
// thì máy B không hề biết: B vẫn đặt vòng check đơn đè lên khung 2–6h của A. Hồ sơ KM là bản DÙNG CHUNG của
// tài khoản đó trên hub — mỗi máy vừa đẩy bản của mình lên vừa kéo bản đã gộp về, luật gộp là KmHoSoGop.
//
// ⚠ KHOÁ LÀ `Login`, KHÔNG PHẢI `(machine_id, login)` — CỐ Ý khác hợp đồng của gương danh bạ
// (OrdersAccountsPushRequest). Gương danh bạ trả lời "MÁY NÀY có những tài khoản nào" nên nó thuộc về máy và
// hub thay trọn danh bạ của đúng máy đó. Hồ sơ KM trả lời "TÀI KHOẢN NÀY có lịch KM gì" — nó thuộc về tài
// khoản, mọi máy nhìn thấy CÙNG một bản. Chép hợp đồng "thay toàn bộ theo machine_id" sang đây là xoá sạch hồ
// sơ của các máy khác.

/// <summary>Hồ sơ KM của MỘT tài khoản (khoá <see cref="Login"/>). Mốc thời gian trong DTO này LUÔN là
/// <b>UTC</b> — client đổi sang giờ địa phương ở đúng hai cửa đẩy/kéo bằng <see cref="KmMocThoiGian"/>.
/// <para>Mọi trường ngoài <see cref="Login"/> để trong THÂN record (property <c>init</c>) chứ KHÔNG thêm tham
/// số vị trí: <c>System.Text.Json</c> bỏ qua field thiếu và giữ nguyên giá trị khởi tạo ở đây, nên hub CŨ
/// (chưa biết field) lẫn client CŨ (không gửi field) đều parse bình thường — deploy hai phía lệch nhau vô hại.
/// Luật này đã ghi ở khối chú thích trên <see cref="OrdersAccountItem"/>, đây là chỗ áp dụng lại.</para></summary>
public sealed record KmProfileItem(string Login)
{
    /// <summary>Bật chạy KM tự động theo chu kỳ.</summary>
    public bool TuDong { get; init; }

    /// <summary>Chu kỳ tự động (ngày). Đọc ra luôn ≥ 1 nhờ <c>KmConfigRepository.ChuanHoaSoNgay</c> ở cửa đọc DB
    /// client.
    /// <para><b>Mặc định 3 — phải KHỚP <c>KmConfigRepository.MacDinhSoNgay</c> và <c>DEFAULT</c> của cột
    /// <c>so_ngay</c> ở CẢ hai DB</b> (client <c>km_config</c> và hub <c>km_profiles</c>). Đổi từ 7 ngày
    /// 25/08/2026 cùng lượt đổi mặc định của form. Nghiệm thu 25/08 truy ra hành vi lúc đó vẫn ĐÚNG (tài khoản
    /// mới vẫn nhận 3, vì bản client luôn gửi <c>so_ngay</c> tường minh), nhưng ba hằng cho CÙNG một khái niệm mà
    /// mang hai giá trị là một cái bẫy đang chờ: chỉ cần một đường nào đó đọc giá trị khởi tạo này (hub CŨ không
    /// gửi field, hoặc một client mới ghi thiếu) là chu kỳ nhảy về 7 ngày, câm lặng.</para></summary>
    public int SoNgay { get; init; } = 3;

    /// <summary>% giảm áp chung cho mọi sản phẩm.</summary>
    public int PhanTram { get; init; } = 5;

    /// <summary>Giờ bắt đầu khuyến mãi, <c>HH:mm</c> (giờ địa phương — là chuỗi người dùng gõ trên form).</summary>
    public string GioBatDau { get; init; } = "";

    /// <summary>Giờ kết thúc khuyến mãi, <c>HH:mm</c>.</summary>
    public string GioKetThuc { get; init; } = "";

    /// <summary>MÁY CHỦ TRÌ = máy đã mở form KM và lưu cấu hình gần nhất, hoặc đã bấm "Máy này nhận chủ trì"
    /// (id suất đơn hàng). Rỗng = chưa máy nào nhận chủ trì (dữ liệu cũ) ⇒ bên đọc phải rơi về hành vi hiện tại.
    /// <para><b>ĐÃ CÓ NGƯỜI ĐỌC</b> (22/08/2026): <c>KmLichTuDong.LaMayChuTri</c> chặn hai cửa — máy không chủ
    /// trì thì KHÔNG chạy lượt KM tự động, và KHÔNG nhường khung check đơn 2–6h. Đừng tin lại câu cũ "phần đọc
    /// thuộc pha C".</para></summary>
    public string OwnerMachine { get; init; } = "";

    /// <summary>Mốc SỬA CẤU HÌNH gần nhất (UTC). Là trọng tài của luật "máy nào sửa sau thắng" trong
    /// <see cref="KmHoSoGop.Gop"/>; <c>null</c> = máy này chưa từng mở form ⇒ thua mọi giá trị.</summary>
    public DateTimeOffset? CfgUpdatedAt { get; init; }

    /// <summary>Mốc lần TẠO ĐƯỢC gần nhất (UTC) — gốc tính chu kỳ "sau xx ngày". Gộp theo MAX.</summary>
    public DateTimeOffset? LanChayCuoi { get; init; }

    /// <summary>Mốc lần lịch tự động BẮT ĐẦU THỬ gần nhất (UTC) — chốt "mỗi tài khoản một lượt thử mỗi khung
    /// 02–04h". Gộp theo MAX: mốc này là thứ chặn hai máy cùng thử trong một đêm.</summary>
    public DateTimeOffset? LanThuTuDong { get; init; }

    // ── COMBO KHUYẾN MÃI (25/08/2026) — chương trình thứ HAI, khung 06–08h ────────────────────────
    // Đi theo ĐÚNG HAI LUẬT SẴN CÓ, KHÔNG đẻ luật gộp thứ ba: 7 trường cấu hình thuộc nhóm "máy nào sửa sau
    // thắng" (`CfgUpdatedAt` chung, đi theo GÓI cùng cấu hình KM), 2 mốc chạy thuộc nhóm MAX.
    // Mọi trường là property `init` có giá trị khởi tạo ⇒ hub CŨ / client CŨ (không gửi field) vẫn parse
    // bình thường và nhận đúng mặc định — luật đã ghi ở khối chú thích trên `OrdersAccountItem`.

    /// <summary>Bật vòng COMBO tự động — độc lập với <see cref="TuDong"/>.</summary>
    public bool ComboBat { get; init; }

    /// <summary>
    /// Giờ bắt đầu chương trình combo, <c>HH:mm</c> (giờ địa phương của người dùng).
    /// <para><b>⚠ Mặc định phải KHỚP mặc định của <c>km_config</c> ("11:00"), KHÔNG được để rỗng.</b> Field này
    /// vắng mặt trong JSON của HUB CŨ (chưa deploy bản có cột combo) ⇒ client nhận giá trị khởi tạo ở đây rồi
    /// GHI THẲNG xuống <c>km_config</c>. Để <c>""</c> thì lượt kéo đầu tiên xoá trắng giờ combo của người dùng,
    /// và đêm sau vòng combo chết ở cửa "CẤU HÌNH KHÔNG DÙNG ĐƯỢC" — im lặng, không ai hiểu vì sao.
    /// Đo được 25/08/2026 bằng <c>KmVongBonTests.MocSuaMoiHonMaHopLe_DuChiMinhNoKhac_ThiVanGhi</c>.</para>
    /// </summary>
    public string ComboGioBatDau { get; init; } = "11:00";

    /// <summary>Giờ kết thúc chương trình combo, <c>HH:mm</c>. Mặc định khớp <c>km_config</c> — xem
    /// <see cref="ComboGioBatDau"/> cho lý do KHÔNG được để rỗng.</summary>
    public string ComboGioKetThuc { get; init; } = "12:00";

    /// <summary>Loại combo: 2 = giảm %, 3 = giảm số tiền, 1 = giá đặc biệt.</summary>
    public int ComboLoai { get; init; } = 3;

    /// <summary>Mua bao nhiêu sản phẩm mới được ưu đãi.</summary>
    public int ComboMuaSl { get; init; } = 2;

    /// <summary>Mức giảm (đồng nếu <see cref="ComboLoai"/> = 3, % nếu = 2).</summary>
    public int ComboGiam { get; init; } = 2000;

    /// <summary>Giới hạn đặt hàng.</summary>
    public int ComboGioiHan { get; init; } = 3;

    /// <summary>Mốc lần TẠO ĐƯỢC gần nhất của vòng COMBO (UTC). Gộp theo MAX.
    /// <para><b>⚠ TÁCH khỏi <see cref="LanChayCuoi"/></b> — gộp hai mốc là vòng KM tự tay khoá vòng combo
    /// (xem <c>KmLichTuDong.MocThuCua</c>).</para></summary>
    public DateTimeOffset? ComboLanChayCuoi { get; init; }

    /// <summary>Mốc vòng COMBO bắt đầu THỬ gần nhất (UTC) — chốt "mỗi khung 06–08h một lượt thử". Gộp theo MAX.</summary>
    public DateTimeOffset? ComboLanThuTuDong { get; init; }

    // ── FLASH SALE CỦA SHOP (28/08/2026) — chương trình thứ BA, chạy CẢ NGÀY thành nhiều GÓI (01/09/2026) ──
    // Đúng HAI LUẬT sẵn có, không đẻ luật gộp thứ ba: công tắc thuộc nhóm "máy nào sửa sau thắng" (đi theo
    // GÓI cùng `CfgUpdatedAt`), hai mốc chạy thuộc nhóm MAX.

    /// <summary>Bật vòng FLASH SALE tự động — độc lập với <see cref="TuDong"/> và <see cref="ComboBat"/>.
    /// <para><b>Mặc định <c>false</c> và điều đó là bắt buộc:</b> field này vắng mặt trong JSON của HUB CŨ, và
    /// client nhận giá trị khởi tạo ở đây rồi GHI THẲNG xuống <c>km_config</c>. Mặc định <c>true</c> sẽ BẬT một
    /// vòng tạo chương trình THẬT trên mọi máy ngay lượt kéo đầu tiên.</para></summary>
    public bool FlashBat { get; init; }

    /// <summary>Mốc <b>NGÀY ĐÍCH đã phủ ĐỦ MỌI SHOP</b> của vòng FLASH (UTC). Gộp theo MAX. <b>TÁCH</b> khỏi hai
    /// mốc kia — xem <c>KmLichTuDong.MocThuCua</c>.
    /// <para><b>⚠ Ý NGHĨA ĐỔI 01/09/2026</b> — trước đó là "lượt gần nhất TẠO ĐƯỢC gì đó". Vì một ngày nay chia
    /// thành nhiều GÓI nhỏ, luật cũ để gói ĐẦU ghi mốc rồi khoá sạch các gói còn lại. Nay chỉ ghi khi cả danh
    /// sách shop của ngày đã xong (<c>AccountsViewModel.ChotLuotFlash</c>), và nó là thứ chặn "mở gói tiếp cho
    /// một ngày đã xong".</para></summary>
    public DateTimeOffset? FlashLanChayCuoi { get; init; }

    /// <summary>Mốc vòng FLASH bắt đầu THỬ gần nhất (UTC) — nay là mốc đo <b>GIÃN CÁCH giữa hai GÓI</b>
    /// (<c>KmLichTuDong.PhutGianCachGoiFlash</c> = 30 phút); trước 01/09/2026 là chốt "mỗi khung 21–24h một lượt
    /// thử", thứ đã chết theo khung tối. Gộp theo MAX.
    /// <para>Đây là mốc CHỐNG nhịp 60 giây phóng lại Brave — khung nay là CẢ NGÀY nên không có nó thì một gói
    /// hỏng nhanh sẽ được mở lại MỖI PHÚT; xem <c>KmConfig.FlashLanThuTuDong</c>.</para></summary>
    public DateTimeOffset? FlashLanThuTuDong { get; init; }

    /// <summary>
    /// <b>%GIẢM áp HÀNG LOẠT của vòng flash</b> (ô thứ nhất khối "Chỉnh sửa hàng loạt"); <c>null</c> = <b>bên
    /// gửi KHÔNG BIẾT trường này</b>.
    ///
    /// <para><b>⚠ NULLABLE, KHÔNG phải <c>int</c> có mặc định 5 — và đó là cả lý do trường này có hình dạng
    /// ấy</b> (03/09/2026). Hai trường flash mới nằm trong nhóm "máy nào sửa sau thắng", tức đi theo GÓI: bản
    /// thắng mang TRỌN cấu hình của nó sang. Nếu khai <c>int = 5</c> thì một HUB CŨ (chưa deploy bản có cột) trả
    /// JSON không có field ⇒ client nhận <c>5</c> ⇒ ghi thẳng xuống <c>km_config</c> ⇒ <b>người dùng vừa đặt 10%
    /// bị reset về 5% ở lượt kéo kế tiếp</b>, đúng 60 giây sau, không một dòng log. Với <c>null</c>, chỗ ánh xạ
    /// (<c>OrdersModuleHost.ApNhomCauHinh</c>) đọc <c>hoSo.X ?? cu.X</c> và GIỮ giá trị cục bộ.</para>
    ///
    /// <para>Đây là cùng ca hỏng mà xmldoc <see cref="ComboGioBatDau"/> mô tả, nhưng ở đó cách chữa là "mặc định
    /// phải khớp mặc định của <c>km_config</c>" — không dùng lại được ở đây, vì hai con số này là thứ người dùng
    /// GÕ và có thể là bất kỳ số nào, không phải một mặc định biết trước.</para>
    /// </summary>
    public int? FlashPhanTramGiam { get; init; }

    /// <summary>"SL sản phẩm khuyến mãi" áp HÀNG LOẠT (ô thứ hai cùng khối); <c>null</c> = bên gửi không biết
    /// trường này. <b>Nullable cùng lý do với <see cref="FlashPhanTramGiam"/></b> — xem xmldoc ở đó.</summary>
    public int? FlashSoLuongThamGia { get; init; }

    /// <summary>Mặc định của hai trường flash — dùng ở phía HUB khi ghi xuống <c>km_profiles</c> cho một bên gửi
    /// KHÔNG BIẾT trường (<c>null</c>): cột phải nhận đúng con số mặc định của <c>km_config</c>, không phải 0.
    /// <para><b>PHẢI khớp <c>KmConfig.MacDinhFlashPhanTramGiam</c>/<c>MacDinhFlashSoLuongThamGia</c> và
    /// <c>DEFAULT</c> của hai cột ở CẢ hai DB</b> — cùng ràng buộc đã ghi ở <see cref="SoNgay"/> cho con số 3.
    /// Hai lớp không tham chiếu nhau được (<c>XuLyDonShopee.Core</c> ↔ <c>Shopee.Core</c>) nên đây là hai hằng
    /// cho một khái niệm; ràng buộc giữ bằng chú thích này và bằng test.</para>
    /// <para><b>THỨ TỰ DEPLOY — và điều kiện của nó</b> (siết lại 03/09/2026, phản biện vòng 2): "deploy server
    /// trước cũng an toàn" chỉ đúng vì một client CŨ (gửi <c>null</c>) KHÔNG BAO GIỜ đè được lên giá trị đang
    /// có — luật gộp lấy <c>nen.X ?? khac.X</c> và câu upsert lấy <c>COALESCE($fptg, flash_phan_tram_giam)</c>.
    /// Hằng dưới đây vì thế chỉ được dùng ở ĐÚNG một ca: dòng CHƯA TỒN TẠI và không bên nào biết trường. Bỏ một
    /// trong hai lớp <c>??</c>/<c>COALESCE</c> là biến hằng này thành thứ ghi đè cấu hình thật.</para></summary>
    public const int MacDinhFlashPhanTramGiam = 5;

    /// <inheritdoc cref="MacDinhFlashPhanTramGiam"/>
    public const int MacDinhFlashSoLuongThamGia = 10;
}

/// <summary>Client đẩy hồ sơ KM của các tài khoản trên máy mình lên hub. Hub GỘP từng dòng theo
/// <see cref="KmHoSoGop.Gop"/> (khoá <c>login</c>) — <b>KHÔNG</b> thay trọn bảng, và KHÔNG đụng login mà lượt
/// đẩy này không nhắc tới. <see cref="MachineId"/> chỉ để ghi log/biết máy nào đẩy.</summary>
public sealed record KmProfilesPushRequest(string MachineId, List<KmProfileItem> Profiles);

/// <summary>
/// <b>Phản hồi của <c>POST /orders/km</c> — MANG THEO BẢN ĐÃ GỘP để client ghi đè lại chính mình.</b>
/// <para><b>Vì sao lượt đẩy phải TRẢ dữ liệu</b> (phản biện vòng 3, chốt 22/08/2026): <c>KmHoSoGop.KepMocSua</c>
/// kẹp mốc sửa rác về "bây giờ" của <b>bên đang đọc</b>. Máy sai đồng hồ (đóng dấu 2028) kéo hồ sơ về thì trên
/// chính nó 2028 KHÔNG phải tương lai ⇒ lượt gộp không đổi gì ⇒ nó giữ nguyên 2028 và <b>đẩy lại đúng mốc ấy ở
/// mỗi lần mở app</b>, mỗi lần lại lùi mất sửa đổi của máy khác. Lặp vô hạn, và nhật ký ở máy khác chỉ nói
/// "cấu hình vừa ĐỔI" — tức nói dối, vì không ai sửa gì. Khép vòng bằng cách để HUB làm nguồn giờ ngoài máy đó:
/// bản gộp đi ngược về client và client ghi đè <c>km_config</c> của mình bằng nó.</para>
/// <para><b>Hub CŨ trả body khác</b> (bản trước trả <c>{"profiles": n}</c>) ⇒ <see cref="HoSoDaGop"/> giữ giá
/// trị khởi tạo là danh sách RỖNG ⇒ client bỏ qua, KHÔNG ném và KHÔNG đổi gì. Mọi trường là property
/// <c>init</c> trong thân record đúng luật ở khối chú thích trên <see cref="OrdersAccountItem"/>.</para>
/// </summary>
/// <summary>Client xin/trả KHUNG cho một lượt KM. <paramref name="MachineId"/> = id suất đơn hàng của máy GỌI —
/// hub dùng nó để không tự dừng chính máy đang xin.</summary>
public sealed record KmKhungRequest(string Login, string MachineId);

/// <summary>
/// Trả lời lượt xin khung. <paramref name="Trong"/> = khoá tài khoản đang trống (hoặc chính máy xin đang giữ)
/// ⇒ client cứ việc xin khoá và chạy.
/// <para><paramref name="Trong"/> = false ⇒ hub VỪA ra lệnh Dừng cho <paramref name="DangDung"/> (tên máy, để
/// client ghi nhật ký cho người đọc hiểu đang chờ ai). Client chờ vài giây rồi gọi lại; lượt gọi khi khoá đã
/// trống trả true mà KHÔNG tạo thêm lệnh nào.</para>
/// </summary>
public sealed record KmXinKhungResponse(bool Trong, string DangDung, string? Loi);

public sealed record KmProfilesPushResponse
{
    /// <summary>Số dòng hub đã ghi (giữ đúng tên trường của body bản cũ để không đổi hợp đồng đang có).</summary>
    public int Profiles { get; init; }

    /// <summary>Bản ĐÃ GỘP của đúng những login vừa đẩy (mốc UTC). Rỗng = hub cũ / không có gì để trả.</summary>
    public List<KmProfileItem> HoSoDaGop { get; init; } = [];
}

// ── Lệnh Hub → SUẤT ĐƠN HÀNG (đi trong phản hồi heartbeat + ack) ──────────────

/// <summary>Hành động hub ra lệnh cho một tài khoản Đơn hàng trên một máy.</summary>
public static class OrdersCommandActions
{
    /// <summary>Mở phiên CHECK ĐƠN. <b>Cũng do hub TỰ phát</b> khi trả khung KM xuyên máy
    /// (<c>HubDatabase.KmKhung</c>) — xem <see cref="Stop"/>.</summary>
    public const string Run = "run";

    /// <summary>
    /// Dừng RIÊNG phiên check đơn của tài khoản trên máy đó — <b>KHÔNG</b> đụng lượt KM/Combo/Flash đang chạy,
    /// KHÔNG huỷ lượt hoãn, KHÔNG đặt cờ "người dùng dừng tay" (nên gói xong là app tự chạy lại check đơn).
    /// <para><b>⚠ ĐỪNG ĐỔI NGHĨA HÀNH ĐỘNG NÀY.</b> Nó là công cụ của TỰ ĐỘNG HOÁ khung xuyên máy chứ không
    /// phải nút của người dùng: <c>HubDatabase.KmKhung</c> phát <c>stop</c> để bảo máy đang giữ check đơn nhả
    /// khung cho một lượt KM ở máy khác, rồi phát <see cref="Run"/> trả khung lại. Cho nó dừng luôn lượt đang
    /// chạy / huỷ lượt hoãn là mỗi lần nhường khung lại giết đúng thứ vừa xin khung.</para>
    /// <para>Nút "✖ Dừng" của NGƯỜI DÙNG trên hub đi bằng <see cref="StopAll"/>.</para>
    /// </summary>
    public const string Stop = "stop";

    /// <summary>Đồng bộ đơn MỘT lượt. CHƯA hỗ trợ ở bản này (client không có điểm vào cấp service — phiên là một
    /// vòng liên tục login→mọi shop→sync), client ack 'failed' nếu nhận được.</summary>
    public const string SyncOnce = "sync-once";
    /// <summary>Đăng nhập lại / kiểm tra tài khoản. CHƯA hỗ trợ ở bản này — xem <see cref="SyncOnce"/>.</summary>
    public const string Relogin = "relogin";

    // ── Bốn hành động NGƯỜI DÙNG bấm trên hub (03/09/2026) ────────────────────────────────────────────
    // Cả bốn đi CHUNG một cửa vào ở client: `AppServices.LenhTuHub` → `AccountsViewModel.XuLyLenhTuHub`, nơi
    // giữ luật ƯU TIÊN FLASH. `Sessions.Start` gọi thẳng từ luồng poll (bản trước) đi VÒNG qua cửa từ chối của
    // app, tức từ hub mở được check đơn ĐÈ lên một gói flash đang chạy — cùng hồ sơ Brave, cùng cổng 47821.

    /// <summary>"✖ Dừng" — dừng MỌI hoạt động của ĐÚNG MỘT tài khoản: check đơn <b>và</b> lượt KM/Combo/Flash
    /// đang chạy CỦA nó <b>và</b> lượt hoãn chạy lại của nó. KHÔNG đụng tài khoản khác trên cùng máy (người dùng
    /// chốt 04/09/2026: "1 acc chỉ chạy trên 1 máy, nên dừng là dừng ACC chứ không dừng client"). KHÔNG tắt lịch
    /// tự động. Chuỗi giữ nguyên <c>stop-all</c> ("dừng tất cả CỦA acc này"), khác <see cref="Stop"/>.</summary>
    public const string StopAll = "stop-all";

    /// <summary>"▶ KM" — chạy vòng "KM của Shop" bằng CẤU HÌNH ĐÃ LƯU của tài khoản.</summary>
    public const string RunKm = "run-km";

    /// <summary>"▶ Combo" — chạy vòng "Combo Khuyến Mãi" bằng cấu hình đã lưu.</summary>
    public const string RunCombo = "run-combo";

    /// <summary>"▶ Flash" — chạy một gói "Flash Sale Của Shop" (ngày đích D+1) bằng cấu hình đã lưu.</summary>
    public const string RunFlash = "run-flash";
}

/// <summary>Vòng đời một lệnh: hub tạo 'pending' → nhịp heartbeat lấy đi thành 'sent' → client ack 'done'/'failed'.
/// 'sent' quá lâu không ack → hub tự quy về 'failed' (client không phản hồi), không kẹt vĩnh viễn.</summary>
public static class OrdersCommandStatuses
{
    public const string Pending = "pending";
    public const string Sent = "sent";
    public const string Done = "done";
    public const string Failed = "failed";
}

/// <summary>Một lệnh hub gửi xuống suất đơn hàng trong phản hồi heartbeat. <see cref="Id"/> là khoá DEDUP:
/// nhịp có thể lặp khi mạng chập chờn, client PHẢI thực thi mỗi Id đúng một lần (chạy lại 'run' giữa chừng một
/// phiên đang chạy là mở lại trình duyệt — hỏng thật).</summary>
public sealed record OrdersCommandDto(string Id, string Login, string Action);

/// <summary>Client báo kết quả thực thi một lệnh về hub. <see cref="Status"/> ∈
/// <see cref="OrdersCommandStatuses.Done"/> | <see cref="OrdersCommandStatuses.Failed"/>.</summary>
public sealed record OrdersCommandAckRequest(string Id, string Status, string? Error);

public sealed record FilePutResponse(bool Ok, int Version, string? Conflict);

/// <summary>Kết quả upsert acc BigSeller từ client lên hub (POST /bigseller/upsert): số acc mới thêm, số acc đã
/// có được cập nhật (field chung hoặc shop), tổng số shop mới thêm. Hub KHÔNG bao giờ xoá acc/shop.</summary>
public sealed record BigSellerUpsertResult(int Added, int Updated, int ShopsAdded);

/// <summary>Client nhờ Hub đăng nhập lại 1 acc BigSeller (gặp verify/mất phiên). <see cref="MachineId"/> để Hub
/// ghi log biết máy nào xin. CỐ Ý không có mật khẩu: credential nằm SẴN trên hub, đừng đẩy qua dây.</summary>
public sealed record BigSellerReloginRequest(string AccountId, string MachineId);

/// <summary>Trạng thái phiên login trên Hub. <see cref="Status"/> = idle|running|needsOtp|success|failed
/// (nguyên văn LoginState.Status). <see cref="Accepted"/> = Hub vừa BẮT ĐẦU phiên mới cho lượt xin này
/// (false = đã có phiên đang chạy → client cứ chờ phiên đó).</summary>
public sealed record BigSellerReloginResponse(bool Accepted, string Status, string Message);
