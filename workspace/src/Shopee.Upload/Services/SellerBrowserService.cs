using Shopee.Core.MassUpload.Seller;
using Shopee.Core.MassUpload.Seller.Login;

namespace Shopee.Upload.Services;

/// <summary>Chỗ mở Brave của màn chính — tách ra để test luật "mỗi lúc một Brave" / "tắt app thì đóng Brave" bằng bản giả.
/// Bản thật duy nhất là <see cref="SellerBrowserService"/> (đi qua <see cref="SellerRunGate"/>).</summary>
public interface ISellerBrowserOpener
{
    Task<SellerGateResult> OpenAsync(SellerAccount account, SellerUploadSettings settings, string profileDir, CancellationToken ct);

    /// <summary>Như <see cref="OpenAsync"/> nhưng mở chế độ <see cref="SellerLaunchMode.Login"/> (CDP loopback, GĐ5b) — cùng gate,
    /// cùng mọi luật proxy / IP.</summary>
    Task<SellerGateResult> OpenForLoginAsync(SellerAccount account, SellerUploadSettings settings, string profileDir, CancellationToken ct);

    /// <summary>Chạy luồng đăng nhập Shopee trên Brave chế độ đăng nhập; xong thì ngắt Playwright, KHÔNG đóng Brave.
    /// <paramref name="log"/> / <paramref name="status"/> gọi từ luồng nền.</summary>
    Task<SellerLoginResult> LoginAsync(SellerBrowserSession session, SellerLoginCredentials credentials, Action<string> log,
        Action<string> status, CancellationToken ct);

    /// <summary>Còn tiến trình Brave nào chạy trên profile của phiên không (quét tiến trình — chậm, gọi ngoài luồng UI). Dùng khi
    /// không hỏi được <c>Process.HasExited</c> (Process đã bị dispose sau một lần đóng thất bại).</summary>
    bool IsBrowserRunning(SellerBrowserSession session);
}

/// <summary>
/// Dựng các lớp GĐ4 thật cho nút "Mở Brave qua proxy" (plan GĐ5a mục 4): <see cref="SellerKiotApi"/> → <see cref="SellerProxyProvider"/>
/// → <see cref="ExitIpVerifier"/> → <see cref="SellerRunGate"/> với <see cref="SellerBrowserLauncher"/> + <see cref="BraveSellerHost"/>.
/// <para>MỘT bản cho cả phiên app: gate giữ danh sách IP đã dùng, provider giữ mốc đổi IP của key — dựng lại mỗi lần bấm là mất
/// luật "IP mới khác mọi IP đã dùng" và gọi <c>/new</c> sớm.</para>
/// <para>Đường mở Brave DUY NHẤT của app là <see cref="SellerRunGate.OpenAsync(SellerAccount, SellerUploadSettings, string, IReadOnlyList{string}, SellerLaunchMode, CancellationToken)"/>
/// (chế độ thường hoặc đăng nhập).</para>
/// </summary>
public sealed class SellerBrowserService : ISellerBrowserOpener
{
    private readonly SellerRunGate _gate;
    private readonly BraveSellerHost _host;

    /// <param name="log">Sink log (gọi từ luồng nền).</param>
    /// <param name="waiting">Đếm ngược chờ mốc đổi IP Kiot (gọi từ luồng nền).</param>
    public SellerBrowserService(Action<string> log, Action<KiotWaitTick> waiting)
    {
        var kiot = new SellerKiotApi(SellerKiotApi.CreateHttpClient());
        var proxies = new SellerProxyProvider(kiot, log: log);
        proxies.Waiting += waiting;
        var verifier = new ExitIpVerifier(log: log);
        _host = new BraveSellerHost(log);
        var launcher = new SellerBrowserLauncher(log: log, host: _host);
        _gate = new SellerRunGate(proxies, verifier, launcher, log: log);
    }

    /// <summary>Kiểm proxy → kiểm IP → mở Brave <c>about:blank</c>, KHÔNG nạp extension nghiệp vụ (GĐ5a chưa có GĐ3).</summary>
    public Task<SellerGateResult> OpenAsync(SellerAccount account, SellerUploadSettings settings, string profileDir, CancellationToken ct) =>
        _gate.OpenAsync(account, settings, profileDir, [], SellerLaunchMode.Normal, ct);

    public Task<SellerGateResult> OpenForLoginAsync(SellerAccount account, SellerUploadSettings settings, string profileDir, CancellationToken ct) =>
        _gate.OpenAsync(account, settings, profileDir, [], SellerLaunchMode.Login, ct);

    public Task<SellerLoginResult> LoginAsync(SellerBrowserSession session, SellerLoginCredentials credentials, Action<string> log,
        Action<string> status, CancellationToken ct) =>
        SellerLoginRunner.RunAsync(session, credentials, log, status, ct);

    /// <summary>Quét brave.exe theo <c>--user-data-dir</c> = profile của phiên (cùng phép so của launcher).</summary>
    public bool IsBrowserRunning(SellerBrowserSession session) => _host.FindProfileProcesses(session.ProfileDir).Count > 0;
}
