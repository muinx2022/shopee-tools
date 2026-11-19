using Shopee.Core.MassUpload.Seller;

namespace Shopee.Core.Tests.MassUpload.Seller;

/// <summary>
/// Chạy tay, KHÔNG chạy trong CI: mở Brave THẬT qua proxy thật rồi đóng. Cách chạy:
/// <list type="number">
/// <item>Đặt biến môi trường <c>SELLER_SMOKE_PROXY</c> = proxy tĩnh (<c>host:port[:user:pass]</c>, http).</item>
/// <item>Bỏ <c>Skip</c> ở dưới, chạy <c>dotnet test --filter SellerBrowserSmokeTests</c>.</item>
/// <item>Trong cửa sổ Brave hiện ra: mở <c>https://api.ipify.org</c> xem IP = IP proxy; với proxy có user/pass thì không được hiện
/// hộp thoại đăng nhập proxy. Test tự đóng Brave sau 60 s.</item>
/// </list>
/// Profile tạm, xoá sau khi chạy.
/// </summary>
public sealed class SellerBrowserSmokeTests
{
    [Fact(Skip = "Chạy tay: mở Brave thật qua proxy thật (đặt SELLER_SMOKE_PROXY).")]
    public async Task MoBraveThat_QuaProxy_RoiDong()
    {
        var raw = Environment.GetEnvironmentVariable("SELLER_SMOKE_PROXY");
        Assert.False(string.IsNullOrWhiteSpace(raw), "Chưa đặt SELLER_SMOKE_PROXY");
        Assert.True(StaticProxyParser.TryParse(raw, ProxyScheme.Http, out var lease, out var err), err);

        var dir = SellerTestData.NewTempDir();
        try
        {
            var settings = new SellerUploadSettings();
            var account = new SellerAccount
            {
                Id = "smoke",
                ProxyOverride = new SellerProxyOverride { Kind = SellerProxyKind.Static, Static = raw },
            };
            using var kiotHttp = SellerKiotApi.CreateHttpClient();
            var gate = new SellerRunGate(new SellerProxyProvider(new SellerKiotApi(kiotHttp)), new ExitIpVerifier(log: Console.WriteLine),
                new SellerBrowserLauncher(Path.Combine(dir, "proxy-auth"), Console.WriteLine), log: Console.WriteLine);

            var r = await gate.OpenAsync(account, settings, Path.Combine(dir, "profile"), [], CancellationToken.None);
            Assert.True(r.Ok, r.Reason);   // launcher đã kiểm tiến trình chính mang --proxy-server + --webrtc-ip-handling-policy
            var prefs = File.ReadAllText(Path.Combine(dir, "profile", "Default", "Preferences"));
            Assert.Contains("disable_non_proxied_udp", prefs);
            Assert.Contains("\"off\"", File.ReadAllText(Path.Combine(dir, "profile", "Local State")));
            // Kiểm tay trong 60 s: https://api.ipify.org = IP proxy; trang kiểm WebRTC (vd browserleaks.com/webrtc) không có srflx IP máy.
            await Task.Delay(TimeSpan.FromSeconds(60));
            Assert.True(await r.Session!.CloseAsync());
            var authRoot = Path.Combine(dir, "proxy-auth");
            Assert.True(!Directory.Exists(authRoot) || Directory.GetDirectories(authRoot).Length == 0);
            Assert.Equal(lease!.HasCredentials, Directory.Exists(authRoot));
        }
        finally { SellerTestData.TryDelete(dir); }
    }
}
