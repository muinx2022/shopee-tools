using System;
using System.Threading.Tasks;

namespace OpenMultiBraveLauncherV3;

/// <summary>
/// Shim chuyển tiếp tới <see cref="Shopee.Core.Proxy.BigSellerProxyResolver"/>.
/// </summary>
public static class BigSellerProxyResolver
{
    public static Task<string?> ResolveServerAsync(
        string? key, string? region, string? proxyType, Action<string>? log = null)
        => Shopee.Core.Proxy.BigSellerProxyResolver.ResolveServerAsync(key, region, proxyType, log);
}
