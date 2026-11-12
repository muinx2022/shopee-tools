using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XuLyDonShopee.Core.Services;

/// <summary>
/// Kết quả đọc số lượng sản phẩm hiện có và hạn mức sản phẩm của shop từ trang "Tất cả sản phẩm".
/// </summary>
public sealed record KetQuaDocSoLuongSp(
    int? TongSoSp,
    int? GioiHanSp,
    string? RawText = null,
    bool TabTatCa = false);

/// <summary>
/// Parser bóc tách số lượng sản phẩm và hạn mức (định dạng xxx / yyy).
/// </summary>
public static class SanPhamParser
{
    private static readonly Regex QuotaRegex = new(
        @"(?<!\d)(\d+(?:[.,]\d+)*)\s*\/\s*(\d+(?:[.,]\d+)*)(?!\d)",
        RegexOptions.Compiled);

    /// <summary>
    /// Bóc tách số lượng hiện có và hạn mức từ chuỗi text (ví dụ: "1.036 / 3.000", "987 / 1000", "1036/3000").
    /// </summary>
    public static (int? TongSp, int? GioiHanSp) TachSoVaGioiHan(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, null);
        }

        var m = QuotaRegex.Match(text);
        if (!m.Success)
        {
            return (null, null);
        }

        var s1 = LamSachSo(m.Groups[1].Value);
        var s2 = LamSachSo(m.Groups[2].Value);

        int? n1 = int.TryParse(s1, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v1) ? v1 : null;
        int? n2 = int.TryParse(s2, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v2) ? v2 : null;

        return (n1, n2);
    }

    private static string LamSachSo(string s) =>
        s.Replace(".", "").Replace(",", "").Trim();

    /// <summary>
    /// Parse JSON do extension gửi về từ action <c>readProductCount</c>.
    /// </summary>
    public static KetQuaDocSoLuongSp ParseKetQua(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new KetQuaDocSoLuongSp(null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            int? tongSp = null;
            if (root.TryGetProperty("tongSp", out var p1) && p1.ValueKind == JsonValueKind.Number)
            {
                tongSp = p1.GetInt32();
            }

            int? gioiHanSp = null;
            if (root.TryGetProperty("gioiHanSp", out var p2) && p2.ValueKind == JsonValueKind.Number)
            {
                gioiHanSp = p2.GetInt32();
            }

            string? rawText = null;
            if (root.TryGetProperty("rawText", out var pr) && pr.ValueKind == JsonValueKind.String)
            {
                rawText = pr.GetString();
            }

            bool tabTatCa = false;
            if (root.TryGetProperty("tabTatCa", out var pt) && (pt.ValueKind == JsonValueKind.True || pt.ValueKind == JsonValueKind.False))
            {
                tabTatCa = pt.GetBoolean();
            }

            // Fallback: nếu extension chưa parse số nhưng có chuỗi rawText
            if ((!tongSp.HasValue || !gioiHanSp.HasValue) && !string.IsNullOrWhiteSpace(rawText))
            {
                var (t, g) = TachSoVaGioiHan(rawText);
                tongSp ??= t;
                gioiHanSp ??= g;
            }

            return new KetQuaDocSoLuongSp(tongSp, gioiHanSp, rawText, tabTatCa);
        }
        catch (JsonException)
        {
            var (t, g) = TachSoVaGioiHan(json);
            return new KetQuaDocSoLuongSp(t, g, json);
        }
    }
}
