namespace Shopee.Core.MassUpload;

/// <summary>Một nhóm phân loại của SP. <see cref="Images"/> cùng độ dài với <see cref="Options"/>; phần tử null/"" = lựa
/// chọn đó không có ảnh. Ảnh là <b>id ảnh gốc</b> của Shopee (vd <c>vn-11134207-…</c>), không phải URL.</summary>
public sealed record ListingTier(string Name, IReadOnlyList<string> Options, IReadOnlyList<string?> Images);

/// <summary>Một model (tổ hợp phân loại). <see cref="Price"/> là VND đã chia 100000: <c>price_before_discount</c> nếu &gt; 0,
/// không thì <c>price</c>.</summary>
public sealed record ListingModel(long ModelId, IReadOnlyList<int> TierIndex, long Price);

/// <summary>Một thuộc tính của SP trong <c>item.attributes[]</c> (giá trị tiếng Anh, có thể nhiều phần ngăn ", ").</summary>
public sealed record ListingAttribute(long Id, string Name, string Value, long ValId);

/// <summary>Những gì bước Đăng Hàng Loạt cần từ JSON <c>get_pc</c> (đọc bởi <see cref="ShopeeListingReader"/>).</summary>
/// <param name="LeafCategoryId">Ngành lá = phần tử CUỐI của <c>item.categories[]</c> (KHÔNG phải <c>item.cat_id</c> — đó là
/// ngành cấp 1).</param>
/// <param name="TopCategoryId">Ngành cấp 1 = phần tử ĐẦU của <c>item.categories[]</c>.</param>
/// <param name="Description">Mô tả chữ đã ghép (đoạn ảnh bị bỏ), chưa cắt theo giới hạn.</param>
/// <param name="HasVariations">false khi SP không biến thể (<c>[{"name":"","options":[""]}]</c> hoặc rỗng) —
/// <see cref="Tiers"/> khi đó rỗng.</param>
/// <param name="Images">Ảnh SP (<c>product_images.images</c>) theo thứ tự, id ảnh gốc.</param>
/// <param name="SizeChartImageId">Id ảnh bảng size gốc (<c>item.size_chart</c>) nếu có — chỉ để tham khảo, bước đăng dùng
/// ảnh bảng size dùng chung.</param>
public sealed record ShopeeListing(
    long ItemId,
    long ShopId,
    string Title,
    string Description,
    long LeafCategoryId,
    long TopCategoryId,
    IReadOnlyList<long> CategoryIds,
    bool HasVariations,
    IReadOnlyList<ListingTier> Tiers,
    IReadOnlyList<ListingModel> Models,
    IReadOnlyList<string> Images,
    IReadOnlyList<ListingAttribute> Attributes,
    string? SizeChartImageId);
