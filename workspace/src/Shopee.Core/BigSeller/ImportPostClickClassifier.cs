using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Shopee.Core.BigSeller;

/// <summary>Một modal đang hiển thị sau khi bấm Import: nội dung + có link "Hộp nháp"/"Draft Listing" (hộp kết quả đã biết) không.</summary>
public sealed record OpenModalInfo(string Text, bool HasDraftBoxLink);

/// <summary>Phân loại kết quả: đã gửi / hộp tiến trình còn đang chạy (chờ tiếp) / BigSeller báo thất bại / chưa chắc.</summary>
public enum ImportVerdictKind { Sent, InProgress, ReportedFailure, Uncertain }

/// <summary>Kết quả phân loại sau click: <see cref="Sent"/> = coi lô đã gửi; ngược lại không MarkDone, kèm lý do và
/// <see cref="Kind"/> (ctor ngắn: true → Sent, false → Uncertain).</summary>
public sealed record ImportPostClickVerdict(bool Sent, string Reason, ImportVerdictKind Kind)
{
    public ImportPostClickVerdict(bool Sent, string Reason)
        : this(Sent, Reason, Sent ? ImportVerdictKind.Sent : ImportVerdictKind.Uncertain) { }
}

/// <summary>Ba con số đọc từ hộp tiến trình ("Total: 50", "Successfully: 50", "Failed: 0" / "Tổng số: 50", "Thành công: 50", "Thất bại: 0");
/// null = chưa hiện/không đọc được. <see cref="RepeatedLabel"/> = một trong ba nhãn xuất hiện hơn một lần trong hộp (không biết số nào là của lô).</summary>
public sealed record ImportBoxCounts(int? Total, int? Success, int? Failed, bool RepeatedLabel = false)
{
    public bool IsComplete => Total.HasValue && Success.HasValue && Failed.HasValue;
}

/// <summary>Trạng thái hộp tiến trình theo số: Running = chưa đủ số hoặc Success + Failed &lt; Total.</summary>
public enum ImportBoxStatus { Running, Succeeded, AllFailed, PartialFailed }

/// <summary>
/// Phân loại thuần "lô Import đã gửi chưa" sau khi modal import đã đóng, xét TRƯỚC khi đóng các hộp thoại sau import.
/// Hộp kết quả đã biết duy nhất có bằng chứng trong code: modal chứa link a.has_underline chữ "Hộp nháp".
/// Hộp tiến trình "Importing… Finish" / "Đang import… Hoàn thành" (<see cref="IsImportProgressBox"/>) được xét theo số (<see cref="JudgeProgressBox"/>):
/// còn chạy → <see cref="ImportVerdictKind.InProgress"/> (chưa phải đã gửi); đủ Total = Successfully, Failed = 0 và Total khớp
/// số SP đã tick → đã gửi; Failed &gt; 0 → <see cref="ImportVerdictKind.ReportedFailure"/>.
/// CHƯA CHẮC (không MarkDone, dừng import shop) khi: URL không còn Crawl List; có toast lỗi; còn hộp lạ; hộp "Hộp nháp"
/// kèm lỗi (thất bại/lỗi/failed/error với số lượng ≠ 0); hoặc trang điều hướng mà không thấy hộp "Hộp nháp"/"Draft Listing".
/// Với <c>requireEvidence</c> (lần xét cuối khi hết cửa sổ theo dõi): không thấy hộp tiến trình lẫn hộp "Hộp nháp" cũng là chưa chắc.
/// </summary>
public static class ImportPostClickClassifier
{
    /// <summary>Độ dài tối đa nội dung hộp/toast đưa vào lý do (log).</summary>
    public const int MaxBoxTextLength = 300;

    private static readonly string[] ErrorWords = ["thất bại", "lỗi", "failed", "error"];

    public static ImportPostClickVerdict Classify(
        bool urlOnCrawl, IReadOnlyList<OpenModalInfo> openModals,
        IReadOnlyList<string>? errorToasts = null, bool navigated = false, int? expectedCount = null, bool requireEvidence = false,
        int shopCount = 1)
    {
        if (!urlOnCrawl)
            return new(false, "URL trang không còn là Crawl List sau khi bấm Import (có thể rớt phiên về login hoặc điều hướng lạ).");

        if (errorToasts is { Count: > 0 })
            return new(false, $"có toast lỗi/thông báo sau khi bấm Import: \"{Trim(string.Join(" | ", errorToasts))}\"");

        ImportPostClickVerdict? running = null;
        foreach (var modal in openModals)
        {
            // Hộp tiến trình "Importing… Finish" luôn hiện sau Import to Stores: xét theo số, không phải "hộp đã biết".
            if (IsImportProgressBox(modal.Text))
            {
                var judged = JudgeProgressBox(modal.Text, expectedCount, shopCount);
                if (judged.Kind == ImportVerdictKind.InProgress) running = judged;
                else if (!judged.Sent) return judged;
                continue;
            }
            if (!modal.HasDraftBoxLink)
                return new(false, $"còn hộp thoại hiển thị KHÔNG phải hộp kết quả đã biết ('Hộp nháp'): \"{Trim(modal.Text)}\"");
            if (BoxReportsError(modal.Text))
                return new(false, $"hộp kết quả 'Hộp nháp' có dấu hiệu lỗi/thất bại: \"{Trim(modal.Text)}\"");
        }

        if (running is not null) return running;

        if (navigated && !openModals.Any(m => m.HasDraftBoxLink))
            return new(false, "trang điều hướng sau khi bấm Import mà không thấy hộp kết quả 'Hộp nháp'.");

        // Hết cửa sổ theo dõi mà KHÔNG thấy hộp tiến trình lẫn hộp "Hộp nháp"/"Draft Listing": không có bằng chứng lô đã gửi.
        // (Hộp tiến trình tới được đây chỉ khi đã đủ số và thành công; ở đây còn lại thì chưa thấy gì.)
        if (requireEvidence && !openModals.Any(m => IsImportProgressBox(m.Text) || m.HasDraftBoxLink))
            return new(false, "không thấy hộp tiến trình import hay hộp kết quả 'Hộp nháp'/'Draft Listing' nào sau khi bấm Import (chưa có bằng chứng lô đã gửi).");

        return new(true, "");
    }

    /// <summary>Bỏ khỏi <paramref name="modals"/> các hộp đã hiện TRƯỚC khi bấm Import (so theo nội dung, mỗi nội dung có sẵn
    /// <c>n</c> lần thì bỏ tối đa <c>n</c> hộp) để hộp cũ chưa đóng không làm lô sau "đã gửi" oan hay "hộp lạ" oan.</summary>
    public static List<OpenModalInfo> ExcludeExistingModals(IEnumerable<OpenModalInfo> modals, IEnumerable<string> existingTexts)
    {
        var remaining = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in existingTexts) remaining[t] = remaining.GetValueOrDefault(t) + 1;
        var result = new List<OpenModalInfo>();
        foreach (var m in modals)
        {
            if (remaining.TryGetValue(m.Text, out var n) && n > 0) { remaining[m.Text] = n - 1; continue; }
            result.Add(m);
        }
        return result;
    }

    /// <summary>Xét một hộp tiến trình theo số. Chưa đủ số hoặc Success + Failed &lt; Total → InProgress (chờ tiếp), trừ khi
    /// chưa đủ số mà đã có chữ lỗi (không đọc nổi mà vẫn nói lỗi → chưa chắc ngay). Đã đủ số: Total khác
    /// <paramref name="expectedCount"/> (số SP đã tick) → chưa chắc; Total = Successfully, Failed = 0 → đã gửi; Failed = Total →
    /// thất bại cả lô; còn lại → thất bại một phần (không biết SP nào lỗi).</summary>
    public static ImportPostClickVerdict JudgeProgressBox(string? text, int? expectedCount = null, int shopCount = 1)
    {
        var counts = ParseProgressCounts(text);
        var status = GetStatus(counts);
        var shown = Trim(text);

        // Nhãn Total / Successfully / Failed lặp lại (vd hộp liệt kê số theo từng shop): không biết số nào là của cả lô → CHƯA CHẮC.
        if (counts.RepeatedLabel)
            return new(false, $"hộp tiến trình import có nhãn Total/Successfully/Failed xuất hiện nhiều hơn một lần, không biết số nào là của lô: \"{shown}\"");

        if (status == ImportBoxStatus.Running)
        {
            if (!counts.IsComplete && BoxReportsError(text))
                return new(false, $"hộp tiến trình import có chữ lỗi/thất bại nhưng chưa đọc được đủ số lượng: \"{shown}\"");
            return new(false, $"hộp tiến trình import đang chạy: \"{shown}\"", ImportVerdictKind.InProgress);
        }

        // Import nhiều shop một lần (k > 1): CHỈ nhận Total = số SP × k (đếm theo cặp SP×shop); Total = số SP thì chưa biết BigSeller đếm kiểu nào
        // (có thể chỉ một shop nhận) nên CHƯA CHẮC, không MarkDone. k = 1: Total phải bằng số SP như cũ. Successfully = Total và Failed = 0 xét bên dưới.
        if (expectedCount is { } expected)
        {
            if (shopCount > 1)
            {
                if (counts.Total == expected)
                    return new(false, $"hộp nhiều shop ({shopCount} shop) báo Total = số SP ({expected}), chưa biết BigSeller đếm kiểu nào — gửi nguyên văn hộp cho dev: \"{shown}\"");
                if ((long?)counts.Total != (long)expected * shopCount)
                    return new(false, $"hộp tiến trình import báo Total={counts.Total} khác số SP × {shopCount} shop ({(long)expected * shopCount}): \"{shown}\"");
            }
            else if (counts.Total != expected)
                return new(false, $"hộp tiến trình import báo Total={counts.Total} khác số SP đã tick ({expected}): \"{shown}\"");
        }

        return status switch
        {
            ImportBoxStatus.Succeeded => new(true, ""),
            ImportBoxStatus.AllFailed => new(false,
                $"BigSeller báo THẤT BẠI cả lô ({counts.Failed}/{counts.Total}): \"{shown}\"", ImportVerdictKind.ReportedFailure),
            _ => new(false,
                $"BigSeller báo thất bại MỘT PHẦN (thành công {counts.Success}, thất bại {counts.Failed}/{counts.Total}; không biết SP nào lỗi): \"{shown}\"",
                ImportVerdictKind.ReportedFailure),
        };
    }

    /// <summary>Đọc Total / Successfully / Failed (hoặc Tổng số / Thành công / Thất bại) từ nội dung hộp (đã gộp khoảng trắng). Số có thể
    /// có dấu phân cách nghìn ("1,234" hoặc "1.234"). Nhãn nào chưa có hoặc không có số liền sau thì null. Nhãn so sau <see cref="Fold"/>
    /// (không dấu, không phân biệt hoa thường).</summary>
    public static ImportBoxCounts ParseProgressCounts(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new(null, null, null);
        var t = Fold(text);
        var repeated = TotalWord.Matches(t).Count > 1 || SuccessWord.Matches(t).Count > 1 || FailedWord.Matches(t).Count > 1;
        return new(ReadCount(TotalLabel, t), ReadCount(SuccessLabel, t), ReadCount(FailedLabel, t), repeated);
    }

    /// <summary>Running khi chưa đủ ba số hoặc Success + Failed &lt; Total; sau đó: Failed = 0 và Success = Total → Succeeded;
    /// Success = 0 và Failed = Total → AllFailed; mọi tổ hợp còn lại (kể cả số vô lý) → PartialFailed.</summary>
    public static ImportBoxStatus GetStatus(ImportBoxCounts counts)
    {
        if (counts.Total is not { } total || counts.Success is not { } success || counts.Failed is not { } failed)
            return ImportBoxStatus.Running;
        if ((long)success + failed < total) return ImportBoxStatus.Running;
        if (failed == 0 && success == total) return ImportBoxStatus.Succeeded;
        if (success == 0 && failed == total) return ImportBoxStatus.AllFailed;
        return ImportBoxStatus.PartialFailed;
    }

    private static int? ReadCount(Regex label, string text)
    {
        var m = label.Match(text);
        if (!m.Success) return null;
        var digits = new string(m.Groups["n"].Value.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : null;
    }

    /// <summary>Hộp tiến trình import của BigSeller. Tiếng Anh ("Importing Shopee Total: 50 Importing... Finish"): chứa đồng thời
    /// "importing" và "finish". Tiếng Việt ("Đang import Shopee Tổng số: 50 Đang nhập.. Hoàn thành", chạy thật 03/11): chứa đồng thời
    /// "đang import" và "hoàn thành". So sau <see cref="Fold"/> (NFC, bỏ dấu, không phân biệt hoa thường). Chỉ có một trong hai
    /// của cùng một ngôn ngữ thì KHÔNG phải hộp này (hộp lạ).</summary>
    public static bool IsImportProgressBox(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = Fold(text);
        return (t.Contains("importing", StringComparison.Ordinal) && t.Contains("finish", StringComparison.Ordinal))
            || (t.Contains("dang import", StringComparison.Ordinal) && t.Contains("hoan thanh", StringComparison.Ordinal));
    }

    // Chuẩn hoá để so chữ giao diện: NFC → NFD bỏ dấu kết hợp, đ→d, gộp khoảng trắng, chữ thường (cùng quy tắc với
    // BigSellerDraftClearer.Normalize ở module UpdateProduct; Core không tham chiếu được module đó nên giữ bản riêng như
    // ImportShopMatcher / ProductLimitToastClassifier). Chỉ để SO SÁNH, không dùng để hiển thị/log.
    private static string Fold(string text)
    {
        var d = text.Normalize(NormalizationForm.FormC).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c is 'đ' or 'Đ' ? 'd' : c);
        }
        return Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC), @"\s+", " ").Trim().ToLowerInvariant();
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

    // Nhãn của hộp tiến trình + số liền sau (cho phép ':' '：' '=' và khoảng trắng; số có thể có dấu phân cách nghìn). Chạy trên chữ đã
    // Fold (không dấu, chữ thường): Anh Total / Success(fully) / Fail(ed); Việt "Tổng số" / "Thành công" / "Thất bại".
    // "Tổng số" có bằng chứng thật (hộp đang chạy 03/11: "Đang import Shopee Tổng số: 50 Đang nhập.. Hoàn thành"). "Thành công" và
    // "Thất bại" là GIẢ ĐỊNH (dịch tương ứng Successfully / Failed, CHƯA có mẫu hộp tiếng Việt ở trạng thái cuối): nếu BigSeller dùng chữ
    // khác thì hộp không bao giờ đủ ba số → hết 120s → "chưa chắc" (an toàn, không MarkDone). Log nguyên văn hộp mỗi lần đổi nội dung
    // (MonitorPostClickAsync) để lần sau có mẫu thật.
    private const string CountTail = @"\w*\s*[:：=]?\s*(?<n>\d+(?:[.,]\d+)*)";
    // Chỉ để ĐẾM số lần nhãn xuất hiện (không đòi có số liền sau).
    private static readonly Regex TotalWord = new(@"(?<![a-z])(?:total|tong so)", RegexOptions.Compiled);
    private static readonly Regex SuccessWord = new(@"(?<![a-z])(?:success|thanh cong)", RegexOptions.Compiled);
    private static readonly Regex FailedWord = new(@"(?<![a-z])(?:fail|that bai)", RegexOptions.Compiled);
    private static readonly Regex TotalLabel = new(@"(?<![a-z])(?:total|tong so)" + CountTail, RegexOptions.Compiled);
    private static readonly Regex SuccessLabel = new(@"(?<![a-z])(?:success|thanh cong)" + CountTail, RegexOptions.Compiled);
    private static readonly Regex FailedLabel = new(@"(?<![a-z])(?:fail|that bai)" + CountTail, RegexOptions.Compiled);
}