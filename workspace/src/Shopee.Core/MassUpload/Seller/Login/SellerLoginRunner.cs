using Microsoft.Playwright;

namespace Shopee.Core.MassUpload.Seller.Login;

/// <summary>
/// Chạy <see cref="SellerLoginFlow"/> trên Brave đã mở chế độ <see cref="SellerLaunchMode.Login"/> (plan GĐ5b mục 2–3): chờ endpoint
/// CDP loopback sẵn sàng → <c>ConnectOverCDPAsync</c> (cách của <c>suite/orders/…/LoginBrowserBootstrap</c>: KHÔNG
/// <c>--enable-automation</c>, KHÔNG tiêm script vá fingerprint, dùng context mặc định = profile thật) → chạy luồng → NGẮT Playwright.
/// <para><b>Không tắt Brave</b> ở bất kỳ nhánh nào (thành công, dừng, lỗi, huỷ): cửa sổ để nguyên cho user xem. Với kết nối
/// <c>ConnectOverCDP</c>, <c>IBrowser.CloseAsync</c> chỉ đóng kết nối, không đóng trình duyệt; đóng Brave là việc của
/// <see cref="SellerBrowserSession.CloseAsync"/>.</para>
/// </summary>
public static class SellerLoginRunner
{
    /// <summary>Chờ <c>/json/version</c> trả 200 (bản check đơn: 15 s).</summary>
    public static readonly TimeSpan CdpEndpointTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Đăng nhập Shopee trên <paramref name="session"/>. Không ném, trừ huỷ. <paramref name="log"/> / <paramref name="status"/>
    /// gọi từ luồng nền, chuỗi đã che mật khẩu.</summary>
    public static async Task<SellerLoginResult> RunAsync(SellerBrowserSession session, SellerLoginCredentials creds,
        Action<string>? log, Action<string>? status, CancellationToken ct)
    {
        string Scrub(string m) => SellerLoginText.Scrub(m, creds.Secrets());
        void L(string m) => log?.Invoke(Scrub(m));

        if (session.CdpPort is not { } port || session.CdpEndpoint is not { } endpoint)
            return new SellerLoginResult(SellerLoginOutcome.Failed, "Brave này không mở ở chế độ đăng nhập (không có CDP) — đóng rồi bấm " +
                                                                     "\"Mở + đăng nhập Shopee\".");
        IPlaywright? pw = null;
        IBrowser? browser = null;
        var aborted = 0;

        // Huỷ → cắt kết nối Playwright NGAY: lệnh Playwright không nhận ct (GotoAsync, EvaluateAsync, gõ phím…) nên chỉ có cách này làm
        // chúng ném lập tức thay vì treo tới hết timeout 30–60 s. Chỉ ngắt kết nối, KHÔNG đóng Brave.
        void Abort()
        {
            if (Interlocked.Exchange(ref aborted, 1) == 1) return;
            var b = browser;
            var p = pw;
            _ = Task.Run(async () =>
            {
                if (b is not null) await DisconnectAsync(b).ConfigureAwait(false);
                try { p?.Dispose(); } catch { /* bỏ qua */ }
            });
        }

        using var reg = ct.Register(Abort);
        try
        {
            await WaitForCdpEndpointAsync(port, CdpEndpointTimeout, ct).ConfigureAwait(false);
            pw = await Playwright.CreateAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            browser = await pw.Chromium.ConnectOverCDPAsync(endpoint, new BrowserTypeConnectOverCDPOptions { Timeout = 30000 })
                .ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            L($"Đã nối Playwright vào Brave qua {endpoint}.");

            // Context mặc định = profile thật (cookie của shop). KHÔNG NewContext: đó là context ẩn danh, không có phiên.
            if (browser.Contexts.Count == 0)
                return new SellerLoginResult(SellerLoginOutcome.Failed, "Nối CDP được nhưng không thấy context của profile — đăng nhập tay.");
            var context = browser.Contexts[0];
            var page = context.Pages.FirstOrDefault(p => !p.IsClosed) ?? await context.NewPageAsync().ConfigureAwait(false);

            var flow = new SellerLoginFlow(new PlaywrightSellerLoginPage(context, page), creds, log: log, status: status);
            return await flow.RunAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
        catch (Exception ex)
        {
            var m = Scrub($"Không nối được Playwright vào Brave: {ex.Message}");
            L(m);
            return new SellerLoginResult(SellerLoginOutcome.Failed, m);
        }
        finally
        {
            if (browser is not null) await DisconnectAsync(browser).ConfigureAwait(false);
            try { pw?.Dispose(); } catch { /* bỏ qua */ }
            if (pw is not null) L("Đã ngắt Playwright — cửa sổ Brave để nguyên.");
        }
    }

    /// <summary>Ngắt kết nối CDP (không đóng Brave), tối đa 5 s.</summary>
    private static async Task DisconnectAsync(IBrowser browser)
    {
        try
        {
            var close = browser.CloseAsync();
            await Task.WhenAny(close, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            _ = close.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);   // lỗi đóng muộn: nuốt, không để unobserved
        }
        catch { /* bỏ qua */ }
    }

    /// <summary>Chờ <c>http://127.0.0.1:&lt;port&gt;/json/version</c> trả 200. Không đi proxy (loopback, không ra mạng).</summary>
    private static async Task WaitForCdpEndpointAsync(int port, TimeSpan timeout, CancellationToken ct)
    {
        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
        var url = $"{SellerCdp.Endpoint(port)}/json/version";
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var res = await http.GetAsync(url, ct).ConfigureAwait(false);
                if (res.IsSuccessStatusCode) return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* chưa sẵn sàng */ }
            await Task.Delay(150, ct).ConfigureAwait(false);
        }
        throw new InvalidOperationException("Quá thời gian chờ endpoint CDP sẵn sàng.");
    }
}
