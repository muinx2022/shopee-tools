using System.Text.RegularExpressions;

namespace Shopee.Core.Coordination;

// Hợp đồng kho ẢNH GỐC trên Hub (GĐ9, plan workspace/plans/2026-11-17-app-dang-sp-shopee-mass-upload.md).
// File có HAI bản GIỐNG HỆT: suite/suite/Shopee.Core/Coordination/SourceImageDtos.cs (Hub link bản này) và
// workspace/src/Shopee.Core/Coordination/SourceImageDtos.cs (app Harvest / Upload) — sửa thì sửa cả hai.

/// <summary>Giới hạn + luật mã ảnh của kho ảnh gốc (<see cref="HubRoutes.SourceImages"/>).</summary>
public static class SourceImageLimits
{
    /// <summary>Cỡ tối đa 1 ảnh gốc (byte) — quá thì Hub trả 413 <c>too-large</c>. Bằng trần của
    /// <see cref="PublicMediaLimits.MaxBytes"/>: ảnh lớn hơn thì bước đổi MD5 (<c>ImageMd5Changer</c>, cả app lẫn Hub) không
    /// nhận, lưu cũng vô dụng. Ảnh SÁT trần (bản đổi MD5 vượt trần: thiếu &lt; 68 byte với JPEG, &lt; 84 byte với PNG) cũng 413.</summary>
    public const int MaxBytes = PublicMediaLimits.MaxBytes;

    /// <summary>Số id tối đa mỗi lần hỏi <see cref="HubRoutes.SourceImagesMissing"/> / <see cref="HubRoutes.SourceImagesLinks"/>
    /// (đếm CẢ id trùng) — quá thì 400 <c>too-many-ids</c>; client tự chia lô.</summary>
    public const int MaxIdsPerCall = 200;

    /// <summary>Độ dài tối đa của <see cref="SourceImageLinksRequest.ShopKey"/> (ký tự).</summary>
    public const int MaxShopKeyLength = 100;

    /// <summary>Chuỗi xác nhận BẮT BUỘC trong body của DELETE <see cref="HubRoutes.SourceImages"/> (xoá sạch kho gốc).</summary>
    public const string DeleteAllConfirm = "XOA-HET-ANH-GOC";

    // Mã ảnh Shopee (vd "vn-11134207-7r98o-lx1abc…", hoặc 32 hex kiểu cũ): chữ, số, '-', '_'; ký tự đầu là chữ/số; dài
    // 8..128. Không có '.', '/', '\' ⇒ không thể đi ra ngoài thư mục kho; tối thiểu 8 ký tự ⇒ không trùng tên thiết bị
    // Windows (CON, NUL, COM1…) và không trùng các đoạn route tĩnh (stats, missing, links).
    private static readonly Regex IdRule = new(@"^[A-Za-z0-9][A-Za-z0-9_-]{7,127}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>id có đúng luật mã ảnh không. Phân biệt hoa thường (id dùng nguyên văn làm khoá HMAC đổi MD5).</summary>
    public static bool IsValidId(string? id) => id is not null && IdRule.IsMatch(id);
}

/// <summary>Body của <see cref="HubRoutes.SourceImagesMissing"/>: ≤ <see cref="SourceImageLimits.MaxIdsPerCall"/> id.</summary>
public sealed record SourceImageIdsRequest(List<string>? Ids);

/// <summary>Kết quả của <see cref="HubRoutes.SourceImagesMissing"/>: các id kho gốc CHƯA có ảnh DÙNG ĐƯỢC (không có file,
/// hoặc có file mà hỏng / ghi dở — PUT lại sẽ thay), theo thứ tự gửi lên (đã khử
/// trùng).</summary>
public sealed record SourceImageMissingResponse(List<string> Missing);

/// <summary>Kết quả PUT 1 ảnh gốc (Hub chỉ nhận ảnh DÙNG ĐƯỢC: JPEG/PNG đúng cấu trúc, đổi MD5 được — khác thì 415
/// <c>unsupported-type</c> / 400 <c>corrupt-image</c> / 413 <c>too-large</c>). <see cref="Existed"/> = id đã có ảnh dùng được,
/// Hub KHÔNG ghi lại (bytes gửi lên bị bỏ qua); <see cref="Replaced"/> = id có file nhưng không dùng được, đã THAY bằng bản
/// này. <see cref="Size"/> / <see cref="ContentType"/> là của bản Hub ĐANG GIỮ.</summary>
public sealed record SourceImagePutResponse(string Id, bool Existed, bool Replaced, long Size, string ContentType);

/// <summary>Body của <see cref="HubRoutes.SourceImagesLinks"/>. <see cref="ShopKey"/> bắt buộc, 1..
/// <see cref="SourceImageLimits.MaxShopKeyLength"/> ký tự (dùng NGUYÊN VĂN làm khoá HMAC đổi MD5, như app Upload);
/// <see cref="Ids"/> ≤ <see cref="SourceImageLimits.MaxIdsPerCall"/> (id trùng được khử).</summary>
public sealed record SourceImageLinksRequest(string? ShopKey, List<string>? Ids);

/// <summary>Link công khai của 1 ảnh gốc ĐÃ ĐỔI MD5 cho shop (Hub đổi lúc Shopee đến lấy, không lưu file, không hạn — link
/// sống chừng nào ảnh gốc còn đúng bytes đó). <see cref="Url"/> = URL https tuyệt đối dạng
/// <c>{PublicBaseUrl}/api/v/{token}.{jpg|png}</c>; token MÃ HOÁ (không đọc ra shop / mã ảnh), gồm dấu vân tay nội dung
/// ảnh gốc — ảnh bị thay thì link cũ 404, phải lấy link mới.</summary>
public sealed record SourceImageLink(string Id, string Url);

/// <summary>Kết quả của <see cref="HubRoutes.SourceImagesLinks"/>: <see cref="Items"/> theo thứ tự id gửi lên (đã khử
/// trùng, KHÔNG gồm id thiếu); <see cref="Missing"/> = id kho gốc chưa có ảnh dùng được (như
/// <see cref="HubRoutes.SourceImagesMissing"/>).</summary>
public sealed record SourceImageLinksResponse(List<SourceImageLink> Items, List<string> Missing);

/// <summary>Kết quả của GET <see cref="HubRoutes.SourceImagesStats"/>. Ổ đĩa = ổ chứa thư mục dữ liệu Hub (-1 = không đo
/// được). Kho gốc: số ảnh / tổng byte / trần. Kho ảnh tạm công khai: số file / tổng byte / trần (số đếm có thể trễ tới
/// vài chục giây nếu job dọn vừa chạy).</summary>
public sealed record SourceImageStatsResponse(long DiskTotalBytes, long DiskFreeBytes,
    long SourceCount, long SourceBytes, long SourceMaxBytes,
    long MediaCount, long MediaBytes, long MediaMaxBytes);

/// <summary>Body của DELETE <see cref="HubRoutes.SourceImages"/>: <see cref="Confirm"/> phải đúng
/// <see cref="SourceImageLimits.DeleteAllConfirm"/>.</summary>
public sealed record SourceImageDeleteAllRequest(string? Confirm);

/// <summary>Kết quả xoá sạch kho gốc: <see cref="Deleted"/> ảnh đã xoá, <see cref="Remaining"/> = số ảnh ĐẾM LẠI trên đĩa
/// sau khi xoá (khác 0 = có file không xoá được), <see cref="FreedBytes"/> byte đã giải phóng, <see cref="Warning"/> = câu
/// cảnh báo cố định: link ảnh (<see cref="HubRoutes.PublicSourceImage"/>) của các file đã xuất sẽ hỏng.</summary>
public sealed record SourceImageDeleteAllResponse(long Deleted, long Remaining, long FreedBytes, string Warning);
