namespace Shopee.Core.MassUpload.Seller.Login;

/// <summary>
/// Thông tin đăng nhập của một lượt (plan GĐ5b mục 1). CỐ Ý là lớp thường, không phải record: <c>ToString</c> tự sinh của record
/// in cả mật khẩu — ở đây <see cref="ToString"/> chỉ nói "đã nhập / chưa nhập".
/// </summary>
public sealed class SellerLoginCredentials
{
    public SellerLoginCredentials(string? login, string? password, string? verifyEmail, string? verifyEmailPassword)
    {
        Login = string.IsNullOrWhiteSpace(login) ? null : login.Trim();
        Password = string.IsNullOrEmpty(password) ? null : password;
        VerifyEmail = string.IsNullOrWhiteSpace(verifyEmail) ? null : verifyEmail.Trim();
        VerifyEmailPassword = string.IsNullOrEmpty(verifyEmailPassword) ? null : verifyEmailPassword;
    }

    public static SellerLoginCredentials FromAccount(SellerAccount a) =>
        new(a.ShopeeLogin, a.ShopeePassword, a.VerifyEmail, a.VerifyEmailPassword);

    public string? Login { get; }
    public string? Password { get; }
    public string? VerifyEmail { get; }
    public string? VerifyEmailPassword { get; }

    /// <summary>Đủ tài khoản + mật khẩu Shopee để tự gõ.</summary>
    public bool HasShopeeLogin => Login is not null && Password is not null;

    /// <summary>Đủ email + mật khẩu email để mở Hotmail khi bị đòi mã.</summary>
    public bool HasMailbox => VerifyEmail is not null && VerifyEmailPassword is not null;

    /// <summary>Mọi mật khẩu — để che khỏi log / trạng thái / câu lỗi (<see cref="SellerLoginText.Scrub"/>).</summary>
    public IEnumerable<string> Secrets()
    {
        if (Password is not null) yield return Password;
        if (VerifyEmailPassword is not null) yield return VerifyEmailPassword;
    }

    public override string ToString() =>
        $"tài khoản {Login ?? "(chưa nhập)"}, mật khẩu {(Password is null ? "(chưa nhập)" : "(đã nhập)")}, " +
        $"email xác minh {VerifyEmail ?? "(chưa nhập)"}, mật khẩu email {(VerifyEmailPassword is null ? "(chưa nhập)" : "(đã nhập)")}";
}

/// <summary>Kết quả một lượt đăng nhập.</summary>
public enum SellerLoginOutcome
{
    /// <summary>Đã gõ / user đã nhập mã, trang về Seller Centre.</summary>
    LoggedIn,
    /// <summary>Profile còn phiên — không gõ gì.</summary>
    AlreadyLoggedIn,
    /// <summary>Gặp form đăng nhập mà chưa nhập tài khoản / mật khẩu Shopee.</summary>
    MissingCredentials,
    /// <summary>Shopee báo lỗi đăng nhập (sai tài khoản / mật khẩu…). Dừng, KHÔNG thử lại.</summary>
    Rejected,
    /// <summary>Chờ user (mã / captcha) quá hạn.</summary>
    TimedOut,
    /// <summary>Không nhận ra trang.</summary>
    Unrecognized,
    /// <summary>Form đăng nhập không có ô tài khoản / mật khẩu như mong đợi.</summary>
    FormNotFound,
    /// <summary>User đóng tab / cửa sổ Brave giữa chừng.</summary>
    WindowClosed,
    /// <summary>Lỗi khác (nối CDP hỏng…).</summary>
    Failed,
}

/// <summary><see cref="Message"/> đã che mật khẩu (<see cref="SellerLoginText.Scrub"/>).</summary>
public sealed record SellerLoginResult(SellerLoginOutcome Outcome, string Message)
{
    public bool Ok => Outcome is SellerLoginOutcome.LoggedIn or SellerLoginOutcome.AlreadyLoggedIn;
}

public static class SellerLoginText
{
    /// <summary>Mật khẩu ngắn hơn mức này không đem đi thay (thay "ab" băm nát log). Mật khẩu Shopee thật dài hơn nhiều.</summary>
    public const int MinScrubLength = 3;

    public const string Mask = "***";

    /// <summary>URL để ghi log: chỉ scheme + host (+ cổng) + path; BỎ query và fragment. Form đăng nhập gửi bằng GET để mật khẩu
    /// dạng URL-encode (<c>password=p%40ss%21</c>) trong query — <see cref="Scrub"/> so chuỗi gốc nên không bắt được. URL không đọc
    /// được → chỉ giữ phần trước <c>?</c> / <c>#</c>.</summary>
    public static string UrlForLog(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        if (Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https")
            return $"{u.Scheme}://{u.Authority}{u.AbsolutePath}";   // Authority không gồm user:pass@
        var cut = url.IndexOfAny(['?', '#']);
        return cut >= 0 ? url[..cut] : url;
    }

    /// <summary>Thay MỌI lần xuất hiện của từng bí mật trong <paramref name="text"/> bằng <see cref="Mask"/>. Bí mật dài làm trước.
    /// Lưới an toàn cuối của Core: luồng không bao giờ cố ý in mật khẩu, nhưng câu lỗi Playwright / trang có thể chứa.</summary>
    public static string Scrub(string text, IEnumerable<string?> secrets)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (var s in secrets.Where(x => x is { Length: >= MinScrubLength }).Distinct(StringComparer.Ordinal).OrderByDescending(x => x!.Length))
            text = text.Replace(s!, Mask, StringComparison.Ordinal);
        // URL nằm trong câu (vd câu lỗi Playwright "navigating to https://…?password=p%40ss") → bỏ query / fragment như UrlForLog.
        return UrlQuery.Replace(text, "$1");
    }

    private static readonly System.Text.RegularExpressions.Regex UrlQuery =
        new(@"(\bhttps?://[^\s?#""'<>]+)[?#][^\s""'<>]*", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}
