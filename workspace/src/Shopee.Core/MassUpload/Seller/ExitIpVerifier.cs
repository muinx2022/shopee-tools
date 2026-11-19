using System.Net;

namespace Shopee.Core.MassUpload.Seller;

public enum ExitIpFailure { None, MachineIpUnavailable, ExitIpUnavailable, SameAsMachine, AlreadyUsed, BadProxyHost }

/// <summary>Kết quả kiểm IP ra. <see cref="Ok"/> = được phép mở Brave với proxy này.</summary>
public sealed record ExitIpCheck(ExitIpFailure Failure, string? ExitIp, string? MachineIp, string? Reason)
{
    public bool Ok => Failure == ExitIpFailure.None;
}

/// <summary>
/// Kiểm IP ra của proxy trước khi mở Brave (plan GĐ4 bước 2–3). Luật:
/// <list type="number">
/// <item>Phải lấy được IP máy (gọi KHÔNG qua proxy, kể cả proxy hệ thống). Không biết IP máy thì không so được ⇒ từ chối.</item>
/// <item>Phải lấy được IP ra qua proxy.</item>
/// <item>IP ra KHÁC IP máy.</item>
/// <item>Proxy đổi IP mỗi shop (<see cref="ProxyLease.MustBeUnusedIp"/>): IP ra KHÁC MỌI IP đã dùng cho shop khác trong lượt (không
/// chỉ shop liền trước — A→B→A cũng bị chặn).</item>
/// </list>
/// Dịch vụ trả IP: <c>api.ipify.org</c> (cách <c>backend/…/KiotProxyClient.CheckProxyAliveAsync</c> đang dùng), dự phòng
/// <c>checkip.amazonaws.com</c>. Cả hai chỉ trả IPv4 dạng chữ, request không mang dữ liệu gì ngoài User-Agent chung.
/// <para>IP máy lấy lại MỖI lần kiểm (mỗi shop): lượt chạy kéo dài nhiều ngày, IP nhà mạng đổi bất cứ lúc nào.</para>
/// </summary>
public sealed class ExitIpVerifier
{
    public static readonly IReadOnlyList<Uri> DefaultServices =
    [
        new("https://api.ipify.org/"),
        new("https://checkip.amazonaws.com/"),
    ];

    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";

    private readonly Func<ProxyLease?, HttpMessageHandler> _handlerFactory;
    private readonly IReadOnlyList<Uri> _services;
    private readonly TimeSpan _timeout;
    private readonly Action<string>? _log;


    /// <param name="handlerFactory">Tạo handler cho một lần hỏi: <c>null</c> = đi thẳng (IP máy), khác null = qua proxy đó.
    /// Mặc định <see cref="CreateHandler"/>. Test tiêm handler giả.</param>
    public ExitIpVerifier(Func<ProxyLease?, HttpMessageHandler>? handlerFactory = null, IReadOnlyList<Uri>? services = null,
        TimeSpan? timeout = null, Action<string>? log = null)
    {
        _handlerFactory = handlerFactory ?? CreateHandler;
        _services = services is { Count: > 0 } ? services : DefaultServices;
        _timeout = timeout ?? TimeSpan.FromSeconds(12);
        _log = log;
    }

    /// <summary>Handler thật. Đi thẳng: <c>UseProxy = false</c> (bỏ cả proxy hệ thống — đó mới là IP máy). Qua proxy:
    /// <see cref="WebProxy"/> đúng <see cref="ProxyLease.ServerUri"/>, kèm <see cref="NetworkCredential"/> nếu proxy có user/pass,
    /// không bypass gì — proxy hỏng thì request hỏng, không rơi về đi thẳng.</summary>
    internal static HttpMessageHandler CreateHandler(ProxyLease? lease)
    {
        if (lease is null) return new SocketsHttpHandler { UseProxy = false, UseCookies = false };
        var proxy = new WebProxy(lease.ServerUri) { BypassProxyOnLocal = false };
        if (lease.HasCredentials) proxy.Credentials = new NetworkCredential(lease.User, lease.Pass);
        return new SocketsHttpHandler { UseProxy = true, Proxy = proxy, UseCookies = false };
    }

    /// <summary>IP máy, hỏi mới mỗi lần. null = mọi dịch vụ đều hỏng.</summary>
    public async Task<string?> GetMachineIpAsync(CancellationToken ct)
    {
        var (ip, err) = await LookupAsync(null, ct).ConfigureAwait(false);
        if (ip is null) { _log?.Invoke($"Không lấy được IP máy: {err}"); return null; }
        return ip;
    }

    /// <summary>Kiểm proxy theo các luật ở đầu lớp. <paramref name="usedIps"/> = IP đã dùng trong lượt → tài khoản đã dùng (null = chưa có).
    /// Huỷ → <see cref="OperationCanceledException"/>.</summary>
    public async Task<ExitIpCheck> VerifyAsync(ProxyLease lease, IReadOnlyDictionary<string, string>? usedIps, CancellationToken ct)
    {
        // GĐ5d vòng 2: host .NET nối tới (IdnHost, đã "làm sạch") phải giống hệt host đưa cho Brave — lệch là kiểm một proxy, mở Brave
        // bằng một chuỗi khác (Brave bỏ proxy sai, đi IP máy). Kiểm trước mọi request mạng.
        if (!ProxyHostRules.IsSafeForBrowser(lease) || !ProxyHostRules.UriHostMatches(lease))
            return new(ExitIpFailure.BadProxyHost, null, null, "Proxy có ký tự lạ ở host/cổng — không kiểm, không mở Brave.");
        var machine = await GetMachineIpAsync(ct).ConfigureAwait(false);
        if (machine is null)
            return new(ExitIpFailure.MachineIpUnavailable, null, null, "Không lấy được IP máy để so — không mở Brave.");

        var (exit, err) = await LookupAsync(lease, ct).ConfigureAwait(false);
        if (exit is null)
            return new(ExitIpFailure.ExitIpUnavailable, null, machine, $"Không lấy được IP ra qua {lease}: {err}");

        if (exit == machine)
            return new(ExitIpFailure.SameAsMachine, exit, machine, $"IP ra qua {lease} TRÙNG IP máy ({exit}) — proxy không che IP.");

        if (lease.MustBeUnusedIp && usedIps is not null)
        {
            foreach (var (ip, account) in usedIps)
                if (Normalize(ip) == exit)
                    return new(ExitIpFailure.AlreadyUsed, exit, machine, $"IP ra {exit} đã dùng cho shop {account} trong lượt — cần IP mới.");
        }

        if (lease.ExpectedIp is not null && Normalize(lease.ExpectedIp) is { } exp && exp != exit)
            _log?.Invoke($"IP ra {exit} khác IP Kiot báo ({exp}) — vẫn dùng vì đã khác IP máy.");
        return new(ExitIpFailure.None, exit, machine, null);
    }

    /// <summary>Hỏi lần lượt các dịch vụ IP, lấy cái đầu tiên trả về một địa chỉ IP hợp lệ.</summary>
    private async Task<(string? Ip, string? Error)> LookupAsync(ProxyLease? lease, CancellationToken ct)
    {
        var errors = new List<string>();
        using var http = new HttpClient(_handlerFactory(lease), disposeHandler: true) { Timeout = _timeout };
        http.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        foreach (var svc in _services)
        {
            try
            {
                using var resp = await http.GetAsync(svc, ct).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) { errors.Add($"{svc.Host}: HTTP {(int)resp.StatusCode}"); continue; }
                var ip = Normalize(body);
                if (ip is null) { errors.Add($"{svc.Host}: trả '{Short(body)}' không phải IP"); continue; }
                return (ip, null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { errors.Add($"{svc.Host}: {ex.GetBaseException().Message}"); }
        }
        return (null, string.Join("; ", errors));
    }

    /// <summary>Chuẩn hoá chuỗi IP để so (bỏ khoảng trắng/xuống dòng); không phải IP → null. Đòi dạng đủ (IPv4 4 phần, hoặc
    /// IPv6 có <c>:</c>) vì <see cref="IPAddress.TryParse(string?, out IPAddress?)"/> nhận cả "123" thành 0.0.0.123.</summary>
    internal static string? Normalize(string? text)
    {
        var t = text?.Trim() ?? "";
        if (t.Count(c => c == '.') != 3 && !t.Contains(':')) return null;
        return IPAddress.TryParse(t, out var ip) ? ip.ToString() : null;
    }

    private static string Short(string s) { s = s.Trim(); return s.Length > 40 ? s[..40] + "…" : s; }
}
