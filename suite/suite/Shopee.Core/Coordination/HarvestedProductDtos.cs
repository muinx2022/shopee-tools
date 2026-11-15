namespace Shopee.Core.Coordination;

// ── DTO kho dữ liệu SP Shopee do app Harvest cào (JSON PDP `GET /api/v4/pdp/get_pc`) — dùng chung client + server ──
// Client tự rút các cột tóm tắt từ JSON (giá đã chia 100000 → VND), Hub lưu JSON ở cột raw (jsonb — Postgres CHUẨN
// HOÁ thứ tự key + khoảng trắng, nên KHÔNG tính lại được raw_hash từ cột raw). raw_hash = hash CHUỖI client gửi:
// gửi lại cùng hash = SP không đổi → Hub không ghi lại, không tăng harvest_count.

/// <summary>Giới hạn của <see cref="HubRoutes.HarvestedProductsBatch"/>.</summary>
public static class HarvestedProductLimits
{
    /// <summary>Số SP tối đa mỗi lần đẩy — quá thì Hub trả 400 CẢ request (client phải tự chia lô).</summary>
    public const int MaxBatch = 50;
    /// <summary>Độ dài tối đa của <see cref="HarvestedProductItem.AccountId"/> / <see cref="HarvestedProductItem.Sheet"/>.</summary>
    public const int MaxKeyLength = 256;
    /// <summary>Độ dài tối đa của <see cref="HarvestedProductItem.Url"/>.</summary>
    public const int MaxUrlLength = 2048;

}

/// <summary>1 SP đã cào. <see cref="RawJson"/> = body JSON <c>get_pc</c> dưới dạng chuỗi — phải là PDP HỢP LỆ:
/// <c>error</c> vắng/null/số 0, <c>data.item</c> là object có <c>item_id</c> khớp <see cref="ItemId"/>. Bộ ba
/// <see cref="AccountId"/>/<see cref="Sheet"/>/<see cref="RowNo"/> = dòng nguồn của shop đích: đủ cả ba thì nối dòng,
/// không có trường nào thì chỉ lưu SP, có 1–2 trường thì Hub từ chối mục (<c>source-partial</c>).
/// <see cref="HarvestedAt"/> = lúc client cào được (BẮT BUỘC) — Hub dùng để bản cào CŨ không ghi đè bản MỚI.</summary>
public sealed record HarvestedProductItem(
    long ItemId, long ShopId, string? Url, string? Name, long? CatId, long[]? CategoryIds, long? BrandId,
    long? PriceMin, long? PriceMax, long? PriceBeforeDiscount, int? TierCount, int? ModelCount,
    string[]? ImageUrls, string[]? VideoUrls, string? SizeChartUrl, string? RawJson, string? RawHash,
    string? AccountId, string? Sheet, int? RowNo, DateTimeOffset? HarvestedAt);

/// <summary>Lô SP đẩy lên Hub. <see cref="MachineId"/> ghi vào cột machine_id của SP (rỗng → Hub lấy header
/// <c>X-Machine-Id</c>). <see cref="ClientNow"/> = giờ máy client LÚC GỬI (BẮT BUỘC, thiếu → 400 cả lô): Hub quy mọi
/// <see cref="HarvestedProductItem.HarvestedAt"/> về đồng hồ Hub bằng (giờ Hub − ClientNow) rồi kẹp không quá giờ Hub,
/// nên máy chạy nhanh/chậm vẫn xếp đúng trước-sau.</summary>
public sealed record HarvestedProductBatchRequest(string? MachineId, DateTimeOffset? ClientNow, List<HarvestedProductItem>? Items);

/// <summary>Kết quả của MỤC thứ <see cref="Index"/> (0-based) trong lô — KHOÁ THEO CHỈ SỐ, không theo itemId: cùng 1
/// itemId có thể nằm ở nhiều mục (khác dòng nguồn), mục này vào mục kia hỏng. <see cref="Ok"/>=true = Hub đã giữ
/// (gồm cả SP trùng hash, không ghi lại) → client xoá ĐÚNG mục đó khỏi hàng đợi; bản CŨ hơn bản Hub đang giữ cũng
/// Ok=true nhưng không ghi gì. <see cref="Applied"/>=true = SP thật sự được insert/update (client đếm "đã lưu");
/// false khi Ok mà không ghi (trùng hash / bản cũ hơn) hoặc khi Ok=false — luôn có Applied ⇒ Ok.
/// <see cref="Ok"/>=false kèm <see cref="Reason"/> = dữ liệu hỏng, gửi lại y nguyên vẫn hỏng.</summary>
public sealed record HarvestedProductResult(int Index, long ItemId, bool Ok, bool Applied, string? Reason);

/// <summary><see cref="Results"/> đúng thứ tự + đủ số mục của request, mỗi mục một kết quả.</summary>
public sealed record HarvestedProductBatchResponse(List<HarvestedProductResult> Results);

/// <summary><see cref="Rows"/> = số dòng nguồn (account, sheet, row_no) đã nối SP; <see cref="Products"/> = số SP
/// khác nhau.</summary>
public sealed record HarvestedProductStats(long Rows, long Products);
