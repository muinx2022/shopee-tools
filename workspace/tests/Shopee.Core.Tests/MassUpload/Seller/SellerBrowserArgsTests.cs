using System.Text.Json;
using Shopee.Core.MassUpload.Seller;

namespace Shopee.Core.Tests.MassUpload.Seller;

/// <summary>GĐ4: tham số Brave (hàm thuần) + extension proxy-auth. Luôn có proxy, chặn WebRTC, không CDP, mật khẩu proxy không
/// bao giờ lên dòng lệnh.</summary>
public sealed class SellerBrowserArgsTests : IDisposable
{
    private const string Profile = @"C:\Data\ShopeeBackend\Workspace\upload\profiles\shop-01";
    private readonly string _dir = SellerTestData.NewTempDir();

    public void Dispose() => SellerTestData.TryDelete(_dir);

    private static ProxyLease Plain => new(ProxyScheme.Http, "113.160.0.1", 20001);
    private static ProxyLease WithAuth => new(ProxyScheme.Http, "proxy.vn", 3128, "alice", "s3cr\"et'</script>");

    [Fact]
    public void LuonCoProxy_WebRtc_ProfileDung_ExtensionDung_KhongCdp()
    {
        var args = SellerBrowserArgs.Build(Profile, Plain, [@"C:\ext\seller-upload", @"C:\ext\b"]);

        Assert.Contains($"--user-data-dir={Profile}", args);
        Assert.Contains("--proxy-server=http://113.160.0.1:20001", args);
        Assert.Single(args, a => a.StartsWith("--proxy-server=", StringComparison.Ordinal));
        // Brave 157: cờ đúng là --webrtc-ip-handling-policy; "--force-…" không tồn tại trong chrome.dll (đo: lộ srflx IP thật).
        Assert.Contains("--webrtc-ip-handling-policy=disable_non_proxied_udp", args);
        Assert.DoesNotContain(args, a => a.StartsWith("--force-webrtc", StringComparison.Ordinal));
        // Hai cờ không tồn tại trong Brave 157.
        Assert.DoesNotContain("--disable-background-mode", args);
        Assert.DoesNotContain("--no-restore-last-session", args);
        Assert.Contains("--disable-quic", args);
        Assert.Contains(@"--load-extension=C:\ext\seller-upload,C:\ext\b", args);
        // Mở trang trống: chưa request nào tới Shopee trước khi extension kiểm IP trong trình duyệt (kiểm hai lớp).
        Assert.Equal("about:blank", args[^1]);
        Assert.DoesNotContain(args, a => a.Contains("shopee.vn", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(args, a => a.Contains("remote-debugging", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(args, a => a.Contains("enable-automation", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(args, a => a.Contains("headless", StringComparison.OrdinalIgnoreCase));

        // Một cờ --disable-features duy nhất, có cờ cho phép --load-extension (Brave 137+).
        var df = Assert.Single(args, a => a.StartsWith("--disable-features=", StringComparison.Ordinal));
        Assert.Contains("DisableLoadExtensionCommandLineSwitch", df);
    }

    [Fact]
    public void StartUrl_ChoPhepDoi()
    {
        var args = SellerBrowserArgs.Build(Profile, Plain, null, SellerBrowserArgs.SellerHomeUrl);
        Assert.Equal("https://banhang.shopee.vn/", args[^1]);
    }

    [Fact]
    public void BypassChiLoopback_KhongDungToiShopee()
    {
        var args = SellerBrowserArgs.Build(Profile, Plain, null);
        var bypass = Assert.Single(args, a => a.StartsWith("--proxy-bypass-list=", StringComparison.Ordinal));
        Assert.Equal("--proxy-bypass-list=localhost;127.0.0.1;[::1]", bypass);
        Assert.DoesNotContain(args, a => a.StartsWith("--proxy-bypass-list", StringComparison.Ordinal)
                                          && (a.Contains("shopee") || a.Contains('*') || a.Contains("<")));
        Assert.DoesNotContain(args, a => a.StartsWith("--no-proxy-server", StringComparison.Ordinal));
        Assert.DoesNotContain(args, a => a.StartsWith("--proxy-auto-detect", StringComparison.Ordinal));
        Assert.DoesNotContain(args, a => a.StartsWith("--load-extension", StringComparison.Ordinal));
    }

    [Fact]
    public void Socks5VaIpv6_DungDangProxyServer()
    {
        var args = SellerBrowserArgs.Build(Profile, new ProxyLease(ProxyScheme.Socks5, "2001:db8::1", 1080), null);
        Assert.Contains("--proxy-server=socks5://[2001:db8::1]:1080", args);
    }

    [Fact]
    public void ThieuProxy_Nem()
    {
        Assert.Throws<ArgumentNullException>(() => SellerBrowserArgs.Build(Profile, null!, null));
        Assert.Throws<ArgumentException>(() => SellerBrowserArgs.Build(Profile, new ProxyLease(ProxyScheme.Http, " ", 8080), null));
        Assert.Throws<ArgumentException>(() => SellerBrowserArgs.Build(Profile, new ProxyLease(ProxyScheme.Http, "1.2.3.4", 0), null));
        Assert.Throws<ArgumentException>(() => SellerBrowserArgs.Build(" ", Plain, null));
        Assert.Throws<NotSupportedException>(() =>
            SellerBrowserArgs.Build(Profile, new ProxyLease(ProxyScheme.Socks5, "1.2.3.4", 1080, "u", "p"), null));
        Assert.Throws<ArgumentException>(() => SellerBrowserArgs.Build(Profile, Plain, [@"C:\a,b"]));
    }

    [Fact]
    public void ProxyCoMatKhau_MatKhauKhongLenDongLenh_ExtensionProxyAuthDung()
    {
        var authDir = ProxyAuthExtension.Write(_dir, "shop-01", WithAuth);
        var args = SellerBrowserArgs.Build(Profile, WithAuth, [authDir, @"C:\ext\seller-upload"]);
        var cmd = SellerBrowserArgs.ToCommandLine(args);

        Assert.Contains("--proxy-server=http://proxy.vn:3128", args);
        foreach (var secret in new[] { "alice", "s3cr", "</script>" })
        {
            Assert.DoesNotContain(args, a => a.Contains(secret, StringComparison.Ordinal));
            Assert.DoesNotContain(secret, cmd);
        }
        Assert.Contains($"--load-extension={authDir},C:\\ext\\seller-upload", args);

        // manifest: MV3, đúng quyền, service worker.
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(authDir, "manifest.json")));
        var m = manifest.RootElement;
        Assert.Equal(3, m.GetProperty("manifest_version").GetInt32());
        var perms = m.GetProperty("permissions").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(["webRequest", "webRequestAuthProvider"], perms);
        Assert.Equal("<all_urls>", m.GetProperty("host_permissions")[0].GetString());
        Assert.Equal("background.js", m.GetProperty("background").GetProperty("service_worker").GetString());

        // background: chỉ trả lời challenge của proxy, đúng host:port, có giới hạn số lần; credentials đọc lại đúng nguyên văn.
        var js = File.ReadAllText(Path.Combine(authDir, "background.js"));
        Assert.Contains("if (!details.isProxy) return {};", js);
        Assert.Contains("Number(c.port) !== PROXY.port", js);
        Assert.Contains("onAuthRequired.addListener", js);
        Assert.Contains("[\"blocking\"]", js);
        Assert.Contains("return { cancel: true }", js);
        Assert.DoesNotContain("</script>", js);              // chuỗi đã được thoát
        var cfgLine = js.Split('\n').Single(l => l.StartsWith("const PROXY = ", StringComparison.Ordinal));
        var cfgJson = cfgLine["const PROXY = ".Length..].TrimEnd(';', '\r');
        using var cfg = JsonDocument.Parse(cfgJson);
        Assert.Equal("proxy.vn", cfg.RootElement.GetProperty("host").GetString());
        Assert.Equal(3128, cfg.RootElement.GetProperty("port").GetInt32());
        Assert.Equal("alice", cfg.RootElement.GetProperty("username").GetString());
        Assert.Equal("s3cr\"et'</script>", cfg.RootElement.GetProperty("password").GetString());
    }

    [Fact]
    public void ProxyKhongMatKhau_KhongSinhExtension()
    {
        Assert.Throws<ArgumentException>(() => ProxyAuthExtension.BuildFiles(Plain));
    }

    [Fact]
    public void ToCommandLine_BocNgoacThamSoCoDauCach_NhanDoiGachCuoi()
    {
        var cmd = SellerBrowserArgs.ToCommandLine([@"--user-data-dir=C:\Users\Ng Xuan\p", "--a", @"--x=C:\a b\", "https://banhang.shopee.vn/"]);
        Assert.Equal(@"""--user-data-dir=C:\Users\Ng Xuan\p"" --a ""--x=C:\a b\\"" https://banhang.shopee.vn/", cmd);
        Assert.Throws<ArgumentException>(() => SellerBrowserArgs.ToCommandLine(["--a=\"x\""]));
    }
}
