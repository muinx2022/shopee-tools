using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Shopee.Core.Proxy;

/// <summary>
/// Phân giải proxy RIÊNG của một tài khoản BigSeller (KiotProxy key) → chuỗi proxy-server
/// (vd "http://host:port" / "socks5://host:port"). Ưu tiên /current (IP sticky ~30' → login và scrape
/// dùng chung 1 IP trong cửa sổ đó); /current lỗi (key chưa kích hoạt / hết hạn / proxy chết) → /new cấp IP mới.
/// Có bước kiểm tra kết nối TCP (IsProxyAliveAsync) trước khi cấp cho Brave để tránh lỗi 'No internet'.
/// Dùng cho CẢ luồng đăng nhập BigSeller (Suite), luồng scrape (engine), và luồng Fan-out cào đa tài khoản.
/// Trả null khi không có key (caller tự hiểu = đi IP máy như cũ).
/// </summary>
public static class BigSellerProxyResolver
{
    public static async Task<string?> ResolveServerAsync(
        string? key, string? region, string? proxyType, Action<string>? log = null)
    {
        key = key?.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return null;

        var type = string.IsNullOrWhiteSpace(proxyType) ? "http" : proxyType.Trim();

        // ƯU TIÊN /current (IP sticky của key) + RETRY 2 lần: mọi lane của CÙNG 1 tk BigSeller phải ra
        // CÙNG 1 IP. Tránh fallback /new vì lỗi tạm thời (rate-limit).
        Dictionary<string, object>? proxy = null;
        Exception? lastCurErr = null;
        for (var attempt = 1; attempt <= 3 && proxy is null; attempt++)
        {
            try { proxy = await KiotProxyClient.GetCurrentProxyAsync(key).ConfigureAwait(false); }
            catch (Exception ex)
            {
                lastCurErr = ex;
                if (attempt < 3) await Task.Delay(1500).ConfigureAwait(false);
            }
        }

        // Kiểm tra xem proxy lấy được từ /current có thực sự sống (kết nối TCP được) không.
        // Chú ý: TUYỆT ĐỐI KHÔNG gọi /out nếu proxy chưa hết hạn (expirationAt còn hạn),
        // vì gọi /out sẽ ép KiotProxy huỷ IP hiện tại và sinh IP mới, làm mất phiên đăng nhập BigSeller (muc_token gắn chặt với IP).
        if (proxy is not null)
        {
            var testServer = KiotProxyClient.BuildProxyServer(proxy, type);
            var isAlive = await KiotProxyClient.IsProxyAliveAsync(testServer, timeoutMs: 4000).ConfigureAwait(false);
            if (!isAlive)
            {
                // Thử lại lần 2 với timeout 5s trước khi kết luận
                isAlive = await KiotProxyClient.IsProxyAliveAsync(testServer, timeoutMs: 5000).ConfigureAwait(false);
            }

            if (!isAlive)
            {
                var expirationAt = proxy.TryGetValue("expirationAt", out var expObj) && long.TryParse(expObj?.ToString(), out var exp) ? exp : 0;
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                if (expirationAt > nowMs)
                {
                    log?.Invoke($"Proxy BigSeller từ /current ({testServer}) kết nối chập chờn nhưng hạn proxy còn đến {DateTimeOffset.FromUnixTimeMilliseconds(expirationAt).ToLocalTime():HH:mm:ss} — giữ nguyên IP để tránh huỷ phiên BigSeller.");
                }
                else
                {
                    log?.Invoke($"Proxy BigSeller từ /current ({testServer}) không phản hồi và đã hết hạn → giải phóng (/out) để cấp IP mới.");
                    await KiotProxyClient.ReleaseProxyAsync(key, log).ConfigureAwait(false);
                    proxy = null;
                }
            }
        }

        if (proxy is null)
        {
            try
            {
                log?.Invoke($"Proxy BigSeller: cấp IP mới (/new)...");
                proxy = await KiotProxyClient.GetNewProxyAsync(key, region, log).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                log?.Invoke($"Proxy BigSeller: không lấy được IP mới cho key ({ex.Message}) — tạm đi IP máy.");
                return null;
            }
        }

        try
        {
            var finalServer = KiotProxyClient.BuildProxyServer(proxy, type);
            var alive = await KiotProxyClient.IsProxyAliveAsync(finalServer, timeoutMs: 4000).ConfigureAwait(false);
            if (!alive)
            {
                alive = await KiotProxyClient.IsProxyAliveAsync(finalServer, timeoutMs: 5000).ConfigureAwait(false);
            }
            if (!alive)
            {
                log?.Invoke($"Proxy ({finalServer}) không phản hồi TCP — tạm đi IP máy.");
                return null;
            }
            return finalServer;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Proxy BigSeller: endpoint không hợp lệ ({ex.Message}) — tạm đi IP máy.");
            return null;
        }
    }
}
