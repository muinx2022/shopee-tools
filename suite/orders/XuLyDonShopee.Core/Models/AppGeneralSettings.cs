using System.Globalization;

namespace XuLyDonShopee.Core.Models;

/// <summary>
/// Cấu hình CHUNG của ứng dụng (màn "Cài đặt"):
/// <list type="bullet">
/// <item><see cref="InvoiceFolder"/> — thư mục lưu phiếu/hóa đơn người dùng chọn; RỖNG nghĩa là "dùng mặc
/// định cạnh app.db", được <see cref="Data.SettingsRepository.GetInvoiceFolder"/> phân giải bằng đường dẫn DB.</item>
/// <item><see cref="OrderIntervalMinutes"/> — số phút giữa các lần tự đọc số "Chờ Lấy Hàng" (kẹp [1,1440]).</item>
/// </list>
/// Bất biến (record) + các hàm thuần <see cref="Parse"/>/<see cref="Normalize"/> để đọc/ghi bảng
/// <c>settings</c> và test được không cần DB.
/// </summary>
public sealed record AppGeneralSettings(string InvoiceFolder, int OrderIntervalMinutes)
{
    /// <summary>
    /// Chu kỳ theo dõi đơn mặc định (phút) — <b>120'</b> (người dùng chốt 01/09/2026; trước đó 90', và trước
    /// nữa là 30' cố định trong <c>AccountSession.RunAsync</c>).
    /// <para><b>Vì sao NỚI</b>: từ 01/09 vòng Flash Sale chạy thành nhiều GÓI NHỎ, mỗi gói chiếm đúng KHE NGHỈ
    /// giữa hai vòng check đơn — nên khe nghỉ phải đủ chỗ cho một gói ~2 shop (2 × ~40 phút). Khe ngắn hơn
    /// <c>KmLichTuDong.PhutKheToiThieuChoGoi</c> (75') thì nhịp bỏ qua hẳn, tức flash sẽ không bao giờ chạy.
    /// Người dùng cũng nhận xét độc lập rằng 90' "vẫn hơi dày".</para>
    /// <para>Đây là MẶC ĐỊNH cho máy mới / DB chưa có khoá <c>order_interval_minutes</c>; máy đã lưu giá trị cũ
    /// giữ nguyên giá trị đó cho tới khi người dùng (hoặc bước deploy) đổi.</para>
    /// </summary>
    public const int DefaultOrderIntervalMinutes = 120;

    /// <summary>Chu kỳ theo dõi đơn tối thiểu (phút) — không cho ≤ 0 kẻo poll liên tục.</summary>
    public const int MinOrderIntervalMinutes = 1;

    /// <summary>Chu kỳ theo dõi đơn tối đa (phút = 1 ngày). Chặn <c>DateTime.AddMinutes</c> khổng lồ vô nghĩa.</summary>
    public const int MaxOrderIntervalMinutes = 1440;

    /// <summary>Mặc định: thư mục rỗng (⇒ dùng mặc định cạnh app.db), chu kỳ
    /// <see cref="DefaultOrderIntervalMinutes"/>.</summary>
    public static AppGeneralSettings Default => new(string.Empty, DefaultOrderIntervalMinutes);

    /// <summary>
    /// Chuẩn hóa: trim thư mục (null → rỗng) và kẹp <see cref="OrderIntervalMinutes"/> vào
    /// [<see cref="MinOrderIntervalMinutes"/>, <see cref="MaxOrderIntervalMinutes"/>] (số người dùng nhập /
    /// DB hỏng có thể ≤ 0 hoặc khổng lồ).
    /// </summary>
    public static AppGeneralSettings Normalize(string? invoiceFolder, int orderIntervalMinutes)
        => new(
            (invoiceFolder ?? string.Empty).Trim(),
            Clamp(orderIntervalMinutes, MinOrderIntervalMinutes, MaxOrderIntervalMinutes));

    private static int Clamp(int value, int min, int max)
        => value < min ? min : (value > max ? max : value);

    /// <summary>
    /// Dựng từ các chuỗi lưu trong bảng <c>settings</c> (chu kỳ null/rỗng/hỏng → <see cref="DefaultOrderIntervalMinutes"/>) rồi
    /// <see cref="Normalize"/>. Không ném — mọi giá trị lạ đều rơi về mặc định an toàn.
    /// </summary>
    public static AppGeneralSettings Parse(string? invoiceFolder, string? orderIntervalMinutes)
        => Normalize(invoiceFolder, ParseInt(orderIntervalMinutes, DefaultOrderIntervalMinutes));

    private static int ParseInt(string? s, int fallback)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    /// <summary>Chuỗi lưu DB cho một số nguyên (invariant culture).</summary>
    public static string IntToStorage(int value) => value.ToString(CultureInfo.InvariantCulture);
}
