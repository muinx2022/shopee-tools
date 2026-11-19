using Microsoft.Playwright;
using Shopee.Toolkit.MsLogin;

namespace Shopee.Core.MassUpload.Seller.Login;

/// <summary>
/// Mở + ĐĂNG NHẬP hộp thư Hotmail/Outlook trong một tab mới của Brave đang đăng nhập Shopee, để USER tự lấy mã. CHÉP từ app check
/// đơn (<c>suite/orders/XuLyDonShopee.Core/Services/MicrosoftMailLogin.cs</c>), giữ nguyên các bước; khác bản gốc ở cách gõ / bấm
/// (GĐ5b vòng 2): ô nhập gõ qua <see cref="HumanFieldFill"/> (tìm lại ô ngay trước khi gõ, kiểm focus từng ký tự, hỏng thì DỪNG), mọi
/// cú bấm có hit-test. Selector lấy thẳng từ bộ dùng
/// chung <see cref="MsLoginSelectors"/> (bản check đơn đi qua alias trong <c>LoginSelectors</c>) — Microsoft đổi form thì sửa ở ĐÓ.
/// <para>CHỈ đăng nhập rồi mở hộp thư: KHÔNG mở mail, KHÔNG bấm gì trong mail. KHÔNG log giá trị mật khẩu.</para>
/// </summary>
internal static class MicrosoftMailLogin
{
    /// <summary>Mở TAB MỚI rồi đăng nhập hộp thư; được thì vào hộp thư Outlook. Trả tab mail (kể cả khi đăng nhập hỏng — để user tự
    /// làm) và cờ <c>LoggedIn</c>. Best-effort — KHÔNG ném (trừ hủy).</summary>
    internal static async Task<(IPage? MailPage, bool LoggedIn)> OpenMailboxSignedInAsync(
        IBrowserContext context, string email, string password, Action<string>? log, Random rng, CancellationToken ct)
    {
        void L(string m) => log?.Invoke(m);

        var mailPage = await context.NewPageAsync().ConfigureAwait(false);
        L("Mở trang đăng nhập Microsoft...");
        try
        {
            await mailPage.GotoAsync("https://login.microsoftonline.com/", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60000,
            }).ConfigureAwait(false);
        }
        catch { /* nuốt lỗi điều hướng — các bước dưới poll selector tự lo */ }

        if (!await LoginHotmailAsync(mailPage, email, password, log, rng, ct).ConfigureAwait(false))
        {
            return (mailPage, false);
        }

        // login.microsoftonline.com hạ cánh ở portal, không phải hộp thư → vào hộp thư Outlook (chỉ mở, không đọc mail).
        L("Vào hộp thư Outlook...");
        try
        {
            await mailPage.GotoAsync("https://outlook.live.com/mail/0/", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60000,
            }).ConfigureAwait(false);
        }
        catch { /* nuốt lỗi điều hướng */ }

        return (mailPage, true);
    }

    /// <summary>Đăng nhập Hotmail/Outlook: username → (nếu có) "Dùng mật khẩu" / "Các cách khác" → password → "Duy trì đăng
    /// nhập?" Có. Mỗi bước "chờ có selector thì làm, không thì bỏ qua" (đã đăng nhập sẵn từ profile → mọi bước tự bỏ qua). Trả
    /// <c>false</c> khi Microsoft báo sai tài khoản / sai mật khẩu.</summary>
    private static async Task<bool> LoginHotmailAsync(
        IPage mailPage, string email, string password, Action<string>? log, Random rng, CancellationToken ct)
    {
        void L(string m) => log?.Invoke(m);
        var mouse = MouseState.CenterOf(mailPage);

        // Bấm CÓ hit-test (không trúng thì không bấm). Bản check đơn bấm mù theo toạ độ ở nút "Tiếp" / "Đăng nhập" / KMSI.
        async Task<bool> Click(IElementHandle el)
        {
            var (x, y, clicked) = await LoginHumanInput.TryHumanClickVisibleAsync(mailPage, el, mouse.X, mouse.Y, rng, ct).ConfigureAwait(false);
            mouse.X = x;
            mouse.Y = y;
            return clicked;
        }

        // Gõ an toàn (GĐ5b vòng 2): tìm LẠI ô ngay trước khi gõ, bấm phải trúng, kiểm focus trước mỗi ký tự (HumanFieldFill).
        async Task<bool> Fill(string[] selectors, string text)
        {
            var el = await LoginPageProbe.FindFirstVisibleByRectsAsync(mailPage, selectors, 3000, ct).ConfigureAwait(false);
            return await HumanFieldFill.FillAsync(el is null ? null : new PlaywrightHumanField(mailPage, el, mouse, rng), text, rng,
                (d, t) => Task.Delay(d, t), ct).ConfigureAwait(false);
        }

        // 0) Có thể mở ra trang landing → bấm "Đăng nhập"/"Sign in" trước.
        var userField = await LoginPageProbe.FindFirstVisibleByRectsAsync(mailPage, MsLoginSelectors.User, 6000, ct).ConfigureAwait(false);
        if (userField is null)
        {
            var signIn = await LoginPageProbe.FindVisibleByTextAsync(mailPage, MsLoginSelectors.SignIn, MsLoginSelectors.SignInRegex, ct, 4000).ConfigureAwait(false);
            if (signIn is not null)
            {
                L("Chưa vào form đăng nhập — bấm 'Đăng nhập'...");
                await Click(signIn).ConfigureAwait(false);
                await Task.Delay(rng.Next(1500, 3500), ct).ConfigureAwait(false);
            }
            userField = await LoginPageProbe.FindFirstVisibleByRectsAsync(mailPage, MsLoginSelectors.User, 15000, ct).ConfigureAwait(false);
        }

        // 1) Username.
        if (userField is not null)
        {
            L("Nhập email đăng nhập hộp thư...");
            if (!await Fill(MsLoginSelectors.User, email).ConfigureAwait(false))
            {
                L("Ô email hộp thư đổi / mất focus giữa lúc gõ — dừng, tự đăng nhập hộp thư.");
                return false;
            }
            var next = await LoginPageProbe.FindFirstVisibleByRectsAsync(mailPage, MsLoginSelectors.Submit, 3000, ct).ConfigureAwait(false);
            if (next is not null) await Click(next).ConfigureAwait(false);
            await Task.Delay(rng.Next(1500, 3000), ct).ConfigureAwait(false);

            if (await LoginPageProbe.IsSelectorVisibleAsync(mailPage, MsLoginSelectors.UsernameError).ConfigureAwait(false))
            {
                L("Email hộp thư không hợp lệ (Microsoft báo lỗi tài khoản).");
                return false;
            }
        }

        // 2) Đưa về Ô MẬT KHẨU — poll tới ~45s: thấy ô mật khẩu → xong; thấy "Dùng/Nhập mật khẩu" → bấm; thấy "Các cách khác để
        //    đăng nhập" (form passwordless) → bấm (vòng sau thấy tile mật khẩu).
        IElementHandle? passField = null;
        var passDeadline = DateTime.UtcNow.AddSeconds(45);
        var clickedOtherWays = false;
        while (DateTime.UtcNow < passDeadline)
        {
            ct.ThrowIfCancellationRequested();

            passField = await LoginPageProbe.FindFirstVisibleByRectsAsync(mailPage, MsLoginSelectors.Password, 1500, ct).ConfigureAwait(false);
            if (passField is not null) break;

            var usePwd = await LoginPageProbe.FindByNormalizedTextInFramesAsync(mailPage, MsLoginSelectors.UsePassword, MsLoginSelectors.UsePasswordNeedles, ct, 1200).ConfigureAwait(false);
            if (usePwd is not null)
            {
                L("Chọn 'Dùng mật khẩu' / 'Nhập mật khẩu'...");
                await Click(usePwd).ConfigureAwait(false);
                await Task.Delay(rng.Next(1200, 2200), ct).ConfigureAwait(false);
                continue;
            }

            var otherWays = await LoginPageProbe.FindByNormalizedTextInFramesAsync(mailPage, MsLoginSelectors.OtherWays, MsLoginSelectors.OtherWaysNeedles, ct, 1200).ConfigureAwait(false);
            if (otherWays is not null)
            {
                L("Form 'Xác minh email' — bấm 'Các cách khác để đăng nhập'...");
                await Click(otherWays).ConfigureAwait(false);
                clickedOtherWays = true;
                await Task.Delay(rng.Next(1200, 2200), ct).ConfigureAwait(false);
                continue;
            }

            await Task.Delay(rng.Next(1200, 2000), ct).ConfigureAwait(false);
        }

        if (passField is null)
        {
            L($"Không đưa được về ô mật khẩu sau 45s ({(clickedOtherWays ? "đã bấm 'Các cách khác' nhưng không thấy tile Mật khẩu" : "không thấy 'Các cách khác'/ô mật khẩu")}; URL: {SellerLoginText.UrlForLog(mailPage.Url)}) — tự đăng nhập hộp thư.");
        }

        // 3) Password (KHÔNG log giá trị).
        if (passField is not null)
        {
            L("Nhập mật khẩu hộp thư...");
            if (!await Fill(MsLoginSelectors.Password, password).ConfigureAwait(false))
            {
                L("Ô mật khẩu hộp thư đổi / mất focus giữa lúc gõ — dừng, tự đăng nhập hộp thư.");
                return false;
            }
            var signIn = await LoginPageProbe.FindFirstVisibleByRectsAsync(mailPage, MsLoginSelectors.Submit, 3000, ct).ConfigureAwait(false);
            if (signIn is not null) await Click(signIn).ConfigureAwait(false);
            await Task.Delay(rng.Next(2000, 4000), ct).ConfigureAwait(false);

            if (await LoginPageProbe.IsSelectorVisibleAsync(mailPage, MsLoginSelectors.PasswordError).ConfigureAwait(false))
            {
                L("Sai mật khẩu hộp thư (Microsoft báo lỗi).");
                return false;
            }
        }

        // 4) "Duy trì đăng nhập?" (KMSI) → "Có". Nút Fluent [data-testid='primaryButton'] CHỈ bấm khi chắc đang ở KMSI (kmsiVideo /
        //    kmsiImage); bản cũ #acceptButton/#idSIButton9. Poll ~8s.
        await Task.Delay(rng.Next(1000, 2500), ct).ConfigureAwait(false);
        var kmsiDeadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < kmsiDeadline)
        {
            ct.ThrowIfCancellationRequested();
            var onKmsi = await LoginPageProbe.IsAnyVisibleByClientRectsAsync(mailPage, MsLoginSelectors.KmsiFormMarkers, ct).ConfigureAwait(false);
            var kmsiSelectors = onKmsi ? MsLoginSelectors.KmsiYesFluent : MsLoginSelectors.KmsiYes;
            var kmsi = await LoginPageProbe.FindFirstVisibleByRectsAsync(mailPage, kmsiSelectors, 1000, ct).ConfigureAwait(false);
            if (kmsi is not null)
            {
                L("Bấm 'Có' để giữ đăng nhập hộp thư...");
                await Click(kmsi).ConfigureAwait(false);
                await Task.Delay(rng.Next(1500, 3000), ct).ConfigureAwait(false);
                break;
            }
            await Task.Delay(rng.Next(500, 900), ct).ConfigureAwait(false);
        }

        return true;
    }
}
