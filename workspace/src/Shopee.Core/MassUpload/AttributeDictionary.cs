using System.Text.RegularExpressions;

namespace Shopee.Core.MassUpload;

/// <summary>
/// Từ điển (mã thuộc tính, giá trị tiếng Anh của <c>get_pc</c>) → giá trị tiếng Việt của bản mẫu nâng cao. JSON trả giá trị
/// EN (<c>Female</c>, <c>Elegant</c>) còn bản mẫu cần tên VN (<c>Nữ</c>, <c>Thanh lịch</c>) — plan "Kết quả GĐ0".
/// So khoá không phân biệt hoa thường. Bản mặc định nạp từ <c>AttributeDictionary.json</c> nhúng trong assembly (bổ sung
/// giá trị = sửa file đó). Ngoài cặp cố định còn có luật mẫu (vd <c>"N Months"</c> → <c>"N tháng"</c>).
/// </summary>
public sealed class AttributeDictionary
{
    private const string ResourceName = "Shopee.Core.MassUpload.AttributeDictionary.json";

    private static readonly Lazy<AttributeDictionary> LazyDefault = new(LoadEmbedded);

    private readonly Dictionary<long, Dictionary<string, string>> _values;
    private readonly List<(HashSet<long> AttrIds, Regex Match, string Replace)> _patterns;

    /// <summary>Từ điển gieo sẵn (file JSON nhúng).</summary>
    public static AttributeDictionary Default => LazyDefault.Value;

    /// <summary>Từ điển rỗng (mọi giá trị đều "chưa dịch được").</summary>
    public static AttributeDictionary Empty { get; } = new(new(), new());

    private AttributeDictionary(Dictionary<long, Dictionary<string, string>> values,
        List<(HashSet<long>, Regex, string)> patterns)
    {
        _values = values;
        _patterns = patterns;
    }

    /// <summary>Dịch MỘT giá trị (không tách ", "). Cặp cố định trước, rồi tới luật mẫu.</summary>
    public bool TryTranslate(long attrId, string englishValue, out string vietnamese)
    {
        var key = (englishValue ?? "").Trim();
        vietnamese = "";
        if (key.Length == 0) return false;
        if (_values.TryGetValue(attrId, out var map) && map.TryGetValue(key, out var vn))
        {
            vietnamese = vn;
            return true;
        }
        foreach (var (ids, match, replace) in _patterns)
        {
            if (ids.Count > 0 && !ids.Contains(attrId)) continue;
            if (!match.IsMatch(key)) continue;
            vietnamese = match.Replace(key, replace);
            return true;
        }
        return false;
    }

    /// <summary>Nạp từ JSON dạng <c>{"attributes":{"&lt;id&gt;":{"values":{"EN":"VN"}}},"patterns":[{"attrIds":[],
    /// "match":"regex","replace":"…"}]}</c>. NÉM <see cref="FormatException"/> khi sai dạng.</summary>
    public static AttributeDictionary Load(Stream json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var values = new Dictionary<long, Dictionary<string, string>>();
            if (doc.RootElement.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
                foreach (var a in attrs.EnumerateObject())
                {
                    if (!long.TryParse(a.Name, out var id)) throw new FormatException("mã thuộc tính không phải số: " + a.Name);
                    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (a.Value.TryGetProperty("values", out var vs) && vs.ValueKind == JsonValueKind.Object)
                        foreach (var v in vs.EnumerateObject())
                            map[v.Name.Trim()] = v.Value.GetString() ?? throw new FormatException($"giá trị rỗng ở {a.Name}/{v.Name}");
                    values[id] = map;
                }

            var patterns = new List<(HashSet<long>, Regex, string)>();
            if (doc.RootElement.TryGetProperty("patterns", out var ps) && ps.ValueKind == JsonValueKind.Array)
                foreach (var p in ps.EnumerateArray())
                {
                    var ids = new HashSet<long>();
                    if (p.TryGetProperty("attrIds", out var ai) && ai.ValueKind == JsonValueKind.Array)
                        foreach (var x in ai.EnumerateArray()) ids.Add(x.GetInt64());
                    var match = p.GetProperty("match").GetString() ?? throw new FormatException("luật mẫu thiếu match");
                    var replace = p.GetProperty("replace").GetString() ?? throw new FormatException("luật mẫu thiếu replace");
                    patterns.Add((ids, new Regex(match, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                        TimeSpan.FromMilliseconds(200)), replace));
                }
            return new AttributeDictionary(values, patterns);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            throw new FormatException("Từ điển thuộc tính sai dạng: " + ex.Message, ex);
        }
    }

    private static AttributeDictionary LoadEmbedded()
    {
        using var s = typeof(AttributeDictionary).Assembly.GetManifestResourceStream(ResourceName)
                      ?? throw new InvalidOperationException("Thiếu tài nguyên nhúng " + ResourceName);
        return Load(s);
    }
}
