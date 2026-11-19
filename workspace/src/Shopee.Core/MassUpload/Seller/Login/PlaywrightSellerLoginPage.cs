using System.Text.Json;
using Microsoft.Playwright;

namespace Shopee.Core.MassUpload.Seller.Login;

/// <summary>
/// Bản thật của <see cref="ISellerLoginPage"/> trên tab Playwright đã nối qua CDP. Gõ ô nhập qua <see cref="SellerLoginFormFill"/> /
/// <see cref="HumanFieldFill"/> (tìm lại ô ngay trước khi gõ, kiểm focus từng ký tự), bấm có hit-test (<see cref="LoginHumanInput"/>),
/// mở Hotmail bằng <see cref="MicrosoftMailLogin"/>. KHÔNG log gì ở đây — mọi log đi qua <see cref="SellerLoginFlow"/> (đã che mật khẩu).
/// </summary>
internal sealed class PlaywrightSellerLoginPage : ISellerLoginPage
{
    /// <summary>Một lần chạy JS đọc mọi dấu hiệu DOM của <see cref="SellerPageSnapshot"/>: chỉ cờ "đang hiện", chữ của
    /// <c>div[role=alert]</c> (tối đa 300 ký tự, đúng phần tử lỗi đăng nhập của form) và độ dài chữ — KHÔNG đọc giá trị ô nhập.</summary>
    private const string SnapshotJs =
        """
        (a) => {
          const vis = el => { for (const r of el.getClientRects()) { if (r.width > 0 && r.height > 0) return true; } return false; };
          const any = sels => sels.some(s => { try { return Array.from(document.querySelectorAll(s)).some(vis); } catch (e) { return false; } });
          const alerts = Array.from(document.querySelectorAll("div[role='alert']")).filter(vis)
            .map(x => (x.innerText || '').trim()).filter(t => t.length > 0).join(' | ');
          const body = document.body ? (document.body.innerText || '') : '';
          return JSON.stringify({
            url: location.href, title: document.title || '', ready: document.readyState,
            loginKey: any(a.loginKey), password: any(a.password), otp: any(a.otp), captcha: any(a.captcha), shell: any(a.shell),
            alert: alerts.slice(0, 300), bodyLen: body.trim().length });
        }
        """;

    /// <summary>So giá trị ô tài khoản TRONG trang; chỉ trả true/false.</summary>
    private const string LoginKeyHoldsJs =
        "(v) => { const e = document.querySelector(\"input[name='loginKey']\"); return !!e && e.value === v; }";

    private static readonly JsonSerializerOptions SnapJson = new() { PropertyNameCaseInsensitive = true };

    private sealed record SnapDto(string? Url, string? Title, string? Ready, bool LoginKey, bool Password, bool Otp, bool Captcha,
        string? Alert, int BodyLen, bool Shell);

    private readonly IBrowserContext _context;
    private readonly IPage _page;
    private readonly Random _rng = new();
    private readonly MouseState _mouse;

    public PlaywrightSellerLoginPage(IBrowserContext context, IPage page)
    {
        _context = context;
        _page = page;
        _mouse = MouseState.CenterOf(page);
    }

    public bool IsClosed => _page.IsClosed;

    public async Task GotoSellerHomeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            await _page.GotoAsync(SellerBrowserArgs.SellerHomeUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60000,
            }).ConfigureAwait(false);
        }
        catch (PlaywrightException) when (!ct.IsCancellationRequested) { /* nuốt lỗi điều hướng — bước dò trang tự lo */ }
        catch (TimeoutException) when (!ct.IsCancellationRequested) { }
        ct.ThrowIfCancellationRequested();
    }

    public async Task<SellerPageSnapshot> SnapshotAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var cookie = await HasSessionCookieAsync().ConfigureAwait(false);
        try
        {
            var json = await _page.EvaluateAsync<string>(SnapshotJs, new
            {
                loginKey = SellerLoginSelectors.LoginKey,
                password = SellerLoginSelectors.PasswordDetect,
                otp = SellerLoginSelectors.OtpInput,
                captcha = SellerLoginSelectors.Captcha,
                shell = SellerLoginSelectors.SellerShell,
            }).ConfigureAwait(false);
            var d = JsonSerializer.Deserialize<SnapDto>(json, SnapJson);
            if (d is not null)
                return new SellerPageSnapshot(d.Url ?? _page.Url, d.Title, d.LoginKey, d.Password, d.Otp, d.Captcha, d.Alert, d.Ready,
                    d.BodyLen, cookie, d.Shell);
        }
        catch (Exception) when (!ct.IsCancellationRequested) { /* đang chuyển trang ("Execution context was destroyed") */ }
        ct.ThrowIfCancellationRequested();
        return new SellerPageSnapshot(SafeUrl(), ReadyState: null, HasSessionCookie: cookie);
    }

    /// <summary>Có cookie phiên hợp lệ cho <c>banhang.shopee.vn</c> không — luật <c>ShopeeAuth.IsSessionCookie</c> (giá trị giữ chỗ
    /// <c>-</c> của Shopee KHÔNG tính). Giá trị cookie chỉ dùng trong hàm, không trả ra, không log.</summary>
    private async Task<bool> HasSessionCookieAsync()
    {
        try
        {
            var cookies = await _context.CookiesAsync([SellerBrowserArgs.SellerHomeUrl]).ConfigureAwait(false);
            return SellerLoginPageClassifier.HasSessionCookie(cookies.Select(c => (c.Domain, c.Name, c.Value)));
        }
        catch (PlaywrightException) { return false; }
    }

    public Task<bool> FillLoginAsync(string login, string password, CancellationToken ct) =>
        SellerLoginFormFill.FillAsync(new Form(this), login, password, _rng, (d, t) => Task.Delay(d, t), ct);

    public async Task<SellerSubmitResult> SubmitAsync(CancellationToken ct)
    {
        // Nút hết disabled khi đã gõ đủ hai ô — chờ tối đa ~8 s, mỗi vòng lấy handle mới.
        var deadline = DateTime.UtcNow.AddSeconds(8);
        IElementHandle? button = null;
        var enabled = false;
        do
        {
            button = await FindSubmitAsync(ct).ConfigureAwait(false);
            if (button is not null)
            {
                try { enabled = await button.IsEnabledAsync().ConfigureAwait(false); }
                catch (PlaywrightException) { enabled = false; }
                if (enabled) break;
            }
            await Task.Delay(300, ct).ConfigureAwait(false);
        }
        while (DateTime.UtcNow < deadline);

        if (button is null) return SellerSubmitResult.NotFound;
        if (!enabled) return SellerSubmitResult.StillDisabled;
        await Task.Delay(_rng.Next(400, 1100), ct).ConfigureAwait(false);
        var (x, y, clicked) = await LoginHumanInput.TryHumanClickVisibleAsync(_page, button, _mouse.X, _mouse.Y, _rng, ct).ConfigureAwait(false);
        _mouse.X = x;
        _mouse.Y = y;
        return clicked ? SellerSubmitResult.Clicked : SellerSubmitResult.NotClicked;
    }

    public async Task<bool> OpenMailboxAsync(string email, string password, Action<string> log, CancellationToken ct)
    {
        var (_, ok) = await MicrosoftMailLogin.OpenMailboxSignedInAsync(_context, email, password, log, _rng, ct).ConfigureAwait(false);
        return ok;
    }

    public async Task BringToFrontAsync()
    {
        try { await _page.BringToFrontAsync().ConfigureAwait(false); } catch { /* best-effort */ }
    }

    /// <summary>Nút đầu tiên đang hiện trong <see cref="SellerLoginSelectors.Submit"/> có chữ đúng "Log In"/"Đăng nhập".</summary>
    private async Task<IElementHandle?> FindSubmitAsync(CancellationToken ct)
    {
        foreach (var sel in SellerLoginSelectors.Submit)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<IElementHandle> els;
            try { els = await _page.QuerySelectorAllAsync(sel).ConfigureAwait(false); }
            catch (PlaywrightException) { continue; }
            foreach (var el in els)
            {
                try
                {
                    if (!await LoginPageProbe.IsElementVisibleByClientRectsAsync(el).ConfigureAwait(false)) continue;
                    if (SellerLoginSelectors.IsSubmitText(await el.InnerTextAsync().ConfigureAwait(false))) return el;
                }
                catch (PlaywrightException) { /* detached */ }
            }
        }
        return null;
    }

    private string SafeUrl()
    {
        try { return _page.Url; } catch { return ""; }
    }

    /// <summary>Form đăng nhập trên tab này cho <see cref="SellerLoginFormFill"/>.</summary>
    private sealed class Form(PlaywrightSellerLoginPage owner) : ISellerLoginForm
    {
        public bool OnSellerHost =>
            Uri.TryCreate(owner.SafeUrl(), UriKind.Absolute, out var u)
            && string.Equals(u.Host, SellerLoginPageClassifier.SellerHost, StringComparison.OrdinalIgnoreCase);

        public Task<IHumanField?> FindLoginKeyAsync(CancellationToken ct) => FindAsync(SellerLoginSelectors.LoginKey, 8000, ct);

        public Task<IHumanField?> FindPasswordAsync(CancellationToken ct) => FindAsync(SellerLoginSelectors.Password, 4000, ct);

        private async Task<IHumanField?> FindAsync(string[] selectors, int timeoutMs, CancellationToken ct)
        {
            var el = await LoginPageProbe.FindFirstVisibleByRectsAsync(owner._page, selectors, timeoutMs, ct).ConfigureAwait(false);
            return el is null ? null : new PlaywrightHumanField(owner._page, el, owner._mouse, owner._rng);
        }

        public async Task<bool> LoginKeyHoldsAsync(string login)
        {
            try { return await owner._page.EvaluateAsync<bool>(LoginKeyHoldsJs, login).ConfigureAwait(false); }
            catch (PlaywrightException) { return false; }
        }

        public async Task ClearLoginKeyAsync()
        {
            try
            {
                var el = await owner._page.QuerySelectorAsync("input[name='loginKey']").ConfigureAwait(false);
                if (el is not null) await el.FillAsync("", new ElementHandleFillOptions { Timeout = 3000 }).ConfigureAwait(false);
            }
            catch (PlaywrightException) { }
            catch (TimeoutException) { }
        }
    }
}
