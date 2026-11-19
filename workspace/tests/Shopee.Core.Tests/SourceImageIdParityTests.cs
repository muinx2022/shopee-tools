using System.Text.Json;
using System.Text.Json.Nodes;
using Shopee.Core.Coordination;
using Shopee.Core.MassUpload;
using Shopee.Core.MassUpload.Export;

namespace Shopee.Core.Tests;

/// <summary>
/// GĐ9 — mã ảnh gốc app Harvest (extension, <c>source-images.js</c> <c>imageIdsOf</c>) đẩy lên Hub phải KHỚP mã ảnh app Upload cần lúc
/// xuất (<see cref="ShopeeListingReader"/> + luật <c>MassUploadExporter.ImageKeys</c>: ảnh lựa chọn nhóm 1 khác rỗng + N ảnh SP đầu,
/// khử trùng) — lệch là Hub thiếu ảnh, app Upload phải tải lại CDN. Hai bên đối chiếu MỘT file <c>Fixtures/source-image-ids/cases.json</c>
/// (test JS so đúng thứ tự, test này so TẬP, gọi đúng code exporter qua reflection). Đổi luật ở một bên mà không đổi fixture thì bên
/// đó đỏ; đổi cả hai cho khớp nhau thì phải sửa fixture. C# không lọc id sai luật Hub (exporter tự xếp vào "missing") → lọc
/// <see cref="SourceImageLimits.IsValidId"/> trước khi so; phần bị lọc phải đúng tập <c>skipped</c> mà JS báo.
/// </summary>
public sealed class SourceImageIdParityTests
{
    private static JsonArray Cases()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            foreach (var candidate in new[]
                     {
                         Path.Combine(dir.FullName, "Fixtures", "source-image-ids", "cases.json"),
                         Path.Combine(dir.FullName, "tests", "Shopee.Core.Tests", "Fixtures", "source-image-ids", "cases.json"),
                     })
                if (File.Exists(candidate)) return JsonNode.Parse(File.ReadAllText(candidate))!["cases"]!.AsArray();
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Fixtures/source-image-ids/cases.json");
    }

    /// <summary>Gọi THẲNG <c>MassUploadExporter.ImageKeys</c> (private, chỉ đọc <c>_opt</c>) qua reflection trên một exporter không qua
    /// ctor (ctor cần kho / worker / Hub — không liên quan luật ảnh). Ai đổi luật ảnh của exporter thì test này đỏ ngay; ai đổi tên /
    /// chữ ký hàm thì test đỏ ở đây (không âm thầm so với bản chép).</summary>
    private static List<string> ImageKeys(ShopeeListing listing)
    {
        var t = typeof(MassUploadExporter);
        var exporter = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t);
        var opt = t.GetField("_opt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                  ?? throw new MissingFieldException(t.FullName, "_opt");
        opt.SetValue(exporter, new MassUploadExportOptions());
        var m = t.GetMethod("ImageKeys", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, [typeof(ShopeeListing)])
                ?? throw new MissingMethodException(t.FullName, "ImageKeys");
        return (List<string>)m.Invoke(exporter, [listing])!;
    }

    public static IEnumerable<object[]> CaseNames() =>
        Cases().Select(c => new object[] { c!["name"]!.GetValue<string>() });

    [Fact]
    public void Fixture_CoDuCa()
    {
        Assert.True(Cases().Count >= 8);
        Assert.Equal(12, new MassUploadExportOptions().ProductImagesPerItem);   // JS: PRODUCT_IMAGES_PER_ITEM
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void ImageKeys_KhopFixture(string name)
    {
        var c = Cases().First(x => x!["name"]!.GetValue<string>() == name)!;
        var listing = ShopeeListingReader.Read(c["json"]!.ToJsonString());
        var keys = ImageKeys(listing);

        var expected = c["expected"]!.AsArray().Select(x => x!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var skipped = c["skipped"]!.AsArray().Select(x => x!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        Assert.True(expected.SetEquals(keys.Where(SourceImageLimits.IsValidId)),
            $"C#: [{string.Join(", ", keys.Where(SourceImageLimits.IsValidId))}] ≠ fixture: [{string.Join(", ", expected)}]");
        Assert.True(skipped.SetEquals(keys.Where(k => !SourceImageLimits.IsValidId(k))),
            $"C# id sai luật: [{string.Join(", ", keys.Where(k => !SourceImageLimits.IsValidId(k)))}] ≠ fixture: [{string.Join(", ", skipped)}]");
    }
}
