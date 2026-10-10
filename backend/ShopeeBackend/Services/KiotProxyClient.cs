using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace ShopeeBackend.Services
{
    public class KiotProxyInfo
    {
        public bool Success { get; set; }
        public string Http { get; set; } = string.Empty;
        public string Socks5 { get; set; } = string.Empty;
        public string RealIpAddress { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public int HttpPort { get; set; }
        public int Socks5Port { get; set; }
        public string Location { get; set; } = string.Empty;
        public long ExpirationAt { get; set; } // Unix ms
        public int Ttl { get; set; }           // Tổng thời gian sống (giây)
        public int Ttc { get; set; }           // Số giây chờ để được đổi tiếp (giây)
        public string ErrorCode { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public int WaitSeconds { get; set; }   // Số giây cần chờ nếu dính TTC cooldown

        /// <summary>
        /// Số giây còn lại trước khi proxy bị hủy/hết hạn
        /// </summary>
        public int SecondsRemaining
        {
            get
            {
                if (ExpirationAt <= 0) return 0;
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return (int)Math.Max(0, (ExpirationAt - now) / 1000);
            }
        }

        public bool IsSafeToRun(int thresholdSeconds = 180) => SecondsRemaining >= thresholdSeconds;
    }

    public static class KiotProxyClient
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        /// <summary>
        /// Lấy thông tin Proxy hiện tại được gán với Key (GET /api/v1/proxies/current?key=...)
        /// </summary>
        public static async Task<KiotProxyInfo> GetCurrentProxyAsync(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return new KiotProxyInfo { ErrorMessage = "Key rỗng" };

            key = key.Trim();
            if (IsStaticIpPort(key))
                return CreateStaticProxyInfo(key);

            string url = $"https://api.kiotproxy.com/api/v1/proxies/current?key={Uri.EscapeDataString(key)}";
            return await QueryKiotApiAsync(url);
        }

        /// <summary>
        /// Lấy proxy mới hoặc đổi proxy khác (GET /api/v1/proxies/new?key=...&region=random)
        /// </summary>
        public static async Task<KiotProxyInfo> GetNewProxyAsync(string key, string region = "random")
        {
            if (string.IsNullOrWhiteSpace(key))
                return new KiotProxyInfo { ErrorMessage = "Key rỗng" };

            key = key.Trim();
            if (IsStaticIpPort(key))
                return CreateStaticProxyInfo(key);

            string url = $"https://api.kiotproxy.com/api/v1/proxies/new?key={Uri.EscapeDataString(key)}&region={Uri.EscapeDataString(region)}";
            return await QueryKiotApiAsync(url);
        }

        /// <summary>
        /// Thoát Proxy ra khỏi Key khi không sử dụng (GET /api/v1/proxies/out?key=...)
        /// </summary>
        public static async Task<bool> ReleaseProxyAsync(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || IsStaticIpPort(key))
                return true;

            try
            {
                string url = $"https://api.kiotproxy.com/api/v1/proxies/out?key={Uri.EscapeDataString(key.Trim())}";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                var response = await Http.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Lấy proxy sẵn sàng hoạt động an toàn:
        /// 1. Kiểm tra proxy hiện tại.
        /// 2. Nếu đã hết hạn hoặc thời gian còn lại < safeSecondsThreshold (mặc định 180s):
        ///    - Yêu cầu cấp mới qua GetNewProxyAsync.
        ///    - Nếu dính thời gian chờ TTC (TIME_TO_CHANGE_INVALID), đếm ngược và tự động thử lại.
        ///    - Không bao giờ trả về lỗi che giấu từ proxy cũ.
        /// </summary>
        public static async Task<KiotProxyInfo> FetchOrRefreshProxyAsync(
            string key,
            int safeSecondsThreshold = 180,
            Action<string>? logCallback = null,
            Func<bool>? isStopRequested = null)
        {
            if (string.IsNullOrWhiteSpace(key))
                return new KiotProxyInfo { ErrorMessage = "Key rỗng" };

            key = key.Trim();
            if (IsStaticIpPort(key))
                return CreateStaticProxyInfo(key);

            // Bước 1: Check proxy hiện tại
            var current = await GetCurrentProxyAsync(key);
            if (current.Success && !string.IsNullOrWhiteSpace(current.Http))
            {
                int rem = current.SecondsRemaining;
                if (rem >= safeSecondsThreshold)
                {
                    logCallback?.Invoke($"Proxy hiện tại còn sống an toàn ({rem}s / TTL {current.Ttl}s): {current.Http}");
                    return current;
                }

                logCallback?.Invoke($"Proxy sắp hết hạn ({rem}s <= {safeSecondsThreshold}s, TTC={current.Ttc}s). Đang yêu cầu cấp IP mới...");
            }
            else
            {
                logCallback?.Invoke($"Chưa có proxy gắn với key ({current.ErrorMessage}). Đang yêu cầu cấp IP mới...");
            }

            // Bước 2: Yêu cầu cấp IP mới với cơ chế chờ TTC cooldown & tự động thử lại
            int maxAttempts = 3;
            KiotProxyInfo? lastNewProxy = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (isStopRequested?.Invoke() == true)
                    return new KiotProxyInfo { ErrorMessage = "Tiến trình đã bị dừng bởi người dùng." };

                var newProxy = await GetNewProxyAsync(key);
                lastNewProxy = newProxy;

                if (newProxy.Success && !string.IsNullOrWhiteSpace(newProxy.Http))
                {
                    logCallback?.Invoke($"🎉 Đã cấp IP mới thành công: {newProxy.Http} (Sống {newProxy.SecondsRemaining}s, Khu vực: {newProxy.Location})");
                    return newProxy;
                }

                // Nếu xin mới chưa được nhưng proxy hiện tại vẫn còn sống tương đối an toàn (> 45s)
                if (current.Success && current.SecondsRemaining > 45 && !string.IsNullOrWhiteSpace(current.Http))
                {
                    logCallback?.Invoke($"⚠️ Chưa thể đổi IP mới ({newProxy.ErrorMessage}). Tạm thời tiếp tục dùng proxy hiện tại ({current.SecondsRemaining}s còn lại)...");
                    return current;
                }

                // Kiểm tra nếu KiotProxy yêu cầu chờ do TTC cooldown (TIME_TO_CHANGE_INVALID)
                int waitTime = newProxy.WaitSeconds;
                if (waitTime <= 0 && newProxy.Ttc > 0)
                {
                    waitTime = newProxy.Ttc;
                }

                if (waitTime > 0 && waitTime <= 180)
                {
                    logCallback?.Invoke($"⏳ KiotProxy yêu cầu chờ {waitTime}s trước khi cấp IP mới. Đang đếm ngược để nhận IP...");
                    int totalWait = waitTime + 2; // +2s đệm an toàn để máy chủ KiotProxy kịp cập nhật
                    for (int w = totalWait; w > 0; w--)
                    {
                        if (isStopRequested?.Invoke() == true)
                            return new KiotProxyInfo { ErrorMessage = "Tiến trình đã bị dừng bởi người dùng." };

                        if (w % 15 == 0 || w <= 5)
                        {
                            logCallback?.Invoke($"⏳ Chờ cấp IP mới: còn {w}s...");
                        }
                        await Task.Delay(1000);
                    }
                    continue; // Sau khi hết thời gian chờ, quay lại vòng lặp để xin cấp IP mới!
                }

                // Nếu gặp lỗi mạng tạm thời hoặc nghẽn server, thử lại sau 5s
                if (attempt < maxAttempts)
                {
                    logCallback?.Invoke($"⚠️ Chưa lấy được IP mới ({newProxy.ErrorMessage}). Đang thử lại lần {attempt + 1}/{maxAttempts} sau 5s...");
                    for (int s = 0; s < 5; s++)
                    {
                        if (isStopRequested?.Invoke() == true)
                            return new KiotProxyInfo { ErrorMessage = "Tiến trình đã bị dừng bởi người dùng." };
                        await Task.Delay(1000);
                    }
                }
            }

            // Nếu sau các lần thử vẫn không thành công, trả về lỗi thực tế từ GetNewProxyAsync
            // (Tuyệt đối không trả về current nếu current đã thất bại để tránh che giấu lỗi)
            if (lastNewProxy != null && !lastNewProxy.Success)
            {
                return lastNewProxy;
            }

            return current;
        }

        /// <summary>
        /// Kiểm tra kết nối Internet thực tế thông qua Proxy (anti-leak & health check).
        /// Gửi request tới api.ipify.org qua WebProxy với retry ngắn.
        /// </summary>
        public static async Task<(bool IsAlive, string PublicIp)> CheckProxyAliveAsync(string proxyAddress, int timeoutSeconds = 8)
        {
            if (string.IsNullOrWhiteSpace(proxyAddress))
                return (false, string.Empty);

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    string proxyUri = proxyAddress.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? proxyAddress
                        : $"http://{proxyAddress}";

                    var handler = new HttpClientHandler
                    {
                        Proxy = new WebProxy(proxyUri),
                        UseProxy = true
                    };

                    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                    var res = await client.GetStringAsync("https://api.ipify.org");
                    string ip = res.Trim();
                    if (!string.IsNullOrWhiteSpace(ip))
                    {
                        return (true, ip);
                    }
                }
                catch
                {
                    if (attempt == 1)
                    {
                        await Task.Delay(1500); // Đợi 1.5s nếu proxy vừa được mở port
                    }
                }
            }

            return (false, string.Empty);
        }

        /// <summary>
        /// Tương thích ngược: Lấy IP:Port chuỗi cho phiên chạy
        /// </summary>
        public static async Task<string> FetchProxyAsync(string key)
        {
            var info = await FetchOrRefreshProxyAsync(key);
            return info.Success ? info.Http : string.Empty;
        }

        private static bool IsStaticIpPort(string key) =>
            key.Contains(":") && !key.StartsWith("http", StringComparison.OrdinalIgnoreCase);

        private static KiotProxyInfo CreateStaticProxyInfo(string key)
        {
            string host = key;
            int port = 80;
            var parts = key.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1], out int p))
            {
                host = parts[0];
                port = p;
            }
            return new KiotProxyInfo
            {
                Success = true,
                Http = key,
                Host = host,
                HttpPort = port,
                RealIpAddress = host,
                ExpirationAt = DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeMilliseconds(),
                Ttl = 86400,
                Ttc = 0
            };
        }

        private static async Task<KiotProxyInfo> QueryKiotApiAsync(string url)
        {
            var result = new KiotProxyInfo();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                var response = await Http.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                bool success = root.TryGetProperty("success", out var sEl) && sEl.GetBoolean();
                result.Success = success;

                if (root.TryGetProperty("error", out var errEl))
                    result.ErrorCode = errEl.GetString() ?? "";

                if (root.TryGetProperty("message", out var mEl))
                    result.ErrorMessage = mEl.GetString() ?? "";

                // Trích xuất số giây chờ nếu gặp lỗi TIME_TO_CHANGE_INVALID ("Gửi lại sau X giây")
                if (!result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(
                        result.ErrorMessage,
                        @"(?:sau|trong)\s*(\d+)\s*giây",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int waitSec))
                    {
                        result.WaitSeconds = waitSec;
                    }
                }

                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                {
                    if (data.TryGetProperty("http", out var hEl)) result.Http = hEl.GetString() ?? "";
                    if (data.TryGetProperty("socks5", out var s5El)) result.Socks5 = s5El.GetString() ?? "";
                    if (data.TryGetProperty("realIpAddress", out var ripEl)) result.RealIpAddress = ripEl.GetString() ?? "";
                    if (data.TryGetProperty("host", out var hostEl)) result.Host = hostEl.GetString() ?? "";
                    if (data.TryGetProperty("location", out var locEl)) result.Location = locEl.GetString() ?? "";
                    if (data.TryGetProperty("httpPort", out var hpEl) && hpEl.TryGetInt32(out int hp)) result.HttpPort = hp;
                    if (data.TryGetProperty("socks5Port", out var spEl) && spEl.TryGetInt32(out int sp)) result.Socks5Port = sp;
                    if (data.TryGetProperty("expirationAt", out var expEl) && expEl.TryGetInt64(out long exp)) result.ExpirationAt = exp;
                    if (data.TryGetProperty("ttl", out var ttlEl) && ttlEl.TryGetInt32(out int ttl)) result.Ttl = ttl;
                    if (data.TryGetProperty("ttc", out var ttcEl) && ttcEl.TryGetInt32(out int ttc)) result.Ttc = ttc;

                    if (string.IsNullOrWhiteSpace(result.Http) && !string.IsNullOrWhiteSpace(result.Host) && result.HttpPort > 0)
                    {
                        result.Http = $"{result.Host}:{result.HttpPort}";
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            return result;
        }
    }
}
