using System.Security.Cryptography;
using Shopee.Hub.Web.Services;

namespace Shopee.Hub.Web.Tests;

/// <summary>
/// Bản chép <see cref="ImageMd5Changer"/> trên Hub phải ra ĐÚNG TỪNG BYTE như bản của app Upload
/// (<c>workspace/src/Shopee.Core/MassUpload/Images/ImageMd5Changer.cs</c>). Fixture <c>Fixtures/source-images</c> SINH BẰNG
/// CODE PHÍA CLIENT (dự án console tạm tham chiếu Shopee.Core của workspace): ảnh vào (<c>tiny.jpg</c>/<c>tiny.png</c> chép từ
/// <c>workspace/tests/Shopee.Core.Tests/Fixtures/images</c>, JPEG không APPn, JPEG APP0+APP1) × 3 shop × 2 mã ảnh → bytes ra
/// + MD5 trong <c>vectors.tsv</c>; WebP: client từ chối, dòng <c>#webp</c> ghi thông điệp.
/// </summary>
public sealed class SourceImageMd5ParityTests
{
    internal static string FixtureDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Fixtures", "source-images");
            if (File.Exists(Path.Combine(candidate, "vectors.tsv"))) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Fixtures/source-images");
    }

    internal sealed record Vector(string Input, string Shop, string Id, string Output, string Md5, string ContentType);

    internal static List<Vector> Vectors() =>
        File.ReadAllLines(Path.Combine(FixtureDir(), "vectors.tsv"))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split('\t'))
            .Select(p => new Vector(p[0], p[1], p[2], p[3], p[4], p[5]))
            .ToList();

    internal static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(FixtureDir(), name));

    [Fact]
    public void MoiVector_KhopTungByteVaMd5VoiClient()
    {
        var vectors = Vectors();
        Assert.Equal(24, vectors.Count);   // 4 ảnh × 3 shop × 2 id — fixture thiếu dòng là test hỏng, không phải xanh
        foreach (var v in vectors)
        {
            var expected = Fixture(v.Output);
            var r = ImageMd5Changer.Change(Fixture(v.Input), v.Shop, v.Id);
            Assert.True(expected.AsSpan().SequenceEqual(r.Bytes), $"lệch byte: {v.Input} / {v.Shop} / {v.Id}");
            Assert.Equal(v.Md5, r.Md5);
            Assert.Equal(v.Md5, Convert.ToHexString(MD5.HashData(expected)).ToLowerInvariant());
            Assert.Equal(v.ContentType, r.ContentType);
        }
    }

    [Fact]
    public void WebP_TuChoiCungThongDiepVoiClient()
    {
        var line = File.ReadAllLines(Path.Combine(FixtureDir(), "vectors.tsv")).Single(l => l.StartsWith("#webp\t"));
        var ex = Assert.Throws<ImageMd5Exception>(() =>
            ImageMd5Changer.Change(Fixture("tiny.webp"), "shop-a", "vn-11134207-7r98o-lx1abcdefghi12"));
        Assert.Equal(line.Split('\t')[1], ex.Message);
    }
}
