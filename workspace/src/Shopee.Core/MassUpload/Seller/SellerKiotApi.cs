using System.Text.RegularExpressions;
using Shopee.Core.Proxy;

namespace Shopee.Core.MassUpload.Seller;

/// <summary>Dữ liệu proxy Kiot trả về (<c>data</c> của <c>/proxies/new</c> và <c>/proxies/current</c>).</summary>
public sealed record KiotProxyData(string? Host, int HttpPort, int Socks5Port, string? Http, string? Socks5, string? RealIp,
    long NextRequestAtMs)
{
    /// <summary>Endpoint theo kiểu: ưu tiên trường <c>http</c>/<c>socks5</c> ("host:port"), không có thì host + cổng.</summary>
    public (string Host, int Port)? Endpoint(ProxyScheme scheme)
    {
        var joined = scheme == ProxyScheme.Socks5 ? Socks5 : Http;
        if (!string.IsNullOrWhiteSpace(joined))
        {
            var s = joined.Trim();
            var sep = s.IndexOf("://", StringComparison.Ordinal);
            if (sep >= 0) s = s[(sep + 3)..];
            var c = s.LastIndexOf(':');
            if (c > 0 && int.TryParse(s[(c + 1)..], out var p) && p is > 0 and <= 65535) return (s[..c].Trim('[', ']'), p);
        }
        var port = scheme == ProxyScheme.Socks5 ? Socks5Port : HttpPort;
        if (!string.IsNullOrWhiteSpace(Host) && port is > 0 and <= 65535) return (Host.Trim(), port);
        return null;
    }
}

/// <summary>Kết quả một lần gọi Kiot. <see cref="Data"/> null = lỗi (<see cref="ErrorCode"/>/<see cref="ErrorMessage"/>).</summary>
public sealed record KiotCallResult(KiotProxyData? Data, string? ErrorCode, string? ErrorMessage, int? WaitSeconds)
{
    /// <summary>Giờ máy chủ Kiot (header <c>Date</c> của phản hồi); null = không có header.</summary>
    public DateTimeOffset? ServerTime { get; init; }

    public bool Ok => Data is not null;

    /// <summary>Key chết (hết hạn / không tồn tại) — lỗi của CẢ lượt nếu là key chung. Dùng lại luật của
    /// <see cref="ProxyFailure.IsFleetWideProxyFailure"/>.</summary>
    public bool IsKeyDead => !Ok && ProxyFailure.IsFleetWideProxyFailure($"{ErrorMessage} | {ErrorCode}");

    public string Describe() => string.Join(" | ", new[] { ErrorMessage, ErrorCode }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

/// <summary>
/// Gọi API KiotProxy (<c>/proxies/new</c>, <c>/proxies/current</c>) qua <see cref="HttpClient"/> TIÊM VÀO — test thay handler
/// giả, không mạng thật.
/// <para>Không dùng thẳng <see cref="KiotProxyClient"/>: lớp đó giữ HttpClient tĩnh (không tiêm được), <c>FetchNewAsync</c> cứng
/// <c>region=random</c>, còn <c>GetNewProxyAsync</c> ném lỗi + ghi cache tĩnh dùng chung với MultiBrave. Ở đây giữ đúng hợp đồng
/// URL/JSON của nó (<c>key</c>, <c>region</c>, <c>data.http/socks5/host/httpPort/socks5Port/realIpAddress/nextRequestAt</c>), cộng
/// cách đọc "Gửi lại sau X giây" (lỗi <c>TIME_TO_CHANGE_INVALID</c>) của <c>backend/…/KiotProxyClient.cs</c>.</para>
/// </summary>
public sealed class SellerKiotApi
{
    public const string BaseUrl = "https://api.kiotproxy.com/api/v1/proxies/";

    private static readonly Regex WaitPattern = new(@"(?:sau|trong)\s*(\d+)\s*giây", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly HttpClient _http;

    public SellerKiotApi(HttpClient http) => _http = http;

    /// <summary>HttpClient cho bản chạy thật: KHÔNG đi proxy hệ thống, timeout 15 s, User-Agent trình duyệt (Cloudflare của Kiot
    /// chặn 403 nếu thiếu — như <see cref="KiotProxyClient"/>).</summary>
    public static HttpClient CreateHttpClient()
    {
        var c = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
        c.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
        return c;
    }

    /// <summary>Đổi IP mới cho key. Kiot chỉ cho đổi khi đã qua <c>nextRequestAt</c>; gọi sớm thì trả lỗi kèm số giây phải chờ.</summary>
    public Task<KiotCallResult> NewAsync(string key, string? region, CancellationToken ct)
    {
        region = string.IsNullOrWhiteSpace(region) ? "random" : region.Trim();
        return CallAsync($"{BaseUrl}new?key={Uri.EscapeDataString(key)}&region={Uri.EscapeDataString(region)}", ct);
    }

    /// <summary>Proxy đang gắn với key (không đổi IP).</summary>
    public Task<KiotCallResult> CurrentAsync(string key, CancellationToken ct) =>
        CallAsync($"{BaseUrl}current?key={Uri.EscapeDataString(key)}", ct);

    private async Task<KiotCallResult> CallAsync(string url, CancellationToken ct)
    {
        string body;
        int status;
        DateTimeOffset? serverTime;
        try
        {
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            status = (int)resp.StatusCode;
            serverTime = resp.Headers.Date;
            body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { return new KiotCallResult(null, null, ex.Message, null); }
        return Parse(status, body) with { ServerTime = serverTime };
    }

    /// <summary>Đọc phản hồi (hàm thuần). HTTP lỗi mà body là JSON thì vẫn lấy <c>error</c>/<c>message</c> trong body.</summary>
    internal static KiotCallResult Parse(int status, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var code = Str(root, "error");
            var msg = Str(root, "message");
            var success = root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
            if (!success || status is < 200 or > 299)
            {
                int? wait = null;
                var m = WaitPattern.Match(msg ?? "");
                if (m.Success && int.TryParse(m.Groups[1].Value, out var w)) wait = w;
                return new KiotCallResult(null, code, msg ?? $"Kiot trả lỗi (HTTP {status}).", wait);
            }
            if (!root.TryGetProperty("data", out var d) || d.ValueKind != JsonValueKind.Object)
                return new KiotCallResult(null, code, "Kiot không trả data.", null);
            var data = new KiotProxyData(
                Str(d, "host"), Int(d, "httpPort"), Int(d, "socks5Port"), Str(d, "http"), Str(d, "socks5"),
                Str(d, "realIpAddress"), d.TryGetProperty("nextRequestAt", out var nr) && nr.TryGetInt64(out var n) ? n : 0);
            return new KiotCallResult(data, null, null, null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            var text = body.Length > 200 ? body[..200] : body;
            return new KiotCallResult(null, null, $"Kiot trả phản hồi không đọc được (HTTP {status}): {text}", null);
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;
}
