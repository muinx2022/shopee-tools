using System.Globalization;
using System.Text;

namespace Shopee.Upload.Import;

/// <summary>Chuẩn hoá chuỗi để so khớp ĐÚNG (không khớp mờ) — GĐ5c: hạ chữ, bỏ dấu tiếng Việt (cả <c>đ</c> → <c>d</c>), bỏ khoảng
/// trắng hai đầu, gộp khoảng trắng giữa (xuống dòng trong ô tiêu đề Excel coi như một dấu cách). Hàm thuần.</summary>
public static class ImportText
{
    public static string Key(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var decomposed = s.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var lastSpace = false;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(ch))
            {
                if (!lastSpace) sb.Append(' ');
                lastSpace = true;
                continue;
            }
            lastSpace = false;
            sb.Append(ch switch { 'đ' or 'Đ' => 'd', _ => char.ToLowerInvariant(ch) });
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public static bool SameKey(string? a, string? b) => Key(a) is { Length: > 0 } k && k == Key(b);
}
