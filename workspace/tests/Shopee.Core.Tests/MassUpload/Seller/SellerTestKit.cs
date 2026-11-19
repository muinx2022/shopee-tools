using System.Net;
using System.Net.Http;
using Shopee.Core.MassUpload.Seller;

namespace Shopee.Core.Tests.MassUpload.Seller;

/// <summary>Đồng hồ giả: <see cref="Delay"/> không ngủ thật mà đẩy giờ lên đúng chừng đó và ghi lại.</summary>
internal sealed class FakeClock(DateTimeOffset start)
{
    public DateTimeOffset Now { get; set; } = start;
    public List<TimeSpan> Delays { get; } = [];

    /// <summary>Chạy trước mỗi lần chờ (test chen thao tác, vd huỷ giữa chừng).</summary>
    public Action<TimeSpan>? OnDelay { get; set; }

    public TimeSpan TotalDelayed => Delays.Aggregate(TimeSpan.Zero, (a, b) => a + b);

    public Task Delay(TimeSpan d, CancellationToken ct)
    {
        OnDelay?.Invoke(d);
        ct.ThrowIfCancellationRequested();
        Delays.Add(d);
        Now += d;
        return Task.CompletedTask;
    }
}

/// <summary>Handler HTTP giả: mỗi request hỏi <see cref="Respond"/>; ghi lại URL đã gọi.</summary>
internal sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<string> Urls { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Urls.Add(request.RequestUri!.ToString());
        return Task.FromResult(respond(request));
    }

    public static HttpResponseMessage Text(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body) };
}

/// <summary>Kiot giả theo đồng hồ giả: <c>/new</c> trước mốc <see cref="NextChangeAt"/> bị từ chối "Gửi lại sau X giây" (như Kiot
/// thật); đổi được thì cấp IP kế trong <see cref="Ips"/> và dời mốc thêm 5 phút. Ghi giờ của mọi lần gọi <c>/new</c>.</summary>
internal sealed class FakeKiot
{
    private readonly FakeClock _clock;
    private int _next;

    public FakeKiot(FakeClock clock, params string[] ips)
    {
        _clock = clock;
        Ips = ips.Length > 0 ? [.. ips] : ["113.160.0.1", "113.160.0.2", "113.160.0.3", "113.160.0.4"];
        Http = new FakeHttp(Handle);
        Api = new SellerKiotApi(new HttpClient(Http));
    }

    public List<string> Ips { get; }
    public FakeHttp Http { get; }
    public SellerKiotApi Api { get; }

    /// <summary>Đồng hồ máy chủ Kiot lệch so với máy bao nhiêu (gửi qua header Date).</summary>
    public TimeSpan ServerSkew { get; set; }
    public DateTimeOffset ServerNow => _clock.Now + ServerSkew;

    /// <summary>Số lần /new bị từ chối vì gọi trước mốc.</summary>
    public int Rejected;

    /// <summary>Mốc được đổi IP, theo GIỜ MÁY CHỦ; null = đổi ngay được.</summary>
    public DateTimeOffset? NextChangeAt { get; set; }
    public string? CurrentIp { get; set; }
    public TimeSpan MinUse { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Bẻ phản hồi (trả null = xử lý thường).</summary>
    public Func<string, HttpResponseMessage?>? Override { get; set; }

    public List<DateTimeOffset> NewCalls { get; } = [];
    public int CurrentCalls;

    public static string Ok(string ip, DateTimeOffset? next) =>
        $$$"""{"success":true,"code":200,"message":"ok","data":{"realIpAddress":"{{{ip}}}","http":"{{{ip}}}:20001","socks5":"{{{ip}}}:30001","host":"{{{ip}}}","httpPort":20001,"socks5Port":30001,"location":"Ha Noi","nextRequestAt":{{{next?.ToUnixTimeMilliseconds() ?? 0}}},"expirationAt":0}}""";

    public const string KeyExpired =
        """{"success":false,"code":400,"error":"KEY_EXPIRED","message":"Key proxy đã hết hạn, vui lòng gia hạn để tiếp tục sử dụng"}""";

    private HttpResponseMessage Handle(HttpRequestMessage req)
    {
        var url = req.RequestUri!.ToString();
        if (Override?.Invoke(url) is { } o) return o;
        var resp = Core(url);
        resp.Headers.Date = ServerNow;
        return resp;
    }

    private HttpResponseMessage Core(string url)
    {
        if (url.Contains("/proxies/current", StringComparison.Ordinal))
        {
            CurrentCalls++;
            return CurrentIp is null
                ? FakeHttp.Text("""{"success":false,"error":"PROXY_NOT_FOUND_BY_KEY","message":"Could not find the proxy being used by key"}""", HttpStatusCode.BadRequest)
                : FakeHttp.Text(Ok(CurrentIp, NextChangeAt));
        }
        if (url.Contains("/proxies/new", StringComparison.Ordinal))
        {
            NewCalls.Add(_clock.Now);
            if (NextChangeAt is { } t && ServerNow < t)
            {
                Rejected++;
                var sec = (int)Math.Ceiling((t - ServerNow).TotalSeconds);
                return FakeHttp.Text($$"""{"success":false,"error":"TIME_TO_CHANGE_INVALID","message":"Gửi lại sau {{sec}} giây"}""", HttpStatusCode.BadRequest);
            }
            CurrentIp = Ips[Math.Min(_next++, Ips.Count - 1)];
            NextChangeAt = ServerNow + MinUse;
            return FakeHttp.Text(Ok(CurrentIp, NextChangeAt));
        }
        return FakeHttp.Text("not found", HttpStatusCode.NotFound);
    }
}

/// <summary>Dịch vụ trả IP giả: handler cho đi thẳng (lease null) trả <see cref="MachineIp"/>; qua proxy trả
/// <see cref="ExitIpFor"/>. Ghi lại từng lần tạo handler (lease nào).</summary>
internal sealed class FakeIpWorld
{
    public string? MachineIp { get; set; } = "27.72.100.100";

    /// <summary>IP ra theo proxy; mặc định = host của proxy (proxy Kiot: host chính là IP ra). null = hỏng.</summary>
    public Func<ProxyLease, string?> ExitIpFor { get; set; } = l => l.Host;

    /// <summary>Bẻ từng dịch vụ (host dịch vụ, lease) — trả null = xử lý thường.</summary>
    public Func<string, ProxyLease?, HttpResponseMessage?>? Override { get; set; }

    public List<ProxyLease?> Handlers { get; } = [];
    public List<string> Hits { get; } = [];

    public HttpMessageHandler Create(ProxyLease? lease)
    {
        Handlers.Add(lease);
        return new FakeHttp(req =>
        {
            var host = req.RequestUri!.Host;
            Hits.Add($"{host}|{lease?.ServerArg ?? "direct"}");
            if (Override?.Invoke(host, lease) is { } o) return o;
            var ip = lease is null ? MachineIp : ExitIpFor(lease);
            return ip is null ? FakeHttp.Text("bad gateway", HttpStatusCode.BadGateway) : FakeHttp.Text(ip + "\n");
        });
    }
}

/// <summary>Launcher giả: đếm số lần mở, giữ request cuối.</summary>
internal sealed class FakeLauncher : ISellerBrowserLauncher
{
    public List<SellerLaunchRequest> Requests { get; } = [];
    public Exception? Throw { get; set; }

    public Task<SellerBrowserSession> LaunchAsync(SellerLaunchRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        if (Throw is not null) throw Throw;
        return Task.FromResult(new SellerBrowserSession(null, request.ProfileDir, request.Proxy, request.ExitIp, () => Task.FromResult(true)));
    }
}

internal static class SellerTestData
{
    public static readonly DateTimeOffset T0 = new(2026, 11, 18, 2, 0, 0, TimeSpan.Zero);

    public static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "seller-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, true); } catch { /* file tạm, bỏ qua */ }
    }
}
