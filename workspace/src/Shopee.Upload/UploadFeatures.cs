namespace Shopee.Upload;

/// <summary>Cờ tính năng của app (GĐ8, user chốt 2026-11-18: app chỉ XUẤT FILE, user tự sửa rồi tự đẩy lên Shopee).</summary>
public static class UploadFeatures
{
    /// <summary>Hiện phần truy cập Shopee của GĐ5a–5d: proxy chung / riêng, profile Brave, đăng nhập Shopee, nút mở Brave / đăng
    /// nhập, nhập mật khẩu từ Excel, nhịp chạy, trần SP/ngày. false = ẩn khỏi giao diện (code + dữ liệu JSON giữ nguyên). Bật lại:
    /// đổi thành true.</summary>
    public const bool ShowBrowserFeatures = false;
}
