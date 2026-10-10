using Microsoft.Playwright;

namespace Shopee.Core.BigSeller;

/// <summary>
/// Trang BigSeller đòi mã email: bấm Send Code, đăng nhập hộp thư Hotmail/Outlook và để tab inbox mở.
/// Không đọc mã, không điền. Người dùng tự lấy mã trên tab Outlook rồi nhập trên trang BigSeller.
/// Luôn trả false để cửa sổ giữ mở cho đến khi người dùng đăng nhập xong (cookie lưu khi poll thấy phiên).
/// </summary>
public static class BigSellerEmailOtp
{
    public static async Task<bool> TrySolveAsync(
        IPage page, string email, string? emailPassword, Action<string>? log, CancellationToken ct,
        bool windowStaysOpen = true)
    {
        var sendBtn = page.Locator("button.submit-btn-send, button:has-text('Send Code')");
        if (await sendBtn.CountAsync().ConfigureAwait(false) > 0)
        {
            try { await sendBtn.First.ClickAsync(new() { Timeout = 4000 }).ConfigureAwait(false); } catch { }
            log?.Invoke("Đã bấm gửi mã về email.");
            await Task.Delay(1500, ct).ConfigureAwait(false);
        }

        if (!windowStaysOpen)
        {
            log?.Invoke("⚠ BigSeller đòi mã. Đăng nhập tất cả chạy ẩn nên không mở hộp thư — dùng Mở Profile Bigseller.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(emailPassword))
        {
            log?.Invoke("⚠ Chưa có mật khẩu email — nhập mã tay trên trang BigSeller đang mở.");
            return false;
        }

        try
        {
            await HotmailOtpReader.OpenInboxAsync(page.Context, email, emailPassword, log, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            log?.Invoke("• Mở hộp thư lỗi: " + ex.Message);
        }

        return false;
    }
}
