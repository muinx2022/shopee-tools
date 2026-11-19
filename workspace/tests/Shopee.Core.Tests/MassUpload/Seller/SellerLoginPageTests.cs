using Shopee.Core.MassUpload.Seller.Login;

namespace Shopee.Core.Tests.MassUpload.Seller;

/// <summary>GĐ5b: phân loại trang Seller Centre (hàm thuần) + luật "đã đăng nhập phải ổn định".</summary>
public sealed class SellerLoginPageTests
{
    internal const string Home = "https://banhang.shopee.vn/";
    internal const string SignIn = "https://banhang.shopee.vn/account/signin?next=https%3A%2F%2Fbanhang.shopee.vn%2F";
    internal const string WrongPassAlert = "Your account and/or password is incorrect, please try again";

    internal static SellerPageSnapshot LoggedIn => new(Home, "Kênh Người Bán", BodyTextLength: 1500, HasSessionCookie: true, SellerShellVisible: true);
    internal static SellerPageSnapshot LoginForm => new(SignIn, "Shopee", LoginKeyVisible: true, PasswordVisible: true, BodyTextLength: 400);
    internal static SellerPageSnapshot WrongPass => LoginForm with { AlertText = WrongPassAlert };
    internal static SellerPageSnapshot Verify => new("https://banhang.shopee.vn/account/verify?method=email", "Xác minh", BodyTextLength: 300);
    internal static SellerPageSnapshot Captcha => new("https://shopee.vn/verify/traffic?is_initial=true", "Shopee", BodyTextLength: 50);

    private static SellerPageKind K(SellerPageSnapshot s) => SellerLoginPageClassifier.Classify(s);

    [Fact]
    public void DaDangNhap_ChiKhiDuMoiDieuKien()
    {
        Assert.Equal(SellerPageKind.LoggedIn, K(LoggedIn));
        Assert.Equal(SellerPageKind.LoggedIn, K(LoggedIn with { Url = "https://banhang.shopee.vn/portal/product/list/all" }));

        // Thiếu bất kỳ điều kiện nào → KHÔNG phải đã đăng nhập.
        Assert.NotEqual(SellerPageKind.LoggedIn, K(LoggedIn with { LoginKeyVisible = true }));
        Assert.NotEqual(SellerPageKind.LoggedIn, K(LoggedIn with { PasswordVisible = true }));
        Assert.NotEqual(SellerPageKind.LoggedIn, K(LoggedIn with { OtpInputVisible = true }));
        Assert.NotEqual(SellerPageKind.LoggedIn, K(LoggedIn with { CaptchaVisible = true }));
        Assert.NotEqual(SellerPageKind.LoggedIn, K(LoggedIn with { ReadyState = "loading" }));
        Assert.NotEqual(SellerPageKind.LoggedIn, K(LoggedIn with { ReadyState = null }));
        Assert.NotEqual(SellerPageKind.LoggedIn, K(LoggedIn with { BodyTextLength = 0 }));
        Assert.NotEqual(SellerPageKind.LoggedIn, K(LoggedIn with { Url = SignIn }));
        Assert.NotEqual(SellerPageKind.LoggedIn, K(LoggedIn with { Url = "https://banhang.shopee.vn/account/login" }));
        Assert.NotEqual(SellerPageKind.LoggedIn, K(LoggedIn with { Url = "https://shopee.vn/" }));
        Assert.NotEqual(SellerPageKind.LoggedIn, K(LoggedIn with { Url = "https://accounts.shopee.vn/seller/" }));
        Assert.NotEqual(SellerPageKind.LoggedIn, K(LoggedIn with { Url = "https://banhang.shopee.vn.evil.com/" }));
        Assert.NotEqual(SellerPageKind.LoggedIn, K(LoggedIn with { Url = "about:blank" }));
        Assert.NotEqual(SellerPageKind.LoggedIn, K(LoggedIn with { HasSessionCookie = false }));
    }

    [Theory]
    [InlineData("https://banhang.shopee.vn/", "502 Bad Gateway", 25)]                 // nginx
    [InlineData("https://banhang.shopee.vn/", "Shopee", 120)]                         // trang bảo trì
    [InlineData("https://banhang.shopee.vn/portal/sale/order", "Shopee", 60)]         // lỗi SPA "Đã có lỗi xảy ra"
    [InlineData("https://banhang.shopee.vn/", "", 500)]
    public void TrangLoi_BaoTri_KhongCookiePhien_KhongPhaiDaDangNhap(string url, string title, int len) =>
        Assert.NotEqual(SellerPageKind.LoggedIn, K(new SellerPageSnapshot(url, title, BodyTextLength: len)));

    [Theory]
    [InlineData("502 Bad Gateway")]
    [InlineData("503 Service Temporarily Unavailable")]
    [InlineData("504 Gateway Time-out")]
    [InlineData("Shopee đang bảo trì")]
    [InlineData("Maintenance")]
    [InlineData("Error")]
    public void TrangLoi_CoCookiePhien_VanKhongPhaiDaDangNhap(string title) =>
        Assert.NotEqual(SellerPageKind.LoggedIn, K(LoggedIn with { Title = title }));

    [Fact]
    public void CookiePhien_TheoLuatShopeeAuth_GiuCho_Gach_KhongTinh()
    {
        const string live = "abcdefghij1234567890";
        Assert.True(SellerLoginPageClassifier.HasSessionCookie([(".shopee.vn", "SPC_F", live), (".shopee.vn", "SPC_EC", live)]));
        Assert.True(SellerLoginPageClassifier.HasSessionCookie([("banhang.shopee.vn", "spc_st", live)]));
        // Shopee đặt giá trị giữ chỗ "-" khi CHƯA đăng nhập (phản biện vòng 2 tái hiện trên Brave thật).
        Assert.False(SellerLoginPageClassifier.HasSessionCookie([(".shopee.vn", "SPC_U", "-"), (".shopee.vn", "SPC_EC", "-")]));
        Assert.False(SellerLoginPageClassifier.HasSessionCookie([(".shopee.vn", "SPC_EC", "12345")]));          // quá ngắn
        Assert.False(SellerLoginPageClassifier.HasSessionCookie([(".shopee.vn", "SPC_U", live)]));              // SPC_U không đủ
        Assert.False(SellerLoginPageClassifier.HasSessionCookie([(".evil.com", "SPC_EC", live)]));
        Assert.False(SellerLoginPageClassifier.HasSessionCookie([(".shopee.vn", "SPC_F", live), (".shopee.vn", "csrftoken", live)]));
        Assert.False(SellerLoginPageClassifier.HasSessionCookie([]));
    }

    [Fact]
    public void DaDangNhap_PhaiCoKhungMenuSellerCentre()
    {
        // Cookie không phân biệt phiên sống / phiên đã hết hạn: trang bảo trì / lỗi có cookie hợp lệ vẫn không phải đã đăng nhập.
        Assert.NotEqual(SellerPageKind.LoggedIn, K(LoggedIn with { SellerShellVisible = false }));
        Assert.NotEqual(SellerPageKind.LoggedIn, K(new SellerPageSnapshot(Home, "Shopee Kênh Người Bán", BodyTextLength: 300, HasSessionCookie: true)));
        Assert.Equal(SellerPageKind.LoggedIn, K(LoggedIn));
    }

    [Theory]
    [InlineData("https://banhang.shopee.vn/account/signin?loginKey=u&password=p%40ss%21#frag", "https://banhang.shopee.vn/account/signin")]
    [InlineData("https://user:p%40ss@banhang.shopee.vn:8443/a/b?x=1", "https://banhang.shopee.vn:8443/a/b")]
    [InlineData("about:blank", "about:blank")]
    [InlineData("chrome-error://chromewebdata/?x=1", "chrome-error://chromewebdata/")]
    [InlineData(null, "")]
    public void UrlForLog_BoQueryVaFragment(string? url, string expected) => Assert.Equal(expected, SellerLoginText.UrlForLog(url));

    [Fact]
    public void Diag_KhongInQuery_ScrubBoQueryTrongCauLoi()
    {
        var s = new SellerPageSnapshot("https://banhang.shopee.vn/account/signin?loginKey=shop&password=Secr%33t%21", "Shopee");
        Assert.Equal("title=[Shopee], url=https://banhang.shopee.vn/account/signin", s.Diag);
        Assert.Equal("lỗi khi tới https://banhang.shopee.vn/x (timeout)",
            SellerLoginText.Scrub("lỗi khi tới https://banhang.shopee.vn/x?password=Secr%33t%21#a (timeout)", []));
    }

    [Fact]
    public void OTaiKhoan_TrenHostLa_KhongPhaiFormDangNhap_KhongGo()
    {
        Assert.Equal(SellerPageKind.Unknown, K(LoginForm with { Url = "https://accounts.shopee.vn/seller/login" }));
        Assert.Equal(SellerPageKind.Unknown, K(LoginForm with { Url = "https://shopee.vn/buyer/login" }));
        Assert.Equal(SellerPageKind.Unknown, K(LoginForm with { Url = "https://banhang.shopee.vn.evil.com/account/signin" }));
        Assert.Equal(SellerPageKind.Unknown, K(WrongPass with { Url = "https://evil.example/account/signin" }));
    }

    [Fact]
    public void FormDangNhap_VaLoiDangNhap()
    {
        Assert.Equal(SellerPageKind.LoginForm, K(LoginForm));
        Assert.Equal(SellerPageKind.LoginForm, K(LoginForm with { Url = Home }));   // form hiện ngay ở trang chủ
        Assert.Equal(SellerPageKind.LoginError, K(WrongPass));
        Assert.Equal(SellerPageKind.LoginError, K(LoginForm with { AlertText = "Tài khoản hoặc mật khẩu không đúng" }));
        Assert.Equal(SellerPageKind.LoginForm, K(LoginForm with { AlertText = "   " }));
        // Trang đăng nhập còn tải (chưa có ô) → chưa rõ, không phải đã đăng nhập.
        Assert.Equal(SellerPageKind.Unknown, K(new SellerPageSnapshot(SignIn, BodyTextLength: 500)));
    }

    [Fact]
    public void XacMinh_TheoUrl_OMa_HoacAlert_KhiKhongCoForm()
    {
        Assert.Equal(SellerPageKind.Verify, K(Verify));
        Assert.Equal(SellerPageKind.Verify, K(new SellerPageSnapshot("https://accounts.shopee.vn/verification/otp?x=1")));
        Assert.Equal(SellerPageKind.Verify, K(LoggedIn with { OtpInputVisible = true }));
        Assert.Equal(SellerPageKind.Verify, K(LoggedIn with { AlertText = "Vui lòng nhập mã xác minh đã gửi tới email" }));
        // Trên form đăng nhập (có ô loginKey) alert có chữ otp / xác minh KHÔNG tính là xác minh — là alert của form (vd "Shopee
        // không bao giờ hỏi mã OTP"); luồng quyết theo luật alert mới / khác.
        Assert.Equal(SellerPageKind.LoginError, K(LoginForm with { AlertText = "Shopee không bao giờ hỏi mã OTP của bạn" }));
        Assert.Equal(SellerPageKind.LoginError, K(LoginForm with { AlertText = "Please verify your login" }));
        Assert.Equal(SellerPageKind.Verify, K(LoginForm with { AlertText = "Nhập mã OTP", OtpInputVisible = true }));
    }

    [Fact]
    public void Captcha_TheoUrl_HoacKhungDangHien_UuTienNhat()
    {
        Assert.Equal(SellerPageKind.Captcha, K(Captcha));
        Assert.Equal(SellerPageKind.Captcha, K(new SellerPageSnapshot("https://banhang.shopee.vn/verify/captcha?x")));
        Assert.Equal(SellerPageKind.Captcha, K(LoggedIn with { CaptchaVisible = true }));
        Assert.Equal(SellerPageKind.Captcha, K(LoginForm with { CaptchaVisible = true }));
    }

    [Theory]
    [InlineData("about:blank")]
    [InlineData("chrome-error://chromewebdata/")]
    [InlineData("")]
    [InlineData("không phải url")]
    public void KhongPhaiTrangWeb_ChuaRo(string url) =>
        Assert.Equal(SellerPageKind.Unknown, K(new SellerPageSnapshot(url, LoginKeyVisible: true, BodyTextLength: 999)));

    [Fact]
    public void OnDinh_PhaiLienTucDuThoiGian_GapTrangKhacLaDemLai()
    {
        var t0 = SellerTestData.T0;
        var s = new SellerLoggedInStability(TimeSpan.FromSeconds(5));
        Assert.False(s.Observe(SellerPageKind.LoggedIn, t0));
        Assert.False(s.Observe(SellerPageKind.LoggedIn, t0.AddSeconds(4)));
        Assert.False(s.Observe(SellerPageKind.LoginForm, t0.AddSeconds(5)));   // đứt → đếm lại
        Assert.False(s.Observe(SellerPageKind.LoggedIn, t0.AddSeconds(6)));
        Assert.False(s.Observe(SellerPageKind.LoggedIn, t0.AddSeconds(10)));
        Assert.True(s.Observe(SellerPageKind.LoggedIn, t0.AddSeconds(11)));
    }

    [Theory]
    [InlineData("Log In", true)]
    [InlineData(" Log in ", true)]
    [InlineData("LOGIN", true)]
    [InlineData("Đăng nhập", true)]
    [InlineData("Log in with QR", false)]
    [InlineData("Login with Main/Sub Account", false)]
    [InlineData("Đăng nhập bằng Facebook", false)]
    [InlineData("Google", false)]
    [InlineData(null, false)]
    public void NutDangNhap_ChiDungChu(string? text, bool expected) =>
        Assert.Equal(expected, SellerLoginSelectors.IsSubmitText(text));

    [Fact]
    public void NutDangNhap_ChuToHopDau_VanKhop() =>
        Assert.True(SellerLoginSelectors.IsSubmitText("Đăng nhập".Normalize(System.Text.NormalizationForm.FormD)));
}
