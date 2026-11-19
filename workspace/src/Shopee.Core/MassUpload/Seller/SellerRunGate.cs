namespace Shopee.Core.MassUpload.Seller;

public enum SellerGateFailure
{
    None,
    /// <summary>Cấu hình proxy thiếu/sai.</summary>
    ProxyConfig,
    /// <summary>Không xin được proxy (Kiot lỗi / chờ quá lâu).</summary>
    ProxyUnavailable,
    /// <summary>Key Kiot chết. <see cref="SellerGateResult.IsFleetWide"/> = key chung ⇒ mọi shop còn lại cũng hỏng.</summary>
    KiotKeyDead,
    /// <summary>Kiểm IP ra không đạt sau số lần thử cho phép.</summary>
    ExitIpRejected,
    /// <summary>IP đạt nhưng mở Brave lỗi.</summary>
    LaunchFailed,
}

/// <summary>Kết quả chuẩn bị một shop. Lỗi → GĐ5 bỏ qua shop (<see cref="Reason"/> ghi sổ) và sang shop kế, trừ khi
/// <see cref="IsFleetWide"/> (dừng cả lượt).</summary>
public sealed record SellerGateResult(SellerGateFailure Failure, string? Reason, SellerBrowserSession? Session, string? ExitIp,
    bool IsFleetWide = false)
{
    public bool Ok => Failure == SellerGateFailure.None && Session is not null;
}

/// <summary>Một IP đã dùng (đã kiểm đạt) — GĐ5 ghi vào sổ của shop để tra khi shop bị đòi xác minh.</summary>
public sealed record SellerIpUse(string AccountId, string ExitIp, string Proxy, DateTimeOffset At);

public sealed class SellerRunGateOptions
{
    /// <summary>Số vòng (xin proxy → kiểm IP) tối đa cho một shop. Kiot đổi IP: vòng sau là IP mới (chờ mốc đổi IP của gói).</summary>
    public int MaxAttempts { get; init; } = 2;

    /// <summary>Nghỉ trước vòng kiểm lại với proxy tĩnh / key giữ IP (proxy vừa mở cổng có thể chưa sẵn sàng).</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Số lần kiểm lại CÙNG proxy khi không lấy được IP ra (proxy chưa sẵn sàng), trước khi xin proxy mới.</summary>
    public int SameProxyRetries { get; init; } = 3;

    public TimeSpan SameProxyRetryDelay { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Cổng mở một shop (plan GĐ4): lấy proxy → kiểm IP ra → mở Brave. Luật cứng: CHỈ có một chỗ gọi
/// <see cref="ISellerBrowserLauncher.LaunchAsync"/>, ngay sau khi <see cref="ExitIpVerifier.VerifyAsync"/> trả đạt, và tham số
/// mở là đúng proxy vừa kiểm. Mọi nhánh lỗi trả <see cref="SellerGateResult"/> có lý do, không ném (trừ huỷ).
/// <para>Một gate cho một lượt (giữ danh sách IP đã dùng — IP ra của shop mới phải khác mọi IP đã dùng cho shop khác). Chạy tuần tự từng shop: đóng Brave của shop này
/// (<see cref="SellerBrowserSession.CloseAsync"/>) rồi mới gọi <see cref="OpenAsync"/> cho shop kế.</para>
/// </summary>
public sealed class SellerRunGate
{
    private readonly SellerProxyProvider _proxies;
    private readonly ExitIpVerifier _verifier;
    private readonly ISellerBrowserLauncher _launcher;
    private readonly SellerRunGateOptions _opt;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Action<string>? _log;
    private readonly List<SellerIpUse> _used = [];

    public SellerRunGate(SellerProxyProvider proxies, ExitIpVerifier verifier, ISellerBrowserLauncher launcher,
        SellerRunGateOptions? options = null, Func<DateTimeOffset>? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null, Action<string>? log = null)
    {
        _proxies = proxies;
        _verifier = verifier;
        _launcher = launcher;
        _opt = options ?? new SellerRunGateOptions();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? ((d, ct) => Task.Delay(d, ct));
        _log = log;
    }

    /// <summary>IP ra của shop gần nhất đã mở được Brave (null = chưa có shop nào).</summary>
    public string? PreviousShopIp { get; private set; }

    public IReadOnlyList<SellerIpUse> UsedIps => _used;

    /// <summary>Chuẩn bị và mở Brave cho <paramref name="account"/>. Huỷ → <see cref="OperationCanceledException"/>.</summary>
    public Task<SellerGateResult> OpenAsync(SellerAccount account, SellerUploadSettings settings, string profileDir,
        IReadOnlyList<string> extensionDirs, CancellationToken ct) =>
        OpenAsync(account, settings, profileDir, extensionDirs, SellerLaunchMode.Normal, ct);

    /// <summary>Như bản trên, chọn chế độ mở. <see cref="SellerLaunchMode.Login"/> (GĐ5b) đi ĐÚNG cùng đường: proxy bắt buộc, kiểm IP
    /// ra, cùng một chỗ gọi launcher — chỉ khác cờ <see cref="SellerLaunchRequest.Mode"/> đưa vào launcher.</summary>
    public async Task<SellerGateResult> OpenAsync(SellerAccount account, SellerUploadSettings settings, string profileDir,
        IReadOnlyList<string> extensionDirs, SellerLaunchMode mode, CancellationToken ct)
    {
        var source = SellerProxySource.Resolve(account, settings, out var cfgError);
        if (source is null) return Fail(SellerGateFailure.ProxyConfig, cfgError);

        string? last = null;
        for (var attempt = 1; attempt <= Math.Max(1, _opt.MaxAttempts); attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (attempt > 1 && !(source.Kind == SellerProxyKind.Kiot && source.RotatePerShop))
                await _delay(_opt.RetryDelay, ct).ConfigureAwait(false);

            var acq = await _proxies.AcquireAsync(source, ct).ConfigureAwait(false);
            if (!acq.Ok)
            {
                var reason = $"{account.Id}: {acq.Error}";
                return acq.IsKeyDead
                    ? Fail(SellerGateFailure.KiotKeyDead, reason, fleetWide: source.IsShared)
                    : Fail(SellerGateFailure.ProxyUnavailable, reason);
            }

            var lease = acq.Lease!;
            var check = await _verifier.VerifyAsync(lease, UsedByOthers(account.Id), ct).ConfigureAwait(false);
            // Không lấy được IP ra = proxy (vừa đổi IP / vừa mở cổng) có thể chưa sẵn sàng → thử lại CÙNG proxy vài lần trước khi
            // bỏ nó (với Kiot, bỏ = tốn một lần đổi IP + chờ 5 phút).
            for (var retry = 1; !check.Ok && check.Failure == ExitIpFailure.ExitIpUnavailable && retry <= _opt.SameProxyRetries; retry++)
            {
                _log?.Invoke($"{account.Id}: chưa lấy được IP ra qua {lease}, thử lại cùng proxy ({retry}/{_opt.SameProxyRetries}).");
                await _delay(_opt.SameProxyRetryDelay, ct).ConfigureAwait(false);
                check = await _verifier.VerifyAsync(lease, UsedByOthers(account.Id), ct).ConfigureAwait(false);
            }
            if (!check.Ok)
            {
                last = check.Reason;
                _log?.Invoke($"{account.Id}: kiểm IP lần {attempt}/{_opt.MaxAttempts} không đạt — {check.Reason}");
                continue;
            }

            // IP đạt — từ đây mới được mở Brave, và mở với ĐÚNG proxy vừa kiểm.
            var ip = check.ExitIp!;
            _log?.Invoke($"{account.Id}: IP ra {ip} qua {lease} (IP máy {check.MachineIp}).");
            try
            {
                var session = await _launcher.LaunchAsync(
                    new SellerLaunchRequest(account.Id, profileDir, lease, ip, extensionDirs, Mode: mode), ct).ConfigureAwait(false);
                // Chỉ IP của shop ĐÃ mở được Brave mới tính là "đã dùng" / "IP shop trước".
                PreviousShopIp = ip;
                _used.Add(new SellerIpUse(account.Id, ip, lease.ToString(), _clock()));
                return new SellerGateResult(SellerGateFailure.None, null, session, ip);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                return Fail(SellerGateFailure.LaunchFailed, $"{account.Id}: mở Brave lỗi: {ex.Message}", exitIp: ip);
            }
        }
        return Fail(SellerGateFailure.ExitIpRejected, $"{account.Id}: {last}");
    }

    /// <summary>IP đã dùng trong lượt cho tài khoản KHÁC (IP → tài khoản). Cùng tài khoản chạy lại (ngày sau) thì IP cũ của nó không
    /// tính là trùng.</summary>
    private Dictionary<string, string> UsedByOthers(string accountId)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var u in _used)
            if (!string.Equals(u.AccountId, accountId, StringComparison.Ordinal)) d.TryAdd(u.ExitIp, u.AccountId);
        return d;
    }

    private SellerGateResult Fail(SellerGateFailure f, string? reason, bool fleetWide = false, string? exitIp = null)
    {
        _log?.Invoke($"Bỏ qua shop — {reason}");
        return new SellerGateResult(f, reason, null, exitIp, fleetWide);
    }
}
