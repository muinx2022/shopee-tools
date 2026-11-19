namespace Shopee.Hub.Web.Services;

/// <summary>Cấu hình khởi động của web-hub (từ appsettings / biến môi trường). Bất biến sau khi khởi động;
/// các trạng thái thay đổi lúc chạy (admin, api token, cờ điều phối) nằm ở bảng <c>settings</c> trong DB.</summary>
public sealed class HubOptions
{
    /// <summary>Thư mục dữ liệu: hub.db + files\ + dp-keys\ + backups\. Env HUB_DATA_DIR ưu tiên.</summary>
    public string DataDir { get; set; } = "";

    /// <summary>Cho phép client (bản WPF cũ) PUT đè config/*.json không. Sau cutover đặt false để web là
    /// nguồn sự thật duy nhất — client cũ đẩy config bị 403 (nuốt lỗi phía client, không hại).</summary>
    public bool AllowClientConfigPush { get; set; } = true;

    /// <summary>Base URL CÔNG KHAI của Hub (vd <c>https://api.schedra.net</c>) để trả URL tuyệt đối cho ảnh ở kho
    /// <c>/api/media</c>. Phải là https tuyệt đối, không query/fragment. Rỗng hoặc sai dạng = coi như chưa cấu hình: upload
    /// ảnh trả 503 <c>public-base-url-missing</c>, không ghi file (log cảnh báo lúc khởi động). Env HUB_PUBLIC_BASE_URL
    /// ưu tiên.</summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>Số ngày ảnh trong kho công khai còn sống (mặc định 7). Hạn ghi chết vào tên file lúc upload — đổi số
    /// này chỉ áp cho ảnh upload sau.</summary>
    public int MediaRetentionDays { get; set; } = 7;

    /// <summary>Trần dung lượng tổng của kho ảnh công khai (byte, mặc định 20 GB). Vượt thì từ chối upload (507
    /// <c>media-quota-exceeded</c>), không xoá ảnh chưa hết hạn.</summary>
    public long MediaMaxTotalBytes { get; set; } = 20480L * 1024 * 1024;

    /// <summary>Đĩa chứa kho ảnh phải còn trống ít nhất chừng này (byte, mặc định 5 GB) SAU khi ghi ảnh mới; không thì từ
    /// chối upload (507 <c>disk-low</c>) — kho ảnh không được làm đầy đĩa của hub.db.</summary>
    public long MediaMinFreeBytes { get; set; } = 5120L * 1024 * 1024;

    /// <summary>Trần dung lượng tổng của kho ẢNH GỐC <c>DataDir/source-images/</c> (byte, mặc định 15 GB; cấu hình
    /// <c>Hub:SourceImages:MaxTotalMb</c>). Vượt thì từ chối ghi (507 <c>source-quota-exceeded</c>), không tự xoá ảnh nào.
    /// Đĩa trống tối thiểu dùng chung <see cref="MediaMinFreeBytes"/> (cùng một ổ, cùng mục đích giữ chỗ cho hub.db).</summary>
    public long SourceImagesMaxTotalBytes { get; set; } = 15360L * 1024 * 1024;

    /// <summary>Giới hạn tần suất link ảnh đổi MD5 CÔNG KHAI <c>/api/v/…</c> theo IP — TOKEN BUCKET: <see cref="SourceImagePublicBurst"/>
    /// = sức chứa (số request dồn một lúc, cấu hình <c>Hub:SourceImages:PublicBurst</c>, mặc định 3000), nạp lại
    /// <see cref="SourceImagePublicRatePerMinute"/> / phút (<c>Hub:SourceImages:PublicRatePerMinute</c>, mặc định 3000; ≥ 60
    /// thì nạp đều mỗi giây, &lt; 60 thì nạp từng token). ≤ 0 = 1. Vượt → 429 + Retry-After + log (gộp 1 dòng/IP/phút).
    /// Mức THOÁNG có chủ ý: 1 file Đăng Hàng Loạt có thể 1.300–3.200 link (SP nhiều phân loại có ảnh) và Shopee có thể kéo dồn
    /// cả file từ MỘT IP trong vài chục giây; chặn nhầm Shopee là hỏng ảnh SP. Mục đích chỉ là chặn một IP gọi dồn dập kéo dài
    /// (mỗi request đọc ảnh gốc ≤ 2 MB + tính MD5).</summary>
    public int SourceImagePublicBurst { get; set; } = 3000;

    /// <summary>Tốc độ nạp lại (request / phút / IP) — xem <see cref="SourceImagePublicBurst"/>.</summary>
    public int SourceImagePublicRatePerMinute { get; set; } = 3000;
}

/// <summary>Khoá bảng <c>settings</c> dùng trong DB (gom 1 nơi tránh gõ sai chuỗi).</summary>
public static class SettingKeys
{
    public const string ApiToken = "api.token";
    public const string AdminUser = "admin.user";
    public const string AdminHash = "admin.hash";
    public const string AdminSalt = "admin.salt";
    public const string AdminIter = "admin.iter";
    public const string DispatcherEnabled = "dispatcher.enabled";
    public const string DispatcherAuto = "dispatcher.auto";
    /// <summary>LEGACY: nhiều dòng URL báo đơn mới. Còn đọc để migrate → <see cref="NotifyWebhookDonMoi"/>.</summary>
    public const string NotifyWebhooks = "notify.webhooks";

    /// <summary>Webhook khi client push đơn MỚI (1 URL). Trống = tắt.</summary>
    public const string NotifyWebhookDonMoi = "notify.webhook_don_moi";

    /// <summary>Webhook lỗi app — hub lưu đồng bộ UI; client gửi. Trống = tắt.</summary>
    public const string NotifyWebhookLoiApp = "notify.webhook_loi_app";

    /// <summary>Webhook đơn trả hàng — hub lưu đồng bộ UI; client gửi. Trống = tắt.</summary>
    public const string NotifyWebhookDonTra = "notify.webhook_don_tra";

    /// <summary>Webhook "máy client rơi offline khi đang giữ việc" (1 URL). Trống = tắt. KHÔNG lùi về ô legacy
    /// <see cref="NotifyWebhooks"/>: đó là lưới an toàn cho 3 kênh CŨ được tách ra từ nó, kênh mới này chưa từng
    /// nằm trong đó nên lùi về sẽ bắn tin máy-offline vào kênh "đơn mới".</summary>
    public const string NotifyWebhookMayOffline = "notify.webhook_may_offline";

    /// <summary>Bật cảnh báo máy offline: "1" = bật, còn lại (kể cả thiếu khoá) = tắt.</summary>
    public const string NotifyMayOfflineBat = "notify.may_offline_bat";

    /// <summary>Ngưỡng mất nhịp (PHÚT) trước khi báo máy offline. Thiếu/không parse được → mặc định
    /// <c>MachineOfflineWatchService.NguongMacDinhPhut</c>.</summary>
    public const string NotifyMayOfflinePhut = "notify.may_offline_phut";

    /// <summary>Webhook TIN TỔNG KẾT CUỐI NGÀY (1 URL). Trống = tắt. KHÔNG lùi về ô legacy
    /// <see cref="NotifyWebhooks"/> (cùng lý do như kênh máy offline: kênh mới chưa từng nằm trong đó).</summary>
    public const string NotifyWebhookTongKet = "notify.webhook_tong_ket";

    /// <summary>Bật tin tổng kết cuối ngày: "1" = bật, còn lại (kể cả thiếu khoá) = tắt.</summary>
    public const string NotifyTongKetBat = "notify.tong_ket_bat";

    /// <summary>GIỜ gửi tin tổng kết (0–23, giờ Việt Nam). Thiếu/không parse được → <c>DailyDigest.GioMacDinh</c>.</summary>
    public const string NotifyTongKetGio = "notify.tong_ket_gio";

    /// <summary>NGÀY VIỆT NAM (<c>yyyy-MM-dd</c>) của tin tổng kết ĐÃ GỬI gần nhất — chống gửi trùng khi hub
    /// restart quanh giờ gửi. Do <c>DailyDigestService</c> ghi, KHÔNG có ô nhập trên /settings.</summary>
    public const string NotifyTongKetDaGuiNgay = "notify.tong_ket_da_gui_ngay";

    /// <summary>KHOÁ BÍ MẬT ký link ảnh đổi MD5 công khai (<c>/api/v/…</c>, GĐ9): 32 byte ngẫu nhiên dạng base64, Hub tự
    /// sinh MỘT lần lúc khởi động đầu tiên (<c>SourceImageLinkSigner</c>). KHÔNG BAO GIỜ tự xoay, KHÔNG có ô trên /settings:
    /// đổi / mất khoá là MỌI link ảnh trong các file Excel đã xuất hỏng hết.</summary>
    public const string SourceImageLinkKey = "source_images.link_key";
}
