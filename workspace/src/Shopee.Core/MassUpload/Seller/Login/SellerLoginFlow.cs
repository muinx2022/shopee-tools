namespace Shopee.Core.MassUpload.Seller.Login;

/// <summary>Kết quả bấm nút "Log In" / "Đăng nhập".</summary>
internal enum SellerSubmitResult
{
    Clicked,
    /// <summary>Không thấy nút.</summary>
    NotFound,
    /// <summary>Nút vẫn <c>disabled</c> sau khi gõ đủ hai ô.</summary>
    StillDisabled,
    /// <summary>Thấy nút nhưng điểm bấm bị phần tử khác che (hit-test hỏng) — không bấm mù.</summary>
    NotClicked,
}

/// <summary>Những gì <see cref="SellerLoginFlow"/> cần từ tab Shopee. Bản thật <see cref="PlaywrightSellerLoginPage"/>; test dùng
/// bản giả để kiểm luật luồng không cần Brave.</summary>
internal interface ISellerLoginPage
{
    /// <summary>Tab Shopee đã bị đóng (user đóng tab / cửa sổ, hoặc mất kết nối CDP).</summary>
    bool IsClosed { get; }

    /// <summary>Điều hướng tới <see cref="SellerBrowserArgs.SellerHomeUrl"/>. Lỗi điều hướng nuốt (bước sau tự dò trang).</summary>
    Task GotoSellerHomeAsync(CancellationToken ct);

    /// <summary>Chụp dấu hiệu trang. Không ném (trừ huỷ): đọc hỏng → snapshot chỉ có URL.</summary>
    Task<SellerPageSnapshot> SnapshotAsync(CancellationToken ct);

    /// <summary>Gõ kiểu người vào <c>loginKey</c> rồi <c>password</c>. false = không thấy ô.</summary>
    Task<bool> FillLoginAsync(string login, string password, CancellationToken ct);

    /// <summary>Chờ nút đăng nhập hết <c>disabled</c> rồi bấm kiểu người (có hit-test).</summary>
    Task<SellerSubmitResult> SubmitAsync(CancellationToken ct);

    /// <summary>Mở tab mới, đăng nhập Hotmail (không đọc mail). true = đã vào hộp thư. Best-effort.</summary>
    Task<bool> OpenMailboxAsync(string email, string password, Action<string> log, CancellationToken ct);

    /// <summary>Đưa tab Shopee lên trước. Best-effort.</summary>
    Task BringToFrontAsync();
}

/// <summary>Các mốc thời gian của luồng (số trong ngoặc là mặc định).</summary>
internal sealed class SellerLoginFlowOptions
{
    /// <summary>Dò trạng thái đầu sau khi vào banhang (SPA còn render) (30 s).</summary>
    public TimeSpan InitialDetectTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Chờ user nhập mã / làm captcha (plan: tối đa 15 phút).</summary>
    public TimeSpan WaitForUserTimeout { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>"Đã đăng nhập" phải giữ liên tục chừng này mới tính (5 s).</summary>
    public TimeSpan StableFor { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Bấm xong mà vẫn ở form (không alert) chừng này thì nhắc user một lần (20 s).</summary>
    public TimeSpan StillOnFormNotice { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>Cookie phiên MỚI xuất hiện mà tab vẫn đứng ở trang LẠ (<see cref="SellerPageKind.Unknown"/>, không ô mã, không captcha)
    /// chừng này (vd user bấm link xác minh mở ra tab khác) → tải lại banhang MỘT lần để xác nhận (15 s). Trang xác minh / captcha thì
    /// không bao giờ tải lại.</summary>
    public TimeSpan ReloadAfterNewCookie { get; init; } = TimeSpan.FromSeconds(15);
}

/// <summary>
/// Luồng tự đăng nhập Seller Centre (plan GĐ5b mục 3), khuôn theo <c>suite/orders/…/SubaccountLoginFlow</c>: gõ user/pass, bấm, khi
/// Shopee đòi mã thì mở hộp thư cho USER tự lấy mã, rồi chỉ THEO DÕI tới khi trang về Seller Centre (tối đa 15 phút). Bỏ phần
/// nghiệp vụ check đơn (bắc cầu SSO subaccount, "Kênh Người bán", chuẩn hoá tab).
/// <list type="bullet">
/// <item><b>Gõ + bấm nhiều nhất MỘT lần mỗi lượt.</b> Shopee báo lỗi (<see cref="SellerPageKind.LoginError"/>) sau khi bấm → DỪNG, trả
/// câu của Shopee; KHÔNG gõ lại (tránh khoá tài khoản). Chỉ tính alert MỚI: khác chữ alert chụp ngay trước khi bấm, hoặc alert cũ đã
/// biến mất rồi hiện lại — alert có sẵn (vd "Phiên đăng nhập đã hết hạn…") không làm luồng dừng. Gõ không an toàn
/// (<see cref="SellerLoginFormFill"/>) → dừng, KHÔNG bấm. Bấm không được thì để user tự bấm, vẫn theo dõi.</item>
/// <item>Huỷ: ném <see cref="OperationCanceledException"/> kể cả khi lệnh Playwright đang treo ném lỗi khác vì runner vừa cắt kết
/// nối.</item>
/// <item>Xác minh: mở Hotmail MỘT lần (có email) rồi đưa tab Shopee lên trước. Captcha: báo user làm tay.</item>
/// <item>Đã đăng nhập = <see cref="SellerPageKind.LoggedIn"/> liên tục <see cref="SellerLoginFlowOptions.StableFor"/>.</item>
/// <item>Mọi log / trạng thái / câu kết quả qua <see cref="SellerLoginText.Scrub"/> với mọi mật khẩu của lượt.</item>
/// <item>Không ném, trừ huỷ (<see cref="OperationCanceledException"/>).</item>
/// </list>
/// </summary>
internal sealed class SellerLoginFlow
{
    private readonly ISellerLoginPage _page;
    private readonly SellerLoginCredentials _creds;
    private readonly SellerLoginFlowOptions _opt;
    private readonly Action<string>? _log;
    private readonly Action<string>? _status;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SellerLoggedInStability _stable;
    private bool _submitted;

    /// <summary>Chữ alert (đã chuẩn hoá) chụp NGAY TRƯỚC khi bấm Đăng nhập — alert có sẵn từ trước (vd "Phiên đăng nhập đã hết
    /// hạn…") không phải câu trả lời của Shopee cho lần bấm này.</summary>
    private string _alertBefore = "";

    /// <summary>Sau khi bấm đã thấy (ở một lần chụp đọc được) alert khác <see cref="_alertBefore"/> / không còn alert — alert cũ hiện
    /// lại sau đó thì tính là câu trả lời mới.</summary>
    private bool _alertBeforeGone;

    /// <summary>Có cookie phiên ở lần chụp ngay trước khi bấm (hoặc lần chụp đầu của bước theo dõi); null = chưa chụp.</summary>
    private bool? _cookieBefore;

    public SellerLoginFlow(ISellerLoginPage page, SellerLoginCredentials creds, SellerLoginFlowOptions? options = null,
        Action<string>? log = null, Action<string>? status = null, Func<DateTimeOffset>? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _page = page;
        _creds = creds;
        _opt = options ?? new SellerLoginFlowOptions();
        _log = log;
        _status = status;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? ((d, ct) => Task.Delay(d, ct));
        _stable = new SellerLoggedInStability(_opt.StableFor);
    }

    public async Task<SellerLoginResult> RunAsync(CancellationToken ct)
    {
        try
        {
            Status("Đang mở banhang.shopee.vn…");
            await _page.GotoSellerHomeAsync(ct).ConfigureAwait(false);

            // ── 1) Dò trạng thái đầu.
            var deadline = _clock() + _opt.InitialDetectTimeout;
            SellerPageSnapshot snap;
            SellerPageKind kind;
            while (true)
            {
                if (_page.IsClosed) return Done(SellerLoginOutcome.WindowClosed, "Tab Shopee đã bị đóng — dừng đăng nhập.");
                snap = await _page.SnapshotAsync(ct).ConfigureAwait(false);
                kind = SellerLoginPageClassifier.Classify(snap);
                if (_stable.Observe(kind, _clock())) return Done(SellerLoginOutcome.AlreadyLoggedIn, "Đã đăng nhập sẵn (profile còn phiên).");
                if (kind is SellerPageKind.LoginForm or SellerPageKind.LoginError or SellerPageKind.Verify or SellerPageKind.Captcha) break;
                if (kind != SellerPageKind.LoggedIn && _clock() >= deadline)
                    return Done(SellerLoginOutcome.Unrecognized, $"Không nhận ra trang sau {_opt.InitialDetectTimeout.TotalSeconds:0} s — " +
                                                                  $"đăng nhập tay trong cửa sổ Brave. {snap.Diag}");
                await _delay(_opt.PollInterval, ct).ConfigureAwait(false);
            }

            // ── 2) Form đăng nhập → gõ + bấm (một lần).
            if (kind is SellerPageKind.LoginForm or SellerPageKind.LoginError)
            {
                var stop = await SubmitOnceAsync(snap, ct).ConfigureAwait(false);
                if (stop is not null) return stop;
            }

            // ── 3) Theo dõi tới khi đăng nhập xong.
            return await WatchAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // Huỷ: runner đóng kết nối Playwright ngay để lệnh đang treo ném — lỗi đó là huỷ, không phải "đăng nhập lỗi".
            throw new OperationCanceledException(ct);
        }
        catch (Exception ex)
        {
            return Done(SellerLoginOutcome.Failed, $"Lỗi khi đăng nhập Shopee: {ex.Message} — để nguyên cửa sổ Brave để làm tay.");
        }
    }

    private static string NormAlert(string? alert) => (alert ?? "").Trim();

    /// <summary>Gõ tài khoản + mật khẩu rồi bấm — CHỈ một lần mỗi lượt (<see cref="_submitted"/>). Trả kết quả dừng, hoặc null = đi
    /// tiếp sang theo dõi.</summary>
    private async Task<SellerLoginResult?> SubmitOnceAsync(SellerPageSnapshot snap, CancellationToken ct)
    {
        if (_submitted) return null;
        if (!_creds.HasShopeeLogin)
            return Done(SellerLoginOutcome.MissingCredentials, "Chưa nhập tài khoản Shopee — nhập tài khoản + mật khẩu rồi thử lại, " +
                                                               "hoặc đăng nhập tay trong cửa sổ Brave.");
        _submitted = true;
        Status("Đang gõ tài khoản + mật khẩu Shopee…");
        if (!await _page.FillLoginAsync(_creds.Login!, _creds.Password!, ct).ConfigureAwait(false))
            return Done(SellerLoginOutcome.FormNotFound, "Không gõ được tài khoản / mật khẩu an toàn (không thấy ô, bấm không trúng, hoặc " +
                                                         $"trang vẽ lại ô giữa lúc gõ) — dừng, KHÔNG bấm Đăng nhập; đăng nhập tay. {snap.Diag}");

        // Alert + cookie NGAY TRƯỚC khi bấm: mốc để phân biệt câu trả lời của lần bấm này với cái có sẵn.
        var pre = await _page.SnapshotAsync(ct).ConfigureAwait(false);
        _alertBefore = NormAlert(pre.AlertText);
        _cookieBefore = pre.HasSessionCookie;

        var r = await _page.SubmitAsync(ct).ConfigureAwait(false);
        if (r == SellerSubmitResult.Clicked)
        {
            Status("Đã bấm Đăng nhập — đang chờ Shopee…");
            return null;
        }
        var why = r switch
        {
            SellerSubmitResult.NotFound => "không thấy nút",
            SellerSubmitResult.StillDisabled => "nút vẫn bị khoá",
            _ => "nút bị che",
        };
        Status($"Không bấm được nút Đăng nhập ({why}) — tự bấm trong cửa sổ Brave; app vẫn theo dõi.");
        return null;
    }

    private async Task<SellerLoginResult> WatchAsync(CancellationToken ct)
    {
        var deadline = _clock() + _opt.WaitForUserTimeout;
        var mailTried = false;
        SellerPageKind? last = null;
        string? lastUnknownUrl = null;
        DateTimeOffset? onFormSince = null;
        var formNoticed = false;
        DateTimeOffset? newCookieSince = null;
        var reloaded = false;

        while (true)
        {
            if (_page.IsClosed) return Done(SellerLoginOutcome.WindowClosed, "Tab Shopee đã bị đóng — dừng theo dõi đăng nhập.");
            var snap = await _page.SnapshotAsync(ct).ConfigureAwait(false);
            var kind = SellerLoginPageClassifier.Classify(snap);
            var now = _clock();
            if (_stable.Observe(kind, now)) return Done(SellerLoginOutcome.LoggedIn, "Đã đăng nhập Shopee.");

            var alert = NormAlert(snap.AlertText);
            var readable = snap.ReadyState is not null;   // lần chụp đọc được DOM (không phải lúc đang chuyển trang)
            var newAlert = alert.Length > 0 && (alert != _alertBefore || _alertBeforeGone);
            if (_submitted && readable && alert != _alertBefore) _alertBeforeGone = true;

            // Cookie phiên MỚI có mà tab đứng ở trang LẠ (vd user bấm link xác minh mở ra tab khác) → tải lại banhang một lần để xác
            // nhận. KHÔNG BAO GIỜ tải lại khi trang là xác minh / captcha / đang có ô nhập mã: user có thể đang gõ mã (phản biện vòng 2
            // tái hiện: tải lại ở shopee.vn/verify/ivs làm mất trắng mã đang gõ).
            _cookieBefore ??= snap.HasSessionCookie;
            if (snap.HasSessionCookie && _cookieBefore == false && kind == SellerPageKind.Unknown
                && !snap.OtpInputVisible && !snap.CaptchaVisible)
            {
                newCookieSince ??= now;
                if (!reloaded && now - newCookieSince.Value >= _opt.ReloadAfterNewCookie)
                {
                    reloaded = true;
                    Status("Đã có phiên đăng nhập mới — tải lại banhang.shopee.vn để xác nhận…");
                    await _page.GotoSellerHomeAsync(ct).ConfigureAwait(false);
                }
            }
            else newCookieSince = null;

            switch (kind)
            {
                case SellerPageKind.LoginError when _submitted && newAlert:
                    // Câu trả lời MỚI của Shopee cho lần bấm này (sai mật khẩu / tài khoản…): DỪNG, không gõ lại.
                    return Done(SellerLoginOutcome.Rejected, RejectedMessage(alert));

                case SellerPageKind.LoginForm or SellerPageKind.LoginError:
                    if (!_submitted)
                    {
                        // Lúc đầu là xác minh / captcha, giờ mới ra form → gõ lần đầu (và duy nhất).
                        var stop = await SubmitOnceAsync(snap, ct).ConfigureAwait(false);
                        if (stop is not null) return stop;
                        break;
                    }
                    onFormSince ??= now;
                    if (!formNoticed && now - onFormSince.Value >= _opt.StillOnFormNotice)
                    {
                        formNoticed = true;
                        Status("Bấm Đăng nhập xong vẫn ở form đăng nhập — app KHÔNG bấm lại; xem trong cửa sổ Brave.");
                    }
                    break;

                case SellerPageKind.Verify when last != SellerPageKind.Verify:
                    if (!_creds.HasMailbox)
                    {
                        Status("Đang chờ mã — tự lấy mã rồi nhập vào trang Shopee.");
                    }
                    else if (!mailTried)
                    {
                        mailTried = true;
                        await OpenMailboxAsync(ct).ConfigureAwait(false);
                    }
                    else
                    {
                        Status("Đang chờ mã — Hotmail đã mở ở tab bên.");
                    }
                    break;

                case SellerPageKind.Captcha when last != SellerPageKind.Captcha:
                    Status("Có captcha — làm tay trong cửa sổ Brave.");
                    break;

                case SellerPageKind.Unknown when snap.Url != lastUnknownUrl:
                    lastUnknownUrl = snap.Url;
                    Log($"Chưa nhận ra trang — chờ tiếp. {snap.Diag}");
                    break;
            }
            if (kind is not (SellerPageKind.LoginForm or SellerPageKind.LoginError)) onFormSince = null;
            last = kind;

            if (_clock() >= deadline)
                return Done(SellerLoginOutcome.TimedOut, $"Chờ {_opt.WaitForUserTimeout.TotalMinutes:0} phút chưa thấy đăng nhập xong — " +
                                                         $"để nguyên cửa sổ Brave. {snap.Diag}");
            await _delay(_opt.PollInterval, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Câu báo khi Shopee từ chối: đúng câu của Shopee; mật khẩu dài hơn giới hạn của ô thì nhắc thêm (gõ vào bị cắt).</summary>
    private string RejectedMessage(string alert)
    {
        var m = $"Shopee báo: {alert} — dừng, KHÔNG thử lại.";
        if (_creds.Password is { Length: > SellerAccountStore.ShopeePasswordMaxLength })
            m += $" Mật khẩu đang dài hơn {SellerAccountStore.ShopeePasswordMaxLength} ký tự — ô mật khẩu của Shopee chỉ nhận " +
                 $"{SellerAccountStore.ShopeePasswordMaxLength} ký tự, phần sau bị cắt; kiểm lại mật khẩu.";
        return m;
    }

    /// <summary>Mở + đăng nhập Hotmail ở tab mới (best-effort), rồi đưa tab Shopee lên trước.</summary>
    private async Task OpenMailboxAsync(CancellationToken ct)
    {
        Status("Shopee đòi mã — đang mở Hotmail…");
        bool ok;
        try { ok = await _page.OpenMailboxAsync(_creds.VerifyEmail!, _creds.VerifyEmailPassword!, Log, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Log($"Lỗi khi mở Hotmail: {ex.Message}");
            ok = false;
        }
        try { await _page.BringToFrontAsync().ConfigureAwait(false); } catch { /* best-effort */ }
        Status(ok
            ? "Đang chờ mã — đã mở Hotmail ở tab bên. Lấy mã rồi nhập vào trang Shopee."
            : "Đang chờ mã — chưa tự đăng nhập được Hotmail, tự đăng nhập ở tab mail (hoặc tự lấy mã) rồi nhập vào trang Shopee.");
    }

    private string Scrub(string text) => SellerLoginText.Scrub(text, _creds.Secrets());

    private void Log(string message) => _log?.Invoke(Scrub(message));

    /// <summary>Một bước mới: hiện lên thanh trạng thái và ghi log.</summary>
    private void Status(string message)
    {
        var m = Scrub(message);
        _status?.Invoke(m);
        _log?.Invoke(m);
    }

    private SellerLoginResult Done(SellerLoginOutcome outcome, string message)
    {
        Status(message);
        return new SellerLoginResult(outcome, Scrub(message));
    }
}
