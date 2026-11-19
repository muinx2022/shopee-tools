namespace Shopee.Core.MassUpload;

/// <summary>Kết quả đổi phân loại của một SP sang mã chuẩn: <see cref="Converted"/> / <see cref="Total"/> giá trị (lựa chọn) đã ghi
/// bằng mã.</summary>
public readonly record struct VariationStandardStats(int Converted, int Total);

/// <summary>
/// Danh sách phân loại chuẩn của Shopee (plan GĐ8b): theo ngành cấp 1, mỗi nhóm chuẩn ("Màu sắc", "Size", "Giới tính") có mã nhóm và
/// các giá trị <c>[nhóm phụ, tên, mã]</c>. Cột phân loại của file Đăng Hàng Loạt nhận cả mã lẫn chữ, trộn được (đã thử thật
/// 2026-11-19), nên phân loại khớp chuẩn thì ghi MÃ (Shopee hiện đúng tên chuẩn), còn lại giữ chữ.
/// <list type="bullet">
/// <item>So khớp ĐÚNG sau chuẩn hoá: làm sạch + NFC (<see cref="MassUploadText.Clean"/>), gộp khoảng trắng, trim, không phân biệt hoa
/// thường. Không khớp gần đúng ("Dài váy 90cm" không thành "90 cm").</item>
/// <item>Tên nhóm không khớp nhóm chuẩn → cả nhóm giữ chữ. Tên nhóm khớp mà KHÔNG giá trị nào ra mã → tên nhóm cũng giữ chữ; chỉ
/// ghi mã nhóm khi có ít nhất một giá trị ra mã.</item>
/// <item>Tên giá trị có ở nhiều nhóm phụ với mã khác nhau ("16" ở cả US lẫn EU) → giữ chữ, không đoán.</item>
/// <item>Sau khi đổi các giá trị trong nhóm phải vẫn khác nhau: hai chữ khác nhau ra cùng một mã, hoặc mã trùng chữ của giá trị khác →
/// giữ chữ cả hai.</item>
/// </list>
/// Bản mặc định nạp từ <c>VariationStandard.json</c> nhúng trong assembly (hiện chỉ có ngành 100017 — Thời Trang Nữ).
/// </summary>
public sealed class VariationStandard
{
    private const string ResourceName = "Shopee.Core.MassUpload.VariationStandard.json";

    private static readonly Lazy<VariationStandard> LazyDefault = new(LoadEmbedded);

    /// <summary>Nhóm chuẩn: mã nhóm + tên giá trị (khoá đã chuẩn hoá) → mã; null = tên mơ hồ (trùng nhiều nhóm phụ, khác mã).</summary>
    private sealed record Group(long Id, Dictionary<string, long?> Values);

    /// <summary>Ngành cấp 1 → tên nhóm (khoá đã chuẩn hoá) → nhóm.</summary>
    private readonly Dictionary<long, Dictionary<string, Group>> _byTop;

    private VariationStandard(Dictionary<long, Dictionary<string, Group>> byTop) => _byTop = byTop;

    /// <summary>Danh sách chuẩn nhúng sẵn.</summary>
    public static VariationStandard Default => LazyDefault.Value;

    /// <summary>Không có danh sách nào (mọi phân loại giữ chữ).</summary>
    public static VariationStandard Empty { get; } = new([]);

    /// <summary>Có danh sách chuẩn cho ngành cấp 1 này không.</summary>
    public bool Covers(long topCategoryId) => _byTop.ContainsKey(topCategoryId);

    /// <summary>Đổi tên nhóm + các lựa chọn (đã cắt / chống trùng) của SP thuộc ngành cấp 1 <paramref name="topCategoryId"/> sang mã
    /// theo luật ở trên — TẠI CHỖ, giữ nguyên thứ tự (chỉ số lựa chọn của các dòng và ảnh theo lựa chọn không đổi). Ngành không có
    /// danh sách → không đổi gì, trả 0/tổng.</summary>
    public VariationStandardStats Apply(long topCategoryId, List<string> tierNames, List<List<string>> optionNames)
    {
        var total = optionNames.Sum(o => o.Count);
        if (!_byTop.TryGetValue(topCategoryId, out var groups)) return new VariationStandardStats(0, total);
        var converted = 0;
        var usedGroups = new HashSet<long>();
        for (var t = 0; t < tierNames.Count && t < optionNames.Count; t++)
        {
            // Hai nhóm cùng khớp một nhóm chuẩn → nhóm sau giữ chữ (Shopee không nhận 2 nhóm cùng mã).
            if (!groups.TryGetValue(Key(tierNames[t]), out var group) || usedGroups.Contains(group.Id)) continue;
            var options = optionNames[t];
            var codes = options.Select(o => group.Values.TryGetValue(Key(o), out var id) ? id : null).ToList();
            // Hai chữ khác nhau ra cùng một mã → giữ chữ cả hai.
            var dupCodes = codes.Where(c => c is not null).GroupBy(c => c).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
            for (var i = 0; i < codes.Count; i++)
                if (codes[i] is { } c && dupCodes.Contains(c)) codes[i] = null;
            // Mã trùng đúng chữ của một lựa chọn khác vẫn giữ chữ → giữ chữ cả lựa chọn đó (giá trị trong nhóm phải khác nhau).
            var keptTexts = options.Where((_, i) => codes[i] is null).Select(Key).ToHashSet(StringComparer.Ordinal);
            var inGroup = 0;
            for (var i = 0; i < codes.Count; i++)
            {
                if (codes[i] is not { } c || keptTexts.Contains(Key(Code(c)))) continue;
                options[i] = Code(c);
                inGroup++;
            }
            // Chỉ ghi mã nhóm khi có ÍT NHẤT một giá trị ra mã (như ca đã thử thật). Không giá trị nào khớp → giữ chữ cả tên nhóm
            // (tổ hợp "mã nhóm + toàn chữ" chưa thử trên Shopee).
            if (inGroup == 0) continue;
            tierNames[t] = Code(group.Id);
            usedGroups.Add(group.Id);
            converted += inGroup;
        }
        return new VariationStandardStats(converted, total);
    }

    /// <summary>Khoá so: làm sạch + NFC, gộp khoảng trắng, trim, chữ thường.</summary>
    internal static string Key(string? s) =>
        string.Join(' ', MassUploadText.Clean(s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    private static string Code(long id) => id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Nạp JSON dạng <c>{"topCategory":100017,"groups":[{"name":"Màu sắc","id":101054,"values":[["nhóm phụ","Đen",6258]]}]}</c>
    /// (một ngành) hoặc mảng các object đó. NÉM <see cref="FormatException"/> khi sai dạng.</summary>
    public static VariationStandard Load(Stream json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var byTop = new Dictionary<long, Dictionary<string, Group>>();
            var roots = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().ToList()
                : [doc.RootElement];
            foreach (var root in roots)
            {
                var top = root.GetProperty("topCategory").GetInt64();
                if (!byTop.TryGetValue(top, out var groups)) byTop[top] = groups = new Dictionary<string, Group>(StringComparer.Ordinal);
                foreach (var g in root.GetProperty("groups").EnumerateArray())
                {
                    var name = g.GetProperty("name").GetString() ?? throw new FormatException("nhóm thiếu tên");
                    var values = new Dictionary<string, long?>(StringComparer.Ordinal);
                    foreach (var v in g.GetProperty("values").EnumerateArray())
                    {
                        var key = Key(v[1].GetString());
                        var id = v[2].GetInt64();
                        if (key.Length == 0) continue;
                        // Cùng tên ở nhiều nhóm phụ: cùng mã thì giữ, khác mã thì đánh dấu mơ hồ (null).
                        values[key] = values.TryGetValue(key, out var old) && old != id ? null : id;
                    }
                    groups[Key(name)] = new Group(g.GetProperty("id").GetInt64(), values);
                }
            }
            return new VariationStandard(byTop);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException)
        {
            throw new FormatException("Danh sách phân loại chuẩn sai dạng: " + ex.Message, ex);
        }
    }

    private static VariationStandard LoadEmbedded()
    {
        using var s = typeof(VariationStandard).Assembly.GetManifestResourceStream(ResourceName)
                      ?? throw new InvalidOperationException("Thiếu tài nguyên nhúng " + ResourceName);
        return Load(s);
    }
}
