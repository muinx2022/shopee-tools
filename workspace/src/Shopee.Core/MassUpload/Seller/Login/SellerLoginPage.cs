using System.Text.RegularExpressions;
using Shopee.Core.Accounts;
using Shopee.Toolkit.MsLogin;

namespace Shopee.Core.MassUpload.Seller.Login;

/// <summary>Trạng thái trang Seller Centre trong lúc đăng nhập (plan GĐ5b mục 3.1).</summary>
public enum SellerPageKind
{
    /// <summary>Không nhận ra (đang chuyển trang, trang lạ). Luồng log <c>title</c> + <c>url</c>, không log nội dung form.</summary>
    Unknown,
    /// <summary>Trông như đã vào Seller Centre — luồng chỉ tính là xong khi giữ nguyên liên tục <see cref="SellerLoggedInStability"/>.</summary>
    LoggedIn,
    LoginForm,
    /// <summary>Form đăng nhập + có <c>[role=alert]</c> (vd "Your account and/or password is incorrect…").</summary>
    LoginError,
    /// <summary>Trang xác minh / đòi mã.</summary>
    Verify,
    Captcha,
}

/// <summary>
/// Ảnh chụp các dấu hiệu của trang, đọc bằng một lần chạy JS (<see cref="PlaywrightSellerLoginPage.SnapshotAsync"/>). Chỉ có cờ
/// "đang hiện", câu cảnh báo của trang và độ dài chữ — KHÔNG có giá trị ô nhập nào.
/// </summary>
/// <param name="AlertText">Chữ của các <c>[role=alert]</c> đang hiện, nối bằng <c>" | "</c> (null/rỗng = không có).</param>
/// <param name="ReadyState"><c>document.readyState</c>.</param>
/// <param name="BodyTextLength">Độ dài <c>body.innerText</c> đã trim — trang trắng / đang quay vòng thì gần 0.</param>
/// <param name="HasSessionCookie">Context có cookie phiên đăng nhập cho <c>banhang.shopee.vn</c>
/// (<see cref="SellerLoginPageClassifier.HasSessionCookie"/>). Cookie dùng chung mọi tab của profile.</param>
/// <param name="SellerShellVisible">Đang hiện link menu của khung Seller Centre (<see cref="SellerLoginSelectors.SellerShell"/>) —
/// dấu hiệu DƯƠNG của đã đăng nhập (cookie không phân biệt được phiên sống với phiên đã hết hạn).</param>
public sealed record SellerPageSnapshot(
    string Url,
    string? Title = null,
    bool LoginKeyVisible = false,
    bool PasswordVisible = false,
    bool OtpInputVisible = false,
    bool CaptchaVisible = false,
    string? AlertText = null,
    string? ReadyState = "complete",
    int BodyTextLength = 0,
    bool HasSessionCookie = false,
    bool SellerShellVisible = false)
{
    /// <summary>Dòng chẩn đoán cho log: chỉ <c>title</c> + URL đã bỏ query / fragment (<see cref="SellerLoginText.UrlForLog"/> — form
    /// gửi bằng GET có thể để <c>password=…</c> dạng URL-encode trong query, <c>Scrub</c> không bắt được).</summary>
    public string Diag => $"title=[{Title}], url={SellerLoginText.UrlForLog(Url)}";
}

/// <summary>
/// Selector + chữ của form đăng nhập <c>banhang.shopee.vn</c> (HTML user gửi 2026-11-18) và các dấu hiệu trang xác minh / captcha.
/// Shopee đổi giao diện thì sửa ĐÚNG file này. Class CSS của Shopee là chuỗi băm (vd <c>Z8lzn1</c>) — không dựa vào class.
/// </summary>
internal static class SellerLoginSelectors
{
    /// <summary>Ô tài khoản: placeholder "Phone number / Username / Email", maxlength 128.</summary>
    internal static readonly string[] LoginKey = ["input[name='loginKey']"];

    /// <summary>Ô mật khẩu để GÕ: <c>name=password</c> trước (đúng form), <c>type=password</c> sau.</summary>
    internal static readonly string[] Password = ["input[name='password']", "input[type='password']"];

    /// <summary>Có ô mật khẩu nào đang hiện (dấu hiệu "chưa đăng nhập").</summary>
    internal static readonly string[] PasswordDetect = ["input[type='password']", "input[name='password']"];

    /// <summary>Nút "Log In" / "Đăng nhập" nằm trong <c>form</c>; lọc tiếp bằng <see cref="IsSubmitText"/> (KHÔNG bấm "Log in with
    /// QR", "Login with Main/Sub Account", Facebook, Google).</summary>
    internal static readonly string[] Submit = ["form button", "button[type='submit']"];

    /// <summary>Ô nhập mã xác minh (dò, chưa có HTML thật của trang xác minh).</summary>
    internal static readonly string[] OtpInput =
    [
        "input[autocomplete='one-time-code']",
        "input[name*='otp' i]",
        "input[placeholder*='otp' i]",
        "input[placeholder*='mã xác minh' i]",
        "input[placeholder*='verification code' i]",
    ];

    /// <summary>Link menu của khung Seller Centre — chỉ có khi đã vào trong (trang lỗi / bảo trì / form đăng nhập không có). Đầu
    /// tiên là đúng link "Tất Cả Sản Phẩm" trong HTML menu user gửi; sau đó là mọi link <c>/portal/…</c> (đường dẫn tương đối hoặc
    /// tuyệt đối trên banhang).</summary>
    internal static readonly string[] SellerShell =
    [
        "a[href='/portal/product/list/live/all']",
        "a[href^='/portal/']",
        "a[href^='https://banhang.shopee.vn/portal/']",
    ];

    /// <summary>Khung captcha đang hiện (dò, chưa có HTML thật của trang captcha).</summary>
    internal static readonly string[] Captcha = ["iframe[src*='captcha' i]", "[id*='captcha' i]", "[class*='captcha' i]"];

    private static readonly Regex SubmitTextRegex = new(@"^(log ?in|dang nhap)$", RegexOptions.CultureInvariant);

    /// <summary>Chữ trên nút đúng là "Log In"/"Login"/"Đăng nhập" (so KHÔNG DẤU, cả chuỗi — "Log in with QR" không khớp).</summary>
    internal static bool IsSubmitText(string? innerText) => SubmitTextRegex.IsMatch(MsLoginSelectors.NormalizeForMatch(innerText));
}

/// <summary>
/// Phân loại trang (hàm thuần, plan GĐ5b mục 3.1 + 3.5). Dấu hiệu chép từ cách app check đơn dò
/// (<c>suite/orders/…/ShopeeSessionState.DetectPageStateAsync</c>: URL <c>captcha</c> / <c>/verify</c> trước, rồi ô đăng nhập, rồi
/// alert có chữ xác minh), thêm dấu hiệu DOM. Thứ tự ưu tiên:
/// <list type="number">
/// <item>URL không phải http(s) (about:blank, trang lỗi) → <see cref="SellerPageKind.Unknown"/>.</item>
/// <item>Captcha: URL có <see cref="CaptchaUrlMarkers"/> (gồm <c>/verify/traffic</c> chống bot của Shopee), hoặc khung captcha đang hiện.</item>
/// <item>Xác minh: đường dẫn có <see cref="VerifyPathMarkers"/>.</item>
/// <item>Ô nhập mã đang hiện, hoặc alert có chữ xác minh (<see cref="VerifyAlertNeedles"/>, so không dấu) → xác minh (kể cả khi
/// form đăng nhập còn hiện: alert đòi mã KHÔNG phải lỗi sai mật khẩu, không được làm luồng dừng).</item>
/// <item>Ô <c>loginKey</c> đang hiện → form đăng nhập (CHỈ trên host <see cref="SellerHost"/>; host khác → Unknown, không gõ); kèm
/// alert khác → <see cref="SellerPageKind.LoginError"/>. Alert có từ TRƯỚC khi bấm hay không là việc của
/// <see cref="SellerLoginFlow"/> (so với alert chụp ngay trước khi bấm).</item>
/// <item>Đã đăng nhập (<see cref="LooksLoggedIn"/>) — mọi điều kiện cùng lúc, BẮT BUỘC có cookie phiên hợp lệ VÀ khung menu Seller Centre đang hiện (trang
/// 502 / bảo trì / lỗi SPA trên banhang không có cookie phiên thì không tính).</item>
/// <item>Còn lại → <see cref="SellerPageKind.Unknown"/>.</item>
/// </list>
/// Thêm dấu hiệu mới: sửa mảng ở đây, thêm ca vào test.
/// </summary>
public static class SellerLoginPageClassifier
{
    public const string SellerHost = "banhang.shopee.vn";

    /// <summary>Ít chữ hơn mức này = trang trắng / đang quay vòng — chưa tính là đã đăng nhập.</summary>
    public const int MinBodyTextForLoggedIn = 20;

    internal static readonly string[] CaptchaUrlMarkers = ["captcha", "/verify/traffic"];
    internal static readonly string[] VerifyPathMarkers = ["/verify", "verification", "/otp"];
    internal static readonly string[] LoginPathMarkers = ["/account/signin", "/signin", "/login"];

    /// <summary>Chữ (không dấu, chữ thường) trong alert báo đang đòi xác minh.</summary>
    internal static readonly string[] VerifyAlertNeedles = ["otp", "ma xac", "xac minh", "verification", "verify"];

    public static SellerPageKind Classify(SellerPageSnapshot s)
    {
        if (!Uri.TryCreate(s.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return SellerPageKind.Unknown;
        var url = s.Url.ToLowerInvariant();
        var path = uri.AbsolutePath.ToLowerInvariant();
        var hasAlert = !string.IsNullOrWhiteSpace(s.AlertText);
        var alertAsksVerify = hasAlert && VerifyAlertNeedles.Any(MsLoginSelectors.NormalizeForMatch(s.AlertText).Contains);

        if (CaptchaUrlMarkers.Any(url.Contains) || s.CaptchaVisible) return SellerPageKind.Captcha;
        if (VerifyPathMarkers.Any(path.Contains)) return SellerPageKind.Verify;
        // Alert có chữ otp / xác minh CHỈ tính là xác minh khi KHÔNG có ô tài khoản: trên form đăng nhập, câu kiểu "Shopee không bao
        // giờ hỏi mã OTP" là alert của form (luật alert mới / khác ở SellerLoginFlow quyết).
        if (s.OtpInputVisible || (alertAsksVerify && !s.LoginKeyVisible)) return SellerPageKind.Verify;
        var onSellerHost = string.Equals(uri.Host, SellerHost, StringComparison.OrdinalIgnoreCase);
        // Form đăng nhập CHỈ tính trên host Seller Centre: host lạ có ô loginKey thì không gõ mật khẩu vào (chưa có HTML thật của
        // host đăng nhập nào khác mà Shopee chuyển tới — thêm vào đây khi có).
        if (s.LoginKeyVisible) return !onSellerHost ? SellerPageKind.Unknown : hasAlert ? SellerPageKind.LoginError : SellerPageKind.LoginForm;
        return onSellerHost && LooksLoggedIn(path, s) ? SellerPageKind.LoggedIn : SellerPageKind.Unknown;
    }

    /// <summary>Có cookie phiên Shopee theo ĐÚNG luật chung của repo <see cref="ShopeeAuth.IsSessionCookie"/> (SPC_ST / SPC_EC trên
    /// domain shopee, giá trị dài hơn 5 ký tự và khác <c>-</c>). Shopee đặt <c>SPC_U=-</c> / <c>SPC_EC=-</c> khi CHƯA đăng nhập —
    /// luật "có giá trị là được" của vòng 2 tính nhầm những cookie giữ chỗ đó. Chỉ trả cờ; giá trị cookie không đi ra khỏi hàm.
    /// Cookie không phân biệt phiên sống / phiên đã hết hạn → <see cref="LooksLoggedIn"/> còn đòi dấu hiệu dương của Seller Centre.</summary>
    public static bool HasSessionCookie(IEnumerable<(string Domain, string Name, string Value)> cookies) =>
        cookies.Any(c => ShopeeAuth.IsSessionCookie(c.Domain ?? "", c.Name ?? "", c.Value ?? ""));

    /// <summary>Tiêu đề trang lỗi / bảo trì thường gặp (so không dấu). Lớp phụ: cookie phiên còn mà máy chủ trả 502 thì vẫn không
    /// tính là đã vào Seller Centre.</summary>
    internal static readonly string[] ErrorTitleNeedles =
        ["bad gateway", "service unavailable", "gateway time", "internal server error", "bao tri", "maintenance", "not found", "error"];

    private static bool LooksLikeErrorTitle(string? title)
    {
        var t = MsLoginSelectors.NormalizeForMatch(title);
        if (t.Length == 0) return false;
        return ErrorTitleNeedles.Any(t.Contains) || Regex.IsMatch(t, @"^\s*[45]\d\d\b", RegexOptions.CultureInvariant);
    }

    /// <summary>Đường dẫn không phải đăng nhập, KHÔNG còn ô tài khoản / mật khẩu / mã / captcha nào hiện, trang đã tải xong và có chữ,
    /// có cookie phiên hợp lệ (<see cref="HasSessionCookie"/>), ĐANG HIỆN khung menu Seller Centre (<see cref="SellerPageSnapshot.SellerShellVisible"/>),
    /// tiêu đề không phải trang lỗi (host đã kiểm ở bên gọi).</summary>
    private static bool LooksLoggedIn(string path, SellerPageSnapshot s) =>
        !LoginPathMarkers.Any(path.Contains)
        && !s.LoginKeyVisible && !s.PasswordVisible && !s.OtpInputVisible && !s.CaptchaVisible
        && string.Equals(s.ReadyState, "complete", StringComparison.OrdinalIgnoreCase)
        && s.BodyTextLength >= MinBodyTextForLoggedIn
        && s.HasSessionCookie
        && s.SellerShellVisible
        && !LooksLikeErrorTitle(s.Title);
}

/// <summary>"Đã đăng nhập" chỉ tính khi trang giữ <see cref="SellerPageKind.LoggedIn"/> LIÊN TỤC ít nhất <c>stableFor</c> (plan
/// GĐ5b mục 3.5: tránh nhầm lúc trang đang chuyển, vd <c>banhang.shopee.vn/</c> vừa tải xong còn chưa kịp chuyển sang trang đăng
/// nhập). Một lần thấy trạng thái khác là đếm lại từ đầu.</summary>
public sealed class SellerLoggedInStability(TimeSpan stableFor)
{
    private DateTimeOffset? _since;

    /// <summary>Ghi một lần quan sát. true = đã đủ ổn định để tính là đăng nhập xong.</summary>
    public bool Observe(SellerPageKind kind, DateTimeOffset now)
    {
        if (kind != SellerPageKind.LoggedIn) { _since = null; return false; }
        _since ??= now;
        return now - _since.Value >= stableFor;
    }
}
