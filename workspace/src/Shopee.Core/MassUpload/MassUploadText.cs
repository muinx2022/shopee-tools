namespace Shopee.Core.MassUpload;

/// <summary>
/// Làm sạch chữ đưa vào file Đăng Hàng Loạt — MỘT luật duy nhất, dùng ở reader (lúc đọc JSON), mapper (trước khi kiểm độ
/// dài) và writer (<see cref="MassUploadTemplateWriter"/> gọi trước khi escape XML). Chữ đã sạch đi qua lần nữa không đổi
/// (idempotent), nên độ dài mapper đo khớp đúng chữ writer ghi ra; còn SP dựng tay không qua mapper vẫn được writer làm sạch.
/// </summary>
internal static class MassUploadText
{
    /// <summary>Bỏ ký tự không hợp lệ trong XML 1.0 (điều khiển &lt; 0x20 trừ tab / xuống dòng, surrogate lẻ, U+FFFE/U+FFFF),
    /// CR / CRLF → LF, rồi chuẩn hoá NFC.</summary>
    public static string Clean(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        StringBuilder? sb = null;
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            var keep = true;
            string? replace = null;
            if (char.IsHighSurrogate(ch))
            {
                if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    sb?.Append(ch).Append(s[i + 1]);
                    i++;
                    continue;
                }
                keep = false;
            }
            else if (char.IsLowSurrogate(ch)) keep = false;
            else if (ch == '\r') replace = i + 1 < s.Length && s[i + 1] == '\n' ? "" : "\n";
            else if (ch is < ' ' and not ('\t' or '\n') or '￾' or '￿') keep = false;

            if (keep && replace is null)
            {
                sb?.Append(ch);
                continue;
            }
            sb ??= new StringBuilder(s, 0, i, s.Length);
            if (replace is not null) sb.Append(replace);
        }
        var result = sb?.ToString() ?? s;
        return result.IsNormalized(NormalizationForm.FormC) ? result : result.Normalize(NormalizationForm.FormC);
    }
}
