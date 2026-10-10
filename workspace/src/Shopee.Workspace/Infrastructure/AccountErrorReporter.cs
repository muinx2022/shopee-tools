using System.Collections.ObjectModel;
using Shopee.Core.Accounts;

namespace Shopee.Workspace.Infrastructure;

/// <summary>
/// Gom xử lý "tk Shopee dính captcha/lỗi lúc chạy".
/// UPSERT dòng vào lưới hiển thị + đánh dấu bền account (Disabled + LastError + CaptchaUrl).
/// </summary>
public static class AccountErrorReporter
{
    /// <summary>Upsert dòng tk lỗi vào <paramref name="rows"/> + đánh dấu bền.</summary>
    public static void Report(ObservableCollection<ErroredAccountRow> rows, string id, string label,
        string reason, string moduleTag, string? captchaUrl)
    {
        var now = DateTime.Now.ToString("HH:mm:ss");
        var row = rows.FirstOrDefault(x => x.Id == id);
        if (row is null) rows.Insert(0, new ErroredAccountRow(id, label, reason, now));
        else { row.Reason = reason; row.Time = now; }

        Flag(id, $"Dính captcha/lỗi ({moduleTag}) — {DateTime.Now:dd/MM HH:mm}: {reason}", captchaUrl);
    }

    private static void Flag(string id, string reason, string? captchaUrl)
    {
        var acc = AccountStore.Shared.Accounts.FirstOrDefault(a => a.Id == id);
        if (acc is null) return;
        var alreadyFlagged = acc.Disabled;
        acc.Disabled = true;
        acc.LastError = reason;
        if (!string.IsNullOrWhiteSpace(captchaUrl)) acc.CaptchaUrl = captchaUrl;
        if (!alreadyFlagged || !string.IsNullOrWhiteSpace(captchaUrl)) AccountStore.Shared.SaveOverlayOnly();
    }
}
