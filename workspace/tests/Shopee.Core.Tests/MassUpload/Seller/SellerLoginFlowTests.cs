using Shopee.Core.MassUpload.Seller.Login;
using static Shopee.Core.Tests.MassUpload.Seller.SellerLoginPageTests;

namespace Shopee.Core.Tests.MassUpload.Seller;

/// <summary>GĐ5b: luật của <see cref="SellerLoginFlow"/> trên tab giả (không Brave, đồng hồ giả): gõ + bấm nhiều nhất MỘT lần, sai
/// mật khẩu thì dừng và trả câu của Shopee, đòi mã thì mở Hotmail một lần, chờ tối đa 15 phút, không mật khẩu nào lọt ra log /
/// trạng thái / câu kết quả.</summary>
public sealed class SellerLoginFlowTests
{
    private const string Pass = "ShopPass#2026";
    private const string MailPass = "MailPass#7788";

    private readonly FakeClock _clock = new(SellerTestData.T0);
    private readonly FakeLoginPage _page = new();
    private readonly List<string> _logs = [];
    private readonly List<string> _statuses = [];

    private static SellerLoginCredentials Full => new("shop.a@gmail.com", Pass, "shop.a@hotmail.com", MailPass);
    private static SellerLoginCredentials NoMail => new("shop.a@gmail.com", Pass, null, null);

    private async Task<SellerLoginResult> Run(SellerLoginCredentials? creds = null)
    {
        var flow = new SellerLoginFlow(_page, creds ?? Full, new SellerLoginFlowOptions(), _logs.Add, _statuses.Add,
            () => _clock.Now, _clock.Delay);
        var r = await flow.RunAsync(CancellationToken.None);
        AssertNoSecrets(r);
        return r;
    }

    /// <summary>Mọi chuỗi luồng đưa ra ngoài đều không chứa mật khẩu.</summary>
    private void AssertNoSecrets(SellerLoginResult r)
    {
        foreach (var text in _logs.Concat(_statuses).Append(r.Message))
        {
            Assert.DoesNotContain(Pass, text);
            Assert.DoesNotContain(MailPass, text);
        }
    }

    private TimeSpan Elapsed => _clock.Now - SellerTestData.T0;

    [Fact]
    public async Task DaDangNhapSan_KhongGoGi_SauKhiOnDinh()
    {
        _page.Script(LoggedIn);

        var r = await Run();

        Assert.Equal(SellerLoginOutcome.AlreadyLoggedIn, r.Outcome);
        Assert.True(r.Ok);
        Assert.Equal(0, _page.FillCalls);
        Assert.Equal(1, _page.GotoCalls);
        Assert.True(Elapsed >= TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TrangChuThoangQuaRoiRaForm_KhongNhamDaDangNhap_GoMotLan()
    {
        // banhang.shopee.vn/ vừa tải xong trông như đã đăng nhập 2 s rồi mới chuyển sang trang đăng nhập.
        _page.Script(LoggedIn, LoggedIn, LoginForm);
        _page.OnSubmit = () => _page.Script(LoggedIn);

        var r = await Run();

        Assert.Equal(SellerLoginOutcome.LoggedIn, r.Outcome);
        Assert.Equal(1, _page.FillCalls);
        Assert.Equal(("shop.a@gmail.com", Pass), _page.Filled.Single());
        Assert.Equal(1, _page.SubmitCalls);
    }

    [Fact]
    public async Task SaiMatKhau_DungNgay_BaoDungCauShopee_KhongThuLai()
    {
        _page.Script(LoginForm);
        // Sau khi bấm: Shopee báo sai, rồi alert tắt mà vẫn ở form — luồng KHÔNG được gõ lại.
        _page.OnSubmit = () => _page.Script(WrongPass, LoginForm);

        var r = await Run();

        Assert.Equal(SellerLoginOutcome.Rejected, r.Outcome);
        Assert.False(r.Ok);
        Assert.Contains(WrongPassAlert, r.Message);
        Assert.Contains(_statuses, s => s.Contains(WrongPassAlert));
        Assert.Equal(1, _page.FillCalls);
        Assert.Equal(1, _page.SubmitCalls);
        Assert.Equal(0, _page.MailCalls);
        Assert.True(Elapsed < TimeSpan.FromMinutes(1));
    }

    private static SellerPageSnapshot StaleAlertForm => LoginForm with { AlertText = "Phiên đăng nhập đã hết hạn, vui lòng đăng nhập lại" };

    [Fact]
    public async Task AlertCoSanTruocKhiBam_KhongTinhLaSai_SangXacMinh_VanMoHotmail()
    {
        // Ca phản biện: trang có sẵn alert "Phiên đăng nhập đã hết hạn…", bấm Log In, trang chuyển sang xác minh.
        _page.Script(StaleAlertForm);
        _page.OnSubmit = () => _page.Script(StaleAlertForm, StaleAlertForm, Verify, Verify, LoggedIn);

        var r = await Run();

        Assert.Equal(SellerLoginOutcome.LoggedIn, r.Outcome);
        Assert.Equal(1, _page.MailCalls);
        Assert.Equal(1, _page.FillCalls);
    }

    [Fact]
    public async Task AlertCoSan_DoiSangCauKhac_LaCauTraLoiMoi_Dung()
    {
        _page.Script(StaleAlertForm);
        _page.OnSubmit = () => _page.Script(StaleAlertForm, WrongPass);

        var r = await Run();

        Assert.Equal(SellerLoginOutcome.Rejected, r.Outcome);
        Assert.Contains(WrongPassAlert, r.Message);
        Assert.DoesNotContain("hết hạn", r.Message);
        Assert.Equal(1, _page.FillCalls);
    }

    [Fact]
    public async Task AlertCoSan_BienMatRoiHienLai_LaCauTraLoiMoi_Dung()
    {
        _page.Script(StaleAlertForm);
        _page.OnSubmit = () => _page.Script(StaleAlertForm, LoginForm, StaleAlertForm);

        var r = await Run();

        Assert.Equal(SellerLoginOutcome.Rejected, r.Outcome);
        Assert.Contains("hết hạn", r.Message);
    }

    [Fact]
    public async Task AlertCoSan_ChupLoiLucChuyenTrang_KhongTinhLaBienMat()
    {
        // Lần chụp hỏng (đang chuyển trang, ReadyState null, không có alert) không được coi là "alert cũ đã biến mất".
        _page.Script(StaleAlertForm);
        _page.OnSubmit = () => _page.Script(new SellerPageSnapshot(SignIn, ReadyState: null), StaleAlertForm, Verify, LoggedIn);

        var r = await Run();

        Assert.Equal(SellerLoginOutcome.LoggedIn, r.Outcome);
    }

    [Fact]
    public async Task SaiMatKhau_MatKhauDaiHon16_NhacGioiHan()
    {
        const string longPass = "Abcdefghijklmnopq";   // 17 ký tự
        _page.Script(LoginForm);
        _page.OnSubmit = () => _page.Script(WrongPass);

        var flow = new SellerLoginFlow(_page, new SellerLoginCredentials("u", longPass, null, null), new SellerLoginFlowOptions(),
            _logs.Add, _statuses.Add, () => _clock.Now, _clock.Delay);
        var r = await flow.RunAsync(CancellationToken.None);

        Assert.Equal(SellerLoginOutcome.Rejected, r.Outcome);
        Assert.Contains("16 ký tự", r.Message);
        Assert.DoesNotContain(longPass, r.Message);

        var page2 = new FakeLoginPage();
        page2.Script(LoginForm);
        page2.OnSubmit = () => page2.Script(WrongPass);
        var r2 = await new SellerLoginFlow(page2, Full, new SellerLoginFlowOptions(), clock: () => _clock.Now, delay: _clock.Delay)
            .RunAsync(CancellationToken.None);
        Assert.DoesNotContain("16 ký tự", r2.Message);
    }

    [Fact]
    public async Task CookiePhienMoi_TabDungOTrangLa_TaiLaiBanhangMotLan()
    {
        // User bấm link xác minh trong mail → tab khác đăng nhập xong; tab của app đứng ở một trang lạ.
        var strange = new SellerPageSnapshot("https://shopee.vn/some/landing", "Shopee", BodyTextLength: 300);
        _page.Script(LoginForm);
        _page.OnSubmit = () => _page.Script(Verify, Verify, strange with { HasSessionCookie = true });
        _page.OnGoto = n => { if (n == 2) _page.Script(LoggedIn); };

        var r = await Run();

        Assert.Equal(SellerLoginOutcome.LoggedIn, r.Outcome);
        Assert.Equal(2, _page.GotoCalls);
        Assert.Contains(_statuses, s => s.Contains("tải lại"));
    }

    [Theory]
    [InlineData("verify")]
    [InlineData("captcha")]
    [InlineData("otp")]
    public async Task CookiePhienMoi_TrangXacMinhCaptchaHoacOMa_KhongBaoGioTaiLai(string kind)
    {
        // Phản biện vòng 2: ở shopee.vn/verify/ivs, tải lại làm mất trắng mã user đang gõ. Cookie "mới" ở đây có thể chỉ là giữ chỗ.
        var page = kind switch
        {
            "verify" => new SellerPageSnapshot("https://shopee.vn/verify/ivs?is_initial=true", "Shopee", OtpInputVisible: true, BodyTextLength: 80),
            "captcha" => Captcha,
            _ => new SellerPageSnapshot("https://shopee.vn/some/page", "Shopee", OtpInputVisible: true, BodyTextLength: 80),
        };
        _page.Script(LoginForm);
        _page.OnSubmit = () => _page.Script(page, page with { HasSessionCookie = true });

        var r = await Run(NoMail);

        Assert.Equal(SellerLoginOutcome.TimedOut, r.Outcome);
        Assert.Equal(1, _page.GotoCalls);
        Assert.DoesNotContain(_statuses, s => s.Contains("tải lại"));
    }

    [Fact]
    public async Task CookiePhienCoTuTruoc_KhongTaiLaiTrangXacMinh()
    {
        // Cookie có sẵn từ trước khi bấm (không phải phiên mới) → không tải lại (kẻo xoá ô mã user đang gõ).
        _page.Script(LoginForm with { HasSessionCookie = true });
        _page.OnSubmit = () => _page.Script(Verify with { HasSessionCookie = true });

        var r = await Run(NoMail);

        Assert.Equal(SellerLoginOutcome.TimedOut, r.Outcome);
        Assert.Equal(1, _page.GotoCalls);
    }

    [Fact]
    public async Task Huy_LenhPlaywrightNemLoiKhac_VanLaHuy()
    {
        // Runner cắt kết nối khi huỷ → lệnh đang chạy ném PlaywrightException; luồng phải báo HUỶ, không phải "lỗi đăng nhập".
        using var cts = new CancellationTokenSource();
        _page.Script(LoginForm);
        _page.OnSubmit = () => { cts.Cancel(); throw new InvalidOperationException("Target page, context or browser has been closed"); };
        var flow = new SellerLoginFlow(_page, Full, new SellerLoginFlowOptions(), _logs.Add, _statuses.Add, () => _clock.Now, _clock.Delay);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flow.RunAsync(cts.Token));
        Assert.DoesNotContain(_statuses, s => s.Contains("Lỗi khi đăng nhập"));
    }

    [Fact]
    public async Task VanOForm_KhongAlert_KhongBamLai_HetGio()
    {
        _page.Script(LoginForm);

        var r = await Run();

        Assert.Equal(SellerLoginOutcome.TimedOut, r.Outcome);
        Assert.Equal(1, _page.FillCalls);
        Assert.Equal(1, _page.SubmitCalls);
        Assert.Contains(_statuses, s => s.Contains("KHÔNG bấm lại"));
    }

    [Fact]
    public async Task DoiMa_CoEmail_MoHotmailMotLan_DuaTabShopeeLen_NhanKhiXong()
    {
        _page.Script(LoginForm);
        _page.OnSubmit = () => _page.Script(Verify, Verify, Verify, Unknown(), Verify, Verify, LoggedIn);

        var r = await Run();

        Assert.Equal(SellerLoginOutcome.LoggedIn, r.Outcome);
        Assert.Equal(1, _page.MailCalls);                  // vào lại trang xác minh lần 2 không mở Hotmail lần nữa
        Assert.Equal(("shop.a@hotmail.com", MailPass), _page.Mailboxes.Single());
        Assert.Equal(1, _page.FrontCalls);
        Assert.Contains(_statuses, s => s.Contains("đã mở Hotmail"));
    }

    [Fact]
    public async Task DoiMa_KhongEmail_BaoTuLayMa_KhongMoHotmail()
    {
        _page.Script(LoginForm);
        _page.OnSubmit = () => _page.Script(Verify, Verify, LoggedIn);

        var r = await Run(NoMail);

        Assert.Equal(SellerLoginOutcome.LoggedIn, r.Outcome);
        Assert.Equal(0, _page.MailCalls);
        Assert.Contains(_statuses, s => s.Contains("tự lấy mã"));
    }

    [Fact]
    public async Task DoiMa_QuaMuoiLamPhut_HetGio_DeNguyenCuaSo()
    {
        _page.Script(LoginForm);
        _page.OnSubmit = () => _page.Script(Verify);

        var r = await Run();

        Assert.Equal(SellerLoginOutcome.TimedOut, r.Outcome);
        Assert.InRange(Elapsed, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(16));
        Assert.Equal(1, _page.MailCalls);
        Assert.Equal(1, _page.FillCalls);
    }

    [Fact]
    public async Task Captcha_BaoLamTay_RoiNhanKhiXong()
    {
        _page.Script(LoginForm);
        _page.OnSubmit = () => _page.Script(Captcha, Captcha, LoggedIn);

        var r = await Run();

        Assert.Equal(SellerLoginOutcome.LoggedIn, r.Outcome);
        Assert.Single(_statuses, s => s.Contains("Có captcha"));
        Assert.Equal(0, _page.MailCalls);
    }

    [Fact]
    public async Task CaptchaTruoc_RoiMoiRaForm_GoLanDau()
    {
        _page.Script(Captcha, Captcha, LoginForm);
        _page.OnSubmit = () => _page.Script(LoggedIn);

        var r = await Run();

        Assert.Equal(SellerLoginOutcome.LoggedIn, r.Outcome);
        Assert.Equal(1, _page.FillCalls);
    }

    [Fact]
    public async Task ThieuMatKhau_KhongGo_BaoChuaNhapTaiKhoan()
    {
        _page.Script(LoginForm);

        var r = await Run(new SellerLoginCredentials("shop.a@gmail.com", null, null, null));

        Assert.Equal(SellerLoginOutcome.MissingCredentials, r.Outcome);
        Assert.Contains("Chưa nhập tài khoản Shopee", r.Message);
        Assert.Equal(0, _page.FillCalls);
    }

    [Fact]
    public async Task KhongThayO_DungKhongBam()
    {
        _page.Script(LoginForm);
        _page.FillResult = false;

        var r = await Run();

        Assert.Equal(SellerLoginOutcome.FormNotFound, r.Outcome);
        Assert.Equal(0, _page.SubmitCalls);
    }

    [Fact]
    public async Task KhongBamDuocNut_KhongBamLai_VanTheoDoi()
    {
        _page.Script(LoginForm);
        _page.SubmitResult = SellerSubmitResult.StillDisabled;
        var n = 0;
        _page.OnSnapshot = () => { if (++n == 10) _page.Script(LoggedIn); };   // user tự bấm

        var r = await Run();

        Assert.Equal(SellerLoginOutcome.LoggedIn, r.Outcome);
        Assert.Equal(1, _page.SubmitCalls);
        Assert.Contains(_statuses, s => s.Contains("tự bấm"));
    }

    [Fact]
    public async Task TrangLa_LogTitleVaUrl_ChuaRo()
    {
        _page.Script(new SellerPageSnapshot("https://shopee.vn/buyer/login", "Trang lạ", BodyTextLength: 10));

        var r = await Run();

        Assert.Equal(SellerLoginOutcome.Unrecognized, r.Outcome);
        Assert.Contains("title=[Trang lạ], url=https://shopee.vn/buyer/login", r.Message);
        Assert.Equal(0, _page.FillCalls);
    }

    [Fact]
    public async Task DongTab_DungTheoDoi()
    {
        _page.Script(LoginForm);
        _page.OnSubmit = () => { _page.Script(Verify); _page.Closed = true; };

        var r = await Run();

        Assert.Equal(SellerLoginOutcome.WindowClosed, r.Outcome);
    }

    [Fact]
    public async Task LoiChuaMatKhau_DuocChe_TrongLogVaKetQua()
    {
        _page.Script(LoginForm);
        _page.FillThrows = new InvalidOperationException($"Keyboard.type '{Pass}' failed");

        var r = await Run();

        Assert.Equal(SellerLoginOutcome.Failed, r.Outcome);
        Assert.Contains(SellerLoginText.Mask, r.Message);
    }

    [Fact]
    public async Task LoiMoHotmailChuaMatKhau_DuocChe_VanTheoDoi()
    {
        _page.Script(LoginForm);
        _page.OnSubmit = () => _page.Script(Verify, Verify, LoggedIn);
        _page.MailThrows = new InvalidOperationException($"fill {MailPass} lỗi");

        var r = await Run();

        Assert.Equal(SellerLoginOutcome.LoggedIn, r.Outcome);
        Assert.Contains(_logs, l => l.Contains("Lỗi khi mở Hotmail") && l.Contains(SellerLoginText.Mask));
    }

    [Fact]
    public async Task Huy_NemOperationCanceled()
    {
        _page.Script(LoginForm);
        _page.OnSubmit = () => _page.Script(Verify);
        using var cts = new CancellationTokenSource();
        var n = 0;
        _clock.OnDelay = _ => { if (++n == 5) cts.Cancel(); };
        var flow = new SellerLoginFlow(_page, Full, new SellerLoginFlowOptions(), _logs.Add, _statuses.Add, () => _clock.Now, _clock.Delay);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flow.RunAsync(cts.Token));
    }

    [Fact]
    public void Credentials_ToString_KhongInMatKhau()
    {
        var s = Full.ToString();
        Assert.DoesNotContain(Pass, s);
        Assert.DoesNotContain(MailPass, s);
        Assert.Contains("shop.a@gmail.com", s);
        Assert.Contains("(đã nhập)", s);
        Assert.Equal([Pass, MailPass], Full.Secrets());
    }

    [Fact]
    public void Scrub_ThayMoiLan_BoQuaBiMatQuaNgan()
    {
        Assert.Equal("a *** b ***", SellerLoginText.Scrub($"a {Pass} b {Pass}", [Pass, null, "", "ab"]));
        Assert.Equal("ab cd", SellerLoginText.Scrub("ab cd", ["ab"]));
    }

    private static SellerPageSnapshot Unknown() => new("https://banhang.shopee.vn/account/signin", ReadyState: "loading");

    /// <summary>Tab giả. <see cref="Script"/> = dãy snapshot trả lần lượt, cái cuối lặp mãi.</summary>
    private sealed class FakeLoginPage : ISellerLoginPage
    {
        private Queue<SellerPageSnapshot> _script = new([new SellerPageSnapshot("about:blank")]);

        public int GotoCalls, FillCalls, SubmitCalls, MailCalls, FrontCalls;
        public List<(string, string)> Filled { get; } = [];
        public List<(string, string)> Mailboxes { get; } = [];
        public bool Closed { get; set; }
        public bool FillResult { get; set; } = true;
        public Exception? FillThrows { get; set; }
        public Exception? MailThrows { get; set; }
        public SellerSubmitResult SubmitResult { get; set; } = SellerSubmitResult.Clicked;
        public Action? OnSubmit { get; set; }
        public Action? OnSnapshot { get; set; }

        /// <summary>Gọi sau mỗi lần điều hướng về banhang, nhận số thứ tự lần gọi (1 = lần mở đầu).</summary>
        public Action<int>? OnGoto { get; set; }

        public void Script(params SellerPageSnapshot[] snaps) => _script = new Queue<SellerPageSnapshot>(snaps);

        public bool IsClosed => Closed;

        public Task GotoSellerHomeAsync(CancellationToken ct) { GotoCalls++; OnGoto?.Invoke(GotoCalls); return Task.CompletedTask; }

        public Task<SellerPageSnapshot> SnapshotAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            OnSnapshot?.Invoke();
            return Task.FromResult(_script.Count > 1 ? _script.Dequeue() : _script.Peek());
        }

        public Task<bool> FillLoginAsync(string login, string password, CancellationToken ct)
        {
            FillCalls++;
            if (FillThrows is not null) throw FillThrows;
            Filled.Add((login, password));
            return Task.FromResult(FillResult);
        }

        public Task<SellerSubmitResult> SubmitAsync(CancellationToken ct)
        {
            SubmitCalls++;
            OnSubmit?.Invoke();
            return Task.FromResult(SubmitResult);
        }

        public Task<bool> OpenMailboxAsync(string email, string password, Action<string> log, CancellationToken ct)
        {
            MailCalls++;
            Mailboxes.Add((email, password));
            if (MailThrows is not null) throw MailThrows;
            return Task.FromResult(true);
        }

        public Task BringToFrontAsync() { FrontCalls++; return Task.CompletedTask; }
    }
}
