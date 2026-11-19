using System.Globalization;
using System.IO.Compression;

namespace Shopee.Core.MassUpload;

/// <summary>Một SP trong file đã ghi: các số dòng Excel (từ 7) của nó.</summary>
public sealed record MassUploadFileEntry(long ItemId, IReadOnlyList<int> Rows);

/// <summary>Một file đã ghi.</summary>
public sealed record MassUploadFile(string Path, IReadOnlyList<MassUploadFileEntry> Items, long SizeBytes);

/// <summary>
/// Ghi SP đã map vào file mẫu Đăng Hàng Loạt của chính shop (plan GĐ2 bước 3, cách đã chứng minh ở GĐ0: Shopee nhận 3/3).
/// <list type="bullet">
/// <item>Sửa THẲNG part XML của sheet "Bản đăng tải" (tìm theo tên): bỏ mọi <c>&lt;row&gt;</c> ≥ 7, thêm dòng mới, cập
/// nhật <c>&lt;dimension&gt;</c>. Dòng 1–6, cols, protection, validation, conditional format giữ nguyên; các part khác
/// chép nguyên nội dung.</item>
/// <item>Ghi theo MÃ CỘT ở dòng 1 (shop khác có bộ cột kênh khác). Cột có style số → <c>&lt;v&gt;</c>; còn lại inline
/// string <c>xml:space="preserve"</c>, escape XML, bỏ ký tự điều khiển không hợp lệ.</item>
/// <item>Chia file: ≤ N SP, ≤ 1000 dòng, file nén ≤ 3 MB (đo sau khi ghi, vượt thì bớt SP rồi ghi lại). Một SP không bao
/// giờ bị tách qua 2 file.</item>
/// </list>
/// Sheet bản nâng cao ~5 MB: xử lý tuyến tính bằng IndexOf, không regex trên cả chuỗi.
/// </summary>
public static class MassUploadTemplateWriter
{
    /// <summary>Ghi <paramref name="products"/> ra một hay nhiều file <c>&lt;prefix&gt;-01.xlsx</c>, <c>-02</c>… trong
    /// <paramref name="outputDirectory"/>. NÉM <see cref="InvalidOperationException"/> khi SP khác ngành cấp 1 với bản mẫu
    /// nâng cao, thiếu cột bắt buộc, có mã cột bản mẫu không có, một SP một mình đã vượt giới hạn file, hoặc thư mục đích
    /// đã có file <c>&lt;prefix&gt;-NN.xlsx</c> (không bao giờ ghi đè).</summary>
    public static IReadOnlyList<MassUploadFile> WriteFiles(string templatePath, TemplateSchema schema,
        IReadOnlyList<MassUploadProduct> products, string outputDirectory, string fileNamePrefix, int productsPerFile,
        MassUploadLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(products);
        if (productsPerFile <= 0) throw new ArgumentOutOfRangeException(nameof(productsPerFile));
        var lim = limits ?? MassUploadLimits.Default;
        Validate(schema, products, lim);
        if (products.Count == 0) return [];

        var template = File.ReadAllBytes(templatePath);
        Directory.CreateDirectory(outputDirectory);
        // Không bao giờ ghi đè: GĐ5 chống tải trùng dựa trên TÊN file — đã có <prefix>-NN.xlsx nào là dừng, chưa ghi gì.
        var existing = Directory.EnumerateFiles(outputDirectory, fileNamePrefix + "-*.xlsx")
            .Select(Path.GetFileName)
            .FirstOrDefault(n => IsOutputName(n!, fileNamePrefix));
        if (existing is not null)
            throw new InvalidOperationException(
                $"Thư mục đích đã có file \"{existing}\" cùng tiền tố \"{fileNamePrefix}\" — không ghi đè");
        var files = new List<MassUploadFile>();
        var i = 0;
        while (i < products.Count)
        {
            var take = 0;
            var rows = 0;
            while (i + take < products.Count && take < productsPerFile
                   && rows + products[i + take].Rows.Count <= lim.MaxDataRowsPerFile)
                rows += products[i + take++].Rows.Count;

            var path = Path.Combine(outputDirectory,
                $"{fileNamePrefix}-{(files.Count + 1).ToString("00", CultureInfo.InvariantCulture)}.xlsx");
            if (File.Exists(path)) // có ai đó tạo chen giữa lượt
                throw new InvalidOperationException($"File đích \"{path}\" đã tồn tại — không ghi đè");
            while (true)
            {
                var file = WriteFile(template, schema, products.Skip(i).Take(take).ToList(), path);
                if (file.SizeBytes <= lim.MaxFileBytes)
                {
                    files.Add(file);
                    break;
                }
                if (take == 1)
                {
                    TryDelete(path);
                    throw new InvalidOperationException(
                        $"SP {products[i].ItemId} một mình đã ra file {file.SizeBytes} byte > {lim.MaxFileBytes}");
                }
                // Ước theo tỉ lệ kích cỡ (nén không tuyến tính nên vẫn đo lại), luôn bớt ít nhất 1 SP.
                var estimate = (int)(take * (double)lim.MaxFileBytes / file.SizeBytes * 0.95);
                take = Math.Clamp(estimate, 1, take - 1);
            }
            i += take;
        }
        return files;
    }

    /// <summary>Ghi đúng các SP này vào MỘT file (không chia). Không kiểm 3 MB — <see cref="WriteFiles"/> lo.</summary>
    public static MassUploadFile WriteFile(byte[] template, TemplateSchema schema, IReadOnlyList<MassUploadProduct> products,
        string outputPath, MassUploadLimits? limits = null)
    {
        var lim = limits ?? MassUploadLimits.Default;
        Validate(schema, products, lim);
        var totalRows = products.Sum(p => p.Rows.Count);
        if (totalRows > lim.MaxDataRowsPerFile)
            throw new InvalidOperationException($"{totalRows} dòng > {lim.MaxDataRowsPerFile} dòng mỗi file");

        var columns = schema.Columns.OrderBy(c => c.Index).ToList();
        var lastColumn = columns[^1].Letter;
        var data = new StringBuilder(totalRows * 2048);
        var entries = new List<MassUploadFileEntry>();
        var rowNo = TemplateSchema.DataStartRow;
        foreach (var p in products)
        {
            var rowNumbers = new List<int>();
            foreach (var row in p.Rows)
            {
                AppendRow(data, rowNo, columns, row);
                rowNumbers.Add(rowNo++);
            }
            entries.Add(new MassUploadFileEntry(p.ItemId, rowNumbers));
        }

        try
        {
            using (var src = new ZipArchive(new MemoryStream(template, writable: false), ZipArchiveMode.Read))
            using (var fs = File.Create(outputPath))
            using (var dst = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var e in src.Entries)
                {
                    var ne = dst.CreateEntry(e.FullName, CompressionLevel.Optimal);
                    try { ne.LastWriteTime = e.LastWriteTime; }
                    catch (ArgumentOutOfRangeException) { /* mốc ngoài khoảng DOS (Go ghi 0) — để mặc định */ }
                    using var to = ne.Open();
                    if (e.FullName == schema.SheetPart)
                    {
                        byte[] sheet;
                        using (var from = e.Open())
                        using (var ms = new MemoryStream())
                        {
                            from.CopyTo(ms);
                            sheet = ms.ToArray();
                        }
                        var lastRow = Math.Max(TemplateSchema.DataStartRow - 1, rowNo - 1);
                        to.Write(RewriteSheet(sheet, data.ToString(), $"A1:{lastColumn}{lastRow}"));
                    }
                    else
                    {
                        using var from = e.Open();
                        from.CopyTo(to);
                    }
                }
            }
        }
        catch
        {
            TryDelete(outputPath);
            throw;
        }
        return new MassUploadFile(outputPath, entries, new FileInfo(outputPath).Length);
    }

    private static void Validate(TemplateSchema schema, IReadOnlyList<MassUploadProduct> products, MassUploadLimits lim)
    {
        var required = schema.Columns.Where(c => c.Required).Select(c => c.Code).ToList();
        // ItemId là khoá gom dòng (cột E) và khoá đối soát kết quả → trùng trong một lượt là lỗi của bên gọi.
        var duplicate = products.GroupBy(p => p.ItemId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"SP {duplicate.Key} xuất hiện {duplicate.Count()} lần trong cùng một lượt ghi");
        foreach (var p in products)
        {
            if (schema.Kind == TemplateKind.Advanced && p.TopCategoryId != schema.TopCategoryId)
                throw new InvalidOperationException(
                    $"SP {p.ItemId} thuộc ngành cấp 1 {p.TopCategoryId}, bản mẫu nâng cao là ngành {schema.TopCategoryId}");
            if (schema.Kind == TemplateKind.Advanced && !schema.HasCategory(p.LeafCategoryId))
                throw new InvalidOperationException($"SP {p.ItemId}: ngành lá {p.LeafCategoryId} không có trong bản mẫu");
            if (p.Rows.Count == 0) throw new InvalidOperationException($"SP {p.ItemId} không có dòng nào");
            if (p.Rows.Count > lim.MaxDataRowsPerFile)
                throw new InvalidOperationException($"SP {p.ItemId} có {p.Rows.Count} dòng > {lim.MaxDataRowsPerFile}");
            foreach (var row in p.Rows)
            {
                foreach (var code in row.Keys)
                    if (!schema.TryGetColumn(code, out _))
                        throw new InvalidOperationException($"SP {p.ItemId}: mã cột \"{code}\" không có trong file mẫu");
                foreach (var code in required)
                    if (!row.TryGetValue(code, out var v) || string.IsNullOrWhiteSpace(v.Text))
                        throw new InvalidOperationException($"SP {p.ItemId}: thiếu giá trị cho cột bắt buộc \"{code}\" của file mẫu");
            }
        }
    }

    private static void AppendRow(StringBuilder sb, int rowNo, List<TemplateColumn> columns,
        IReadOnlyDictionary<string, MassUploadCell> row)
    {
        var r = rowNo.ToString(CultureInfo.InvariantCulture);
        sb.Append("<row r=\"").Append(r).Append("\">");
        foreach (var col in columns)
        {
            if (!row.TryGetValue(col.Code, out var cell) || cell.Text.Length == 0) continue;
            sb.Append("<c r=\"").Append(col.Letter).Append(r).Append('"');
            if (col.Style is int st) sb.Append(" s=\"").Append(st.ToString(CultureInfo.InvariantCulture)).Append('"');
            if (col.IsNumeric && cell.IsNumber)
                sb.Append("><v>").Append(cell.Text).Append("</v></c>");
            else
            {
                sb.Append(" t=\"inlineStr\"><is><t xml:space=\"preserve\">");
                AppendEscaped(sb, cell.Text);
                sb.Append("</t></is></c>");
            }
        }
        sb.Append("</row>");
    }

    /// <summary>Làm sạch bằng <see cref="MassUploadText.Clean"/> (CÙNG luật với reader/mapper: bỏ ký tự không hợp lệ trong
    /// XML 1.0, CR / CRLF → LF, NFC) rồi escape XML.</summary>
    internal static void AppendEscaped(StringBuilder sb, string s)
    {
        foreach (var ch in MassUploadText.Clean(s))
        {
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(ch); break;
            }
        }
    }

    /// <summary>Thay các <c>&lt;row&gt;</c> ≥ 7 trong sheetData bằng <paramref name="newRows"/> và đặt lại
    /// <c>&lt;dimension ref&gt;</c>. Phần còn lại của part giữ nguyên từng ký tự. Quét tuyến tính bằng IndexOf.</summary>
    internal static byte[] RewriteSheet(byte[] sheetBytes, string newRows, string dimensionRef)
    {
        var bom = sheetBytes.Length >= 3 && sheetBytes[0] == 0xEF && sheetBytes[1] == 0xBB && sheetBytes[2] == 0xBF;
        var xml = Encoding.UTF8.GetString(sheetBytes, bom ? 3 : 0, sheetBytes.Length - (bom ? 3 : 0));

        var open = xml.IndexOf("<sheetData", StringComparison.Ordinal);
        if (open < 0) throw new InvalidDataException("Sheet \"Bản đăng tải\" không có <sheetData>");
        var openEnd = xml.IndexOf('>', open);
        if (openEnd < 0) throw new InvalidDataException("Thẻ <sheetData> hỏng");

        var sb = new StringBuilder(xml.Length + newRows.Length);
        AppendWithDimension(sb, xml, open, dimensionRef);

        if (xml[openEnd - 1] == '/')
        {
            // <sheetData/> rỗng → mở/đóng để chứa dòng mới.
            sb.Append("<sheetData>").Append(newRows).Append("</sheetData>");
            sb.Append(xml, openEnd + 1, xml.Length - openEnd - 1);
            return Encode(sb, bom);
        }

        var close = xml.IndexOf("</sheetData>", openEnd, StringComparison.Ordinal);
        if (close < 0) throw new InvalidDataException("Thiếu </sheetData>");
        sb.Append(xml, open, openEnd + 1 - open);

        var pos = openEnd + 1;
        var lastRow = 0;
        while (true)
        {
            var start = xml.IndexOf("<row", pos, close - pos, StringComparison.Ordinal);
            if (start < 0) break;
            var tagEnd = xml.IndexOf('>', start, close - start);
            if (tagEnd < 0) throw new InvalidDataException("Thẻ <row> hỏng");
            var selfClosing = xml[tagEnd - 1] == '/';
            int end;
            if (selfClosing) end = tagEnd + 1;
            else
            {
                var rowClose = xml.IndexOf("</row>", tagEnd, close - tagEnd, StringComparison.Ordinal);
                if (rowClose < 0) throw new InvalidDataException("Thiếu </row>");
                end = rowClose + "</row>".Length;
            }
            var rowNo = RowNumber(xml, start, tagEnd) ?? lastRow + 1;
            lastRow = rowNo;
            if (rowNo < TemplateSchema.DataStartRow) sb.Append(xml, start, end - start);
            pos = end;
        }
        sb.Append(newRows);
        sb.Append(xml, close, xml.Length - close);
        return Encode(sb, bom);
    }

    /// <summary>Chép <c>xml[0..until)</c>, thay giá trị <c>ref</c> của <c>&lt;dimension&gt;</c> đầu tiên (nếu có).</summary>
    private static void AppendWithDimension(StringBuilder sb, string xml, int until, string dimensionRef)
    {
        var dim = xml.IndexOf("<dimension", 0, until, StringComparison.Ordinal);
        if (dim >= 0)
        {
            var dimEnd = xml.IndexOf('>', dim, until - dim);
            var refAt = dimEnd < 0 ? -1 : xml.IndexOf("ref=\"", dim, dimEnd - dim, StringComparison.Ordinal);
            if (refAt >= 0)
            {
                var valStart = refAt + "ref=\"".Length;
                var valEnd = xml.IndexOf('"', valStart, dimEnd - valStart);
                if (valEnd >= 0)
                {
                    sb.Append(xml, 0, valStart).Append(dimensionRef);
                    sb.Append(xml, valEnd, until - valEnd);
                    return;
                }
            }
        }
        sb.Append(xml, 0, until);
    }

    /// <summary>Giá trị thuộc tính <c>r</c> trong thẻ mở <c>&lt;row …&gt;</c> (xml[start..tagEnd]).</summary>
    private static int? RowNumber(string xml, int start, int tagEnd)
    {
        var at = start;
        while (true)
        {
            at = xml.IndexOf("r=\"", at, tagEnd - at, StringComparison.Ordinal);
            if (at < 0) return null;
            // đúng thuộc tính "r" (đứng sau khoảng trắng), không phải "spans=…" hay "customHeight=…".
            if (char.IsWhiteSpace(xml[at - 1]))
            {
                var vs = at + 3;
                var ve = xml.IndexOf('"', vs, tagEnd - vs);
                return ve > vs && int.TryParse(xml.AsSpan(vs, ve - vs), NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                    ? n : null;
            }
            at += 3;
        }
    }

    private static byte[] Encode(StringBuilder sb, bool bom)
    {
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        if (!bom) return body;
        var withBom = new byte[body.Length + 3];
        withBom[0] = 0xEF; withBom[1] = 0xBB; withBom[2] = 0xBF;
        Buffer.BlockCopy(body, 0, withBom, 3, body.Length);
        return withBom;
    }

    /// <summary>Tên đúng dạng <c>&lt;prefix&gt;-&lt;số&gt;.xlsx</c> mà <see cref="WriteFiles"/> sinh ra.</summary>
    private static bool IsOutputName(string name, string prefix)
    {
        if (!name.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase)
            || !name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) return false;
        var number = name.AsSpan(prefix.Length + 1, name.Length - prefix.Length - 1 - ".xlsx".Length);
        return number.Length > 0 && number.IndexOfAnyExceptInRange('0', '9') < 0;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
