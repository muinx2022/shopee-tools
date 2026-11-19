namespace Shopee.Core.MassUpload;

/// <summary>
/// Giới hạn của file Đăng Hàng Loạt Shopee (ghi ở sheet "Hướng dẫn" / dòng 6 của bản mẫu, và đo ở GĐ0). Gom một chỗ để
/// Shopee đổi thì chỉ sửa ở đây. Độ dài tính theo đơn vị UTF-16 của .NET (emoji = 2, an toàn cho trần trên).
/// </summary>
public sealed record MassUploadLimits
{
    public static MassUploadLimits Default { get; } = new();

    public int MinNameLength { get; init; } = 10;
    public int MaxNameLength { get; init; } = 120;
    public int MinDescriptionLength { get; init; } = 100;
    public int MaxDescriptionLength { get; init; } = 3000;

    /// <summary>Tên nhóm phân loại (F / I).</summary>
    public int MaxTierNameLength { get; init; } = 14;

    /// <summary>Tên lựa chọn phân loại (G / J).</summary>
    public int MaxOptionLength { get; init; } = 20;

    /// <summary>Số lựa chọn tối đa của MỘT nhóm (áp cho cả nhóm 1 lẫn nhóm 2).</summary>
    public int MaxOptionsPerTier { get; init; } = 20;

    /// <summary>Tích số lựa chọn của 2 nhóm.</summary>
    public int MaxCombinationsTwoTiers { get; init; } = 50;

    /// <summary>Giá đắt nhất / rẻ nhất trong một SP.</summary>
    public decimal MaxPriceRatio { get; init; } = 5;

    public long MinPrice { get; init; } = 1_000;
    public long MaxPrice { get; init; } = 120_000_000;

    /// <summary>Ảnh SP: bìa (Q) + 8 ảnh (R..Y).</summary>
    public int MaxImages { get; init; } = 9;

    /// <summary>Số giá trị tối đa của một thuộc tính nhiều giá trị (ngăn bằng <c>;</c> không cách).</summary>
    public int MaxAttributeValues { get; init; } = 5;

    /// <summary>Số dòng dữ liệu tối đa mỗi file (validation của bản mẫu áp tới dòng 1007 → 1000 dòng từ dòng 7).</summary>
    public int MaxDataRowsPerFile { get; init; } = 1000;

    /// <summary>Kích cỡ file nén tối đa Shopee nhận ("≤ 3.0 MB"); lấy 3.000.000 byte cho chắc.</summary>
    public long MaxFileBytes { get; init; } = 3_000_000;
}
