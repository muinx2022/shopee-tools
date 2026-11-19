namespace Shopee.Core.MassUpload.Export;

/// <summary>Một file mẫu user chọn, đã đọc cấu trúc.</summary>
public sealed record ExportTemplate(string Path, TemplateSchema Schema)
{
    /// <summary>Phần "ngành" trong tên file xuất: mã ngành cấp 1 của bản nâng cao, <c>basic</c> với bản basic.</summary>
    public string GroupName => Schema.Kind == TemplateKind.Advanced
        ? Schema.TopCategoryId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "basic";

    /// <summary>Mô tả ngắn cho màn / log (vd <c>nâng cao · ngành 100009 · shop 214522044</c>).</summary>
    public string Describe() => (Schema.Kind == TemplateKind.Advanced ? $"nâng cao · ngành {GroupName}" : "basic")
                                + (Schema.ShopId.Length > 0 ? $" · shop {Schema.ShopId}" : "");
}

/// <summary>
/// Chia SP theo file mẫu (plan GĐ8 mục 3), hàm thuần:
/// <list type="bullet">
/// <item>SP vào bản mẫu NÂNG CAO có ngành cấp 1 (ô C2) = ngành cấp 1 của SP (<see cref="ShopeeListing.TopCategoryId"/>).</item>
/// <item>Không có bản nâng cao đúng ngành mà có bản basic → vào bản basic (basic không có cột thuộc tính → SP vào Bản nháp).</item>
/// <item>Không có cả hai → không có mẫu (null) — caller báo "thiếu mẫu ngành X", không xuất SP đó.</item>
/// </list>
/// </summary>
public static class ExportTemplates
{
    /// <summary>Đọc các file mẫu. File không đọc được / không phải mẫu Đăng Hàng Loạt → lỗi kèm tên file. Hai bản nâng cao cùng
    /// ngành, hơn một bản basic, hay các mẫu thuộc nhiều shop (ô D2 khác nhau) → lỗi: không đoán SP vào mẫu nào / shop nào.</summary>
    public static (List<ExportTemplate> Templates, List<string> Errors) Load(IEnumerable<string> paths)
    {
        var templates = new List<ExportTemplate>();
        var errors = new List<string>();
        foreach (var raw in paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var name = System.IO.Path.GetFileName(raw);
            if (!File.Exists(raw)) { errors.Add($"Không thấy file mẫu {raw}"); continue; }
            try { templates.Add(new ExportTemplate(raw, TemplateSchema.Load(raw))); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException
                                           or System.Xml.XmlException)
            {
                errors.Add($"File mẫu {name} không đọc được: {ex.Message}");
            }
        }
        errors.AddRange(Check(templates));
        return (templates, errors);
    }

    /// <summary>Lỗi của bộ mẫu (xem <see cref="Load"/>).</summary>
    public static IReadOnlyList<string> Check(IReadOnlyList<ExportTemplate> templates)
    {
        var errors = new List<string>();
        foreach (var g in templates.Where(t => t.Schema.Kind == TemplateKind.Advanced).GroupBy(t => t.Schema.TopCategoryId))
            if (g.Count() > 1)
                errors.Add($"Có {g.Count()} file mẫu cùng ngành {g.Key}: {string.Join(", ", g.Select(t => System.IO.Path.GetFileName(t.Path)))} — giữ một file.");
        var basics = templates.Where(t => t.Schema.Kind == TemplateKind.Basic).ToList();
        if (basics.Count > 1)
            errors.Add($"Có {basics.Count} file mẫu basic: {string.Join(", ", basics.Select(t => System.IO.Path.GetFileName(t.Path)))} — giữ một file.");
        var shops = templates.Select(t => t.Schema.ShopId).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (shops.Count > 1)
            errors.Add($"Các file mẫu thuộc {shops.Count} shop khác nhau (shop_id {string.Join(", ", shops)}) — mỗi lượt chỉ dùng mẫu của một shop.");
        return errors;
    }

    /// <summary>Mẫu cho SP thuộc ngành cấp 1 <paramref name="topCategoryId"/>, null = thiếu mẫu.</summary>
    public static ExportTemplate? Route(IReadOnlyList<ExportTemplate> templates, long topCategoryId) =>
        templates.FirstOrDefault(t => t.Schema.Kind == TemplateKind.Advanced && t.Schema.TopCategoryId == topCategoryId)
        ?? templates.FirstOrDefault(t => t.Schema.Kind == TemplateKind.Basic);
}
