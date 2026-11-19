using System.Security.Cryptography;

namespace Shopee.Core.MassUpload.Images;

/// <summary>
/// Ảnh bảng size dùng chung của một lượt. <see cref="Key"/> (<see cref="MassUploadDefaults.SizeChartImageKey"/>,
/// <c>image_prep.source_key</c>) = <c>sizechart:&lt;SHA-256 nội dung&gt;:&lt;đường dẫn đầy đủ, CHỮ THƯỜNG&gt;</c> — chuẩn hoá chữ
/// thường chỉ để SO (cùng file gõ khác hoa/thường ra cùng khoá); <see cref="FilePath"/> = đường dẫn đầy đủ GIỮ NGUYÊN hoa/thường,
/// là thứ duy nhất dùng để ĐỌC file (lưu ở <c>image_prep.source_path</c>).
/// <para>Có dấu vân tay nội dung nên đổi ảnh khác vào CÙNG đường dẫn ra khoá MỚI → bước đệm làm ảnh mới; khoá cũ không ai hỏi
/// tới nữa (ảnh bảng size không bao giờ bị xoá chủ động — Hub tự dọn theo TTL, dòng sổ cũ nằm lại, vài dòng mỗi shop).</para>
/// </summary>
public sealed record SizeChartImage(string Key, string FilePath)
{
    private const string Prefix = "sizechart:";

    /// <summary>Đọc file, tính khoá. File không có → <see cref="FileNotFoundException"/> "Chưa có ảnh bảng size" (lỗi cấu hình
    /// của cả lượt — không chạy lượt).</summary>
    public static SizeChartImage FromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException($"Chưa có ảnh bảng size: không thấy file {path}", path);
        var full = Path.GetFullPath(path);
        using var s = File.OpenRead(full);
        var sha = Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
        return new SizeChartImage(Prefix + sha + ":" + full.ToLowerInvariant(), full);
    }

    /// <summary>Tách khoá do <see cref="FromFile"/> sinh ra thành (SHA-256 hex, đường dẫn CHỮ THƯỜNG — chỉ để so, không để đọc
    /// file). Sai dạng → false.</summary>
    public static bool TryParse(string key, out string sha256, out string normalizedPath)
    {
        sha256 = normalizedPath = "";
        if (key is null || !key.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var parts = key[Prefix.Length..].Split(':', 2);
        if (parts.Length != 2 || parts[0].Length != 64 || parts[1].Length == 0) return false;
        (sha256, normalizedPath) = (parts[0], parts[1]);
        return true;
    }
}
