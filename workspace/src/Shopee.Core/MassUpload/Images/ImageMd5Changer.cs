using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Shopee.Core.Coordination;

namespace Shopee.Core.MassUpload.Images;

/// <summary>Ảnh không đổi MD5 được (định dạng không hỗ trợ, file hỏng, quá cỡ) — <see cref="Exception.Message"/> là lý do
/// tiếng Việt để ghi vào <c>image_prep.last_error</c>. Đây là lỗi VĨNH VIỄN của ảnh nguồn (thử lại cũng vậy).</summary>
public sealed class ImageMd5Exception(string message) : Exception(message);

/// <summary>Ảnh đã đổi MD5: bytes mới, MD5 (hex thường) của bytes đó, kiểu MIME (<c>image/jpeg</c> | <c>image/png</c>).</summary>
public sealed record ImageMd5Result(byte[] Bytes, string Md5, string ContentType);

/// <summary>
/// Đổi MD5 ảnh ở MỨC BYTE, KHÔNG mã hoá lại (plan GĐ2 bước 4.2): giữ nguyên điểm ảnh, chỉ chèn một đoạn siêu dữ liệu chứa
/// chuỗi TẤT ĐỊNH theo (shop, ảnh gốc) — cùng (shop, ảnh) luôn ra cùng bytes; khác shop ra MD5 khác.
/// <list type="bullet">
/// <item>JPEG: segment COM (<c>FF FE</c>) sau dãy APPn đầu file (APP0 JFIF / APP1 Exif…); không có APPn thì ngay sau SOI.</item>
/// <item>PNG: chunk <c>tEXt</c> (keyword <c>Comment</c>, CRC-32 đúng) ngay sau IHDR.</item>
/// </list>
/// Định dạng khác (WebP, GIF, rác), ảnh gốc hoặc ảnh ra vượt <see cref="PublicMediaLimits.MaxBytes"/> →
/// <see cref="ImageMd5Exception"/>.
/// </summary>
public static class ImageMd5Changer
{
    /// <summary>Muối cố định của HMAC — đổi muối là đổi MD5 của MỌI ảnh (ảnh đã host vẫn dùng được, chỉ lần làm lại mới khác).</summary>
    private static readonly byte[] Salt = Encoding.ASCII.GetBytes("shopee-upload/image-md5/v1");

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Đổi MD5 của <paramref name="source"/> cho shop <paramref name="shopKey"/>, ảnh gốc <paramref name="sourceKey"/>.</summary>
    public static ImageMd5Result Change(byte[] source, string shopKey, string sourceKey)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > PublicMediaLimits.MaxBytes)
            throw new ImageMd5Exception($"ảnh gốc {source.Length / 1024} KB vượt trần {PublicMediaLimits.MaxBytes / 1024 / 1024} MB");
        var tag = Tag(shopKey, sourceKey);
        byte[] bytes;
        string type;
        if (IsJpeg(source)) { bytes = InsertJpegComment(source, tag); type = "image/jpeg"; }
        else if (IsPng(source)) { bytes = InsertPngText(source, tag); type = "image/png"; }
        else throw new ImageMd5Exception($"định dạng ảnh không hỗ trợ ({Sniff(source)}), chỉ nhận JPEG/PNG");
        if (bytes.Length > PublicMediaLimits.MaxBytes)
            throw new ImageMd5Exception($"ảnh sau khi đổi MD5 vượt trần {PublicMediaLimits.MaxBytes / 1024 / 1024} MB");
        return new ImageMd5Result(bytes, Md5Hex(bytes), type);
    }

    /// <summary>MD5 (hex thường) của một mảng bytes — dùng chung cho bước kiểm ảnh đã host.</summary>
    public static string Md5Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();

    /// <summary>Chuỗi ASCII 64 ký tự hex = HMAC-SHA256(muối, shop ‖ 0 ‖ ảnh): tất định, không đoán ngược ra shop.</summary>
    internal static byte[] Tag(string shopKey, string sourceKey)
    {
        var data = Encoding.UTF8.GetBytes((shopKey ?? "") + "\0" + (sourceKey ?? ""));
        return Encoding.ASCII.GetBytes(Convert.ToHexString(HMACSHA256.HashData(Salt, data)).ToLowerInvariant());
    }

    private static bool IsJpeg(byte[] b) => b.Length >= 4 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

    /// <summary>PNG hợp lệ ở mức cần cho việc chèn: chữ ký + chunk đầu là IHDR dài 13.</summary>
    private static bool IsPng(byte[] b) =>
        b.Length >= 33 && b.AsSpan(0, 8).SequenceEqual(PngSignature)
        && BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(8, 4)) == 13
        && b[12] == (byte)'I' && b[13] == (byte)'H' && b[14] == (byte)'D' && b[15] == (byte)'R';

    /// <summary>Tên gọi định dạng để ghi lý do (WebP / GIF / không rõ).</summary>
    private static string Sniff(byte[] b)
    {
        if (b.Length == 0) return "rỗng";
        if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F'
            && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "WebP";
        if (b.Length >= 4 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8') return "GIF";
        if (b.Length >= 8 && b.AsSpan(0, 8).SequenceEqual(PngSignature)) return "PNG hỏng";
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xD8) return "JPEG hỏng";
        return "không rõ";
    }

    /// <summary>Chèn <c>FF FE</c> + độ dài 2 byte big-endian (tính cả 2 byte độ dài) + chuỗi SAU dãy segment APPn
    /// (<c>FF E0</c>..<c>FF EF</c>: APP0 JFIF, APP1 Exif…) liền sau SOI — giữ đúng chuẩn JFIF/Exif (APP0/APP1 phải đứng ngay sau
    /// SOI). Không có APPn thì chèn ngay sau SOI. Segment APPn có độ dài vượt file → JPEG hỏng.</summary>
    private static byte[] InsertJpegComment(byte[] src, byte[] text)
    {
        var at = 2;
        while (at + 4 <= src.Length && src[at] == 0xFF && src[at + 1] is >= 0xE0 and <= 0xEF)
        {
            var len = BinaryPrimitives.ReadUInt16BigEndian(src.AsSpan(at + 2, 2));
            if (len < 2 || at + 2 + len > src.Length) throw new ImageMd5Exception("JPEG hỏng: segment APPn vượt cỡ file");
            at += 2 + len;
        }
        var segLen = 2 + text.Length;   // ≤ 65535 — chuỗi cố định 64 byte
        var outBytes = new byte[src.Length + 2 + segLen];
        src.AsSpan(0, at).CopyTo(outBytes);
        outBytes[at] = 0xFF;
        outBytes[at + 1] = 0xFE;
        BinaryPrimitives.WriteUInt16BigEndian(outBytes.AsSpan(at + 2, 2), (ushort)segLen);
        text.CopyTo(outBytes.AsSpan(at + 4));
        src.AsSpan(at).CopyTo(outBytes.AsSpan(at + 4 + text.Length));
        return outBytes;
    }

    /// <summary>Chữ ký + IHDR (33 byte đầu) + chunk <c>tEXt</c> ("Comment" ‖ 0 ‖ chuỗi, CRC-32 trên type + data) + phần còn lại.</summary>
    private static byte[] InsertPngText(byte[] src, byte[] text)
    {
        const int afterIhdr = 8 + 4 + 4 + 13 + 4;
        var keyword = "Comment"u8;
        var dataLen = keyword.Length + 1 + text.Length;
        var chunk = new byte[4 + 4 + dataLen + 4];
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(0, 4), (uint)dataLen);
        "tEXt"u8.CopyTo(chunk.AsSpan(4));
        keyword.CopyTo(chunk.AsSpan(8));
        chunk[8 + keyword.Length] = 0;
        text.CopyTo(chunk.AsSpan(8 + keyword.Length + 1));
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + dataLen, 4), Crc32(chunk.AsSpan(4, 4 + dataLen)));
        var outBytes = new byte[src.Length + chunk.Length];
        src.AsSpan(0, afterIhdr).CopyTo(outBytes);
        chunk.CopyTo(outBytes.AsSpan(afterIhdr));
        src.AsSpan(afterIhdr).CopyTo(outBytes.AsSpan(afterIhdr + chunk.Length));
        return outBytes;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    /// <summary>CRC-32 chuẩn PNG (ISO-HDLC, đa thức đảo 0xEDB88320).</summary>
    internal static uint Crc32(ReadOnlySpan<byte> data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
