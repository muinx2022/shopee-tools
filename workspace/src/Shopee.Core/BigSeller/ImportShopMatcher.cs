using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Shopee.Core.BigSeller;

/// <summary>Kết quả <see cref="ImportShopMatcher.PickMany"/>: chỉ số nhãn theo thứ tự tên shop, hoặc lý do không chọn được (Indices rỗng).</summary>
public sealed record ImportShopPick(IReadOnlyList<int> Indices, string? Error);

/// <summary>
/// Chọn ĐÚNG shop trong danh sách nhãn của modal "Import to Stores" (BigSeller). Trước đây so khớp bằng JS
/// <c>includes</c> rồi lấy phần tử đầu tiên khớp → "shop1" có thể chọn nhầm "shop10". Hàm thuần để test được;
/// JS chỉ đọc nhãn + tick theo CHỈ SỐ trả về từ đây.
/// </summary>
public static class ImportShopMatcher
{
    /// <summary>Không có nhãn nào khớp (hoặc tên shop rỗng).</summary>
    public const int NotFound = -1;
    /// <summary>Có ≥2 nhãn cùng khớp ở tầng đầu tiên có kết quả — không chắc chọn cái nào nên KHÔNG chọn.</summary>
    public const int Ambiguous = -2;

    // Hậu tố cuối nhãn dạng "(...)" hoặc "[...]" (vd "(Shopee)", "[VN]"), có thể nhiều cái liền nhau.
    private static readonly Regex TrailingBracketSuffix =
        new(@"(\s*(\([^()]*\)|\[[^\[\]]*\]))+\s*$", RegexOptions.Compiled);

    /// <summary>Trả về chỉ số nhãn trong <paramref name="labels"/>, <see cref="NotFound"/> hoặc <see cref="Ambiguous"/>.
    /// Ba tầng, tầng nào ra đúng 1 kết quả thì lấy, ra nhiều hơn 1 thì mơ hồ, không ra gì thì xuống tầng sau:
    /// (1) nhãn bằng tên shop sau chuẩn hoá (bỏ dấu, hạ chữ, gộp khoảng trắng); (2) bằng nhau theo bản compact
    /// (chỉ giữ chữ/số); (3) bỏ các hậu tố cuối nhãn dạng "(...)"/"[...]" rồi so bằng nhau theo cả hai bản trên.
    /// Ngoài ra KHÔNG gì được coi là khớp — nhãn chỉ "chứa" tên (vd "Sully Store 2" với "Sully Store") là không khớp.</summary>
    public static int Pick(IReadOnlyList<string> labels, string shopName)
    {
        var target = Normalize(shopName);
        if (target.Length == 0) return NotFound;
        var targetCompact = Compact(target);

        var normalized = new string[labels.Count];
        for (var i = 0; i < labels.Count; i++) normalized[i] = Normalize(labels[i]);

        var tier1 = Collect(normalized, n => n == target);
        if (tier1.Count > 0) return Resolve(tier1);

        // Tên toàn ký tự đặc biệt → compact rỗng → bỏ so khớp compact (nếu không "---" sẽ bằng mọi nhãn chỉ có ký tự đặc biệt).
        if (targetCompact.Length > 0)
        {
            var tier2 = Collect(normalized, n => Compact(n) == targetCompact);
            if (tier2.Count > 0) return Resolve(tier2);
        }

        var tier3 = Collect(normalized, n =>
        {
            var stripped = StripBracketSuffix(n);
            return stripped == target || (targetCompact.Length > 0 && Compact(stripped) == targetCompact);
        });
        return tier3.Count > 0 ? Resolve(tier3) : NotFound;
    }

    /// <summary>Chọn NHIỀU shop một lần (import nhiều shop trong một lô): mỗi tên qua <see cref="Pick"/>. Chỉ thành công khi MỌI tên ra đúng
    /// một nhãn và hai tên không dùng chung một nhãn. Thiếu hoặc mơ hồ bất kỳ tên nào thì <see cref="ImportShopPick.Error"/> khác null và
    /// <see cref="ImportShopPick.Indices"/> rỗng — caller KHÔNG được import lô đó. <see cref="ImportShopPick.Indices"/> theo thứ tự
    /// <paramref name="shopNames"/>.</summary>
    public static ImportShopPick PickMany(IReadOnlyList<string> labels, IReadOnlyList<string> shopNames)
    {
        if (shopNames.Count == 0) return new([], "Không có shop nào để chọn.");
        var indices = new List<int>(shopNames.Count);
        var problems = new List<string>();
        for (var i = 0; i < shopNames.Count; i++)
        {
            var index = Pick(labels, shopNames[i]);
            if (index == NotFound) { problems.Add($"không tìm thấy shop '{shopNames[i]}'"); continue; }
            if (index == Ambiguous) { problems.Add($"nhiều nhãn cùng khớp '{shopNames[i]}'"); continue; }
            var clash = indices.IndexOf(index);
            if (clash >= 0) { problems.Add($"'{shopNames[clash]}' và '{shopNames[i]}' cùng khớp nhãn '{labels[index]}'"); continue; }
            indices.Add(index);
        }
        return problems.Count > 0 ? new([], string.Join("; ", problems)) : new(indices, null);
    }

    /// <summary>Tập chỉ số đang được tick (<paramref name="checkedFlags"/>[i] = true) có BẰNG ĐÚNG tập <paramref name="expectedIndices"/> không
    /// (không thiếu, không dư). Dùng để đọc lại hộp chọn shop trước khi bấm Import.</summary>
    public static bool CheckedSetMatches(IReadOnlyList<bool> checkedFlags, IReadOnlyCollection<int> expectedIndices)
    {
        var expected = new HashSet<int>(expectedIndices);
        for (var i = 0; i < checkedFlags.Count; i++)
            if (checkedFlags[i] != expected.Contains(i)) return false;
        return expected.All(i => i >= 0 && i < checkedFlags.Count);
    }

    private static int Resolve(List<int> hits) => hits.Count == 1 ? hits[0] : Ambiguous;

    private static List<int> Collect(string[] normalized, Func<string, bool> predicate)
    {
        var hits = new List<int>();
        for (var i = 0; i < normalized.Length; i++)
            if (predicate(normalized[i])) hits.Add(i);
        return hits;
    }

    // Chuẩn hoá nhãn/tên: bỏ dấu (NFD + bỏ dấu kết hợp), đ→d, gộp khoảng trắng, trim, chữ thường.
    private static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch is 'đ' or 'Đ' ? 'd' : ch);
        }
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim().ToLowerInvariant();
    }

    // Bản compact: chỉ giữ chữ và số (mọi chữ cái Unicode, không chỉ a-z — nếu không "店铺1" và "小店1" cùng thành "1")
    // của chuỗi ĐÃ chuẩn hoá.
    private static string Compact(string normalized)
    {
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }

    // Bỏ các hậu tố cuối "(...)"/"[...]" khỏi nhãn ĐÃ chuẩn hoá ("sully store (shopee)" → "sully store").
    private static string StripBracketSuffix(string normalized) =>
        TrailingBracketSuffix.Replace(normalized, "").Trim();
}
