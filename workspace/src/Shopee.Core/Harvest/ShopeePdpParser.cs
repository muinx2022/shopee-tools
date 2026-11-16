using System.Globalization;

namespace Shopee.Core.Harvest;

/// <summary>Một model (tổ hợp biến thể) của SP. Giá đã chia 100000 (VND). <see cref="PriceBeforeDiscount"/> đã quy đổi:
/// Shopee trả 0 = "không giảm" → lấy <see cref="Price"/>.</summary>
public sealed record ShopeePdpModel(long ModelId, string Name, int[] TierIndex, long? Price, long? PriceBeforeDiscount);

/// <summary>Các cột tóm tắt rút từ JSON <c>get_pc</c> (khớp <see cref="Coordination.HarvestedProductItem"/>). Trường nào
/// không đọc được thì null (không ném) — <c>raw</c> vẫn giữ đủ để bước sau tự đọc lại.</summary>
public sealed record ShopeePdpSummary(
    long ItemId, long ShopId, string? Name, long? CatId, long[] CategoryIds, long? BrandId,
    long? PriceMin, long? PriceMax, long? PriceBeforeDiscount, int TierCount, int ModelCount,
    string[] ImageUrls, string[] VideoUrls, string? SizeChartUrl, IReadOnlyList<ShopeePdpModel> Models);

/// <summary>
/// Rút cột tóm tắt từ JSON <c>get_pc</c> theo bảng đường dẫn ở plan mục 1 (gốc <c>data</c>). Chịu được các ca biên đã gặp
/// ở fixture thật: SP không biến thể <c>tier_variations = [{"name":"","options":[""]}]</c> (→ 0 tầng), <c>brand</c> null /
/// <c>brand_id</c> 0, <c>size_chart</c> null hoặc "", <c>product_images.video</c> null, model
/// <c>price_before_discount = 0</c>, danh mục 2–4 cấp, <c>attributes</c> null. Video lấy từ <c>product_images.video</c>,
/// KHÔNG tin <c>item.item_has_video</c> (Shopee báo false dù có video).
/// </summary>
public static class ShopeePdpParser
{
    /// <summary>CDN ảnh Shopee VN — đã thử 200 với cả id tiền tố <c>sg-</c> lẫn <c>vn-</c>.</summary>
    public const string ImageBase = "https://down-vn.img.susercontent.com/file/";

    /// <summary>Giá trong JSON là micro-VND: chia cho số này ra VND.</summary>
    public const long PriceDivisor = 100000;

    /// <summary>Đọc JSON (đã hoặc chưa làm sạch). NÉM <see cref="HarvestItemRejectedException"/> khi không có <c>data.item</c> với
    /// <c>item_id</c> dương; còn lại không ném.</summary>
    public static ShopeePdpSummary Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = HarvestPayloadCleaner.MaxDepth }); }
        catch (JsonException ex) { throw new HarvestItemRejectedException("JSON PDP không đọc được: " + ex.Message, ex); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !TryObj(root, "data", out var data) || !TryObj(data, "item", out var item))
                throw new HarvestItemRejectedException("JSON PDP thiếu data.item");
            var itemId = Long(item, "item_id") ?? 0;
            if (itemId <= 0) throw new HarvestItemRejectedException("JSON PDP thiếu data.item.item_id");
            var shopId = Long(item, "shop_id") ?? 0;

            var categoryIds = new List<long>();
            if (TryArr(item, "categories", out var cats))
                foreach (var c in cats.EnumerateArray())
                    if (c.ValueKind == JsonValueKind.Object && Long(c, "catid") is long id && id > 0) categoryIds.Add(id);
            long? catId = categoryIds.Count > 0 ? categoryIds[^1] : Long(item, "cat_id") is long cid && cid > 0 ? cid : null;

            var models = new List<ShopeePdpModel>();
            if (TryArr(item, "models", out var ms))
                foreach (var m in ms.EnumerateArray())
                {
                    if (m.ValueKind != JsonValueKind.Object) continue;
                    var price = Vnd(Long(m, "price"));
                    var before = Vnd(Long(m, "price_before_discount"));
                    var tierIndex = TryObj(m, "extinfo", out var ext) && TryArr(ext, "tier_index", out var ti)
                        ? ti.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var v) ? v : -1).ToArray()
                        : [];
                    models.Add(new ShopeePdpModel(Long(m, "model_id") ?? 0, Str(m, "name") ?? "", tierIndex, price,
                        before is > 0 ? before : price));
                }

            var priceBefore = Vnd(Long(item, "price_before_discount"));
            if (priceBefore is not > 0)
            {
                // 0 = không giảm: lấy giá trước giảm cao nhất trong các model (đã quy 0 → price), hết đường thì price_max.
                var fromModels = models.Select(x => x.PriceBeforeDiscount).Where(x => x is > 0).Max();
                priceBefore = fromModels ?? Vnd(Long(item, "price_max"));
            }

            return new ShopeePdpSummary(
                itemId, shopId, Str(item, "title"), catId, [.. categoryIds],
                Long(item, "brand_id"),
                Vnd(Long(item, "price_min")), Vnd(Long(item, "price_max")), priceBefore,
                CountTiers(item), models.Count,
                ImageUrls(data), VideoUrls(data), ImageUrl(Str(item, "size_chart")), models);
        }
    }

    /// <summary>Số tầng biến thể THẬT. SP không biến thể có 1 tầng giả <c>{"name":"","options":[""]}</c> → không đếm.</summary>
    private static int CountTiers(JsonElement item)
    {
        if (!TryArr(item, "tier_variations", out var tiers)) return 0;
        var n = 0;
        foreach (var t in tiers.EnumerateArray())
        {
            if (t.ValueKind != JsonValueKind.Object) continue;
            var named = !string.IsNullOrWhiteSpace(Str(t, "name"));
            var hasOption = TryArr(t, "options", out var opts)
                            && opts.EnumerateArray().Any(o => o.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(o.GetString()));
            if (named || hasOption) n++;
        }
        return n;
    }

    private static string[] ImageUrls(JsonElement data)
    {
        if (!TryObj(data, "product_images", out var pi) || !TryArr(pi, "images", out var imgs)) return [];
        return imgs.EnumerateArray()
            .Select(x => x.ValueKind == JsonValueKind.String ? ImageUrl(x.GetString()) : null)
            .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary><c>product_images.video.default_format.url</c> trước, rồi <c>formats[].url</c>; bỏ trùng.</summary>
    private static string[] VideoUrls(JsonElement data)
    {
        if (!TryObj(data, "product_images", out var pi) || !TryObj(pi, "video", out var video)) return [];
        var urls = new List<string>();
        if (TryObj(video, "default_format", out var def) && Str(def, "url") is { Length: > 0 } d) urls.Add(d);
        if (TryArr(video, "formats", out var formats))
            foreach (var f in formats.EnumerateArray())
                if (f.ValueKind == JsonValueKind.Object && Str(f, "url") is { Length: > 0 } u) urls.Add(u);
        return urls.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Id ảnh → URL CDN; đã là URL thì giữ; rỗng/null → null.</summary>
    private static string? ImageUrl(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        id = id.Trim();
        return id.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || id.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? id : ImageBase + id;
    }

    private static long? Vnd(long? micro) => micro is long v ? v / PriceDivisor : null;

    private static bool TryObj(JsonElement e, string name, out JsonElement value) =>
        e.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object;

    private static bool TryArr(JsonElement e, string name, out JsonElement value) =>
        e.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Array;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Số nguyên, hoặc chuỗi chỉ gồm chữ số (vài id Shopee trả dạng chuỗi). Khác đi → null.</summary>
    private static long? Long(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt64(out var n) ? n : null,
            JsonValueKind.String => long.TryParse(v.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null,
            _ => null,
        };
    }
}
