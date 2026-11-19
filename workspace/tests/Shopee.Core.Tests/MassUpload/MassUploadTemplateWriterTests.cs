using System.Text;
using Shopee.Core.MassUpload;
using static Shopee.Core.Tests.MassUpload.MassUploadTestData;

namespace Shopee.Core.Tests.MassUpload;

public sealed class MassUploadTemplateWriterTests : IDisposable
{
    private readonly string _dir = NewTempDir();

    public void Dispose() => HarvestTestData.TryDelete(_dir);

    private static MassUploadProduct Product(long itemId, int rows, string? description = null)
    {
        var l = OneTier(Enumerable.Range(0, rows).Select(i => 10_000L + i).ToArray(), description: description)
            with { ItemId = itemId };
        return MapOk(l, AdvancedSchema);
    }

    /// <summary>Đọc lại file: mọi ô dòng ≥ 7 phải đúng bằng dòng mapper (theo mã cột → chữ cái), không thừa ô nào.</summary>
    private static void AssertRowsMatch(string file, TemplateSchema schema, IReadOnlyList<MassUploadProduct> products,
        MassUploadFile written)
    {
        var sheet = ReadSheet(file, TemplateSchema.MainSheetName);
        var expectedRows = products.SelectMany(p => p.Rows).ToList();
        var dataRows = sheet.Keys.Where(r => r >= 7).OrderBy(r => r).ToList();
        Assert.Equal(Enumerable.Range(7, expectedRows.Count), dataRows);
        for (var i = 0; i < expectedRows.Count; i++)
        {
            var got = sheet[7 + i];
            var want = expectedRows[i].Where(kv => kv.Value.Text.Length > 0).ToList();
            Assert.Equal(want.Count, got.Count);
            foreach (var (code, cell) in want)
            {
                Assert.True(schema.TryGetColumn(code, out var col));
                var read = got[col.Letter];
                Assert.Equal(cell.Text, read.Value);
                Assert.Equal(col.Style?.ToString(), read.Style);
                Assert.Equal(col.IsNumeric && cell.IsNumber ? null : "inlineStr", read.Type);
            }
        }
        var next = 7;
        Assert.Equal(products.Count, written.Items.Count);
        for (var p = 0; p < products.Count; p++)
        {
            Assert.Equal(products[p].ItemId, written.Items[p].ItemId);
            Assert.Equal(Enumerable.Range(next, products[p].Rows.Count), written.Items[p].Rows);
            next += products[p].Rows.Count;
        }
    }

    [Theory]
    [InlineData(Advanced)]
    [InlineData(Basic)]
    public void Ghi_doc_lai_tung_o_va_moi_part_khac_giu_nguyen(string template)
    {
        var tpl = TemplatePath(template);
        var schema = TemplateSchema.Load(tpl);
        var products = new[] { "pdp-2tang-8model.json", "pdp-motaanh-brand-voucher.json" }
            .Select(f => MapOk(Fixture(f), schema)).ToList();

        var files = MassUploadTemplateWriter.WriteFiles(tpl, schema, products, _dir, "out", 30);
        var f = Assert.Single(files);
        Assert.Equal(Path.Combine(_dir, "out-01.xlsx"), f.Path);
        AssertRowsMatch(f.Path, schema, products, f);

        // Mọi part khác giống hệt nội dung (so hash sau giải nén), không thêm/bớt part.
        var before = PartHashes(tpl);
        var after = PartHashes(f.Path);
        Assert.Equal(before.Keys.OrderBy(k => k), after.Keys.OrderBy(k => k));
        foreach (var (part, hash) in before)
            if (part != schema.SheetPart) Assert.Equal(hash, after[part]);

        // Sheet chính: ngoài dimension và các dòng ≥ 7, từng ký tự giữ nguyên (dòng 1–6, cols, protection, validation…).
        var orig = PartText(tpl, schema.SheetPart);
        var now = PartText(f.Path, schema.SheetPart);
        var lastCol = schema.Columns[^1].Letter;
        Assert.Contains($"<dimension ref=\"A1:{lastCol}{6 + 10}\"", now); // 8 + 2 dòng
        var head = orig.IndexOf("<row r=\"7\"", StringComparison.Ordinal);
        var origData = orig.IndexOf("</sheetData>", StringComparison.Ordinal);
        var nowRow7 = now.IndexOf("<row r=\"7\"", StringComparison.Ordinal);
        var nowData = now.IndexOf("</sheetData>", StringComparison.Ordinal);
        Assert.Equal(-1, head); // bản mẫu chưa có dữ liệu
        Assert.Equal(StripDimension(orig[..origData]), StripDimension(now[..nowRow7]));
        Assert.Equal(orig[origData..], now[nowData..]);

        // Dòng 1–6 đọc lại y như bản mẫu.
        var origSheet = ReadSheet(tpl, TemplateSchema.MainSheetName);
        var nowSheet = ReadSheet(f.Path, TemplateSchema.MainSheetName);
        for (var r = 1; r <= 6; r++) Assert.Equal(origSheet[r], nowSheet[r]);
    }

    private static string StripDimension(string xml)
    {
        var at = xml.IndexOf("<dimension ref=\"", StringComparison.Ordinal);
        var end = xml.IndexOf('"', at + 16);
        return xml[..at] + xml[end..];
    }

    [Theory]
    [InlineData(32, null)]   // bỏ channel_id.5004 (AF) → mọi cột sau dời trái 1
    [InlineData(null, 30)]   // chèn kênh mới ở AD → mọi cột từ AD dời phải 1
    [InlineData(32, 30)]     // vừa bỏ vừa chèn: AD..AE dời phải, sau AF giữ nguyên chữ cái nhưng mã khác
    public void Bo_cot_kenh_khac_van_ghi_dung_cot_theo_ma(int? removeCol, int? insertCol)
    {
        var tpl = ShiftColumnsTemplate(_dir, removeCol, insertCol);
        var schema = TemplateSchema.Load(tpl);
        var orig = AdvancedSchema;
        int Map(int c)
        {
            if (removeCol is int r && c > r) c--;
            if (insertCol is int k && c >= k) c++;
            return c;
        }

        // Schema: số cột, mã kênh, chữ cái mới của các cột phía sau (thuộc tính / ps_brand / et_title_reason).
        Assert.Equal(69 - (removeCol is null ? 0 : 1) + (insertCol is null ? 0 : 1), schema.Columns.Count);
        Assert.Equal(removeCol is null, schema.TryGetColumn("channel_id.5004", out _));
        if (insertCol is int ins)
        {
            Assert.True(schema.TryGetColumn(NewChannelCode, out var nc));
            Assert.Equal(Letter(ins), nc.Letter);
        }
        foreach (var col in orig.Columns.Where(c => c.Index != removeCol))
        {
            Assert.True(schema.TryGetColumn(col.Code, out var now), col.Code);
            Assert.Equal(Letter(Map(col.Index)), now.Letter);
            Assert.Equal((col.Style, col.IsNumeric, col.Required, col.Title), (now.Style, now.IsNumeric, now.Required, now.Title));
        }
        Assert.True(schema.TryGetColumn("ps_brand", out var brand));
        Assert.Equal(Letter(Map(38)), brand.Letter);
        // HiddenCatProps dời theo → mức thuộc tính vẫn đúng mã; HiddenAttr khoá theo mã nên không đổi.
        var gender = "ps_product_global_attribute.100022";
        Assert.Equal(AttributeRequirement.Mandatory, schema.GetRequirement(100026, gender));
        Assert.Equal(["ps_product_global_attribute.100022"],
            schema.Columns.Where(c => schema.GetRequirement(100026, c.Code) == AttributeRequirement.Mandatory).Select(c => c.Code));
        Assert.Equal(AttributeRequirement.Optional, schema.GetRequirement(100026, "ps_product_global_attribute.100169"));
        Assert.Equal(["Nữ", "Nam", "Unisex"], schema.GetListValues(100026, gender));

        // Ghi: dữ liệu vào đúng chữ cái MỚI theo mã.
        var products = new[] { "pdp-2tang-8model.json", "pdp-motaanh-brand-voucher.json" }
            .Select(f => MapOk(Fixture(f), schema)).ToList();
        var f = Assert.Single(MassUploadTemplateWriter.WriteFiles(tpl, schema, products, _dir, "shift", 30));
        AssertRowsMatch(f.Path, schema, products, f);
        var sheet = ReadSheet(f.Path, TemplateSchema.MainSheetName);
        Assert.True(schema.TryGetColumn(gender, out var g));
        Assert.True(schema.TryGetColumn("ps_product_global_attribute.100169", out var style));
        Assert.True(schema.TryGetColumn("et_title_reason", out var reason));
        Assert.Equal("Nữ", sheet[7][g.Letter].Value);
        Assert.Equal("Binh thương;Thanh lịch", sheet[15][style.Letter].Value); // dòng đầu của SP thứ 2
        Assert.False(sheet[7].ContainsKey(brand.Letter));
        Assert.False(sheet[7].ContainsKey(reason.Letter));
        Assert.Contains($"<dimension ref=\"A1:{reason.Letter}16\"", PartText(f.Path, schema.SheetPart));
    }

    [Fact]
    public void ItemId_trung_trong_mot_luot_thi_loi_ro_khong_ghi_gi()
    {
        var products = new[] { Product(1, 2), Product(2, 1), Product(1, 3) };
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MassUploadTemplateWriter.WriteFiles(TemplatePath(Advanced), AdvancedSchema, products, _dir, "dup", 30));
        Assert.Contains("SP 1 xuất hiện 2 lần", ex.Message);
        Assert.Empty(Directory.GetFiles(_dir));
        // Cả khi 2 bản trùng sẽ rơi vào 2 file khác nhau (N = 1).
        Assert.Throws<InvalidOperationException>(() =>
            MassUploadTemplateWriter.WriteFiles(TemplatePath(Advanced), AdvancedSchema, products, _dir, "dup", 1));
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Khong_ghi_de_file_da_ton_tai()
    {
        var products = new[] { Product(1, 2) };
        var existing = Path.Combine(_dir, "ow-01.xlsx");
        File.WriteAllText(existing, "cũ");
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MassUploadTemplateWriter.WriteFiles(TemplatePath(Advanced), AdvancedSchema, products, _dir, "ow", 30));
        Assert.Contains("ow-01.xlsx", ex.Message);
        Assert.Equal("cũ", File.ReadAllText(existing));
        Assert.Equal([existing], Directory.GetFiles(_dir));

        // Chỉ file có số của lượt trước (ow-07) cũng chặn — không ghi gì.
        File.Delete(existing);
        File.WriteAllText(Path.Combine(_dir, "ow-07.xlsx"), "cũ");
        Assert.Throws<InvalidOperationException>(() =>
            MassUploadTemplateWriter.WriteFiles(TemplatePath(Advanced), AdvancedSchema, products, _dir, "ow", 30));
        Assert.Single(Directory.GetFiles(_dir));

        // Tên khác dạng (tiền tố khác / không phải số) không chặn.
        File.WriteAllText(Path.Combine(_dir, "ow-x.xlsx"), "");
        File.WriteAllText(Path.Combine(_dir, "owa-01.xlsx"), "");
        var ok = MassUploadTemplateWriter.WriteFiles(TemplatePath(Advanced), AdvancedSchema, products, _dir, "o", 30);
        Assert.Equal("o-01.xlsx", Path.GetFileName(Assert.Single(ok).Path));

        // Ghi lần 2 cùng tiền tố → từ chối, file lần 1 còn nguyên.
        var before = File.ReadAllBytes(ok[0].Path);
        Assert.Throws<InvalidOperationException>(() =>
            MassUploadTemplateWriter.WriteFiles(TemplatePath(Advanced), AdvancedSchema, products, _dir, "o", 30));
        Assert.Equal(before, File.ReadAllBytes(ok[0].Path));
    }

    [Fact]
    public void Ghi_lai_len_file_da_co_du_lieu_thi_xoa_dong_cu()
    {
        var tpl = TemplatePath(Gd0Result); // bản mẫu basic GĐ0 đã có 29 dòng dữ liệu
        var schema = TemplateSchema.Load(tpl);
        var products = new[] { MapOk(Fixture("pdp-khong-bien-the.json"), schema) };
        var f = Assert.Single(MassUploadTemplateWriter.WriteFiles(tpl, schema, products, _dir, "re", 30));
        var sheet = ReadSheet(f.Path, TemplateSchema.MainSheetName);
        Assert.Equal([7], sheet.Keys.Where(r => r >= 7));
        Assert.Contains("<dimension ref=\"A1:AL7\"", PartText(f.Path, schema.SheetPart));
    }

    [Fact]
    public void Ky_tu_dac_biet_ghi_va_doc_lai_dung()
    {
        const string name = "Vòng <tay> & \"bạc\" 'xịn' 💎 ok";
        var desc = "Dòng 1 <b>&amp;</b>\nDòng 2 😀 emoji\r\nDòng 3\ttab\u0001\u0008 điều khiển\uD800 lẻ " + LongText(120);
        var l = OneTier([10_000], title: name, description: desc);
        var p = MapOk(l, AdvancedSchema);
        var f = MassUploadTemplateWriter.WriteFile(File.ReadAllBytes(TemplatePath(Advanced)), AdvancedSchema, [p],
            Path.Combine(_dir, "special.xlsx"));
        var row = ReadSheet(f.Path, TemplateSchema.MainSheetName)[7];
        Assert.Equal(name, row["B"].Value);
        var expectDesc = "Dòng 1 <b>&amp;</b>\nDòng 2 😀 emoji\nDòng 3\ttab điều khiển lẻ " + LongText(120);
        Assert.Equal(expectDesc, row["C"].Value);
    }

    [Fact]
    public void Writer_tu_lam_sach_SP_dung_tay_khong_qua_mapper()
    {
        // Dòng dựng TAY (bỏ qua mapper): writer phải tự lọc ký tự điều khiển / surrogate lẻ, CR → LF, NFC.
        var nfd = "Vòng bạc".Normalize(NormalizationForm.FormD);
        Assert.NotEqual("Vòng bạc", nfd);
        var dirtyName = nfd + "\u0001 tay\uD800 <&>";
        var dirtyDesc = "A\rB\r\nC\u0008\uDC00 " + nfd + "\t\"q\"";
        var p = Product(1, 1);
        var row = new Dictionary<string, MassUploadCell>(p.Rows[0])
        {
            [MassUploadMapper.ColName] = MassUploadCell.Of(dirtyName),
            [MassUploadMapper.ColDescription] = MassUploadCell.Of(dirtyDesc),
            [MassUploadMapper.ColTier1Option] = MassUploadCell.Of("Hồng".Normalize(NormalizationForm.FormD) + "\u001F"),
        };
        var hand = new MassUploadProduct(1, 100009, 100026, [row], [], [], []);
        var f = MassUploadTemplateWriter.WriteFile(File.ReadAllBytes(TemplatePath(Advanced)), AdvancedSchema, [hand],
            Path.Combine(_dir, "hand.xlsx"));
        var read = ReadSheet(f.Path, TemplateSchema.MainSheetName)[7];
        Assert.Equal("Vòng bạc tay <&>", read["B"].Value);
        Assert.Equal("A\nB\nC Vòng bạc\t\"q\"", read["C"].Value);
        Assert.Equal("Hồng", read["G"].Value);
        Assert.All(new[] { read["B"].Value, read["C"].Value, read["G"].Value },
            v => Assert.True(v.IsNormalized(NormalizationForm.FormC)));
        // XML thô: không còn CR (XML đọc lại đã chuẩn hoá CR nên kiểm thẳng trên part).
        var xml = PartText(f.Path, AdvancedSchema.SheetPart);
        var at = xml.IndexOf("<c r=\"C7\"", StringComparison.Ordinal);
        Assert.DoesNotContain('\r', xml[at..xml.IndexOf("</c>", at, StringComparison.Ordinal)]);
    }

    [Fact]
    public void Chia_file_theo_N_SP_mot_SP_khong_bi_tach()
    {
        var products = Enumerable.Range(1, 5).Select(i => Product(i, 3)).ToList();
        var files = MassUploadTemplateWriter.WriteFiles(TemplatePath(Advanced), AdvancedSchema, products, _dir, "n", 2);
        Assert.Equal([2, 2, 1], files.Select(f => f.Items.Count));
        Assert.Equal(["n-01.xlsx", "n-02.xlsx", "n-03.xlsx"], files.Select(f => Path.GetFileName(f.Path)));
        Assert.Equal(products.Select(p => p.ItemId), files.SelectMany(f => f.Items).Select(i => i.ItemId));
        AssertRowsMatch(files[1].Path, AdvancedSchema, products.Skip(2).Take(2).ToList(), files[1]);
    }

    [Fact]
    public void Chia_file_theo_so_dong()
    {
        var products = Enumerable.Range(1, 4).Select(i => Product(i, 15)).ToList();
        var lim = MassUploadLimits.Default with { MaxDataRowsPerFile = 40 };
        var files = MassUploadTemplateWriter.WriteFiles(TemplatePath(Advanced), AdvancedSchema, products, _dir, "r", 30, lim);
        Assert.Equal([2, 2], files.Select(f => f.Items.Count)); // 15 + 15 = 30 ≤ 40, thêm SP thứ 3 = 45 > 40
        Assert.All(files, f => Assert.True(f.Items.Sum(i => i.Rows.Count) <= 40));
    }

    [Fact]
    public void Chia_file_theo_3MB_khi_mo_ta_dai()
    {
        // Mô tả 3000 ký tự ngẫu nhiên (336 ký hiệu ≈ 8,4 bit/ký tự — gần như không nén được), khác nhau ở từng dòng:
        // 100 SP × 10 dòng = 1000 dòng (vừa trần N và trần dòng) nhưng file nén > 3 MB → phải bớt SP.
        var rnd = new Random(7);
        var template = Product(0, 10);
        var products = Enumerable.Range(1, 120).Select(i => template with
        {
            ItemId = i,
            Rows = template.Rows.Select(r => (IReadOnlyDictionary<string, MassUploadCell>)new Dictionary<string, MassUploadCell>(r)
            {
                [MassUploadMapper.ColDescription] = MassUploadCell.Of(
                    new string(Enumerable.Range(0, 3000).Select(_ => (char)rnd.Next(0x100, 0x250)).ToArray())),
            }).ToList(),
        }).ToList();
        var files = MassUploadTemplateWriter.WriteFiles(TemplatePath(Advanced), AdvancedSchema, products, _dir, "mb", 100);
        Assert.True(files.Count >= 2);
        Assert.True(files[0].Items.Count < 100, $"file đầu {files[0].Items.Count} SP — trần 3 MB phải cắt trước trần N/dòng");
        Assert.All(files, f =>
        {
            Assert.True(f.SizeBytes <= 3_000_000, $"{f.Path}: {f.SizeBytes}");
            Assert.Equal(f.SizeBytes, new FileInfo(f.Path).Length);
        });
        Assert.Equal(products.Select(p => p.ItemId), files.SelectMany(f => f.Items).Select(i => i.ItemId));
        Assert.Equal(files.Count, Directory.GetFiles(_dir, "mb-*.xlsx").Length); // không để file thừa
        AssertRowsMatch(files[^1].Path, AdvancedSchema,
            products.Skip(files.Take(files.Count - 1).Sum(f => f.Items.Count)).ToList(), files[^1]);
    }

    [Fact]
    public void SP_khac_nganh_cap_1_voi_ban_nang_cao_thi_loi_ro()
    {
        var p = MapOk(Fixture("pdp-1tang-70model-4capdm.json"), BasicSchema); // ngành cấp 1 100637
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MassUploadTemplateWriter.WriteFiles(TemplatePath(Advanced), AdvancedSchema, [p], _dir, "x", 30));
        Assert.Contains("100637", ex.Message);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Thieu_cot_bat_buoc_hoac_ma_cot_la_thi_loi_ro()
    {
        var p = Product(1, 1);
        var missing = p with { Rows = p.Rows.Select(r => (IReadOnlyDictionary<string, MassUploadCell>)r
            .Where(kv => kv.Key != MassUploadMapper.ColWeight).ToDictionary(kv => kv.Key, kv => kv.Value)).ToList() };
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MassUploadTemplateWriter.WriteFiles(TemplatePath(Advanced), AdvancedSchema, [missing], _dir, "x", 30));
        Assert.Contains("ps_weight", ex.Message);

        var unknown = p with { Rows = p.Rows.Select(r => (IReadOnlyDictionary<string, MassUploadCell>)
            new Dictionary<string, MassUploadCell>(r) { ["ps_khong_co"] = MassUploadCell.Of("x") }).ToList() };
        ex = Assert.Throws<InvalidOperationException>(() =>
            MassUploadTemplateWriter.WriteFiles(TemplatePath(Advanced), AdvancedSchema, [unknown], _dir, "x", 30));
        Assert.Contains("ps_khong_co", ex.Message);
    }

    [Fact]
    public void Viet_lai_sheet_chi_bo_dong_tu_7_ke_ca_dong_tu_dong()
    {
        const string xml = "<?xml version=\"1.0\"?><worksheet><dimension ref=\"A1\"></dimension><sheetData>"
                           + "<row r=\"6\" spans=\"1:2\"><c r=\"A6\"><v>6</v></c></row><row r=\"7\"/>"
                           + "<row customHeight=\"1\" r=\"8\"><c r=\"A8\"><v>8</v></c></row></sheetData><x r=\"9\"/></worksheet>";
        var bytes = MassUploadTemplateWriter.RewriteSheet(Encoding.UTF8.GetBytes(xml), "<row r=\"7\">N</row>", "A1:B7");
        Assert.Equal("<?xml version=\"1.0\"?><worksheet><dimension ref=\"A1:B7\"></dimension><sheetData>"
                     + "<row r=\"6\" spans=\"1:2\"><c r=\"A6\"><v>6</v></c></row><row r=\"7\">N</row></sheetData><x r=\"9\"/></worksheet>",
            Encoding.UTF8.GetString(bytes));
    }
}
