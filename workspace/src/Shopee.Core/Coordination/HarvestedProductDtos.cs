namespace Shopee.Core.Coordination;

// ── DTO kho dữ liệu SP Shopee do app Harvest cào (JSON PDP `GET /api/v4/pdp/get_pc`) — dùng chung client + server ──
// Client tự rút các cột tóm tắt từ JSON (giá đã chia 100000 → VND), Hub lưu JSON ở cột raw (jsonb — Postgres CHUẨN
// HOÁ thứ tự key + khoảng trắng, nên KHÔNG tính lại được raw_hash từ cột raw). raw_hash = hash CHUỖI client gửi:
// gửi lại cùng hash = SP không đổi → Hub không ghi lại, không tăng harvest_count.
// Bản CHÉP của suite/suite/Shopee.Core/Coordination/HarvestedProductDtos.cs (Hub biên dịch bản đó) — sửa thì sửa cả hai.

/// <summary>Giới hạn của <see cref="HubRoutes.HarvestedProductsBatch"/>.</summary>
public static class HarvestedProductLimits
{
    /// <summary>Số SP tối đa mỗi lần đẩy — quá thì Hub trả 400 CẢ request (client phải tự chia lô).</summary>
    public const int MaxBatch = 50;
    /// <summary>Độ dài tối đa của <see cref="HarvestedProductItem.AccountId"/> / <see cref="HarvestedProductItem.Sheet"/>.</summary>
    public const int MaxKeyLength = 256;
    /// <summary>Độ dài tối đa của <see cref="HarvestedProductItem.Url"/> / <see cref="HarvestedProductItem.SourceUrl"/>.</summary>
    public const int MaxUrlLength = 2048;
    /// <summary>Số dòng tối đa mỗi trang của <see cref="HubRoutes.HarvestedProductsRows"/> — mỗi dòng kèm <c>raw</c>
    /// ~60–80 KB, 50 dòng ≈ 4 MB/trang.</summary>
    public const int MaxRowsPage = 50;
    /// <summary>Số itemId tối đa mỗi lần gọi <see cref="HubRoutes.HarvestedProductsByItems"/> (đếm cả id trùng) — quá
    /// thì Hub trả 400 <c>too-many-ids</c>; client tự chia lô.</summary>
    public const int MaxByItems = 50;

}

/// <summary>1 SP đã cào. <see cref="RawJson"/> = body JSON <c>get_pc</c> dưới dạng chuỗi — phải là PDP HỢP LỆ:
/// <c>error</c> vắng/null/số 0, <c>data.item</c> là object có <c>item_id</c> khớp <see cref="ItemId"/>. Bộ ba
/// <see cref="AccountId"/>/<see cref="Sheet"/>/<see cref="RowNo"/> = dòng nguồn của shop đích: đủ cả ba thì nối dòng,
/// không có trường nào thì chỉ lưu SP, có 1–2 trường thì Hub từ chối mục (<c>source-partial</c>).
/// <see cref="HarvestedAt"/> = lúc client cào được (BẮT BUỘC) — Hub dùng để bản cào CŨ không ghi đè bản MỚI.
/// <see cref="Url"/> = link SP ĐÃ làm sạch (cột <c>harvested_products.url</c>); <see cref="SourceUrl"/> (tuỳ chọn) = link
/// GỐC của dòng nguồn (cột <c>harvested_product_sources.url</c> — bước up đối chiếu với link hiện tại của dòng), vắng
/// thì Hub dùng <see cref="Url"/>.</summary>
public sealed record HarvestedProductItem(
    long ItemId, long ShopId, string? Url, string? Name, long? CatId, long[]? CategoryIds, long? BrandId,
    long? PriceMin, long? PriceMax, long? PriceBeforeDiscount, int? TierCount, int? ModelCount,
    string[]? ImageUrls, string[]? VideoUrls, string? SizeChartUrl, string? RawJson, string? RawHash,
    string? AccountId, string? Sheet, int? RowNo, DateTimeOffset? HarvestedAt, string? SourceUrl = null);

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

// ── Hỏi SP nào ĐÃ có dữ liệu còn hạn (<see cref="HubRoutes.HarvestedProductsKnown"/>) — app Harvest bỏ qua, không mở trang ──

/// <summary>Giới hạn của <see cref="HubRoutes.HarvestedProductsKnown"/>.</summary>
public static class HarvestedKnownLimits
{
    /// <summary>Số itemId tối đa mỗi lần hỏi (đếm cả id trùng) — quá thì Hub trả 400 <c>too-many-ids</c>; client tự chia lô.</summary>
    public const int MaxItems = 500;
    /// <summary>Trần của <see cref="HarvestedKnownRequest.MaxAgeDays"/> (≈ 10 năm).</summary>
    public const int MaxAgeDays = 3650;
}

/// <summary>Body của <see cref="HubRoutes.HarvestedProductsKnown"/>: ≤ <see cref="HarvestedKnownLimits.MaxItems"/> itemId,
/// <see cref="MaxAgeDays"/> = chỉ tính là "đã có" khi mốc lấy gần nhất không cũ hơn chừng này ngày (1..<see cref="HarvestedKnownLimits.MaxAgeDays"/>).</summary>
public sealed record HarvestedKnownRequest(List<long>? ItemIds, int? MaxAgeDays);

/// <summary>1 SP đã có dữ liệu còn hạn: <see cref="HarvestedAt"/> = mốc lấy gần nhất (đồng hồ Hub; lấy lại cùng JSON cũng
/// làm mới mốc này).</summary>
public sealed record HarvestedKnownItem(long ItemId, DateTimeOffset HarvestedAt);

/// <summary>Kết quả của <see cref="HubRoutes.HarvestedProductsKnown"/>: chỉ các id CÓ dữ liệu còn hạn (không kèm raw).</summary>
public sealed record HarvestedKnownResponse(List<HarvestedKnownItem> Items);

/// <summary><see cref="Rows"/> = số dòng nguồn (account, sheet, row_no) đã nối SP; <see cref="Products"/> = số SP
/// khác nhau.</summary>
public sealed record HarvestedProductStats(long Rows, long Products);

// ── Đọc SP đã harvest theo dòng nguồn (bước đăng SP lên Shopee) ──

/// <summary>1 liên kết dòng nguồn → SP đã có dữ liệu. <see cref="RowNo"/> = CHỈ SỐ DÒNG NÉN theo Harvest lúc thấy SP,
/// CÓ THỂ CŨ sau khi chèn/xoá dòng (Harvest không gửi lại SP đã xong) — KHÔNG dùng để đăng SP (bước đăng tra theo itemId
/// qua <see cref="HubRoutes.HarvestedProductsByItems"/>). <see cref="Url"/> = link GỐC của dòng
/// (<c>harvested_product_sources.url</c>); <see cref="LinkedAt"/> = lần cuối liên kết được ghi
/// (<c>harvested_product_sources.harvested_at</c>); <see cref="HarvestedAt"/> = mốc của bản <see cref="Raw"/> đang giữ
/// (<c>harvested_products.harvested_at</c>, đồng hồ Hub); <see cref="Raw"/> = JSON <c>get_pc</c> dạng LỒNG (không phải
/// chuỗi). Từ migration 5 khoá liên kết là (account, sheet, itemId) nên MỘT rowNo có thể ra nhiều dòng (SP cũ từng ở
/// dòng đó vẫn giữ liên kết).</summary>
public sealed record HarvestedSourceRow(int RowNo, string? Url, long ItemId, long ShopId, DateTimeOffset HarvestedAt,
    DateTimeOffset LinkedAt, JsonElement Raw);

/// <summary>Con trỏ trang: lấy các dòng có (rowNo, itemId) LỚN HƠN (<see cref="AfterRow"/>, <see cref="AfterItemId"/>).</summary>
public sealed record HarvestedRowsCursor(int AfterRow, long AfterItemId);

/// <summary>1 trang của <see cref="HubRoutes.HarvestedProductsRows"/>. <see cref="Next"/> null = hết dữ liệu.</summary>
public sealed record HarvestedRowsPage(List<HarvestedSourceRow> Rows, HarvestedRowsCursor? Next);

/// <summary>Body của <see cref="HubRoutes.HarvestedProductsByItems"/>: ≤ <see cref="HarvestedProductLimits.MaxByItems"/>
/// itemId. Thiếu <see cref="ItemIds"/> → 400; mảng rỗng → 200 với danh sách rỗng; id trùng được khử.</summary>
public sealed record HarvestedByItemsRequest(List<long>? ItemIds);

/// <summary>1 SP đã harvest: <see cref="HarvestedAt"/> = mốc của bản <see cref="Raw"/> (đồng hồ Hub);
/// <see cref="Raw"/> = JSON <c>get_pc</c> dạng LỒNG.</summary>
public sealed record HarvestedItemData(long ItemId, long ShopId, DateTimeOffset HarvestedAt, JsonElement Raw);

/// <summary>Kết quả của <see cref="HubRoutes.HarvestedProductsByItems"/>: các SP CÓ dữ liệu, theo thứ tự id gửi lên
/// (đã khử trùng); id chưa có dữ liệu thì không có mặt.</summary>
public sealed record HarvestedByItemsResponse(List<HarvestedItemData> Items);

// ── Kho ảnh công khai có hạn (<see cref="HubRoutes.PublicMedia"/>) ──

/// <summary>Giới hạn của <see cref="HubRoutes.PublicMedia"/>.</summary>
public static class PublicMediaLimits
{
    /// <summary>Cỡ tối đa 1 ảnh (byte) — quá thì Hub trả 413 <c>too-large</c>.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;
}

/// <summary>Kết quả upload 1 ảnh. <see cref="Path"/> = đường dẫn GET công khai (tương đối gốc Hub);
/// <see cref="Url"/> = URL TUYỆT ĐỐI công khai (Hub chưa cấu hình base URL công khai thì từ chối upload 503
/// <c>public-base-url-missing</c>, nên 200 luôn có Url — client thiếu Url thì coi là lỗi, không tự ghép);
/// <see cref="ExpiresAt"/> = mốc ảnh hết hạn (UTC) — sau mốc này GET trả 404.</summary>
public sealed record PublicMediaUploadResponse(string Path, string? Url, DateTimeOffset ExpiresAt);
