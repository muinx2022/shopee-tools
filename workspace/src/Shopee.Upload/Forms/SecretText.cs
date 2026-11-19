namespace Shopee.Upload.Forms;

/// <summary>
/// Che bí mật (key Kiot, mật khẩu proxy, mật khẩu Shopee / email) — hàm thuần.
/// <list type="bullet">
/// <item><see cref="Mask"/>: nhãn cạnh ô nhập — tối đa 4 ký tự cuối, không quá nửa độ dài; ≤ 4 ký tự thì không hiện ký tự nào (plan
/// GĐ5a mục 5, sửa ở GĐ5c). CHỈ dùng cho nhãn trên form.</item>
/// <item><see cref="Redact"/>: chuỗi sắp vào log / thanh trạng thái / câu lỗi — bí mật bị che HẲN (<see cref="RedactedMark"/>), không
/// để lộ cả 4 ký tự cuối (GĐ5b vòng 2), cùng ngưỡng độ dài với Core (<c>SellerLoginText</c>).</item>
/// </list>
/// </summary>
public static class SecretText
{
    /// <summary>Bí mật ngắn hơn mức này không đem đi thay trong log (thay "ab" sẽ băm nát mọi dòng log). Bằng
    /// <see cref="Shopee.Core.MassUpload.Seller.Login.SellerLoginText.MinScrubLength"/> của Core.</summary>
    public const int MinRedactLength = Shopee.Core.MassUpload.Seller.Login.SellerLoginText.MinScrubLength;

    /// <summary>Thay bí mật trong log — cùng dấu của Core.</summary>
    public const string RedactedMark = Shopee.Core.MassUpload.Seller.Login.SellerLoginText.Mask;

    /// <summary>Nhãn của ô đã nhập mà quá ngắn để hiện đuôi (≤ 4 ký tự: hiện đuôi là hiện nguyên bí mật).</summary>
    public const string ShortMask = "(đã nhập)";

    /// <summary><c>…abcd</c>; rỗng → <paramref name="empty"/>; ≤ 4 ký tự → <see cref="ShortMask"/>. Dài hơn: tối đa 4 ký tự cuối
    /// nhưng KHÔNG quá một nửa độ dài (mật khẩu 5 ký tự chỉ hiện 2) — GĐ5c. Chỉ cho nhãn cạnh ô nhập.</summary>
    public static string Mask(string? secret, string empty = "(chưa nhập)")
    {
        var s = secret?.Trim() ?? "";
        if (s.Length == 0) return empty;
        if (s.Length <= 4) return ShortMask;
        return "…" + s[^Math.Min(4, s.Length / 2)..];
    }

    /// <summary>Thay mọi lần xuất hiện của từng bí mật trong <paramref name="text"/> bằng <see cref="RedactedMark"/>. Bí mật dài xử
    /// lý trước (một key có thể chứa key khác).</summary>
    public static string Redact(string text, IEnumerable<string?> secrets)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (var s in secrets
                     .Select(x => x?.Trim())
                     .Where(x => x is { Length: >= MinRedactLength })
                     .Distinct(StringComparer.Ordinal)
                     .OrderByDescending(x => x!.Length))
        {
            text = text.Replace(s!, RedactedMark, StringComparison.Ordinal);
        }
        return text;
    }
}
