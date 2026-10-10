using ClosedXML.Excel;

namespace Shopee.Core.BigSeller;

/// <summary>Kết quả gộp: số dòng dữ liệu (không tính header) và ghi chú số cột từng sheet.</summary>
public sealed record SharedWorkbookMergeResult(int DataRowCount, IReadOnlyList<string> ColumnNotes);

/// <summary>
/// Nối các sheet đã chọn thành một workbook mới, một sheet tên <see cref="SheetName"/>.
/// Không sửa file nguồn. Sheet thiếu tên thì ném lỗi và không ghi file đích.
/// </summary>
public static class SharedWorkbookMerge
{
    public const string SheetName = "Gop";

    public static SharedWorkbookMergeResult Build(string sourcePath, IReadOnlyList<string> sheetNames, string destPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new InvalidOperationException("Không thấy file workbook chung.");
        if (sheetNames is null || sheetNames.Count == 0)
            throw new InvalidOperationException("Chưa chọn sheet.");
        if (string.IsNullOrWhiteSpace(destPath))
            throw new ArgumentException("Thiếu đường dẫn file gộp.", nameof(destPath));

        using var fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var src = new XLWorkbook(fs);
        foreach (var name in sheetNames)
        {
            if (!src.TryGetWorksheet(name, out _))
                throw new InvalidOperationException($"Không có sheet \"{name}\".");
        }

        var notes = new List<string>();
        string?[] header = [];
        var rows = new List<string?[]>();
        var first = true;

        foreach (var name in sheetNames)
        {
            var ws = src.Worksheet(name);
            var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            var mismatch = !first && HeadersDiffer(ws, header, lastCol);
            notes.Add(mismatch ? $"{name}: {lastCol} cột (header lệch)" : $"{name}: {lastCol} cột");

            if (first)
            {
                header = new string?[lastCol];
                for (var c = 1; c <= lastCol; c++)
                    header[c - 1] = CellText(ws.Cell(1, c));
                first = false;
            }
            else if (lastCol > header.Length)
            {
                Array.Resize(ref header, lastCol);
            }

            for (var r = 2; r <= lastRow; r++)
            {
                var width = Math.Max(lastCol, 0);
                var vals = new string?[width];
                var any = false;
                for (var c = 1; c <= lastCol; c++)
                {
                    var text = CellText(ws.Cell(r, c));
                    vals[c - 1] = text;
                    if (!string.IsNullOrWhiteSpace(text)) any = true;
                }
                if (any) rows.Add(vals);
            }
        }

        var colCount = header.Length;
        foreach (var row in rows)
            if (row.Length > colCount) colCount = row.Length;

        var dir = Path.GetDirectoryName(Path.GetFullPath(destPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = destPath + ".writing.xlsx";
        try
        {
            using (var dst = new XLWorkbook())
            {
                var ws = dst.Worksheets.Add(SheetName);
                for (var c = 0; c < colCount; c++)
                    ws.Cell(1, c + 1).Value = c < header.Length ? header[c] ?? "" : "";
                for (var i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    for (var c = 0; c < row.Length; c++)
                    {
                        if (!string.IsNullOrEmpty(row[c]))
                            ws.Cell(i + 2, c + 1).Value = row[c];
                    }
                }
                dst.SaveAs(tmp);
            }
            File.Move(tmp, destPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }

        return new SharedWorkbookMergeResult(rows.Count, notes);
    }

    private static bool HeadersDiffer(IXLWorksheet ws, string?[] header, int lastCol)
    {
        var n = Math.Min(header.Length, lastCol);
        for (var c = 1; c <= n; c++)
        {
            if (!string.Equals(CellText(ws.Cell(1, c)), header[c - 1] ?? "", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static string CellText(IXLCell cell) => cell.GetString();
}
