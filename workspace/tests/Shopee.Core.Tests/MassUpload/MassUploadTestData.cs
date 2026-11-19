using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Shopee.Core.MassUpload;

namespace Shopee.Core.Tests.MassUpload;

/// <summary>Dữ liệu dùng chung cho test Đăng Hàng Loạt (GĐ2a): file mẫu thật, listing tổng hợp, bộ đọc xlsx ĐỘC LẬP với
/// code đang test (XDocument, không dùng <c>XlsxParts</c>) để kiểm file ghi ra.</summary>
internal static class MassUploadTestData
{
    public const string Basic = "basic.xlsx";
    public const string Advanced = "advanced-100009.xlsx";

    /// <summary>File script GĐ0 đã ghi (bản mẫu basic, 3 SP / 29 dòng) và Shopee nhận 3/3.</summary>
    public const string Gd0Result = "gd0-basic-3sp.xlsx";

    public const string ImageBase = "https://img.test/";

    public static readonly MassUploadDefaults Defaults = new(100, 200, 10, 11, 12, "size-chart-shared");

    private static readonly Lazy<TemplateSchema> LazyBasic = new(() => TemplateSchema.Load(TemplatePath(Basic)));
    private static readonly Lazy<TemplateSchema> LazyAdvanced = new(() => TemplateSchema.Load(TemplatePath(Advanced)));

    public static TemplateSchema BasicSchema => LazyBasic.Value;
    public static TemplateSchema AdvancedSchema => LazyAdvanced.Value;

    public static string Resolve(string id) => ImageBase + id;

    public static string TemplatePath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Fixtures", "mass-upload", name);
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(dir.FullName, "tests", "Shopee.Core.Tests", "Fixtures", "mass-upload", name);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(name);
    }

    public static ShopeeListing Fixture(string name) => ShopeeListingReader.Read(HarvestTestData.Fixture(name));

    public static MassUploadProduct MapOk(ShopeeListing listing, TemplateSchema schema, MassUploadLimits? limits = null,
        AttributeDictionary? dictionary = null)
    {
        var r = MassUploadMapper.Map(listing, Defaults, schema, Resolve, dictionary, limits);
        return r as MassUploadProduct ?? throw new Xunit.Sdk.XunitException("SP bị bỏ: " + ((MassUploadSkip)r).Reason);
    }

    /// <summary>Listing tổng hợp: 1 nhóm "Màu" với <paramref name="prices"/> (VND) — mỗi giá một lựa chọn, có ảnh.</summary>
    public static ShopeeListing OneTier(long[] prices, string? title = null, long leaf = 100026, long top = 100009,
        IReadOnlyList<string>? optionNames = null, IReadOnlyList<string?>? optionImages = null,
        IReadOnlyList<ListingAttribute>? attributes = null, string? description = null, IReadOnlyList<string>? images = null)
    {
        var options = optionNames ?? prices.Select((_, i) => "Màu " + (i + 1)).ToList();
        var imgs = optionImages ?? options.Select((_, i) => (string?)("opt-" + i)).ToList();
        return new ShopeeListing(
            1001, 77, title ?? "Vòng tay bạc nữ đính đá phong cách Hàn Quốc", description ?? LongText(150),
            leaf, top, [top, leaf], true,
            [new ListingTier("Màu", options, imgs)],
            prices.Select((p, i) => new ListingModel(i + 1, [i], p)).ToList(),
            images ?? ["img-0", "img-1"], attributes ?? [new ListingAttribute(100022, "Gender", "Female", 652)], null);
    }

    /// <summary>Listing 2 nhóm a × b, mọi tổ hợp có model giá 100.000.</summary>
    public static ShopeeListing TwoTiers(int a, int b, long leaf = 100026, long top = 100009)
    {
        var t1 = Enumerable.Range(0, a).Select(i => "Màu " + i).ToList();
        var t2 = Enumerable.Range(0, b).Select(i => "Size " + i).ToList();
        var models = new List<ListingModel>();
        for (var i = 0; i < a; i++)
            for (var j = 0; j < b; j++) models.Add(new ListingModel(i * 1000 + j, [i, j], 100_000));
        return new ShopeeListing(2002, 77, "Áo thun nam nữ cotton form rộng", LongText(200), leaf, top, [top, leaf], true,
            [new ListingTier("Màu sắc", t1, t1.Select((_, i) => (string?)("c-" + i)).ToList()),
             new ListingTier("Kích cỡ", t2, t2.Select(_ => (string?)null).ToList())],
            models, ["img-0"], [new ListingAttribute(100022, "Gender", "Female", 652)], null);
    }

    /// <summary>Chuỗi tiếng Việt dài <paramref name="n"/> ký tự (có khoảng trắng).</summary>
    public static string LongText(int n)
    {
        const string words = "Sản phẩm chất lượng cao giao hàng nhanh toàn quốc ";
        var sb = new StringBuilder();
        while (sb.Length < n) sb.Append(words);
        return sb.ToString(0, n).Trim() + ".";
    }

    public static string NewTempDir() => HarvestTestData.NewTempDir();

    // ── Đọc xlsx độc lập ────────────────────────────────────────────────────────────────────────────────────────────

    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>Một ô đọc lại: giá trị + kiểu (<c>t</c>) + style.</summary>
    public sealed record ReadCell(string Value, string? Type, string? Style);

    /// <summary>Đọc sheet theo tên → dòng → chữ cái cột → ô.</summary>
    public static Dictionary<int, Dictionary<string, ReadCell>> ReadSheet(string xlsx, string sheetName)
    {
        using var zip = ZipFile.OpenRead(xlsx);
        var sst = zip.GetEntry("xl/sharedStrings.xml") is { } se
            ? XDocument.Load(se.Open()).Root!.Elements(Main + "si").Select(si => string.Concat(
                si.Descendants(Main + "t").Where(t => t.Parent?.Name != Main + "rPh").Select(t => t.Value))).ToList()
            : [];
        var part = SheetPart(zip, sheetName);
        var doc = XDocument.Load(zip.GetEntry(part)!.Open());
        var result = new Dictionary<int, Dictionary<string, ReadCell>>();
        foreach (var row in doc.Root!.Element(Main + "sheetData")!.Elements(Main + "row"))
        {
            var r = int.Parse(row.Attribute("r")!.Value);
            var cells = new Dictionary<string, ReadCell>();
            foreach (var c in row.Elements(Main + "c"))
            {
                var col = new string(c.Attribute("r")!.Value.TakeWhile(char.IsLetter).ToArray());
                var t = c.Attribute("t")?.Value;
                var v = t switch
                {
                    "s" => sst[int.Parse(c.Element(Main + "v")!.Value)],
                    "inlineStr" => string.Concat(c.Element(Main + "is")!.Descendants(Main + "t").Select(x => x.Value)),
                    _ => c.Element(Main + "v")?.Value ?? "",
                };
                cells[col] = new ReadCell(v, t, c.Attribute("s")?.Value);
            }
            result[r] = cells;
        }
        return result;
    }

    public static string SheetPart(ZipArchive zip, string sheetName)
    {
        XDocument wb, rels;
        using (var s = zip.GetEntry("xl/workbook.xml")!.Open()) wb = XDocument.Load(s);
        var rid = wb.Descendants(Main + "sheet").Single(s => s.Attribute("name")!.Value == sheetName).Attribute(Rel + "id")!.Value;
        using (var s = zip.GetEntry("xl/_rels/workbook.xml.rels")!.Open()) rels = XDocument.Load(s);
        var target = rels.Root!.Elements().Single(e => e.Attribute("Id")!.Value == rid).Attribute("Target")!.Value;
        return "xl/" + target;
    }

    /// <summary>SHA-256 nội dung (đã giải nén) của từng part.</summary>
    public static Dictionary<string, string> PartHashes(string xlsx)
    {
        using var zip = ZipFile.OpenRead(xlsx);
        return zip.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var s = e.Open();
            return Convert.ToHexString(SHA256.HashData(s));
        });
    }

    public static string PartText(string xlsx, string part)
    {
        using var zip = ZipFile.OpenRead(xlsx);
        using var r = new StreamReader(zip.GetEntry(part)!.Open(), Encoding.UTF8);
        return r.ReadToEnd();
    }

    /// <summary>Bản sao file mẫu nâng cao với một ô của <c>HiddenCatProps</c> đổi giá trị bằng shared string của ô khác
    /// (vd biến cột custom thành MANDATORY để thử luật ghi nguyên EN).</summary>
    public static string PatchCatProps(string dir, string targetCell, string sourceCell)
    {
        var src = TemplatePath(Advanced);
        var dst = Path.Combine(dir, "patched-" + targetCell + ".xlsx");
        File.Copy(src, dst, true);
        using var zip = ZipFile.Open(dst, ZipArchiveMode.Update);
        var part = SheetPart(zip, TemplateSchema.CatPropsSheetName);
        string xml;
        using (var r = new StreamReader(zip.GetEntry(part)!.Open(), Encoding.UTF8)) xml = r.ReadToEnd();
        string ValueOf(string cell)
        {
            var at = xml.IndexOf($"<c r=\"{cell}\"", StringComparison.Ordinal);
            var vs = xml.IndexOf("<v>", at, StringComparison.Ordinal) + 3;
            return xml[vs..xml.IndexOf("</v>", vs, StringComparison.Ordinal)];
        }
        var srcVal = ValueOf(sourceCell);
        var tAt = xml.IndexOf($"<c r=\"{targetCell}\"", StringComparison.Ordinal);
        var tvs = xml.IndexOf("<v>", tAt, StringComparison.Ordinal) + 3;
        var tve = xml.IndexOf("</v>", tvs, StringComparison.Ordinal);
        xml = xml[..tvs] + srcVal + xml[tve..];
        zip.GetEntry(part)!.Delete();
        using (var w = new StreamWriter(zip.CreateEntry(part).Open(), new UTF8Encoding(false))) w.Write(xml);
        return dst;
    }

    public const string NewChannelCode = "channel_id.99001";

    /// <summary>
    /// Bản sao bản mẫu nâng cao giả lập shop có BỘ CỘT KÊNH KHÁC: bỏ cột <paramref name="removeCol"/> (1-based, theo bản
    /// gốc) và/hoặc chèn cột kênh mới <see cref="NewChannelCode"/> ở vị trí <paramref name="insertCol"/> (theo toạ độ sau
    /// khi bỏ). Mọi cột phía sau dời chữ cái — ở cả sheet chính (dòng 1–6 + &lt;cols&gt;) lẫn <c>HiddenCatProps</c> (căn
    /// cùng chữ cái), đúng như Shopee sinh mẫu cho shop khác.
    /// </summary>
    public static string ShiftColumnsTemplate(string dir, int? removeCol, int? insertCol)
    {
        var dst = Path.Combine(dir, $"shift-{removeCol}-{insertCol}.xlsx");
        File.Copy(TemplatePath(Advanced), dst, true);
        int Map(int c)
        {
            if (removeCol is int r && c > r) c--;
            if (insertCol is int k && c >= k) c++;
            return c;
        }
        static int Index(string cellRef) =>
            cellRef.TakeWhile(char.IsLetter).Aggregate(0, (n, ch) => n * 26 + (ch - 'A' + 1));

        using var zip = ZipFile.Open(dst, ZipArchiveMode.Update);
        foreach (var sheetName in new[] { TemplateSchema.MainSheetName, TemplateSchema.CatPropsSheetName })
        {
            var part = SheetPart(zip, sheetName);
            XDocument doc;
            using (var s = zip.GetEntry(part)!.Open()) doc = XDocument.Load(s);
            var main = sheetName == TemplateSchema.MainSheetName;
            foreach (var row in doc.Root!.Element(Main + "sheetData")!.Elements(Main + "row"))
            {
                var rowNo = row.Attribute("r")!.Value;
                foreach (var c in row.Elements(Main + "c").ToList())
                {
                    var col = Index(c.Attribute("r")!.Value);
                    if (col == removeCol) c.Remove();
                    else c.SetAttributeValue("r", Letter(Map(col)) + rowNo);
                }
                if (main && rowNo == "1" && insertCol is int k)
                {
                    var cell = new XElement(Main + "c", new XAttribute("r", Letter(k) + "1"), new XAttribute("s", "6"),
                        new XAttribute("t", "inlineStr"), new XElement(Main + "is", new XElement(Main + "t", NewChannelCode + "|0|0")));
                    var after = row.Elements(Main + "c").FirstOrDefault(c => Index(c.Attribute("r")!.Value) > k);
                    if (after is null) row.Add(cell); else after.AddBeforeSelf(cell);
                }
            }
            if (main)
            {
                var cols = doc.Root.Element(Main + "cols")!;
                foreach (var col in cols.Elements(Main + "col").ToList())
                {
                    var min = int.Parse(col.Attribute("min")!.Value);
                    if (min == removeCol) { col.Remove(); continue; }
                    col.SetAttributeValue("min", Map(min));
                    col.SetAttributeValue("max", Map(int.Parse(col.Attribute("max")!.Value)));
                }
                if (insertCol is int k)
                    cols.Add(new XElement(Main + "col", new XAttribute("customWidth", "true"), new XAttribute("max", k),
                        new XAttribute("min", k), new XAttribute("style", "6"), new XAttribute("width", "30")));
            }
            zip.GetEntry(part)!.Delete();
            using var w = zip.CreateEntry(part).Open();
            using var xw = System.Xml.XmlWriter.Create(w, new System.Xml.XmlWriterSettings { Encoding = new UTF8Encoding(false) });
            doc.Save(xw);
        }
        return dst;
    }

    /// <summary>Bản sao bản mẫu nâng cao có thêm giá trị danh sách trong <c>HiddenAttr</c> cho (mã cột, ngành lá) — biến một
    /// cột tùy chỉnh thành kiểu danh sách với ngành đó.</summary>
    public static string AddHiddenAttr(string dir, string code, long leaf, params string[] values)
    {
        var dst = Path.Combine(dir, $"attr-{leaf}-{code.Split('.')[^1]}.xlsx");
        File.Copy(TemplatePath(Advanced), dst, true);
        var entry = AdvancedSchema.CategoryEntry(leaf)!;
        using var zip = ZipFile.Open(dst, ZipArchiveMode.Update);
        var part = SheetPart(zip, TemplateSchema.AttrValuesSheetName);
        XDocument doc;
        using (var s = zip.GetEntry(part)!.Open()) doc = XDocument.Load(s);
        var data = doc.Root!.Element(Main + "sheetData")!;
        var last = data.Elements(Main + "row").Max(r => int.Parse(r.Attribute("r")!.Value));
        static XElement Inline(string r, string text) => new(Main + "c", new XAttribute("r", r), new XAttribute("t", "inlineStr"),
            new XElement(Main + "is", new XElement(Main + "t", text)));
        foreach (var v in values)
        {
            last++;
            data.Add(new XElement(Main + "row", new XAttribute("r", last), Inline("A" + last, code + entry), Inline("B" + last, v)));
        }
        zip.GetEntry(part)!.Delete();
        using var w = zip.CreateEntry(part).Open();
        using var xw = System.Xml.XmlWriter.Create(w, new System.Xml.XmlWriterSettings { Encoding = new UTF8Encoding(false) });
        doc.Save(xw);
        return dst;
    }

    public static string Letter(int index)
    {
        var s = "";
        for (; index > 0; index = (index - 1) / 26) s = (char)('A' + (index - 1) % 26) + s;
        return s;
    }
}
