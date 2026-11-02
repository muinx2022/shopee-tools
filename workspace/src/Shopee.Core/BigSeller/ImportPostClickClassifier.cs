using System.Text;
using System.Text.RegularExpressions;

namespace Shopee.Core.BigSeller;

/// <summary>Một modal đang hiển thị sau khi bấm Import: nội dung + có link "Hộp nháp" (hộp kết quả đã biết) không.</summary>
public sealed record OpenModalInfo(string Text, bool HasDraftBoxLink);

/// <summary>Kết quả phân loại sau click: <see cref="Sent"/> = coi lô đã gửi; ngược lại là CHƯA CHẮC kèm lý do.</summary>
public sealed record ImportPostClickVerdict(bool Sent, string Reason);

/// <summary>
/// Phân loại thuần "lô Import đã gửi chưa" sau khi modal import đã đóng, xét TRƯỚC khi đóng các hộp thoại sau import.
/// Hộp kết quả đã biết duy nhất có bằng chứng trong code: modal chứa link a.has_underline chữ "Hộp nháp".
/// CHƯA CHẮC (không MarkDone, dừng import shop) khi: URL không còn Crawl List; có toast lỗi; còn hộp lạ; hộp "Hộp nháp"
/// kèm lỗi (thất bại/lỗi/failed/error với số lượng ≠ 0); hoặc trang điều hướng mà không thấy hộp "Hộp nháp".
/// </summary>
public static class ImportPostClickClassifier
{
    /// <summary>Độ dài tối đa nội dung hộp/toast đưa vào lý do (log).</summary>
    public const int MaxBoxTextLength = 300;

    private static readonly string[] ErrorWords = ["thất bại", "lỗi", "failed", "error"];

    public static ImportPostClickVerdict Classify(
        bool urlOnCrawl, IReadOnlyList<OpenModalInfo> openModals,
        IReadOnlyList<string>? errorToasts = null, bool navigated = false)
    {
        if (!urlOnCrawl)
            return new(false, "URL trang không còn là Crawl List sau khi bấm Import (có thể rớt phiên về login hoặc điều hướng lạ).");

        if (errorToasts is { Count: > 0 })
            return new(false, $"có toast lỗi/thông báo sau khi bấm Import: \"{Trim(string.Join(" | ", errorToasts))}\"");

        foreach (var modal in openModals)
        {
            if (!modal.HasDraftBoxLink)
                return new(false, $"còn hộp thoại hiển thị KHÔNG phải hộp kết quả đã biết ('Hộp nháp'): \"{Trim(modal.Text)}\"");
            if (BoxReportsError(modal.Text))
                return new(false, $"hộp kết quả 'Hộp nháp' có dấu hiệu lỗi/thất bại: \"{Trim(modal.Text)}\"");
        }

        if (navigated && !openModals.Any(m => m.HasDraftBoxLink))
            return new(false, "trang điều hướng sau khi bấm Import mà không thấy hộp kết quả 'Hộp nháp'.");

        return new(true, "");
    }

    /// <summary>Cắt nội dung hộp/toast tới <see cref="MaxBoxTextLength"/> ký tự để đưa vào log.</summary>
    public static string Trim(string? text)
    {
        var t = (text ?? "").Trim();
        return t.Length > MaxBoxTextLength ? t[..MaxBoxTextLength] + "…" : t;
    }

    /// <summary>Hộp kết quả có nói tới lỗi không: chứa "thất bại"/"lỗi"/"failed"/"error" (không phân biệt hoa thường).
    /// Với mỗi từ: nếu NGAY SAU từ có số (cho phép khoảng trắng, ':', '：', '=', '-') thì số đó quyết định — 0 là không lỗi,
    /// khác 0 là lỗi ("Thất bại 0", "Thất bại: 5"). Chỉ khi sau từ KHÔNG có số mới xét số ngay TRƯỚC từ ("0 failed" không lỗi,
    /// "20 failed" lỗi). Nhờ vậy "Thành công 0 Thất bại 5" vẫn là lỗi (số 0 là của nhãn bên cạnh). Dạng số đứng trước nhãn
    /// ở giữa câu ("5 Thất bại 0 Thành công") có thể đọc sai.</summary>
    public static bool BoxReportsError(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Normalize(NormalizationForm.FormC);
        foreach (var word in ErrorWords)
        {
            var from = 0;
            while (from <= t.Length - word.Length)
            {
                var idx = t.IndexOf(word, from, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;
                var after = NumberAfter.Match(t[(idx + word.Length)..]);
                bool zero;
                if (after.Success) zero = IsZero(after.Groups["n"].Value);
                else
                {
                    var before = NumberBefore.Match(t[..idx]);
                    zero = before.Success && IsZero(before.Groups["n"].Value);
                }
                if (!zero) return true;
                from = idx + word.Length;
            }
        }
        return false;
    }

    private static bool IsZero(string number) => ZeroNumber.IsMatch(number);

    // Số NGAY SAU từ lỗi (bỏ qua khoảng trắng và ':' '：' '=' '-'); dấu '.' ',' cuối câu không thuộc số ("Thất bại 0. Xem").
    private static readonly Regex NumberAfter = new(@"^[\s:：=\-]*(?<n>\d+(?:[.,]\d+)*)", RegexOptions.Compiled);
    // Số ngay TRƯỚC từ lỗi (chỉ cách bằng khoảng trắng) và không dính vào số dài hơn ("20 failed" ≠ 0).
    private static readonly Regex NumberBefore = new(@"(?<![\d.,])(?<n>\d+(?:[.,]\d+)*)\s*$", RegexOptions.Compiled);
    private static readonly Regex ZeroNumber = new(@"^0+(?:[.,]0+)*$", RegexOptions.Compiled);
}