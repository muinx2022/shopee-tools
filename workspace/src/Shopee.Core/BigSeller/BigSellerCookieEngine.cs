using System.Text.Json;
using Shopee.Core.Cdp;

namespace Shopee.Core.BigSeller;

/// <summary>
/// Engine DÙNG CHUNG quản lý cookie + phiên đăng nhập BigSeller cho cả Shopee Scrape lẫn BigSeller Update.
/// Tự chứa: nói CDP qua <see cref="CdpSession"/> của Core (port-based) nên KHÔNG phụ thuộc CdpClient riêng
/// của từng module. Gói trọn "bí quyết" giữ phiên mà trước đây chỉ Scrape có:
///  • <see cref="ImportKeepingLiveTokenAsync"/> — KHÔNG đè muc_token đang sống trong browser bằng token cũ
///    từ file (server xoay token liên tục; đè token cũ = server đá phiên = "log in first"), NHƯNG luôn nạp đè
///    nếu muc_token trong browser thuộc tài khoản BigSeller khác (uid/puid khác nhau).
///  • <see cref="WriteBackLiveTokenAsync"/> — ghi NGƯỢC token (server vừa xoay) trở lại file sau mỗi lần
///    thành công, để lần mở sau dùng token tươi thay vì token thiu (chỉ khi cùng uid).
/// muc_token = cookie giữ phiên BigSeller. Xem ghi chú dự án [[bigseller-single-session]].
/// <para>Chia làm 4 file cùng class (partial): file này = hằng + predicate + đọc cookie từ browser;
/// <c>.CookieFile.cs</c> = đọc/ghi FILE cookie (atomic); <c>.Importer.cs</c> = nạp file → browser qua 2
/// transport CDP + ghi ngược ra file; <c>.SessionPolicy.cs</c> = luật giữ token sống / so tuổi &amp; uid token.</para>
/// </summary>
public static partial class BigSellerCookieEngine
{
    public const string AuthCookieName = "muc_token";

    public const string DefaultListingUrl =
        "https://www.bigseller.com/web/listing/shopee/index.htm?bsStatus=1";

    // ──────────────────────────────────────────────────────────────────────────────
    //  Predicate / token-info
    // ──────────────────────────────────────────────────────────────────────────────

    public static bool IsBigSellerCookie(Dictionary<string, object?> cookie) =>
        (cookie.GetValueOrDefault("domain") as string ?? "")
            .Contains("bigseller", StringComparison.OrdinalIgnoreCase);

    public static bool HasAuthCookie(IEnumerable<Dictionary<string, object?>> cookies) =>
        cookies.Any(c =>
            IsBigSellerCookie(c) &&
            string.Equals(c.GetValueOrDefault("name") as string, AuthCookieName, StringComparison.OrdinalIgnoreCase) &&
            (c.GetValueOrDefault("value") as string ?? "").Length > 5);

    /// <summary>muc_token để SO SÁNH "token nào mới hơn": giá trị (so trùng) + hạn (expires). null = không có.</summary>
    public readonly record struct AuthTokenInfo(string Value, DateTimeOffset? Expires);

    private static AuthTokenInfo? ToAuthTokenInfo(Dictionary<string, object?>? c)
    {
        if (c is null) return null;
        var val = c.GetValueOrDefault("value") as string ?? "";
        if (val.Length <= 5) return null;
        DateTimeOffset? exp = null;
        if (c.TryGetValue("expires", out var e) && e is not null)
        {
            double secs = e switch
            {
                long l => l,
                double d => d,
                _ => double.TryParse(e.ToString(), out var p) ? p : -1,
            };
            if (secs > 0) exp = DateTimeOffset.FromUnixTimeSeconds((long)secs);
        }
        return new AuthTokenInfo(val, exp);
    }

    // ──────────────────────────────────────────────────────────────────────────────
    //  Đọc cookie từ browser (qua CDP)
    // ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Toàn bộ cookie BigSeller đang có trong browser (Storage.getCookies cấp browser).</summary>
    public static async Task<List<Dictionary<string, object?>>> GetBigSellerCookiesAsync(
        int cdpPort, CancellationToken ct = default)
    {
        await using var s = await CdpSession.ConnectToBrowserAsync(cdpPort, ct).ConfigureAwait(false);
        var result = await s.SendAsync("Storage.getCookies", null, ct).ConfigureAwait(false);
        if (!result.TryGetProperty("cookies", out var cookiesEl) || cookiesEl.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<Dictionary<string, object?>>();
        foreach (var cookie in cookiesEl.EnumerateArray())
        {
            var map = new Dictionary<string, object?>();
            foreach (var p in cookie.EnumerateObject())
            {
                map[p.Name] = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.Number => p.Value.TryGetInt64(out var i) ? i : p.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => p.Value.ToString(),
                };
            }
            if (IsBigSellerCookie(map)) list.Add(map);
        }
        return list;
    }

    /// <summary>browser ĐÃ có cookie đăng nhập (muc_token) chưa — để xác nhận import thành công.</summary>
    public static async Task<bool> HasAuthCookieInBrowserAsync(int cdpPort, CancellationToken ct = default)
    {
        try { return HasAuthCookie(await GetBigSellerCookiesAsync(cdpPort, ct).ConfigureAwait(false)); }
        catch { return false; }
    }

    /// <summary>muc_token ĐANG có trong BROWSER. null nếu chưa có. Dùng để QUYẾT ĐỊNH có nên nạp đè token
    /// từ file hay không (đừng đè token server vừa xoay = giết phiên → "log in first").</summary>
    public static async Task<AuthTokenInfo?> GetBrowserAuthTokenInfoAsync(int cdpPort, CancellationToken ct = default)
    {
        try
        {
            var cookies = await GetBigSellerCookiesAsync(cdpPort, ct).ConfigureAwait(false);
            return ToAuthTokenInfo(cookies.FirstOrDefault(x =>
                string.Equals(x.GetValueOrDefault("name") as string, AuthCookieName, StringComparison.OrdinalIgnoreCase)));
        }
        catch { return null; }
    }

    /// <summary>Chuỗi CHẨN ĐOÁN muc_token đang có trong browser (giá trị rút gọn + uid + hạn) để trả lời
    /// "login first / nhầm tài khoản": không có / khác uid / server đá phiên / token hết hạn.</summary>
    public static async Task<string> GetAuthCookieDebugAsync(int cdpPort, CancellationToken ct = default)
    {
        try
        {
            var cookies = await GetBigSellerCookiesAsync(cdpPort, ct).ConfigureAwait(false);
            var c = cookies.FirstOrDefault(x =>
                string.Equals(x.GetValueOrDefault("name") as string, AuthCookieName, StringComparison.OrdinalIgnoreCase));
            if (c is null) return "(không có muc_token)";

            var val = c.GetValueOrDefault("value") as string ?? "";
            var prefix = val.Length <= 8 ? val : val[..8];
            var uid = GetJwtUserId(val);
            var uidStr = uid.HasValue ? $" uid={uid.Value}" : "";
            var expStr = "session (không hạn)";
            if (c.TryGetValue("expires", out var e) && e is not null)
            {
                double secs = e switch
                {
                    long l => l,
                    double d => d,
                    _ => double.TryParse(e.ToString(), out var p) ? p : -1,
                };
                if (secs > 0)
                {
                    var exp = DateTimeOffset.FromUnixTimeSeconds((long)secs);
                    expStr = $"{exp.LocalDateTime:dd/MM HH:mm} (còn {(exp - DateTimeOffset.Now).TotalHours:0.0}h)";
                }
            }
            return $"muc_token={prefix}…(len {val.Length}){uidStr} hạn={expStr}";
        }
        catch (Exception ex) { return $"(lỗi đọc token: {ex.Message})"; }
    }

    /// <summary>
    /// Kiểm tra nhanh xem phiên đăng nhập BigSeller của tài khoản còn sống hay không bằng cách gọi
    /// API POST https://www.bigseller.com/api/v1/user/getUserAccount.json (nhẹ, nhanh, không cần mở browser).
    /// Phân biệt 3 kết quả (<see cref="BigSellerSessionProbe"/>): server TRẢ LỜI "sống" / "từ chối", hay KHÔNG HỎI
    /// ĐƯỢC (rớt mạng, timeout…) — rớt mạng KHÔNG được coi là mất phiên (đăng nhập lúc đó cũng hỏng theo).
    /// Không có file cookie / không có token → Rejected, không chạm mạng.
    /// Huỷ qua <paramref name="ct"/> → ném <see cref="OperationCanceledException"/> (không nuốt).
    /// </summary>
    public static async Task<BigSellerSessionProbe> ProbeSessionAsync(BigSellerAccount account, string? proxyServer = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(account.CookieFile) || !File.Exists(account.CookieFile))
            return BigSellerSessionProbe.Rejected;

        var tokenInfo = GetFileAuthTokenInfo(account.CookieFile);
        if (tokenInfo is null || string.IsNullOrWhiteSpace(tokenInfo.Value.Value))
            return BigSellerSessionProbe.Rejected;

        var tokenStr = tokenInfo.Value.Value;

        // Đọc danh sách cookie từ file để gửi kèm request
        var cookieHeader = $"muc_token={tokenStr}";
        try
        {
            var json = await File.ReadAllTextAsync(account.CookieFile, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var cList = root.ValueKind == JsonValueKind.Array
                ? root
                : root.TryGetProperty("cookies", out var cp) ? cp : default;

            if (cList.ValueKind == JsonValueKind.Array)
            {
                var pairs = new List<string>();
                foreach (var c in cList.EnumerateArray())
                {
                    var name = c.TryGetProperty("name", out var np) ? np.GetString() : null;
                    var val = c.TryGetProperty("value", out var vp) ? vp.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(name) && val != null)
                        pairs.Add($"{name}={val}");
                }
                if (pairs.Count > 0)
                    cookieHeader = string.Join("; ", pairs);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { }

        var endpoints = new[]
        {
            "https://www.bigseller.com/api/v1/user/getUserAccount.json",
            "https://www.bigseller.pro/api/v1/user/getUserAccount.json"
        };

        var results = new List<BigSellerSessionProbe>(endpoints.Length);
        foreach (var endpoint in endpoints)
        {
            try
            {
                HttpMessageHandler handler;
                if (!string.IsNullOrWhiteSpace(proxyServer))
                {
                    var pUri = proxyServer.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? proxyServer : $"http://{proxyServer}";
                    handler = new HttpClientHandler { Proxy = new System.Net.WebProxy(pUri), UseProxy = true };
                }
                else
                {
                    handler = new HttpClientHandler();
                }

                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
                using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
                req.Headers.Add("Cookie", cookieHeader);
                req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
                req.Headers.Add("Accept", "application/json, text/plain, */*");
                var origin = endpoint.Contains(".pro", StringComparison.OrdinalIgnoreCase) ? "https://www.bigseller.pro" : "https://www.bigseller.com";
                req.Headers.Add("Origin", origin);
                req.Headers.Add("Referer", $"{origin}/web/listing/shopee/index.htm?bsStatus=1");
                req.Headers.Add("X-Requested-With", "XMLHttpRequest");
                req.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

                using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                results.Add(ClassifyProbeResponse((int)resp.StatusCode, body));
            }
            // Caller huỷ → ném ra; còn timeout của HttpClient (ct chưa huỷ) hay lỗi kết nối → Unreachable.
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { results.Add(BigSellerSessionProbe.Unreachable); }
        }

        return CombineProbes(results);
    }

    /// <summary>Phân loại phản hồi của MỘT endpoint (thuần, test được không cần mạng). JSON có <c>code</c> ≠ 0
    /// hoặc HTTP 401/403 → Rejected; 2xx + JSON <c>code == 0</c> → Alive; còn lại (HTTP status khác, body không
    /// phải JSON / không có <c>code</c> số) → Unreachable (không kết luận được gì về phiên).</summary>
    public static BigSellerSessionProbe ClassifyProbeResponse(int statusCode, string? body)
    {
        if (statusCode is 401 or 403) return BigSellerSessionProbe.Rejected;
        try
        {
            using var doc = JsonDocument.Parse(body ?? "");
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("code", out var codeProp)
                && codeProp.ValueKind == JsonValueKind.Number && codeProp.TryGetInt32(out var code))
            {
                if (code != 0) return BigSellerSessionProbe.Rejected;
                return statusCode is >= 200 and < 300 ? BigSellerSessionProbe.Alive : BigSellerSessionProbe.Unreachable;
            }
        }
        catch (JsonException) { }
        return BigSellerSessionProbe.Unreachable;
    }

    /// <summary>Gộp kết quả nhiều endpoint: có Alive → Alive; không thì có Unreachable → Unreachable; còn lại
    /// (toàn Rejected) → Rejected; danh sách rỗng → Unreachable. Unreachable thắng Rejected vì tk chỉ sống trên 1
    /// cụm (.com HOẶC .pro): endpoint kia có thể luôn từ chối token, nên "chập mạng ở endpoint thật + endpoint
    /// kia từ chối" KHÔNG được đọc là Rejected (sẽ kéo đăng nhập rồi MarkFailed oan). Phiên chết thật vẫn đăng
    /// nhập qua isSessionIssue (2001/401006 từ endpoint sync).</summary>
    public static BigSellerSessionProbe CombineProbes(IEnumerable<BigSellerSessionProbe> results)
    {
        var any = false;
        var unreachable = false;
        foreach (var r in results)
        {
            if (r == BigSellerSessionProbe.Alive) return BigSellerSessionProbe.Alive;
            if (r == BigSellerSessionProbe.Unreachable) unreachable = true;
            any = true;
        }
        return !any || unreachable ? BigSellerSessionProbe.Unreachable : BigSellerSessionProbe.Rejected;
    }

    /// <summary>true nếu phiên còn sống (server xác nhận). Rejected và Unreachable đều là false — caller cần
    /// phân biệt hai trường hợp đó thì dùng <see cref="ProbeSessionAsync"/>.</summary>
    public static async Task<bool> IsSessionAliveAsync(BigSellerAccount account, string? proxyServer = null, CancellationToken ct = default) =>
        await ProbeSessionAsync(account, proxyServer, ct).ConfigureAwait(false) == BigSellerSessionProbe.Alive;

    public static Task<bool> IsSessionAliveAsync(BigSellerAccount account, CancellationToken ct) =>
        IsSessionAliveAsync(account, null, ct);
}

/// <summary>Kết quả hỏi server BigSeller về phiên: <see cref="Alive"/> = server xác nhận còn sống;
/// <see cref="Rejected"/> = server từ chối (hoặc không có cookie/token để hỏi); <see cref="Unreachable"/> = không
/// hỏi được (rớt mạng/timeout/trả lời lạ) → KHÔNG kết luận được phiên chết.</summary>
public enum BigSellerSessionProbe { Alive, Rejected, Unreachable }
