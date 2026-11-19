using System.IO.Compression;

namespace Shopee.Core.MassUpload;

/// <summary>Loại bản mẫu theo ô A2: <c>basic</c> (không có cột thương hiệu/thuộc tính) hay <c>advanced</c> (theo ngành
/// cấp 1).</summary>
public enum TemplateKind { Basic, Advanced }

/// <summary>Mức của một cột thuộc tính với một ngành lá, theo sheet ẩn <c>HiddenCatProps</c>.</summary>
public enum AttributeRequirement { None, Irrelevant, Optional, Mandatory }

/// <summary>Kiểu nhập của cột thuộc tính, theo dòng 6 của sheet chính ("[Kiểu nhập]…").</summary>
public enum AttributeInputKind { Unknown, List, Custom }

/// <summary>Một cột của sheet "Bản đăng tải".</summary>
/// <param name="Code">Mã máy đọc ở dòng 1, phần trước <c>|</c> (vd <c>ps_price</c>).</param>
/// <param name="Required">Cờ thứ 2 ở dòng 1 (<c>ps_price|1|1</c> → true).</param>
/// <param name="Style">Style mặc định của cột (<c>&lt;col style&gt;</c>), null nếu không có.</param>
/// <param name="IsNumeric">Style của cột là kiểu số → ghi số bằng <c>&lt;v&gt;</c>, còn lại ghi chuỗi.</param>
/// <param name="Title">Tên cột ở dòng 3.</param>
/// <param name="MultiValue">Dòng 6 cho nhập tối đa 5 giá trị ngăn bằng <c>;</c>.</param>
public sealed record TemplateColumn(
    string Letter, int Index, string Code, bool Required, int? Style, bool IsNumeric, string Title,
    AttributeInputKind InputKind, bool MultiValue)
{
    public const string AttributePrefix = "ps_product_global_attribute.";

    public bool IsAttribute => Code.StartsWith(AttributePrefix, StringComparison.Ordinal);

    /// <summary>Mã thuộc tính của cột <c>ps_product_global_attribute.&lt;id&gt;</c>, null với cột khác.</summary>
    public long? AttributeId =>
        IsAttribute && long.TryParse(Code.AsSpan(AttributePrefix.Length), out var id) ? id : null;
}

/// <summary>
/// Cấu trúc file mẫu Đăng Hàng Loạt của MỘT shop (plan mục 2 + "Kết quả GĐ0"): cột theo mã ở dòng 1, loại / hash / ngành
/// cấp 1 / shop_id ở dòng 2, style số của từng cột, và với bản nâng cao: mức thuộc tính theo ngành lá
/// (<c>HiddenCatProps</c>, căn theo CÙNG chữ cái cột với sheet chính) và giá trị hợp lệ của thuộc tính kiểu danh sách
/// (<c>HiddenAttr</c>, khoá <c>"&lt;mã cột&gt;&lt;id lá&gt;-&lt;đường dẫn&gt;"</c>).
/// </summary>
public sealed class TemplateSchema
{
    public const string MainSheetName = "Bản đăng tải";
    public const string CatPropsSheetName = "HiddenCatProps";
    public const string AttrValuesSheetName = "HiddenAttr";

    /// <summary>Sheet hiện "Ánh xạ giá trị thuộc tính": theo từng ngành lá × thuộc tính, giá trị hợp lệ (thuộc tính chọn theo danh sách)
    /// hoặc giá trị đề xuất (thuộc tính "đề xuất hoặc tùy chỉnh"). Dòng 1 = mã cột thuộc tính, dòng 3 = đường dẫn ngành (gộp ô theo
    /// khối), dòng 7 trở đi = các giá trị.</summary>
    public const string ValueMappingSheetName = "Ánh xạ giá trị thuộc tính";

    /// <summary>Dòng dữ liệu đầu tiên; dòng 1–6 là phần đầu của Shopee, giữ nguyên.</summary>
    public const int DataStartRow = 7;

    private readonly Dictionary<string, TemplateColumn> _byCode;
    private readonly Dictionary<long, Dictionary<string, AttributeRequirement>> _requirements;
    private readonly Dictionary<(long Leaf, string Code), List<string>> _listValues;
    private readonly Dictionary<long, string> _categoryEntries;
    private readonly Dictionary<(long Leaf, string Code), List<string>> _mappedValues;

    private TemplateSchema(TemplateKind kind, string checksum, long? topCategoryId, string shopId, string sheetPart,
        List<TemplateColumn> columns, Dictionary<long, string> categoryEntries,
        Dictionary<long, Dictionary<string, AttributeRequirement>> requirements,
        Dictionary<(long, string), List<string>> listValues, Dictionary<(long, string), List<string>> mappedValues)
    {
        Kind = kind;
        Checksum = checksum;
        TopCategoryId = topCategoryId;
        ShopId = shopId;
        SheetPart = sheetPart;
        Columns = columns;
        _byCode = columns.GroupBy(c => c.Code, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        _categoryEntries = categoryEntries;
        _requirements = requirements;
        _listValues = listValues;
        _mappedValues = mappedValues;
    }

    public TemplateKind Kind { get; }

    /// <summary>Ô B2 (hash 32 hex Shopee dùng kiểm mẫu) — giữ nguyên khi ghi.</summary>
    public string Checksum { get; }

    /// <summary>Ngành cấp 1 của bản nâng cao (ô C2); null với bản basic (C2 = 0).</summary>
    public long? TopCategoryId { get; }

    /// <summary>Ô D2.</summary>
    public string ShopId { get; }

    /// <summary>Part XML của sheet "Bản đăng tải" trong zip (vd <c>xl/worksheets/sheet2.xml</c>).</summary>
    public string SheetPart { get; }

    /// <summary>Các cột theo thứ tự chữ cái.</summary>
    public IReadOnlyList<TemplateColumn> Columns { get; }

    /// <summary>Ngành lá có trong <c>HiddenCatProps</c>.</summary>
    public IReadOnlyCollection<long> LeafCategoryIds => _categoryEntries.Keys;

    public bool TryGetColumn(string code, out TemplateColumn column) => _byCode.TryGetValue(code, out column!);

    public bool HasCategory(long leafId) => _categoryEntries.ContainsKey(leafId);

    /// <summary>Chuỗi ngành của <c>HiddenCatProps</c> cột A (vd <c>"100026-Phụ Kiện Thời Trang/Vòng tay &amp; Lắc tay"</c>).</summary>
    public string? CategoryEntry(long leafId) => _categoryEntries.GetValueOrDefault(leafId);

    public AttributeRequirement GetRequirement(long leafId, string code) =>
        _requirements.TryGetValue(leafId, out var m) && m.TryGetValue(code, out var r) ? r : AttributeRequirement.None;

    /// <summary>Giá trị hợp lệ (theo thứ tự trong <c>HiddenAttr</c>) của cột thuộc tính kiểu danh sách cho ngành lá; rỗng
    /// nếu không có.</summary>
    public IReadOnlyList<string> GetListValues(long leafId, string code) =>
        _listValues.TryGetValue((leafId, code), out var l) ? l : [];

    /// <summary>Giá trị (hợp lệ hoặc đề xuất) của thuộc tính cho ngành lá theo sheet <see cref="ValueMappingSheetName"/>, theo thứ tự
    /// trong sheet; rỗng nếu không có.</summary>
    public IReadOnlyList<string> GetMappedValues(long leafId, string code) =>
        _mappedValues.TryGetValue((leafId, code), out var l) ? l : [];

    public static TemplateSchema Load(string path)
    {
        using var fs = File.OpenRead(path);
        return Load(fs);
    }

    /// <summary>Đọc cấu trúc file mẫu. NÉM <see cref="InvalidDataException"/> khi không phải file mẫu Đăng Hàng Loạt
    /// (thiếu sheet, dòng 1/2 sai dạng).</summary>
    public static TemplateSchema Load(Stream xlsx)
    {
        using var zip = new ZipArchive(xlsx, ZipArchiveMode.Read, leaveOpen: true);
        var sst = XlsxParts.ReadSharedStrings(zip);
        var numericStyles = XlsxParts.ReadNumericStyles(zip);

        var part = XlsxParts.FindSheetPart(zip, MainSheetName)
                   ?? throw new InvalidDataException($"File mẫu không có sheet \"{MainSheetName}\"");
        XlsxSheetData main;
        using (var s = OpenPart(zip, part)) main = XlsxParts.ReadSheet(s, sst, maxRow: DataStartRow - 1);

        var kindText = (main.Get(2, 1) ?? "").Trim();
        var kind = kindText switch
        {
            "basic" => TemplateKind.Basic,
            "advanced" => TemplateKind.Advanced,
            _ => throw new InvalidDataException($"Ô A2 của file mẫu không phải basic/advanced: \"{kindText}\""),
        };
        long? top = long.TryParse(main.Get(2, 3), out var t) && t > 0 ? t : null;
        if (kind == TemplateKind.Advanced && top is null)
            throw new InvalidDataException("Bản mẫu nâng cao thiếu mã ngành cấp 1 ở ô C2");

        var columns = new List<TemplateColumn>();
        if (main.Rows.TryGetValue(1, out var row1))
            foreach (var cell in row1.Values.OrderBy(c => c.Column))
            {
                var raw = cell.Value ?? "";
                if (raw.Length == 0) continue;
                var parts = raw.Split('|');
                var style = main.ColumnStyles.TryGetValue(cell.Column, out var st) ? st : (int?)null;
                var hint = main.Get(6, cell.Column) ?? "";
                columns.Add(new TemplateColumn(
                    XlsxParts.ColumnLetter(cell.Column), cell.Column, parts[0].Trim(),
                    parts.Length > 1 && parts[1].Trim() == "1",
                    style, style is int sv && numericStyles.Contains(sv),
                    (main.Get(3, cell.Column) ?? "").Trim(),
                    InputKindOf(hint), hint.Contains("semicolon", StringComparison.OrdinalIgnoreCase)));
            }
        if (columns.Count == 0) throw new InvalidDataException("Dòng 1 của file mẫu không có mã cột");

        var entries = new Dictionary<long, string>();
        var requirements = new Dictionary<long, Dictionary<string, AttributeRequirement>>();
        var listValues = new Dictionary<(long, string), List<string>>();
        if (XlsxParts.FindSheetPart(zip, CatPropsSheetName) is { } catPart)
        {
            XlsxSheetData cat;
            using (var s = OpenPart(zip, catPart)) cat = XlsxParts.ReadSheet(s, sst);
            var codeByColumn = columns.ToDictionary(c => c.Index, c => c.Code);
            foreach (var (rowNo, cells) in cat.Rows)
            {
                if (rowNo < DataStartRow || !cells.TryGetValue(1, out var a) || a.Value is not { } entry) continue;
                var dash = entry.IndexOf('-');
                if (dash <= 0 || !long.TryParse(entry.AsSpan(0, dash), out var leaf)) continue;
                entries[leaf] = entry;
                var map = new Dictionary<string, AttributeRequirement>(StringComparer.Ordinal);
                foreach (var c in cells.Values)
                    if (c.Column > 1 && codeByColumn.TryGetValue(c.Column, out var code) && RequirementOf(c.Value) is { } req)
                        map[code] = req;
                requirements[leaf] = map;
            }

            if (XlsxParts.FindSheetPart(zip, AttrValuesSheetName) is { } attrPart)
            {
                // Khoá cột A = mã cột thuộc tính + chuỗi ngành của HiddenCatProps (không có dấu ngăn) → tra bằng bảng
                // dựng sẵn mọi tổ hợp, không tự tách số.
                var keyMap = new Dictionary<string, (long, string)>(StringComparer.Ordinal);
                foreach (var col in columns.Where(c => c.IsAttribute))
                    foreach (var (leaf, entry) in entries)
                        keyMap[col.Code + entry] = (leaf, col.Code);
                XlsxSheetData attr;
                using (var s = OpenPart(zip, attrPart)) attr = XlsxParts.ReadSheet(s, sst);
                foreach (var cells in attr.Rows.Values)
                {
                    if (!cells.TryGetValue(1, out var a) || a.Value is not { } key || !keyMap.TryGetValue(key, out var k)) continue;
                    if (!cells.TryGetValue(2, out var b) || string.IsNullOrWhiteSpace(b.Value)) continue;
                    if (!listValues.TryGetValue(k, out var list)) listValues[k] = list = [];
                    list.Add(b.Value!);
                }
            }
        }

        var mapped = ReadValueMapping(zip, sst, entries);
        return new TemplateSchema(kind, (main.Get(2, 2) ?? "").Trim(), top, (main.Get(2, 4) ?? "").Trim(), part,
            columns, entries, requirements, listValues, mapped);
    }

    /// <summary>Đọc sheet <see cref="ValueMappingSheetName"/>: mỗi cột = (ngành ở dòng 3, kéo dài tới cột có ngành kế tiếp; mã thuộc
    /// tính ở dòng 1) → các giá trị từ dòng 7. Ngành ghi bằng đường dẫn (không có mã) → đổi sang ngành lá qua chuỗi
    /// <c>"&lt;id&gt;-&lt;đường dẫn&gt;"</c> của <c>HiddenCatProps</c>. Không có sheet / không khớp ngành → bỏ qua.</summary>
    private static Dictionary<(long, string), List<string>> ReadValueMapping(ZipArchive zip, List<string> sst,
        Dictionary<long, string> entries)
    {
        var result = new Dictionary<(long, string), List<string>>();
        if (entries.Count == 0 || XlsxParts.FindSheetPart(zip, ValueMappingSheetName) is not { } part) return result;
        var leafByPath = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (leaf, entry) in entries)
        {
            var dash = entry.IndexOf('-');
            if (dash > 0) leafByPath[MassUploadText.Clean(entry[(dash + 1)..]).Trim()] = leaf;
        }
        XlsxSheetData sheet;
        using (var s = OpenPart(zip, part)) sheet = XlsxParts.ReadSheet(s, sst);
        if (!sheet.Rows.TryGetValue(1, out var codes)) return result;
        long? current = null;
        foreach (var col in codes.Keys.Order())
        {
            if (col == 1) continue;
            var path = sheet.Get(3, col);
            if (!string.IsNullOrWhiteSpace(path))
                current = leafByPath.TryGetValue(MassUploadText.Clean(path).Trim(), out var leaf) ? leaf : null;
            var code = (codes[col].Value ?? "").Split('|')[0].Trim();
            if (current is not long l || code.Length == 0) continue;
            var values = new List<string>();
            foreach (var (rowNo, cells) in sheet.Rows)
                if (rowNo >= DataStartRow && cells.TryGetValue(col, out var c) && !string.IsNullOrWhiteSpace(c.Value))
                    values.Add(c.Value!);
            if (values.Count > 0) result[(l, code)] = values;
        }
        return result;
    }

    private static Stream OpenPart(ZipArchive zip, string part) =>
        (zip.GetEntry(part) ?? throw new InvalidDataException("File mẫu thiếu part " + part)).Open();

    private static AttributeRequirement? RequirementOf(string? s) => s?.Trim() switch
    {
        "MANDATORY" => AttributeRequirement.Mandatory,
        "OPTIONAL" => AttributeRequirement.Optional,
        "IRRELEVANT" => AttributeRequirement.Irrelevant,
        _ => null,
    };

    private static AttributeInputKind InputKindOf(string hint)
    {
        if (hint.Contains("Chọn các tùy chọn theo danh sách", StringComparison.OrdinalIgnoreCase)) return AttributeInputKind.List;
        if (hint.Contains("giá trị tùy chỉnh", StringComparison.OrdinalIgnoreCase)) return AttributeInputKind.Custom;
        return AttributeInputKind.Unknown;
    }
}
