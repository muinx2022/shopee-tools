using System.Globalization;
using ClosedXML.Excel;

namespace Shopee.Upload.Import;

/// <summary>Một dòng của file mật khẩu. <see cref="ToString"/> KHÔNG in mật khẩu (lỡ đem đi log / nội suy chuỗi vẫn an toàn).</summary>
public sealed class PasswordRow(int rowNumber, string acc, string pass, string mail, string mailPass)
{
    /// <summary>Số dòng trong sheet (1 = dòng đầu) — để báo cho user tìm.</summary>
    public int RowNumber { get; } = rowNumber;

    /// <summary>Tài khoản Shopee — đã trim.</summary>
    public string Acc { get; } = acc;

    /// <summary>Mật khẩu Shopee — GIỮ NGUYÊN từng ký tự (không trim).</summary>
    public string Pass { get; } = pass;

    /// <summary>Email xác minh — đã trim.</summary>
    public string Mail { get; } = mail;

    /// <summary>Mật khẩu email — giữ nguyên từng ký tự.</summary>
    public string MailPass { get; } = mailPass;

    public override string ToString() => $"dòng {RowNumber} ({Acc})";
}

/// <summary>Đọc được file: tên sheet + các dòng có ACC (dòng ACC trống bị bỏ, không báo).</summary>
public sealed record PasswordSheet(string SheetName, int HeaderRow, IReadOnlyList<PasswordRow> Rows)
{
    /// <summary>Số dòng bị BỎ vì có ô lỗi (<c>#N/A</c>, <c>#DIV/0!</c>, công thức vòng tròn…) hoặc ô ACC / Pass / pass Mail chỉ có
    /// khoảng trắng — chỉ số dòng, không giá trị.</summary>
    public IReadOnlyList<int> BadRows { get; init; } = [];

    /// <summary>Sheet đang đọc là sheet ẩn (không sheet hiện nào có đủ 4 cột).</summary>
    public bool Hidden { get; init; }
}

/// <summary>Lỗi đọc file mật khẩu — câu lỗi chỉ có tên cột / tên sheet / số dòng, KHÔNG có giá trị ô.</summary>
public sealed class PasswordFileException(string message) : Exception(message);

/// <summary>
/// Đọc file mật khẩu shop (plan GĐ5c mục 2) bằng ClosedXML.
/// <list type="bullet">
/// <item>CHỈ đọc 4 cột tìm theo tên tiêu đề: <c>ACC</c> (hoặc "tài khoản"), <c>Pass</c>, <c>mail</c>, <c>pass Mail</c>. So tên sau
/// <see cref="ImportText.Key"/> và KHỚP CHÍNH XÁC — không "contains": "pass Mail" chứa cả "mail" lẫn "pass". Các cột khác (SĐT, định
/// danh, ngân hàng…) không bao giờ được đọc.</item>
/// <item>Hàng tiêu đề tìm trong <see cref="HeaderScanRows"/> dòng đầu của từng sheet; lấy sheet ĐANG HIỆN đầu tiên có đủ 4 cột, không
/// có mới tới sheet ẩn (báo rõ là sheet ẩn). Một cột xuất hiện 2 lần trên cùng hàng tiêu đề → lỗi (không đoán cột nào).</item>
/// <item>Ô lỗi (giá trị lỗi Excel, hoặc tính công thức ném lỗi) ở bất kỳ cột nào trong 4 cột, hoặc ô ACC / Pass / pass Mail chỉ có
/// khoảng trắng → bỏ CẢ dòng, báo số dòng. Ô mail chỉ có khoảng trắng → coi là trống.</item>
/// <item>Giá trị = chữ hiển thị của ô: ô số định dạng General đọc thành số nguyên đầy đủ (không <c>1.2E+5</c>); ô số có định dạng
/// riêng (vd <c>000000</c>) đọc theo định dạng đó (giữ số 0 đầu); ô chữ đọc nguyên văn.</item>
/// <item>Mật khẩu giữ nguyên từng ký tự; ACC và email trim.</item>
/// </list>
/// </summary>
public static class PasswordSheetReader
{
    public const int HeaderScanRows = 10;

    /// <summary>Tên cột chuẩn (để báo) và các tên chấp nhận (đã chuẩn hoá).</summary>
    internal static readonly (string Display, string[] Names)[] Columns =
    [
        ("ACC", ["acc", "tai khoan"]),
        ("Pass", ["pass"]),
        ("mail", ["mail", "email"]),
        ("pass Mail", ["pass mail", "pass email"]),
    ];

    public static PasswordSheet Read(string path)
    {
        XLWorkbook wb;
        try
        {
            // FileShare.ReadWrite: user hay để file đang mở trong Excel.
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            wb = new XLWorkbook(fs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PasswordFileException($"Không mở được file: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Lỗi của thư viện đọc xlsx có thể dẫn nội dung ô — chỉ báo loại lỗi.
            throw new PasswordFileException($"Không đọc được file .xlsx ({ex.GetType().Name}). File có đúng là Excel .xlsx không?");
        }

        using (wb)
        {
            string? firstProblem = null;
            // Sheet đang hiện trước, sheet ẩn sau (giữ thứ tự trong file ở mỗi nhóm).
            foreach (var ws in wb.Worksheets.OrderBy(w => w.Visibility == XLWorksheetVisibility.Visible ? 0 : 1))
            {
                var found = FindHeader(ws, out var problem);
                if (found is { } h)
                {
                    var rows = ReadRows(ws, h.Row, h.Cols, out var bad);
                    return new PasswordSheet(ws.Name, h.Row, rows) { BadRows = bad, Hidden = ws.Visibility != XLWorksheetVisibility.Visible };
                }
                firstProblem ??= $"Sheet \"{ws.Name}\": {problem}";
            }
            throw new PasswordFileException(
                (firstProblem ?? "File không có sheet nào.") +
                $" Cần hàng tiêu đề (trong {HeaderScanRows} dòng đầu) có đủ 4 cột: {string.Join(", ", Columns.Select(c => c.Display))}.");
        }
    }

    /// <summary>Hàng tiêu đề của một sheet: hàng đầu tiên (trong <see cref="HeaderScanRows"/> dòng) có đủ 4 cột. Không có → null +
    /// mô tả hàng gần đủ nhất (thiếu cột nào / cột nào lặp).</summary>
    private static (int Row, int[] Cols)? FindHeader(IXLWorksheet ws, out string problem)
    {
        problem = "không thấy hàng tiêu đề nào có các cột cần.";
        var bestCount = 0;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (var r = 1; r <= HeaderScanRows; r++)
        {
            var hits = new List<int>[Columns.Length];
            for (var i = 0; i < hits.Length; i++) hits[i] = [];
            for (var c = 1; c <= lastCol; c++)
            {
                string key;
                try { key = ImportText.Key(ws.Cell(r, c).GetFormattedString()); }
                catch (Exception) { continue; }   // ô công thức lỗi ở vùng tiêu đề — coi như trống
                if (key.Length == 0) continue;
                for (var i = 0; i < Columns.Length; i++)
                    if (Columns[i].Names.Contains(key)) hits[i].Add(c);
            }

            var count = hits.Count(h => h.Count > 0);
            if (count == 0) continue;
            var dup = Columns.Where((_, i) => hits[i].Count > 1).Select(c => c.Display).ToList();
            if (count == Columns.Length && dup.Count == 0) return (r, hits.Select(h => h[0]).ToArray());
            if (count > bestCount || dup.Count > 0)
            {
                bestCount = count;
                var missing = Columns.Where((_, i) => hits[i].Count == 0).Select(c => c.Display).ToList();
                problem = $"hàng {r} " +
                          string.Join("; ", new[]
                          {
                              missing.Count > 0 ? "thiếu cột " + string.Join(", ", missing) : null,
                              dup.Count > 0 ? "có cột lặp " + string.Join(", ", dup) + " (không biết lấy cột nào)" : null,
                          }.Where(x => x is not null)) + ".";
                if (dup.Count > 0) return null;   // hàng tiêu đề thật nhưng mơ hồ — không dò tiếp các hàng dưới (là dữ liệu)
            }
        }
        return null;
    }

    private static List<PasswordRow> ReadRows(IXLWorksheet ws, int headerRow, int[] cols, out List<int> bad)
    {
        var rows = new List<PasswordRow>();
        bad = [];
        var last = ws.LastRowUsed()?.RowNumber() ?? headerRow;
        for (var r = headerRow + 1; r <= last; r++)
        {
            var cells = new string?[cols.Length];
            var broken = false;
            for (var i = 0; i < cols.Length && !broken; i++)
            {
                try
                {
                    var cell = ws.Cell(r, cols[i]);
                    if (cell.IsEmpty()) { cells[i] = ""; continue; }
                    if (cell.Value.IsError) { broken = true; continue; }
                    cells[i] = CellText(cell);
                }
                catch (Exception)
                {
                    // ClosedXML tính công thức khi đọc: vòng tròn / hàm không hỗ trợ → ném. Bỏ dòng, không đưa câu lỗi (có thể dẫn ô).
                    broken = true;
                }
            }
            if (broken) { bad.Add(r); continue; }

            string acc = cells[0]!, pass = cells[1]!, mail = cells[2]!, mailPass = cells[3]!;
            if (acc.Length == 0) continue;                                   // dòng trống / không có ACC
            // Chỉ khoảng trắng ở ACC / mật khẩu: gần như chắc là gõ nhầm — không đoán (mật khẩu không trim).
            if (IsBlankNotEmpty(acc) || IsBlankNotEmpty(pass) || IsBlankNotEmpty(mailPass)) { bad.Add(r); continue; }
            rows.Add(new PasswordRow(r, acc.Trim(), pass, mail.Trim(), mailPass));
        }
        return rows;
    }

    private static bool IsBlankNotEmpty(string s) => s.Length > 0 && string.IsNullOrWhiteSpace(s);

    /// <summary>Chữ của ô như user thấy, nhưng KHÔNG phụ thuộc culture của máy với số nguyên: ô số không có phần lẻ → số nguyên theo
    /// <see cref="CultureInfo.InvariantCulture"/> bất kể định dạng (không mũ, không dấu phân cách nghìn — <c>#,##0</c> ở máy vi-VN
    /// ra <c>1.234</c>, ở en-US ra <c>1,234</c>); riêng định dạng toàn số 0 (<c>000000</c>) thì đệm số 0 đầu như Excel hiện. Số có
    /// phần lẻ: General → dạng ngắn invariant; định dạng riêng → chữ hiển thị của ClosedXML.</summary>
    internal static string CellText(IXLCell cell)
    {
        if (cell.IsEmpty()) return "";
        var v = cell.Value;
        if (v.IsText) return v.GetText();
        if (v.IsNumber)
        {
            var n = v.GetNumber();
            if (n == Math.Floor(n) && Math.Abs(n) < 1e17)
            {
                var format = cell.Style.NumberFormat.Format;
                return IsZeroPad(format)
                    ? n.ToString(format, CultureInfo.InvariantCulture)
                    : n.ToString("0", CultureInfo.InvariantCulture);
            }
            if (IsGeneral(cell)) return n.ToString("0.###############", CultureInfo.InvariantCulture);
        }
        return cell.GetFormattedString();
    }

    /// <summary>Định dạng chỉ gồm số 0 (<c>0</c>, <c>000000</c>) — đệm số 0 đầu.</summary>
    private static bool IsZeroPad(string? format) => !string.IsNullOrEmpty(format) && format.All(ch => ch == '0');

    private static bool IsGeneral(IXLCell cell)
    {
        var nf = cell.Style.NumberFormat;
        return string.IsNullOrEmpty(nf.Format) ? nf.NumberFormatId == 0 : string.Equals(nf.Format, "General", StringComparison.OrdinalIgnoreCase);
    }
}
