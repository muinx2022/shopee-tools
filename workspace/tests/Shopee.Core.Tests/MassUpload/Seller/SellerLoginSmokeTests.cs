using Shopee.Core.MassUpload.Seller;
using Shopee.Core.MassUpload.Seller.Login;

namespace Shopee.Core.Tests.MassUpload.Seller;

/// <summary>
/// Chạy tay, KHÔNG chạy trong CI (GĐ5b): mở Brave THẬT chế độ đăng nhập qua proxy thật, tự đăng nhập Shopee, ngắt Playwright, rồi
/// đóng Brave. Cách chạy:
/// <list type="number">
/// <item>Biến môi trường: <c>SELLER_SMOKE_PROXY</c> (<c>host:port[:user:pass]</c>, http), <c>SELLER_SMOKE_LOGIN</c>,
/// <c>SELLER_SMOKE_PASSWORD</c>; tuỳ chọn <c>SELLER_SMOKE_EMAIL</c> + <c>SELLER_SMOKE_EMAIL_PASSWORD</c>.</item>
/// <item>Bỏ <c>Skip</c>, chạy <c>dotnet test --filter SellerLoginSmokeTests</c>.</item>
/// <item>Bị đòi mã thì tự lấy mã trong tab Hotmail và nhập vào trang Shopee (luồng chờ tối đa 15 phút).</item>
/// </list>
/// Sau khi luồng xong, test kiểm Brave VẪN chạy (Playwright đã ngắt, không tắt Brave), rồi mới đóng. Profile tạm, xoá sau khi chạy.
/// </summary>
public sealed class SellerLoginSmokeTests
{
    [Fact(Skip = "Chạy tay: mở Brave thật + đăng nhập Shopee thật (đặt SELLER_SMOKE_PROXY / _LOGIN / _PASSWORD).")]
    public async Task MoBraveThat_DangNhapShopee_NgatPlaywright_BraveConChay()
    {
        var raw = Environment.GetEnvironmentVariable("SELLER_SMOKE_PROXY");
        Assert.False(string.IsNullOrWhiteSpace(raw), "Chưa đặt SELLER_SMOKE_PROXY");
        var creds = new SellerLoginCredentials(
            Environment.GetEnvironmentVariable("SELLER_SMOKE_LOGIN"),
            Environment.GetEnvironmentVariable("SELLER_SMOKE_PASSWORD"),
            Environment.GetEnvironmentVariable("SELLER_SMOKE_EMAIL"),
            Environment.GetEnvironmentVariable("SELLER_SMOKE_EMAIL_PASSWORD"));

        var dir = SellerTestData.NewTempDir();
        try
        {
            var account = new SellerAccount
            {
                Id = "smoke-login",
                ProxyOverride = new SellerProxyOverride { Kind = SellerProxyKind.Static, Static = raw },
            };
            using var kiotHttp = SellerKiotApi.CreateHttpClient();
            var host = new BraveSellerHost(Console.WriteLine);
            var gate = new SellerRunGate(new SellerProxyProvider(new SellerKiotApi(kiotHttp)), new ExitIpVerifier(log: Console.WriteLine),
                new SellerBrowserLauncher(Path.Combine(dir, "proxy-auth"), Console.WriteLine, host), log: Console.WriteLine);

            var r = await gate.OpenAsync(account, new SellerUploadSettings(), Path.Combine(dir, "profile"), [], SellerLaunchMode.Login,
                CancellationToken.None);
            Assert.True(r.Ok, r.Reason);
            Assert.NotNull(r.Session!.CdpPort);

            var login = await SellerLoginRunner.RunAsync(r.Session, creds, Console.WriteLine, Console.WriteLine, CancellationToken.None);
            Console.WriteLine($"Kết quả: {login.Outcome} — {login.Message}");
            Assert.NotEmpty(host.FindProfileProcesses(r.Session.ProfileDir));   // Playwright đã ngắt, Brave vẫn chạy

            await Task.Delay(TimeSpan.FromSeconds(20));                          // nhìn cửa sổ
            Assert.True(await r.Session.CloseAsync());
            Assert.True(login.Ok, login.Message);
        }
        finally { SellerTestData.TryDelete(dir); }
    }
}
