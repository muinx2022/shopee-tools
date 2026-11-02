using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Shopee.Core.BigSeller;
using Shopee.Core.Proxy;

namespace Shopee.Core.Scrape;

public sealed record BigSellerFanOutResult(bool Success, string Message, int Code = 0);

/// <summary>
/// Client gửi dữ liệu sản phẩm đã cào từ Lead Account tới các tài khoản BigSeller khác (Fan-out).
/// Sử dụng endpoint POST https://www.bigseller.com/api/v1/product/crawl/shopee/clientCrawl.json
/// (hoặc fallback bigseller.pro nếu cluster tài khoản nằm trên .pro).
/// Đi qua proxy riêng của từng tài khoản BigSeller để bảo toàn phiên đăng nhập và tránh bị logout.
/// </summary>
public static class BigSellerFanOutClient
{
    public static async Task<BigSellerFanOutResult> PostCrawlAsync(
        BigSellerAccount account,
        string url,
        string jsonData,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(account.CookieFile) || !File.Exists(account.CookieFile))
            return new BigSellerFanOutResult(false, $"Tài khoản '{account.DisplayName}' chưa có file cookie BigSeller. Vui lòng đăng nhập tài khoản trước.");

        var tokenInfo = BigSellerCookieEngine.GetFileAuthTokenInfo(account.CookieFile);
        if (tokenInfo is null || string.IsNullOrWhiteSpace(tokenInfo.Value.Value))
            return new BigSellerFanOutResult(false, $"Tài khoản '{account.DisplayName}' không tìm thấy muc_token hợp lệ. Vui lòng đăng nhập lại.");

        var tokenStr = tokenInfo.Value.Value;
        var puid = BigSellerCookieEngine.GetJwtUserId(tokenStr)?.ToString() ?? TryExtractPuid(tokenStr);
        if (string.IsNullOrWhiteSpace(puid))
            return new BigSellerFanOutResult(false, $"Không trích xuất được puid từ muc_token của tài khoản '{account.DisplayName}'.");

        // Đọc toàn bộ cookie từ file (gồm JSESSIONID, fingerPrint, MYJ_yosu7anwoc…) để gửi kèm request
        var cookieHeader = BuildFullCookieHeader(account.CookieFile, tokenStr);

        // Resolve proxy riêng của tài khoản nếu có cấu hình
        string? proxyServer = null;
        if (account.HasProxy)
        {
            try
            {
                proxyServer = await BigSellerProxyResolver.ResolveServerAsync(
                    account.KiotProxyKey, account.Region, account.ProxyType, log).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                log?.Invoke($"[{account.DisplayName}] Lỗi kết nối proxy BigSeller: {ex.Message}");
            }
        }

        using var handler = new HttpClientHandler();
        if (!string.IsNullOrWhiteSpace(proxyServer))
        {
            handler.Proxy = new WebProxy(proxyServer);
            handler.UseProxy = true;
        }

        using var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        var endpoints = new[]
        {
            "https://www.bigseller.com/api/v1/product/crawl/shopee/clientCrawl.json",
            "https://www.bigseller.pro/api/v1/product/crawl/shopee/clientCrawl.json"
        };

        BigSellerFanOutResult lastResult = new(false, "Không kết nối được BigSeller.");

        foreach (var endpoint in endpoints)
        {
            try
            {
                using var formData = new MultipartFormDataContent();
                formData.Add(new StringContent(puid), "uid");
                formData.Add(new StringContent(url), "url");
                formData.Add(new StringContent("1"), "type");
                formData.Add(new StringContent("0"), "isOpenSkip");
                formData.Add(new StringContent("shopee"), "platform");
                formData.Add(new StringContent(string.IsNullOrWhiteSpace(jsonData) ? "{}" : jsonData), "jsonData");

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = formData
                };

                request.Headers.Add("Cookie", cookieHeader);
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
                request.Headers.Add("Accept", "application/json, text/plain, */*");

                using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;
                var code = root.TryGetProperty("code", out var cp) && cp.TryGetInt32(out var c) ? c : -1;
                var msg = root.TryGetProperty("msg", out var mp) ? mp.GetString() : "";

                if (code == 0)
                {
                    return new BigSellerFanOutResult(true, "Thành công", 0);
                }

                // Nếu lỗi 401006 / 401002 (hết hạn phiên) trên bigseller.com, thử tiếp bigseller.pro trước khi báo lỗi
                if (code == 2001 && (msg?.Contains("401006") == true || msg?.Contains("401002") == true))
                {
                    lastResult = new BigSellerFanOutResult(
                        false,
                        $"Tài khoản '{account.DisplayName}' bị hết hạn phiên BigSeller (mã {code}: {msg}) — cần đăng nhập lại.",
                        code);
                    continue;
                }

                return new BigSellerFanOutResult(false, $"BigSeller trả lỗi ({code}): {msg}", code);
            }
            catch (Exception ex)
            {
                lastResult = new BigSellerFanOutResult(false, $"Lỗi gửi dữ liệu tới BigSeller ({endpoint}): {ex.Message}");
            }
        }

        return lastResult;
    }

    private static string BuildFullCookieHeader(string cookieFile, string defaultToken)
    {
        try
        {
            if (!File.Exists(cookieFile)) return $"muc_token={defaultToken}";
            var json = File.ReadAllText(cookieFile);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var cList = root.ValueKind == JsonValueKind.Array
                ? root
                : root.TryGetProperty("cookies", out var cp) ? cp : default;

            if (cList.ValueKind == JsonValueKind.Array)
            {
                var pairs = new List<string>();
                var hasToken = false;
                foreach (var c in cList.EnumerateArray())
                {
                    var name = c.TryGetProperty("name", out var np) ? np.GetString() : null;
                    var val = c.TryGetProperty("value", out var vp) ? vp.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(name) && val != null)
                    {
                        pairs.Add($"{name}={val}");
                        if (name == "muc_token") hasToken = true;
                    }
                }
                if (!hasToken) pairs.Add($"muc_token={defaultToken}");
                if (pairs.Count > 0) return string.Join("; ", pairs);
            }
        }
        catch { }

        return $"muc_token={defaultToken}";
    }

    /// <summary>
    /// Trích xuất puid (BigSeller User ID) từ JWT muc_token nếu GetJwtUserId không có.
    /// </summary>
    public static string? TryExtractPuid(string jwtToken)
    {
        try
        {
            var parts = jwtToken.Split('.');
            if (parts.Length < 2) return null;
            var payload = parts[1];
            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }
            var bytes = Convert.FromBase64String(payload);
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.TryGetProperty("info", out var infoEl))
            {
                var infoStr = infoEl.GetString();
                if (!string.IsNullOrWhiteSpace(infoStr))
                {
                    using var infoDoc = JsonDocument.Parse(infoStr);
                    if (infoDoc.RootElement.TryGetProperty("puid", out var puidEl))
                    {
                        return puidEl.ToString();
                    }
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}
