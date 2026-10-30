using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Shopee.Core.Progress;

namespace Shopee.Core.BigSeller;

/// <summary>
/// Đối chiếu nhanh khoảng dòng Excel (từ dòng .. đến dòng) cho tập itemId đã hoàn thành trong <see cref="OpProgressStore"/>
/// bằng streaming OpenXML (ZipArchive + XmlReader, không khoá file, không nạp cả workbook 30MB vào ClosedXML).
/// Kết quả được lưu ngược vào <see cref="OpProgressStore.SetRowRange"/> để các lần mở sau đọc tức thì (0ms).
/// </summary>
public static class WorkbookRowRangeResolver
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelAttrNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly Regex ShopeeDotIdRegex = new(@"i\.\d+\.(\d+)", RegexOptions.Compiled);
    private static readonly Regex ShopeeProductIdRegex = new(@"product/\d+/(\d+)", RegexOptions.Compiled);

    private static readonly SemaphoreSlim WorkerGate = new(1, 1);
    private static readonly ConcurrentDictionary<string, byte> AttemptedKeys = new(StringComparer.Ordinal);

    private static readonly object SharedStringsGate = new();
    private static string _cachedWorkbookPath = "";
    private static long _cachedWriteTicks;
    private static WeakReference<string[]>? _cachedSharedStrings;

    /// <summary>Đưa yêu cầu tính (MinRow..MaxRow) vào luồng nền nếu bản ghi đã có Done nhưng chưa lưu MinRow/MaxRow.</summary>
    public static void QueueResolveIfMissing(
        string accountId, string progressSheet, string op,
        string workbookPath, string dataSheet, int itemIdColumn, int linkColumn, int doneCount)
    {
        if (doneCount <= 0) return;
        if (string.IsNullOrWhiteSpace(workbookPath) || string.IsNullOrWhiteSpace(dataSheet)) return;
        if (!File.Exists(workbookPath)) return;

        long writeTicks;
        try { writeTicks = File.GetLastWriteTimeUtc(workbookPath).Ticks; }
        catch { return; }

        var attemptKey = $"{accountId}|{progressSheet}|{op}|{doneCount}|{workbookPath}|{dataSheet}|{writeTicks}";
        if (!AttemptedKeys.TryAdd(attemptKey, 0)) return;

        _ = Task.Run(async () =>
        {
            await WorkerGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var done = OpProgressStore.Shared.GetDone(accountId, progressSheet, op);
                if (done.Count == 0) return;

                var idSet = new HashSet<string>(done.Keys, StringComparer.Ordinal);
                var (minRow, maxRow) = TryFindRowRange(workbookPath, dataSheet, itemIdColumn, linkColumn, idSet);
                if (minRow > 0 && maxRow > 0)
                    OpProgressStore.Shared.SetRowRange(accountId, progressSheet, op, minRow, maxRow);
            }
            catch
            {
                // Best-effort cho hiển thị UI; lỗi đọc tạm thời không ảnh hưởng luồng chạy chính.
            }
            finally
            {
                WorkerGate.Release();
            }
        });
    }

    public static (int MinRow, int MaxRow) TryFindRowRange(
        string workbookPath, string sheetName, int itemIdColumn, int linkColumn, IReadOnlySet<string> doneIds)
    {
        if (doneIds.Count == 0 || string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
            return (0, 0);

        using var fs = new FileStream(workbookPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);

        var sheetEntryPath = ResolveSheetEntryPath(zip, sheetName);
        if (string.IsNullOrEmpty(sheetEntryPath)) return (0, 0);

        var sheetEntry = zip.GetEntry(sheetEntryPath)
            ?? zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, sheetEntryPath, StringComparison.OrdinalIgnoreCase));
        if (sheetEntry is null) return (0, 0);

        var sharedStrings = GetOrLoadSharedStrings(zip, workbookPath);

        int minRow = int.MaxValue;
        int maxRow = 0;

        using var sheetStream = sheetEntry.Open();
        using var reader = XmlReader.Create(sheetStream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "row") continue;

            var rAttr = reader.GetAttribute("r");
            if (!int.TryParse(rAttr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowNum) || rowNum < 2)
                continue;

            if (reader.IsEmptyElement) continue;

            string? itemIdVal = null;
            string? linkVal = null;
            var rowDepth = reader.Depth;

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == rowDepth && reader.LocalName == "row")
                    break;

                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "c")
                    continue;

                var cellRef = reader.GetAttribute("r");
                var colIdx = ParseColumnIndex(cellRef);
                if (colIdx != itemIdColumn && colIdx != linkColumn)
                    continue;

                var cellType = reader.GetAttribute("t");
                var val = ReadCellValue(reader, cellType, sharedStrings);
                if (colIdx == itemIdColumn) itemIdVal = val;
                if (colIdx == linkColumn) linkVal = val;
            }

            var rowId = !string.IsNullOrWhiteSpace(itemIdVal)
                ? itemIdVal.Trim()
                : ExtractShopeeId(linkVal);

            if (!string.IsNullOrEmpty(rowId) && doneIds.Contains(rowId))
            {
                if (rowNum < minRow) minRow = rowNum;
                if (rowNum > maxRow) maxRow = rowNum;
            }
        }

        return maxRow > 0 ? (minRow, maxRow) : (0, 0);
    }

    private static string? ResolveSheetEntryPath(ZipArchive zip, string targetSheetName)
    {
        var wbEntry = zip.GetEntry("xl/workbook.xml");
        var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (wbEntry is null || relsEntry is null) return null;

        XDocument wbDoc;
        using (var s = wbEntry.Open()) wbDoc = XDocument.Load(s);

        var trimmed = targetSheetName.Trim();
        var normTarget = SharedWorkbookCatalog.NormalizeSheetKey(trimmed);

        XElement? matchedSheet = null;
        foreach (var sh in wbDoc.Descendants(MainNs + "sheet"))
        {
            var name = sh.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (string.Equals(name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                matchedSheet = sh;
                break;
            }
            if (matchedSheet is null && SharedWorkbookCatalog.NormalizeSheetKey(name) == normTarget)
                matchedSheet = sh;
        }

        var relId = matchedSheet?.Attribute(RelAttrNs + "id")?.Value;
        if (string.IsNullOrWhiteSpace(relId)) return null;

        XDocument relsDoc;
        using (var s = relsEntry.Open()) relsDoc = XDocument.Load(s);

        var rel = relsDoc.Descendants(PkgRelNs + "Relationship")
            .FirstOrDefault(r => string.Equals(r.Attribute("Id")?.Value, relId, StringComparison.Ordinal));
        var target = rel?.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target)) return null;

        target = target.Replace('\\', '/').TrimStart('/');
        if (!target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
            target = "xl/" + target;
        return target;
    }

    private static string[] GetOrLoadSharedStrings(ZipArchive zip, string workbookPath)
    {
        long ticks = 0;
        try { ticks = File.GetLastWriteTimeUtc(workbookPath).Ticks; } catch { }

        lock (SharedStringsGate)
        {
            if (ticks > 0
                && string.Equals(_cachedWorkbookPath, workbookPath, StringComparison.OrdinalIgnoreCase)
                && _cachedWriteTicks == ticks
                && _cachedSharedStrings is not null
                && _cachedSharedStrings.TryGetTarget(out var cached))
            {
                return cached;
            }
        }

        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];

        var list = new List<string>(4096);
        var sb = new StringBuilder();
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "si") continue;
            if (reader.IsEmptyElement) { list.Add(""); continue; }

            sb.Clear();
            var siDepth = reader.Depth;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == siDepth && reader.LocalName == "si")
                    break;
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t" && !reader.IsEmptyElement)
                {
                    sb.Append(reader.ReadElementContentAsString());
                }
            }
            list.Add(sb.ToString());
        }

        var arr = list.ToArray();
        lock (SharedStringsGate)
        {
            _cachedWorkbookPath = workbookPath;
            _cachedWriteTicks = ticks;
            _cachedSharedStrings = new WeakReference<string[]>(arr);
        }
        return arr;
    }

    private static string ReadCellValue(XmlReader reader, string? cellType, string[] sharedStrings)
    {
        if (reader.IsEmptyElement) return "";
        var cDepth = reader.Depth;
        string? rawV = null;
        StringBuilder? inlineSb = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == cDepth && reader.LocalName == "c")
                break;

            if (reader.NodeType != XmlNodeType.Element || reader.IsEmptyElement) continue;

            if (reader.LocalName == "v")
            {
                rawV = reader.ReadElementContentAsString();
            }
            else if (reader.LocalName == "t")
            {
                inlineSb ??= new StringBuilder();
                inlineSb.Append(reader.ReadElementContentAsString());
            }
        }

        if (cellType == "s")
        {
            return int.TryParse(rawV, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx)
                   && (uint)idx < (uint)sharedStrings.Length
                ? sharedStrings[idx]
                : "";
        }

        if (cellType == "inlineStr")
            return inlineSb?.ToString() ?? "";

        if (string.IsNullOrEmpty(rawV))
            return "";

        if (cellType != "str"
            && (rawV.IndexOf('E') >= 0 || rawV.IndexOf('e') >= 0 || rawV.IndexOf('.') >= 0)
            && decimal.TryParse(rawV, NumberStyles.Float, CultureInfo.InvariantCulture, out var dec))
        {
            return decimal.Truncate(dec).ToString(CultureInfo.InvariantCulture);
        }

        return rawV;
    }

    private static int ParseColumnIndex(string? cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return 0;
        int col = 0;
        foreach (var ch in cellRef)
        {
            if (ch is >= 'A' and <= 'Z')
                col = col * 26 + (ch - 'A' + 1);
            else if (ch is >= 'a' and <= 'z')
                col = col * 26 + (ch - 'a' + 1);
            else
                break;
        }
        return col;
    }

    private static string ExtractShopeeId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        if (url.Contains("/verify/captcha", StringComparison.OrdinalIgnoreCase)
            || url.Contains("/verify/traffic", StringComparison.OrdinalIgnoreCase))
            return "";

        var m1 = ShopeeDotIdRegex.Match(url);
        if (m1.Success) return m1.Groups[1].Value;

        var m2 = ShopeeProductIdRegex.Match(url);
        if (m2.Success) return m2.Groups[1].Value;

        return "";
    }
}
