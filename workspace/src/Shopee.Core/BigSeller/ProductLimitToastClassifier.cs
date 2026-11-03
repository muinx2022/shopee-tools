using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Shopee.Core.BigSeller;

/// <summary>Một toast máy ghi đã bắt được: nguyên văn, mốc <c>Date.now()</c> của trang lúc ghi, loại suy từ class
/// ("success" / "info" / "loading" / "warning" / "error"; mục chỉ đến từ <c>[role=alert]</c> mang tiền tố "alert:",
/// vd "alert:warning" — chỉ để lấy mẫu log, không bao giờ kích hoạt dừng shop).</summary>
public sealed record RecordedToast(string Text, long Ts, string Kind);

/// <summary>
/// Nhận diện thuần text "toast BigSeller báo số sản phẩm vượt giới hạn được phép" (VN/EN) ở bước Lưu &amp; Publish.
/// Chưa có nguyên văn toast thật nên khớp theo luật gọn trên text đã chuẩn hóa (bỏ dấu, thường), mọi từ so theo
/// RANH GIỚI TỪ, theo thứ tự: (a) từ cảnh báo/tạm thời/tần suất → false; (b) "listing limit" đi cùng động từ
/// đạt/vượt → true; (c) từ của trường/media (tên, giá, tồn kho, ảnh…) → false; (d) tín hiệu giới hạn VÀ từ chỉ sản phẩm → true.
/// Khớp nhầm thì (cùng điều kiện 2 SP khác nhau ở tầng gọi) shop bị dừng nên luật cố ý hẹp; câu thật không khớp thì app
/// chạy như cũ (log nguyên văn toast để bổ sung mẫu).
/// </summary>
public static class ProductLimitToastClassifier
{
    private const RegexOptions Opts = RegexOptions.CultureInvariant;

    // (a) Cảnh báo sắp tới / thành công / gói không giới hạn / phần trăm DƯỚI 100 → không phải "đã vượt".
    //     "chua dat" / "chua vuot" (không phải "chua" trơn: "chưa xử lý" không liên quan). Câu mang nghĩa TẦN SUẤT (giới hạn
    //     theo ngày/phút/lượt gọi, không phải số SP của shop): hom nay, moi phut, per minute, today, request limit,
    //     cap nhat san pham, update limit. KHÔNG còn "thử lại"/"try again": câu thật có thể kèm lời khuyên đó.
    private static readonly Regex Transient = new(
        @"\b(sap|chua dat|chua vuot|about to|thanh cong|unlimited|hom nay|moi phut|per minute|today|request limit|cap nhat san pham|update limit)\b" +
        @"|\bsuccess|(?<![\d.])\d{1,2}(\.\d+)? ?%", Opts);

    // (b) "listing limit" CHỈ tự đủ khi đi cùng động từ đạt/vượt (reach*, exceed*, dat, vuot, over) — "Listing limit
    //     settings saved" không phải toast vượt. Thắng cả từ loại trừ (vd "…listing limit, please delete some products or media").
    private static readonly Regex ListingLimit = new(@"\blisting limit\b", Opts);
    private static readonly Regex ListingLimitVerb = new(@"\b(reach\w*|exceed\w*|dat|vuot|over)\b", Opts);

    // (c) Giới hạn của trường/media, không phải số SP. "gia" (giá) không tính khi đi liền "han" ("gia hạn gói").
    private static readonly Regex Exclusions = new(
        @"\b(ten|tieu de|mo ta|ky tu|gia(?! han\b)|ton kho|trong luong|can nang|kich thuoc|hinh anh|anh|videos?|media|dung luong|bien the|phan loai|thuoc tinh" +
        @"|ma san pham|chieu dai|chieu rong|chieu cao|sku|duoc chon" +
        @"|names?|titles?|descriptions?|characters?|prices?|stocks?|weights?|sizes?|images?|pictures?|photos?|attributes?|variations?|variants?" +
        @"|length|width|height|quantity|selected)\b", Opts);

    // (d) Tín hiệu giới hạn.
    private static readonly Regex LimitSignalVn = new(
        @"\b(vuot( qua)? (gioi han|so luong)|vuot muc gioi han|vuot han muc|da qua gioi han|het han muc|dat (den |toi )?gioi han|gioi han dang)\b", Opts);
    private static readonly Regex ExceedWord = new(@"\bexceed(s|ed|ing)?\b", Opts);
    private static readonly Regex ExceedObject = new(@"\b(limit|maximum|quota|number)\b", Opts);
    private static readonly Regex ReachWord = new(@"\breach(ed|es)?\b", Opts);
    private static readonly Regex ReachObject = new(@"\b(limit|maximum)\b", Opts);
    // "over the limit" / "over the product limit": liền kề, không để "hover over the product to view, no limit" lọt.
    private static readonly Regex OverLimit = new(@"\bover the (\w+ )?limit\b", Opts);

    // (d) Từ chỉ sản phẩm.
    private static readonly Regex ProductWord = new(@"\b(san pham|sp|products?|listings?|items?|mat hang)\b", Opts);

    /// <summary>True khi <paramref name="text"/> là toast báo vượt giới hạn số sản phẩm; null/rỗng → false.</summary>
    public static bool IsProductLimitToast(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = Normalize(text);

        if (Transient.IsMatch(t)) return false;
        if (ListingLimit.IsMatch(t) && ListingLimitVerb.IsMatch(t)) return true;
        if (Exclusions.IsMatch(t)) return false;
        return HasLimitSignal(t) && ProductWord.IsMatch(t);
    }

    private static bool HasLimitSignal(string t)
        => LimitSignalVn.IsMatch(t)
           || (ExceedWord.IsMatch(t) && ExceedObject.IsMatch(t))
           || (ReachWord.IsMatch(t) && ReachObject.IsMatch(t))
           || OverLimit.IsMatch(t);

    /// <summary>
    /// Trong danh sách toast máy ghi, trả NGUYÊN VĂN mục ĐẦU TIÊN là toast vượt giới hạn số SP; bỏ qua mục không được
    /// kích hoạt (<see cref="IsTriggerKind"/>: success/info/loading, và mục chỉ đến từ <c>[role=alert]</c>) và mục ghi TRƯỚC
    /// <paramref name="sinceTs"/> (toast cũ, trước lúc bấm Lưu). Không có → null.
    /// </summary>
    public static string? FindFirstProductLimitToast(IEnumerable<RecordedToast> toasts, long sinceTs)
    {
        foreach (var x in toasts)
        {
            if (x.Ts < sinceTs) continue;
            if (!IsTriggerKind(x.Kind)) continue;
            if (IsProductLimitToast(x.Text)) return x.Text;
        }
        return null;
    }

    /// <summary>Loại toast có quyền kích hoạt dừng shop? warning/error của TOAST THẬT có; success/info/loading không;
    /// mục "alert…" (chỉ <c>[role=alert]</c>: banner, vùng báo lỗi cố định…) không — chỉ để lấy mẫu log.</summary>
    public static bool IsTriggerKind(string? kind)
    {
        if (string.IsNullOrEmpty(kind)) return true;   // thiếu loại = coi là error (cùng quy ước máy ghi)
        return !(string.Equals(kind, "success", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(kind, "info", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(kind, "loading", StringComparison.OrdinalIgnoreCase)
                 || kind.StartsWith("alert", StringComparison.OrdinalIgnoreCase));
    }

    // Bỏ dấu tiếng Việt + đ→d + hạ chữ thường + gộp khoảng trắng (cùng quy tắc Normalize của BigSellerSaveSuccessHelper;
    // Core không tham chiếu được module UpdateProduct nên giữ bản riêng).
    private static string Normalize(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c == 'đ' || c == 'Đ') { sb.Append('d'); continue; }
            sb.Append(char.ToLowerInvariant(c));
        }
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }
}
