using System.Security.Cryptography;
using Shopee.Core.Coordination;
using Shopee.Core.MassUpload.Images;
using static Shopee.Core.Tests.MassUpload.Images.ImageTestData;

namespace Shopee.Core.Tests.MassUpload.Images;

/// <summary>GĐ2b: <see cref="ImageMd5Changer"/> — đổi MD5 ở mức byte (JPEG COM sau SOI, PNG tEXt sau IHDR), tất định theo
/// (shop, ảnh), file ra vẫn đúng cấu trúc, từ chối định dạng khác / quá cỡ.</summary>
public sealed class ImageMd5ChangerTests
{
    private static string Md5(byte[] b) => Convert.ToHexString(MD5.HashData(b)).ToLowerInvariant();

    /// <summary>Ghép một segment JPEG: <c>FF marker</c> + độ dài (gồm 2 byte độ dài) + payload.</summary>
    private static byte[] Seg(byte marker, byte[] payload) =>
        [0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)((payload.Length + 2) & 0xFF), .. payload];

    /// <summary>JPEG tự dựng: SOI + các segment đầu cho trước + DQT + SOS + vài byte dữ liệu nén + EOI.</summary>
    private static byte[] SyntheticJpeg(params byte[][] head) =>
        [0xFF, 0xD8, .. head.SelectMany(b => b), .. Seg(0xDB, new byte[65]), .. Seg(0xDA, new byte[10]), 0x12, 0x34, 0x56, 0xFF, 0xD9];

    private static readonly byte[] App0 = Seg(0xE0, [.. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 0, 0]);
    private static readonly byte[] App1 = Seg(0xE1, [.. "Exif\0\0"u8, .. "MM\0*"u8, 0, 0, 0, 8, 0, 0]);

    /// <summary>Kiểm 1 JPEG đầu vào: COM nằm NGAY SAU dãy APPn, bytes trước/sau điểm chèn giữ nguyên, cấu trúc hợp lệ.</summary>
    private static void AssertComAfterApp(byte[] src, int appCount)
    {
        var r = ImageMd5Changer.Change(src, "shop-a", "vn-111");

        Assert.Equal("image/jpeg", r.ContentType);
        Assert.Equal(Md5(r.Bytes), r.Md5);
        Assert.NotEqual(Md5(src), r.Md5);
        var before = WalkJpeg(src);
        var after = WalkJpeg(r.Bytes);
        Assert.All(before.Take(appCount), m => Assert.InRange(m, (byte)0xE0, (byte)0xEF));
        Assert.False(before[appCount] is >= 0xE0 and <= 0xEF);
        Assert.Equal([.. before.Take(appCount), 0xFE, .. before.Skip(appCount)], after);   // COM sau APPn, thứ tự giữ nguyên
        var at = 2;
        for (var i = 0; i < appCount; i++) at += 2 + ((src[at + 2] << 8) | src[at + 3]);
        var segLen = (r.Bytes[at + 2] << 8) | r.Bytes[at + 3];
        Assert.Equal(src.AsSpan(0, at).ToArray(), r.Bytes.AsSpan(0, at).ToArray());
        Assert.Equal(src.AsSpan(at).ToArray(), r.Bytes.AsSpan(at + 2 + segLen).ToArray());
        Assert.Equal(src.Length + 2 + segLen, r.Bytes.Length);
        Assert.Equal(r.Bytes, ImageMd5Changer.Change(src, "shop-a", "vn-111").Bytes);       // tất định
    }

    [Fact]
    public void Jpeg_Fixture_App0Jfif_ComSauApp0() => AssertComAfterApp(Jpeg, 1);

    [Fact]
    public void Jpeg_App0VaApp1Exif_ComSauCaHai() => AssertComAfterApp(SyntheticJpeg(App0, App1), 2);

    [Fact]
    public void Jpeg_KhongCoAppn_ComNgaySauSoi() => AssertComAfterApp(SyntheticJpeg(), 0);

    [Fact]
    public void Jpeg_SegmentAppnVuotCoFile_TuChoi()
    {
        byte[] bad = [0xFF, 0xD8, 0xFF, 0xE1, 0x7F, 0xFF, 1, 2, 3];
        var ex = Assert.Throws<ImageMd5Exception>(() => ImageMd5Changer.Change(bad, "s", "k"));
        Assert.Contains("JPEG hỏng", ex.Message);
    }

    [Fact]
    public void Png_ChenTextSauIhdr_CrcDung_DiemAnhGiuNguyen()
    {
        var src = Png;
        var r = ImageMd5Changer.Change(src, "shop-a", "vn-222");

        Assert.Equal("image/png", r.ContentType);
        Assert.Equal(Md5(r.Bytes), r.Md5);
        Assert.NotEqual(Md5(src), r.Md5);
        var chunks = WalkPng(r.Bytes);                                     // ném nếu CRC sai / thứ tự sai
        Assert.Equal("IHDR", chunks[0].Type);
        Assert.Equal("tEXt", chunks[1].Type);
        Assert.StartsWith("Comment\0", System.Text.Encoding.Latin1.GetString(chunks[1].Data));
        Assert.Equal(WalkPng(src).Select(c => c.Type), chunks.Where((_, i) => i != 1).Select(c => c.Type));
        Assert.Equal(PngPixels(src), PngPixels(r.Bytes));
    }

    [Fact]
    public void TatDinh_CungShopCungAnh_CungBytes_KhacShopKhacMd5()
    {
        foreach (var src in new[] { Jpeg, Png })
        {
            var a1 = ImageMd5Changer.Change(src, "shop-a", "vn-1");
            var a2 = ImageMd5Changer.Change(src, "shop-a", "vn-1");
            var b = ImageMd5Changer.Change(src, "shop-b", "vn-1");
            var otherKey = ImageMd5Changer.Change(src, "shop-a", "vn-2");

            Assert.Equal(a1.Bytes, a2.Bytes);
            Assert.NotEqual(a1.Md5, b.Md5);
            Assert.NotEqual(a1.Md5, otherKey.Md5);
            Assert.Equal(a1.Bytes.Length, b.Bytes.Length);
        }
    }

    [Theory]
    [InlineData("webp")]
    [InlineData("gif")]
    [InlineData("rac")]
    [InlineData("rong")]
    [InlineData("png-hong")]
    public void DinhDangKhac_Tu_Choi_CoLyDo(string kind)
    {
        byte[] data = kind switch
        {
            "webp" => [.. "RIFF"u8, 0x10, 0, 0, 0, .. "WEBPVP8 "u8, 0, 0, 0, 0],
            "gif" => [.. "GIF89a"u8, 1, 0, 1, 0, 0, 0, 0],
            "rac" => [.. "hello world, khong phai anh"u8],
            "rong" => [],
            _ => [.. Png.AsSpan(0, 8), 0, 0, 0, 5, .. "IDAT"u8, 1, 2, 3, 4, 5, 0, 0, 0, 0, .. new byte[20]],
        };
        var ex = Assert.Throws<ImageMd5Exception>(() => ImageMd5Changer.Change(data, "s", "k"));
        Assert.Contains("không hỗ trợ", ex.Message);
        if (kind == "webp") Assert.Contains("WebP", ex.Message);
        if (kind == "gif") Assert.Contains("GIF", ex.Message);
    }

    [Fact]
    public void QuaCo_TuChoi_CaAnhGoc_LanAnhRa()
    {
        var jpeg = Jpeg;
        var big = new byte[PublicMediaLimits.MaxBytes + 1];
        jpeg.CopyTo(big, 0);
        var ex = Assert.Throws<ImageMd5Exception>(() => ImageMd5Changer.Change(big, "s", "k"));
        Assert.Contains("vượt trần", ex.Message);

        // Ảnh gốc vừa đúng trần: hợp lệ đầu vào, nhưng chèn thêm segment thì vượt → cũng từ chối (Hub sẽ trả 413).
        var edge = new byte[PublicMediaLimits.MaxBytes];
        jpeg.CopyTo(edge, 0);
        Assert.Throws<ImageMd5Exception>(() => ImageMd5Changer.Change(edge, "s", "k"));
    }
}
