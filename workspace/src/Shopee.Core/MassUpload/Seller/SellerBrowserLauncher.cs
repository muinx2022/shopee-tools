using Shopee.Core.Browser;
using System.Text.RegularExpressions;
using Shopee.Core.Infrastructure;
using Shopee.Core.Platform;

namespace Shopee.Core.MassUpload.Seller;

/// <summary>Yêu cầu mở Brave cho một shop. <see cref="ExitIp"/> = IP ra đã kiểm đạt (<see cref="ExitIpVerifier"/>).
/// <see cref="Mode"/> = <see cref="SellerLaunchMode.Login"/> thì mở kèm CDP loopback cho luồng đăng nhập (GĐ5b).</summary>
public sealed record SellerLaunchRequest(string AccountId, string ProfileDir, ProxyLease Proxy, string ExitIp,
    IReadOnlyList<string> ExtensionDirs, string StartUrl = SellerBrowserArgs.StartUrl,
    SellerLaunchMode Mode = SellerLaunchMode.Normal);

/// <summary>Brave đang mở của một shop. <see cref="CloseAsync"/> đóng sạch và đợi tiến trình thoát — gọi xong mới sang shop kế.
/// Gọi nhiều lần an toàn.</summary>
public sealed class SellerBrowserSession : IAsyncDisposable
{
    private readonly Func<Task<bool>> _close;
    private Task<bool>? _closing;
    private readonly object _lock = new();

    /// <param name="cdpPort">Cổng CDP loopback đã kiểm (chỉ chế độ <see cref="SellerLaunchMode.Login"/>); null = mở thường.</param>
    public SellerBrowserSession(Process? process, string profileDir, ProxyLease proxy, string exitIp, Func<Task<bool>> close,
        int? cdpPort = null)
    {
        Process = process;
        ProfileDir = profileDir;
        Proxy = proxy;
        ExitIp = exitIp;
        _close = close;
        CdpPort = cdpPort;
    }

    public Process? Process { get; }
    public string ProfileDir { get; }
    public ProxyLease Proxy { get; }
    public string ExitIp { get; }

    /// <summary>Cổng CDP (chỉ nghe 127.0.0.1) của Brave chế độ đăng nhập; null = Brave mở thường, không CDP.</summary>
    public int? CdpPort { get; }

    /// <summary>Endpoint cho <c>ConnectOverCDPAsync</c>; null = không CDP.</summary>
    public string? CdpEndpoint => CdpPort is { } p ? SellerCdp.Endpoint(p) : null;

    /// <summary>true = đã xác nhận mọi tiến trình Brave của profile thoát. Đang đóng / đã đóng xong (true) → trả lại đúng lượt đó;
    /// lượt trước trả false hoặc lỗi (Brave chưa thoát hẳn) → gọi lại sẽ đóng THÊM một lượt.</summary>
    public Task<bool> CloseAsync()
    {
        lock (_lock)
        {
            if (_closing is { IsCompleted: true } done && !(done.IsCompletedSuccessfully && done.Result)) _closing = null;
            return _closing ??= _close();
        }
    }

    public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);
}

/// <summary>Mở Brave cho một shop. Tách giao diện để <see cref="SellerRunGate"/> test được bằng bản giả.</summary>
public interface ISellerBrowserLauncher
{
    Task<SellerBrowserSession> LaunchAsync(SellerLaunchRequest request, CancellationToken ct);
}

/// <summary>
/// Phần chạm hệ điều hành của <see cref="SellerBrowserLauncher"/> — chính sách máy, tìm/dọn tiến trình Brave theo profile, khởi
/// động tiến trình. Tách ra để test được luật của launcher (còn Brave sót → không mở, cờ sai → tắt, …) mà không cần Brave thật.
/// </summary>
public interface ISellerBrowserHost
{
    /// <summary>Đường dẫn brave.exe; null = chưa cài.</summary>
    string? FindBraveExe();

    /// <summary>Mọi giá trị chính sách máy của Brave, dạng <c>HKLM\Tên</c> / <c>HKCU\Tên</c> (launcher tự lọc giá trị nguy hiểm).</summary>
    IReadOnlyList<string> ReadBravePolicyValues();

    /// <summary>brave.exe có <c>--user-data-dir</c> trỏ tới profile, so CHUẨN HOÁ
    /// (<see cref="SellerBraveProcesses.FilterByProfile"/>).</summary>
    IReadOnlyList<SellerProcInfo> FindProfileProcesses(string profileDir);

    /// <summary>Giết mọi Brave (kèm crashpad mồ côi) đang chạy trên profile. Best-effort, không ném.</summary>
    void ReapProfile(string profileDir);

    /// <summary>Tạo thư mục profile + dọn trạng thái phiên cũ (không đụng cookie/đăng nhập).</summary>
    void PrepareProfile(string profileDir);

    /// <summary>Khởi động Brave với dòng lệnh đã dựng.</summary>
    Process? Start(string exe, string commandLine);

    /// <summary>Xin đóng êm (đóng cửa sổ) tiến trình <paramref name="pid"/>. Best-effort.</summary>
    void RequestClose(int pid);

    /// <summary>Buộc tắt: kill cây <paramref name="process"/> rồi giết mọi tiến trình của profile. Best-effort.</summary>
    void Kill(Process? process, string profileDir);

    /// <summary>Điểm đang nghe TCP của máy — chế độ <see cref="SellerLaunchMode.Login"/> kiểm cổng CDP chỉ nghe loopback.
    /// Mặc định đọc thật từ hệ điều hành (<see cref="SellerCdp.SystemListeners"/>).</summary>
    IReadOnlyList<System.Net.IPEndPoint> FindTcpListeners() => SellerCdp.SystemListeners();
}

/// <summary>Bản thật: dùng lại <see cref="BrowserLauncher.Detect"/>, <see cref="BraveTeardown"/>, <see cref="BraveCachePolicy"/>,
/// <see cref="BraveJobObject"/> (Job Object: app chết thì Brave chết theo), nguồn tiến trình
/// <see cref="PlatformServices.ProcessFinder"/>. Phép khớp profile là của Seller (<see cref="SellerBraveProcesses"/>), chặt hơn
/// <c>BraveProcessReaper</c> — nên sau bước dọn chung còn giết thêm các tiến trình mà phép so chuẩn hoá bắt được.</summary>
public sealed class BraveSellerHost(Action<string>? log = null) : ISellerBrowserHost
{
    private const string PolicyKey = @"SOFTWARE\Policies\BraveSoftware\Brave";
    private static readonly string[] BraveOnly = ["brave.exe"];

    public string? FindBraveExe() => BrowserLauncher.Detect(BrowserKind.Brave);

    public IReadOnlyList<string> ReadBravePolicyValues()
    {
        var list = new List<string>();
        if (!OperatingSystem.IsWindows()) return list;
        foreach (var (hive, name) in new[] { (Microsoft.Win32.Registry.LocalMachine, "HKLM"), (Microsoft.Win32.Registry.CurrentUser, "HKCU") })
        {
            try
            {
                using var k = hive.OpenSubKey(PolicyKey);
                if (k is null) continue;
                list.AddRange(k.GetValueNames().Select(v => $"{name}\\{v}"));
                list.AddRange(k.GetSubKeyNames().Select(v => $"{name}\\{v}"));   // vd ProxySettings dạng khoá con
            }
            catch (Exception ex) { log?.Invoke($"Không đọc được chính sách Brave ({name}): {ex.Message}"); }
        }
        return list;
    }

    public IReadOnlyList<SellerProcInfo> FindProfileProcesses(string profileDir) =>
        SellerBraveProcesses.FilterByProfile(PlatformServices.ProcessFinder.Enumerate(BraveOnly, log), profileDir);

    public void ReapProfile(string profileDir)
    {
        BraveTeardown.Reap(profileDir, includeCrashpadOrphans: true, sleepAfterReapMs: 400, log: log);
        KillMatches(profileDir);
    }

    public void PrepareProfile(string profileDir)
    {
        Directory.CreateDirectory(profileDir);
        BraveCachePolicy.PrepareProfileForLaunch(profileDir, ProfileLaunchPrep.ClearSessionRestore | ProfileLaunchPrep.MarkCleanShutdown);
    }

    public Process? Start(string exe, string commandLine) => BraveJobObject.Start(exe, commandLine);

    public void RequestClose(int pid)
    {
        try { using var p = Process.GetProcessById(pid); p.CloseMainWindow(); } catch { }
    }

    public void Kill(Process? process, string profileDir)
    {
        var p = process;
        BraveTeardown.KillAndReap(ref p, profileDir,
            new BraveTeardownOptions { WaitForExitMs = 5000, IncludeCrashpadOrphans = true, Log = log });
        KillMatches(profileDir);
    }

    private void KillMatches(string profileDir)
    {
        foreach (var m in FindProfileProcesses(profileDir))
        {
            try { using var proc = Process.GetProcessById(m.Pid); proc.Kill(entireProcessTree: true); proc.WaitForExit(2000); }
            catch { }
        }
    }
}

/// <summary>
/// Mở Brave thật, KHÔNG CDP (plan GĐ4 bước 2): tham số từ <see cref="SellerBrowserArgs"/>; mọi thao tác tiến trình qua
/// <see cref="ISellerBrowserHost"/> (mặc định <see cref="BraveSellerHost"/>).
/// <list type="bullet">
/// <item><b>Chế độ <see cref="SellerLaunchMode.Login"/> (GĐ5b):</b> mọi bước dưới giữ nguyên, chỉ thêm <see cref="SellerBrowserArgs.CdpFlag"/>.
/// Trước khi mở xoá <c>DevToolsActivePort</c> cũ (khỏi đọc nhầm cổng phiên trước); sau khi kiểm tiến trình chính, chờ Brave ghi
/// cổng (<see cref="DevToolsPortTimeout"/>) rồi kiểm cổng CHỈ nghe loopback (<see cref="SellerCdp.CheckLoopbackOnly"/>). Không
/// đạt → giết mọi tiến trình của profile, ném.</item>
/// <item><b>Một profile một Brave:</b> trước khi mở, dọn mọi Brave còn chạy trên profile đó và KIỂM LẠI là hết — còn thì ném, không
/// khởi động. Lý do quan trọng hơn chuyện khoá profile: Brave là single-instance theo profile — còn một tiến trình cũ (mở tay,
/// không proxy) thì lệnh mở mới chỉ chuyển URL sang tiến trình cũ và <c>--proxy-server</c> mới bị bỏ qua ⇒ đi IP máy. Trong app
/// thì giữ danh sách profile đang mở (chung mọi instance), mở trùng là ném.</item>
/// <item><b>Chính sách máy:</b> registry <c>HKLM/HKCU\SOFTWARE\Policies\BraveSoftware\Brave</c> có <c>Proxy*</c> /
/// <c>WebRtcIPHandling*</c> (chính sách đè cờ dòng lệnh) → không mở.</item>
/// <item><b>Chuẩn bị profile:</b> dọn session (host) rồi ghi lớp 2 WebRTC + tắt DoH (<see cref="SellerProfilePrefs"/>).</item>
/// <item><b>Kiểm sau khi mở:</b> tiến trình chính của profile phải chạy đúng <c>--proxy-server</c> + cờ WebRTC; sai/không thấy trong
/// <see cref="PostStartTimeout"/> → giết mọi tiến trình của profile, ném (gate báo <c>LaunchFailed</c>).</item>
/// <item><b>Proxy có user/pass:</b> sinh extension proxy-auth vào <c>&lt;upload&gt;\proxy-auth\&lt;Id&gt;-&lt;guid 32 hex&gt;</c>, xoá khi đóng
/// và khi mở lỗi; lần mở sau dọn bản sót của ĐÚNG tài khoản đó (khớp chính xác cả tên bằng regex — Id được chứa <c>-</c>, mẫu
/// <c>&lt;Id&gt;-*</c> sẽ xoá nhầm của tài khoản <c>&lt;Id&gt;-x</c>).</item>
/// <item><b>Đóng:</b> xin đóng êm mọi tiến trình của profile (Brave kịp ghi cookie đăng nhập), đợi; quá hạn thì kill + quét; rồi đợi
/// tới khi không còn tiến trình nào của profile.</item>
/// </list>
/// </summary>
public sealed class SellerBrowserLauncher : ISellerBrowserLauncher
{
    private static readonly HashSet<string> OpenProfiles = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _proxyAuthRoot;
    private readonly ISellerBrowserHost _host;
    private readonly Action<string>? _log;

    public TimeSpan GracefulCloseTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ExitTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>Thời gian chờ thấy tiến trình chính sau khi Start.</summary>
    public TimeSpan PostStartTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Chế độ đăng nhập: thời gian chờ Brave ghi <c>DevToolsActivePort</c> (bản check đơn chờ 15 s).</summary>
    public TimeSpan DevToolsPortTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public SellerBrowserLauncher(string? proxyAuthRoot = null, Action<string>? log = null, ISellerBrowserHost? host = null)
    {
        _proxyAuthRoot = proxyAuthRoot ?? Path.Combine(SuitePaths.ModuleDir("upload"), "proxy-auth");
        _log = log;
        _host = host ?? new BraveSellerHost(log);
    }

    /// <summary>Mở trên luồng nền (dọn tiến trình cũ dùng WMI, chậm). Lỗi → ném; thư mục proxy-auth vừa tạo được xoá.</summary>
    public Task<SellerBrowserSession> LaunchAsync(SellerLaunchRequest req, CancellationToken ct) =>
        Task.Run(() => LaunchCore(req, ct), ct);

    private SellerBrowserSession LaunchCore(SellerLaunchRequest req, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (req.Proxy is null) throw new InvalidOperationException("Không có proxy thì không mở Brave.");
        var profile = Path.GetFullPath(req.ProfileDir);
        var key = profile.TrimEnd('\\', '/');

        lock (OpenProfiles)
        {
            if (!OpenProfiles.Add(key)) throw new InvalidOperationException($"Profile {profile} đang mở trong app.");
        }

        string? authDir = null;
        try
        {
            var exe = _host.FindBraveExe()
                ?? throw new FileNotFoundException("Không tìm thấy brave.exe. Hãy cài Brave Browser.");

            var policies = SellerBraveProcesses.BlockingPolicies(_host.ReadBravePolicyValues());
            if (policies.Count > 0)
                throw new InvalidOperationException(
                    $"Máy có chính sách Brave đè proxy/WebRTC ({string.Join(", ", policies)}) — không mở. Gỡ chính sách trong registry " +
                    @"SOFTWARE\Policies\BraveSoftware\Brave rồi chạy lại.");

            _host.ReapProfile(profile);
            if (_host.FindProfileProcesses(profile).Count > 0)
                throw new InvalidOperationException($"Còn Brave đang chạy trên profile {profile}, không tắt được — không mở (sẽ đi sai proxy).");

            _host.PrepareProfile(profile);
            SellerProfilePrefs.Apply(profile);

            var exts = new List<string>(req.ExtensionDirs ?? []);
            if (req.Proxy.HasCredentials)
            {
                CleanupStaleAuthDirs(req.AccountId);
                authDir = ProxyAuthExtension.Write(_proxyAuthRoot, req.AccountId, req.Proxy);
                exts.Insert(0, authDir);
            }

            var portFile = Path.Combine(profile, SellerCdp.DevToolsActivePortFile);
            if (req.Mode == SellerLaunchMode.Login && File.Exists(portFile)) File.Delete(portFile);   // xoá không được → ném, không mở

            var args = SellerBrowserArgs.Build(profile, req.Proxy, exts, req.StartUrl, req.Mode);
            ct.ThrowIfCancellationRequested();
            var proc = _host.Start(exe, SellerBrowserArgs.ToCommandLine(args));
            VerifyStarted(proc, profile, req.Proxy, req.Mode);
            int? cdpPort = req.Mode == SellerLaunchMode.Login ? VerifyCdp(proc, profile, portFile) : null;
            _log?.Invoke($"Đã mở Brave (PID {SafePid(proc)}) cho {req.AccountId} qua {req.Proxy}, IP ra {req.ExitIp}" +
                         (cdpPort is { } cp ? $", chế độ đăng nhập (CDP 127.0.0.1:{cp})." : "."));

            var dir = authDir;
            return new SellerBrowserSession(proc, profile, req.Proxy, req.ExitIp, () => CloseCoreAsync(proc, profile, key, dir), cdpPort);
        }
        catch
        {
            if (authDir is not null) TryDeleteDir(authDir);
            lock (OpenProfiles) OpenProfiles.Remove(key);
            throw;
        }
    }

    /// <summary>Sau khi Start: chờ (tối đa <see cref="PostStartTimeout"/>) thấy tiến trình chính của profile, bắt buộc nó chạy đúng
    /// proxy + cờ WebRTC (<see cref="SellerBraveProcesses.CheckMain"/>). Sai/không thấy → giết mọi tiến trình của profile rồi ném.</summary>
    private void VerifyStarted(Process? proc, string profile, ProxyLease lease, SellerLaunchMode mode)
    {
        var sw = Stopwatch.StartNew();
        string? error;
        while (true)
        {
            var procs = _host.FindProfileProcesses(profile);
            error = SellerBraveProcesses.CheckMain(procs, lease, mode);
            if (error is null) return;
            if (procs.Any(p => p.IsMain) || sw.Elapsed >= PostStartTimeout) break;   // đã thấy tiến trình chính mà sai cờ → khỏi chờ
            Thread.Sleep(PollInterval);
        }
        _log?.Invoke($"Brave mở sai: {error} — tắt.");
        _host.Kill(proc, profile);
        throw new InvalidOperationException($"Brave mở sai: {error}.");
    }

    /// <summary>Chế độ đăng nhập: chờ Brave ghi cổng CDP vào <c>DevToolsActivePort</c> rồi kiểm cổng chỉ nghe loopback. Không đạt
    /// → giết mọi tiến trình của profile rồi ném.</summary>
    private int VerifyCdp(Process? proc, string profile, string portFile)
    {
        var sw = Stopwatch.StartNew();
        int? port = null;
        while (port is null)
        {
            try { if (File.Exists(portFile)) port = SellerCdp.ParseDevToolsActivePort(File.ReadAllText(portFile)); }
            catch (IOException) { }   // Brave đang ghi dở — vòng sau đọc lại
            if (port is not null || sw.Elapsed >= DevToolsPortTimeout) break;
            Thread.Sleep(PollInterval);
        }

        var error = port is { } p
            ? SellerCdp.CheckLoopbackOnly(_host.FindTcpListeners(), p)
            : $"quá {DevToolsPortTimeout.TotalSeconds:0} s chưa thấy Brave ghi cổng CDP ({SellerCdp.DevToolsActivePortFile})";
        if (error is null) return port!.Value;
        _log?.Invoke($"Brave chế độ đăng nhập sai: {error} — tắt.");
        _host.Kill(proc, profile);
        throw new InvalidOperationException($"Brave chế độ đăng nhập sai: {error}.");
    }

    private async Task<bool> CloseCoreAsync(Process? proc, string profile, string key, string? authDir)
    {
        try
        {
            // 1) Đóng êm: xin đóng cửa sổ mọi tiến trình Brave của profile (tiến trình có cửa sổ mới nhận).
            foreach (var p in _host.FindProfileProcesses(profile)) _host.RequestClose(p.Pid);
            var gone = await WaitGoneAsync(profile, GracefulCloseTimeout).ConfigureAwait(false);

            // 2) Quá hạn: kill cây + quét theo --user-data-dir.
            if (!gone)
            {
                _log?.Invoke("Brave không tự đóng — buộc tắt.");
                _host.Kill(proc, profile);
                proc = null;
                gone = await WaitGoneAsync(profile, ExitTimeout).ConfigureAwait(false);
            }
            if (!gone) _log?.Invoke($"Brave của profile {profile} vẫn chưa thoát hẳn.");
            return gone;
        }
        finally
        {
            try { proc?.Dispose(); } catch { }
            if (authDir is not null) TryDeleteDir(authDir);
            lock (OpenProfiles) OpenProfiles.Remove(key);
        }
    }

    private async Task<bool> WaitGoneAsync(string profile, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            if (_host.FindProfileProcesses(profile).Count == 0) return true;
            if (sw.Elapsed >= timeout) return false;
            await Task.Delay(PollInterval).ConfigureAwait(false);
        }
    }

    /// <summary>true nếu <paramref name="dirName"/> đúng là thư mục proxy-auth của <paramref name="accountId"/>:
    /// <c>&lt;Id&gt;-&lt;32 hex thường&gt;</c> (dạng <see cref="ProxyAuthExtension.Write"/> sinh ra), khớp CẢ tên.</summary>
    internal static bool IsAuthDirOf(string dirName, string accountId) =>
        Regex.IsMatch(dirName, "^" + Regex.Escape(accountId) + "-[0-9a-f]{32}$", RegexOptions.CultureInvariant);

    /// <summary>Xoá thư mục proxy-auth sót của tài khoản (app chết trước khi kịp đóng). Brave của profile đã bị dọn ở bước trước
    /// nên không còn ai giữ file.</summary>
    private void CleanupStaleAuthDirs(string accountId)
    {
        try
        {
            if (!Directory.Exists(_proxyAuthRoot)) return;
            foreach (var d in Directory.GetDirectories(_proxyAuthRoot))
                if (IsAuthDirOf(Path.GetFileName(d), accountId)) TryDeleteDir(d);
        }
        catch { }
    }

    /// <summary>Xoá thư mục, thử lại vài lần (Brave vừa thoát có thể còn giữ file một lúc).</summary>
    private void TryDeleteDir(string dir)
    {
        for (var i = 0; i < 5; i++)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); return; }
            catch { Thread.Sleep(200); }
        }
        _log?.Invoke($"Không xoá được thư mục xác thực proxy {dir}.");
    }

    private static string SafePid(Process? p) { try { return p?.Id.ToString() ?? "?"; } catch { return "?"; } }
}
