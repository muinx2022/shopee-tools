namespace Shopee.Core.MassUpload;

/// <summary>
/// Giá trị mặc định theo tài khoản seller cho những cột JSON <c>get_pc</c> không có (plan mục 1 + "Chốt thiết kế sau GĐ0").
/// </summary>
/// <param name="StockPerVariation">Tồn kho MỖI phân loại (cột L) — Shopee cộng theo model (GĐ0: 8 model × 100 = 800).</param>
/// <param name="WeightGrams">Cân nặng, đơn vị gram (cột Z — GĐ0 xác nhận gram).</param>
/// <param name="LengthCm">Dài (cm, cột AA).</param>
/// <param name="WidthCm">Rộng (cm, cột AB).</param>
/// <param name="HeightCm">Cao (cm, cột AC).</param>
/// <param name="SizeChartImageKey">Khoá ảnh bảng size dùng chung, đưa qua resolver ảnh để lấy URL cho cột O của MỌI SP.</param>
/// <param name="ProductsPerFile">Số SP tối đa mỗi file (plan GĐ5 "Nhịp chạy": 30).</param>
public sealed record MassUploadDefaults(
    int StockPerVariation,
    int WeightGrams,
    int LengthCm,
    int WidthCm,
    int HeightCm,
    string SizeChartImageKey,
    int ProductsPerFile = 30);
