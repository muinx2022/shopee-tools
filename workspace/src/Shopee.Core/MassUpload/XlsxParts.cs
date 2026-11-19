using System.IO.Compression;
using System.Xml;

namespace Shopee.Core.MassUpload;

/// <summary>Một ô đọc được: giá trị đã giải (shared string / inline string / số thô) và style của ô.</summary>
internal readonly record struct XlsxCell(int Column, string? Value, int? Style);

/// <summary>Kết quả đọc một sheet: style theo cột (<c>&lt;cols&gt;</c>) và các ô theo dòng.</summary>
internal sealed class XlsxSheetData
{
    public Dictionary<int, int> ColumnStyles { get; } = new();
    public SortedDictionary<int, Dictionary<int, XlsxCell>> Rows { get; } = new();

    public string? Get(int row, int column) =>
        Rows.TryGetValue(row, out var r) && r.TryGetValue(column, out var c) ? c.Value : null;
}

/// <summary>
/// Đọc part XML của file xlsx bằng <see cref="XmlReader"/> (đọc luồng, không dựng DOM) — file mẫu Shopee do thư viện Go
/// sinh, XML không chuẩn (openpyxl không mở được) nên không dùng ClosedXML / OpenXML SDK. Sheet "Bản đăng tải" của bản mẫu
/// nâng cao ~5 MB, nên đọc dừng sớm theo <c>maxRow</c>.
/// </summary>
internal static class XlsxParts
{
    public const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        CloseInput = false,
    };

    /// <summary>Đường dẫn part của sheet tên <paramref name="sheetName"/> (qua <c>xl/workbook.xml</c> + rels), null nếu
    /// không có.</summary>
    public static string? FindSheetPart(ZipArchive zip, string sheetName)
    {
        string? rid = null;
        using (var s = Open(zip, "xl/workbook.xml"))
        using (var r = XmlReader.Create(s, Settings))
            while (r.Read())
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "sheet" && r.GetAttribute("name") == sheetName)
                {
                    rid = r.GetAttribute("id", RelNs);
                    break;
                }
        if (rid is null) return null;

        using (var s = Open(zip, "xl/_rels/workbook.xml.rels"))
        using (var r = XmlReader.Create(s, Settings))
            while (r.Read())
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "Relationship" && r.GetAttribute("Id") == rid)
                {
                    var target = r.GetAttribute("Target") ?? "";
                    return target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                }
        return null;
    }

    public static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var list = new List<string>();
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return list;
        using var s = entry.Open();
        using var r = XmlReader.Create(s, Settings);
        while (r.Read())
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "si")
                list.Add(ReadTextRuns(r));
        return list;
    }

    /// <summary>Style nào là kiểu SỐ (numFmt dạng số, không phải General / Text / ngày) — đọc từ <c>xl/styles.xml</c>.</summary>
    public static HashSet<int> ReadNumericStyles(ZipArchive zip)
    {
        var customFormats = new Dictionary<int, string>();
        var xfNumFmts = new List<int>();
        var entry = zip.GetEntry("xl/styles.xml");
        if (entry is null) return [];
        using (var s = entry.Open())
        using (var r = XmlReader.Create(s, Settings))
        {
            var inCellXfs = false;
            while (r.Read())
            {
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "numFmt"
                    && int.TryParse(r.GetAttribute("numFmtId"), out var id))
                    customFormats[id] = r.GetAttribute("formatCode") ?? "";
                else if (r.NodeType == XmlNodeType.Element && r.LocalName == "cellXfs")
                    inCellXfs = !r.IsEmptyElement;
                else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "cellXfs")
                    inCellXfs = false;
                else if (inCellXfs && r.NodeType == XmlNodeType.Element && r.LocalName == "xf")
                    xfNumFmts.Add(int.TryParse(r.GetAttribute("numFmtId"), out var f) ? f : 0);
            }
        }

        var numeric = new HashSet<int>();
        for (var i = 0; i < xfNumFmts.Count; i++)
            if (IsNumericFormat(xfNumFmts[i], customFormats)) numeric.Add(i);
        return numeric;
    }

    /// <summary>numFmt dựng sẵn kiểu số (0, 0.00, #,##0, %, khoa học…) hoặc numFmt tự định nghĩa có 0/# mà không có @ và
    /// không phải ngày giờ. General (0) và Text (49) không tính.</summary>
    private static bool IsNumericFormat(int id, Dictionary<int, string> custom)
    {
        if (custom.TryGetValue(id, out var code))
        {
            if (code.Contains('@')) return false;
            var lower = code.ToLowerInvariant();
            if (lower.IndexOfAny(['y', 'd', 'h', 's']) >= 0 && !lower.Contains('#') && !lower.Contains('0')) return false;
            return lower.Contains('0') || lower.Contains('#');
        }
        return id is >= 1 and <= 4 or >= 9 and <= 11 or >= 37 and <= 40 or 48;
    }

    /// <summary>Đọc sheet tới dòng <paramref name="maxRow"/> (dừng ngay khi gặp dòng lớn hơn — các dòng trong sheetData
    /// theo thứ tự tăng).</summary>
    public static XlsxSheetData ReadSheet(Stream sheetXml, IReadOnlyList<string> sharedStrings, int maxRow = int.MaxValue)
    {
        var data = new XlsxSheetData();
        using var r = XmlReader.Create(sheetXml, Settings);
        var lastRow = 0;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element) continue;
            if (r.LocalName == "col")
            {
                if (int.TryParse(r.GetAttribute("min"), out var min) && int.TryParse(r.GetAttribute("max"), out var max)
                    && int.TryParse(r.GetAttribute("style"), out var style))
                    for (var c = min; c <= Math.Min(max, 16384); c++) data.ColumnStyles[c] = style;
            }
            else if (r.LocalName == "row")
            {
                var rowNo = int.TryParse(r.GetAttribute("r"), out var rn) ? rn : lastRow + 1;
                lastRow = rowNo;
                if (rowNo > maxRow) break;
                if (r.IsEmptyElement) continue;
                var cells = new Dictionary<int, XlsxCell>();
                data.Rows[rowNo] = cells;
                ReadRowCells(r, sharedStrings, cells);
            }
            else if (r.LocalName is "sheetProtection" or "dataValidations" or "conditionalFormatting")
                break; // sau sheetData — không cần đọc phần validation khổng lồ.
        }
        return data;
    }

    private static void ReadRowCells(XmlReader r, IReadOnlyList<string> sst, Dictionary<int, XlsxCell> cells)
    {
        var depth = r.Depth;
        var lastCol = 0;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.EndElement && r.Depth == depth) return;
            if (r.NodeType != XmlNodeType.Element || r.LocalName != "c") continue;
            var refAttr = r.GetAttribute("r");
            var col = refAttr is null ? lastCol + 1 : ColumnIndex(refAttr);
            lastCol = col;
            var type = r.GetAttribute("t");
            int? style = int.TryParse(r.GetAttribute("s"), out var st) ? st : null;
            string? value = null;
            if (!r.IsEmptyElement)
            {
                var cDepth = r.Depth;
                string? v = null;
                r.Read();
                // ReadElementContentAsString / Skip tự đưa con trỏ sang nút kế → không Read thêm sau chúng.
                while (!r.EOF && !(r.NodeType == XmlNodeType.EndElement && r.Depth == cDepth))
                {
                    if (r.NodeType == XmlNodeType.Element && r.LocalName == "v") v = r.ReadElementContentAsString();
                    else if (r.NodeType == XmlNodeType.Element && r.LocalName == "is") { value = ReadTextRuns(r); r.Read(); }
                    else if (r.NodeType == XmlNodeType.Element) r.Skip();
                    else r.Read();
                }
                if (type == "s")
                    value = v is not null && int.TryParse(v, out var idx) && idx >= 0 && idx < sst.Count ? sst[idx] : null;
                else if (type != "inlineStr")
                    value = v;
            }
            cells[col] = new XlsxCell(col, value, style);
        }
    }

    /// <summary>Ghép các <c>&lt;t&gt;</c> trong phần tử hiện tại (<c>si</c> / <c>is</c>), bỏ phiên âm <c>rPh</c>. Kết thúc ở
    /// thẻ đóng của phần tử đó.</summary>
    private static string ReadTextRuns(XmlReader r)
    {
        if (r.IsEmptyElement) return "";
        var depth = r.Depth;
        var sb = new StringBuilder();
        r.Read();
        // ReadElementContentAsString / Skip tự đưa con trỏ sang nút kế → không Read thêm sau chúng.
        while (!r.EOF && !(r.NodeType == XmlNodeType.EndElement && r.Depth == depth))
        {
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "rPh") r.Skip();
            else if (r.NodeType == XmlNodeType.Element && r.LocalName == "t") sb.Append(r.ReadElementContentAsString());
            else r.Read();
        }
        return sb.ToString();
    }

    private static Stream Open(ZipArchive zip, string name) =>
        (zip.GetEntry(name) ?? throw new InvalidDataException("File xlsx thiếu part " + name)).Open();

    /// <summary>"AB12" / "AB" → 28 (1-based).</summary>
    public static int ColumnIndex(string cellRef)
    {
        var n = 0;
        foreach (var ch in cellRef)
        {
            if (ch is >= 'A' and <= 'Z') n = n * 26 + (ch - 'A' + 1);
            else if (ch is >= 'a' and <= 'z') n = n * 26 + (ch - 'a' + 1);
            else break;
        }
        return n;
    }

    /// <summary>28 → "AB".</summary>
    public static string ColumnLetter(int index)
    {
        var s = "";
        while (index > 0)
        {
            var rem = (index - 1) % 26;
            s = (char)('A' + rem) + s;
            index = (index - 1) / 26;
        }
        return s;
    }
}
