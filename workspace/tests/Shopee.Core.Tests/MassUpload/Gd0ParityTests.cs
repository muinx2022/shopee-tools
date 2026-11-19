using Shopee.Core.MassUpload;
using static Shopee.Core.Tests.MassUpload.MassUploadTestData;

namespace Shopee.Core.Tests.MassUpload;

/// <summary>
/// Đối chứng GĐ0: ghi bản mẫu basic với đúng 3 fixture script GĐ0 đã dùng, so các ô chính với file GĐ0 Shopee đã nhận 3/3
/// (<c>Shopee_mass_upload_GD0_TEST_3sp.xlsx</c>). Ngoài D (SKU) / O (ảnh bảng size) vốn mới thêm, và C (GĐ0 bỏ dòng trống
/// 10001 trong mô tả) — mọi ô khác phải khớp.
/// </summary>
public sealed class Gd0ParityTests : IDisposable
{
    private readonly string _dir = NewTempDir();

    public void Dispose() => HarvestTestData.TryDelete(_dir);

    [Fact]
    public void Ghi_ban_basic_khop_file_GD0_Shopee_da_nhan()
    {
        const string cdn = "https://down-vn.img.susercontent.com/file/";
        var defaults = new MassUploadDefaults(100, 200, 10, 10, 10, "size");
        var schema = BasicSchema;
        var products = new[] { "pdp-khong-bien-the.json", "pdp-2tang-8model.json", "pdp-1tang-70model-4capdm.json" }
            .Select(f => (MassUploadProduct)MassUploadMapper.Map(Fixture(f), defaults, schema, id => cdn + id))
            .ToList();
        var file = Assert.Single(MassUploadTemplateWriter.WriteFiles(TemplatePath(Basic), schema, products, _dir, "gd0", 30));

        var ours = ReadSheet(file.Path, TemplateSchema.MainSheetName);
        var gd0 = ReadSheet(TemplatePath(Gd0Result), TemplateSchema.MainSheetName);
        var ourRows = ours.Keys.Where(r => r >= 7).OrderBy(r => r).ToList();
        var gd0Rows = gd0.Keys.Where(r => r >= 7).OrderBy(r => r).ToList();
        Assert.Equal(29, gd0Rows.Count); // 1 + 8 + 20
        Assert.Equal(gd0Rows, ourRows);

        var ignored = new HashSet<string> { "C", "D", "O" };
        foreach (var r in gd0Rows)
        {
            var want = gd0[r].Where(kv => !ignored.Contains(kv.Key)).ToDictionary();
            var got = ours[r].Where(kv => !ignored.Contains(kv.Key)).ToDictionary();
            Assert.Equal(want.Keys.OrderBy(k => k), got.Keys.OrderBy(k => k));
            foreach (var (col, cell) in want)
                Assert.True(cell == got[col], $"{col}{r}: GĐ0 {cell} ≠ {got[col]}");
        }
        // Các cột cốt lõi đã thực sự được so (không phải so rỗng với rỗng).
        Assert.Equal("100242", gd0[7]["A"].Value);
        Assert.Equal("2500000", ours[7]["K"].Value);
        Assert.Equal("Hộp", ours[8]["I"].Value);
        Assert.Equal("Cúp thế giới.", ours[16]["G"].Value);
        // Cột mới thêm: D = item_id gốc, O = ảnh bảng size dùng chung.
        Assert.Equal("47908509202", ours[7]["D"].Value);
        Assert.Equal(cdn + "size", ours[35]["O"].Value);
    }
}
