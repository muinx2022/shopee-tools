using System.Buffers.Binary;
using System.IO.Compression;

namespace Shopee.Core.Tests.MassUpload.Images;

/// <summary>Ảnh mẫu thật (16×12, sinh bằng System.Drawing của Windows rồi lưu vào <c>Fixtures/images</c>) và bộ kiểm cấu
/// trúc JPEG / PNG ĐỘC LẬP với code đang test (CRC tính từng bit, không dùng bảng của <c>ImageMd5Changer</c>).</summary>
internal static class ImageTestData
{
    public static byte[] Jpeg => File.ReadAllBytes(FixturePath("tiny.jpg"));
    public static byte[] Png => File.ReadAllBytes(FixturePath("tiny.png"));

    public static string FixturePath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Fixtures", "images", name);
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(dir.FullName, "tests", "Shopee.Core.Tests", "Fixtures", "images", name);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(name);
    }

    /// <summary>Duyệt JPEG: SOI, các segment có độ dài khớp tới SOS, dữ liệu nén, kết thúc EOI. Trả danh sách marker (byte
    /// thứ 2) theo thứ tự trước SOS. Hỏng → ném.</summary>
    public static List<byte> WalkJpeg(byte[] b)
    {
        if (b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8) throw new InvalidDataException("thiếu SOI");
        if (b[^2] != 0xFF || b[^1] != 0xD9) throw new InvalidDataException("thiếu EOI");
        var markers = new List<byte>();
        var i = 2;
        while (true)
        {
            if (i + 4 > b.Length || b[i] != 0xFF) throw new InvalidDataException($"marker hỏng ở {i}");
            var m = b[i + 1];
            var len = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(i + 2, 2));
            if (len < 2 || i + 2 + len > b.Length) throw new InvalidDataException($"độ dài segment hỏng ở {i}");
            markers.Add(m);
            i += 2 + len;
            if (m == 0xDA) return markers;   // SOS: phần sau là dữ liệu nén tới EOI
        }
    }

    /// <summary>Duyệt PNG: chữ ký, từng chunk (độ dài, CRC đúng), IHDR đầu, IEND cuối. Trả (type, data) theo thứ tự.</summary>
    public static List<(string Type, byte[] Data)> WalkPng(byte[] b)
    {
        byte[] sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (!b.AsSpan(0, 8).SequenceEqual(sig)) throw new InvalidDataException("sai chữ ký PNG");
        var chunks = new List<(string, byte[])>();
        var i = 8;
        while (i < b.Length)
        {
            var len = (int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(i, 4));
            var type = System.Text.Encoding.ASCII.GetString(b, i + 4, 4);
            var data = b.AsSpan(i + 8, len).ToArray();
            var crc = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(i + 8 + len, 4));
            if (crc != BitCrc32(b.AsSpan(i + 4, 4 + len))) throw new InvalidDataException($"CRC sai ở chunk {type}");
            chunks.Add((type, data));
            i += 12 + len;
        }
        if (i != b.Length) throw new InvalidDataException("đuôi thừa");
        if (chunks[0].Item1 != "IHDR" || chunks[^1].Item1 != "IEND") throw new InvalidDataException("thứ tự chunk sai");
        return chunks;
    }

    /// <summary>Giải nén IDAT (zlib) — chứng minh dữ liệu điểm ảnh còn nguyên, đọc được.</summary>
    public static byte[] PngPixels(byte[] b)
    {
        var idat = WalkPng(b).Where(c => c.Type == "IDAT").SelectMany(c => c.Data).ToArray();
        using var z = new ZLibStream(new MemoryStream(idat), CompressionMode.Decompress);
        using var ms = new MemoryStream();
        z.CopyTo(ms);
        return ms.ToArray();
    }

    private static uint BitCrc32(ReadOnlySpan<byte> data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var x in data)
        {
            c ^= x;
            for (var k = 0; k < 8; k++) c = (c >> 1) ^ (0xEDB88320u & (uint)-(int)(c & 1));
        }
        return ~c;
    }
}
