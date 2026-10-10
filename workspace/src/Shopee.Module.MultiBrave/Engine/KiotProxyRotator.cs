using Shopee.Core.Proxy;

namespace OpenMultiBraveLauncherV3;

/// <summary>
/// CHỌN/XOAY proxy cho một instance: ưu tiên IP đang giữ của key KiotProxy (<c>/current</c>), chỉ gọi
/// <c>/new</c> khi IP hết hạn, và tránh lấy lại đúng proxy vừa chết khi đang khôi phục. Cũng lo nhánh proxy
/// thủ công / không proxy. Tách khỏi <see cref="BraveInstanceSession"/> — phiên chỉ nhận về chuỗi
/// <c>proxyServer</c> + fingerprint để dựng args và so sánh.
/// </summary>
internal sealed class KiotProxyRotator(Func<InstanceConfig?> config, Action<string> log)
{
    private InstanceConfig Config =>
        config() ?? throw new InvalidOperationException("Chưa cấu hình instance.");

    /// <summary>Proxy dùng cho lần mở profile này: Kiot key → thủ công → không proxy (theo đúng thứ tự ưu tiên cũ).</summary>
    public async Task<(string? proxyServer, Dictionary<string, object>? proxyData)> ResolveForLaunchAsync()
    {
        var cfg = Config;

        var kiotKey = cfg.KiotProxyKey.Trim();
        if (!string.IsNullOrWhiteSpace(kiotKey))
        {
            var proxy = await GetWorkingProxyAsync().ConfigureAwait(false);
            var server = BuildProxyServer(proxy, cfg.ProxyType);
            return (server, proxy);
        }

        var manual = cfg.ManualProxy.Trim();
        if (!string.IsNullOrWhiteSpace(manual))
        {
            var server = NormalizeManualProxy(manual, cfg.ProxyType);
            log($"Dùng proxy thủ công: {server}");
            return (server, null);
        }

        if (cfg.RequireProxy)
            throw new InvalidOperationException(
                "Instance chưa có proxy (KiotProxy key / proxy thủ công) — không mở profile để tránh login Shopee bằng IP máy.");

        log(cfg.RequireProxy
            ? "Không có proxy — mở profile bằng IP máy (đã xác nhận bỏ qua cảnh báo)."
            : "Không có Kiot key / proxy thủ công — mở profile không proxy.");
        return (null, null);
    }

    public static string NormalizeManualProxy(string input, string proxyType)
    {
        var s = input.Trim();
        if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("socks4://", StringComparison.OrdinalIgnoreCase))
            return s;

        var scheme = proxyType.Equals("socks5", StringComparison.OrdinalIgnoreCase) ? "socks5://" : "http://";
        return scheme + s;
    }

    private Task<Dictionary<string, object>> GetProxyAsync()
    {
        if (config() is null) throw new InvalidOperationException("Chua cau hinh instance.");
        return KiotProxyClient.GetNewProxyAsync(Config.KiotProxyKey, Config.Region, log);
    }

    public async Task<Dictionary<string, object>> GetWorkingProxyAsync(
        int maxAttempts = 5,
        bool preferFresh = false,
        string? avoidFingerprint = null)
    {
        // Mở profile thường: /current để giữ IP ~30 phút. preferFresh (traffic/error, proxy chết): gọi /new
        // ngay. Key Kiot chết thì không xoay — ném ra để job dừng.
        Exception? lastError = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            Dictionary<string, object> proxy;
            try
            {
                proxy = preferFresh
                    ? await GetProxyAsync().ConfigureAwait(false)
                    : await GetCurrentOrNewAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (ProxyFailure.IsFleetWideProxyFailure(ex.Message)) throw;
                lastError = ex;
                if (attempt < maxAttempts)
                    await Task.Delay(preferFresh ? 10_000 : 2_000).ConfigureAwait(false);
                continue;
            }

            var fingerprint = BuildFingerprint(proxy);
            if (!string.IsNullOrWhiteSpace(avoidFingerprint) &&
                string.Equals(fingerprint, avoidFingerprint, StringComparison.Ordinal))
            {
                try
                {
                    proxy = await GetProxyAsync().ConfigureAwait(false);
                    fingerprint = BuildFingerprint(proxy);
                }
                catch (Exception ex)
                {
                    if (ProxyFailure.IsFleetWideProxyFailure(ex.Message)) throw;
                    lastError = ex;
                    if (attempt < maxAttempts)
                        await Task.Delay(preferFresh ? 10_000 : 2_000).ConfigureAwait(false);
                    continue;
                }

                if (string.Equals(fingerprint, avoidFingerprint, StringComparison.Ordinal))
                {
                    lastError = new InvalidOperationException(
                        $"KiotProxy vẫn trả về proxy cũ đang lỗi: {BuildProxyServer(proxy, Config.ProxyType)}");
                    if (attempt < maxAttempts)
                        await Task.Delay(preferFresh ? 10_000 : 2_000).ConfigureAwait(false);
                    continue;
                }
            }

            var proxyServer = BuildProxyServer(proxy, Config.ProxyType);
            log($"Proxy từ KiotProxy ({attempt}/{maxAttempts}): {proxyServer}");
            return proxy;
        }
        throw lastError ?? new InvalidOperationException("Không lấy được proxy.");
    }

    /// <summary>/current còn IP thì dùng. IP sticky hết vòng đời thì /new một lần. Key chết thì không /new.</summary>
    private async Task<Dictionary<string, object>> GetCurrentOrNewAsync()
    {
        try
        {
            return await GetCurrentProxyAsync().ConfigureAwait(false);
        }
        catch (Exception curEx) when (IsProxyExpiredError(curEx.Message))
        {
            return await GetProxyAsync().ConfigureAwait(false);
        }
    }

    public Task<Dictionary<string, object>> GetCurrentProxyAsync()
    {
        if (config() is null) throw new InvalidOperationException("Chua cau hinh instance.");
        return KiotProxyClient.GetCurrentProxyAsync(Config.KiotProxyKey);
    }

    public static string BuildProxyServer(Dictionary<string, object> proxy, string selectedType) =>
        KiotProxyClient.BuildProxyServer(proxy, selectedType);

    public static string BuildFingerprint(Dictionary<string, object> proxy) =>
        KiotProxyClient.BuildFingerprint(proxy);

    /// <summary>IP sticky hết vòng đời hoặc key chưa từng có IP. Key Kiot chết (KEY_EXPIRED) thì KHÔNG tính —
    /// xoay IP không cứu được.</summary>
    public static bool IsProxyExpiredError(string? msg)
    {
        if (string.IsNullOrWhiteSpace(msg)) return false;
        if (ProxyFailure.IsFleetWideProxyFailure(msg)) return false;
        return msg.Contains("PROXY_NOT_FOUND_BY_KEY", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("Could not find the proxy being used by key", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("hết hạn", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("het han", StringComparison.OrdinalIgnoreCase);
    }
}
