using System.Globalization;
using Shopee.Core.Harvest;

namespace Shopee.Core.MassUpload;

/// <summary>
/// Đọc JSON <c>get_pc</c> (cột <c>raw</c> trên Hub, gốc <c>data</c>) ra <see cref="ShopeeListing"/> cho bước Đăng Hàng Loạt.
/// Bộ đọc RIÊNG của bước đăng — không đụng <see cref="ShopeePdpParser"/> của Harvest (plan GĐ2 bước 1).
/// <list type="bullet">
/// <item>Ngành lá = phần tử cuối của <c>item.categories[].catid</c>; ngành cấp 1 = phần tử đầu. Không có danh mục → 0
/// (mapper sẽ bỏ qua SP).</item>
/// <item>Mô tả: đoạn <c>type</c> 0 của <c>product_description.paragraph_list</c> (không có thì
/// <c>item.rich_text_description.paragraph_list</c>), <c>type</c> 10001 = <c>empty_paragraph_count</c> dòng trống, đoạn ảnh
/// (<c>type</c> 1) bỏ. Không ra chữ nào thì lấy <c>item.description</c>.</item>
/// <item>Giá model: <c>price_before_discount</c> &gt; 0 thì dùng, không thì <c>price</c>; chia 100000, làm tròn tới đồng.</item>
/// <item>SP không biến thể <c>[{"name":"","options":[""]}]</c> → <see cref="ShopeeListing.HasVariations"/> = false.</item>
/// </list>
/// </summary>
public static class ShopeeListingReader
{
    /// <summary>Giá trong JSON là micro-VND: chia cho số này ra VND.</summary>
    public const long PriceDivisor = 100000;

    /// <summary>Mã đoạn chữ / đoạn dòng trống trong <c>paragraph_list</c>.</summary>
    private const int ParagraphText = 0, ParagraphEmptyLines = 10001;

    /// <summary>Đọc từ chuỗi JSON. NÉM <see cref="FormatException"/> khi JSON hỏng hoặc thiếu <c>data.item.item_id</c>.</summary>
    public static ShopeeListing Read(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = HarvestPayloadCleaner.MaxDepth }); }
        catch (JsonException ex) { throw new FormatException("JSON get_pc không đọc được: " + ex.Message, ex); }
        using (doc) return Read(doc.RootElement);
    }

    /// <summary>Đọc từ gốc JSON (object có <c>data</c>, hoặc chính object <c>data</c>). NÉM <see cref="FormatException"/>
    /// khi thiếu <c>item</c> / <c>item_id</c> dương.</summary>
    public static ShopeeListing Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("JSON get_pc không phải object");
        var data = TryObj(root, "data", out var d) ? d : root;
        if (!TryObj(data, "item", out var item)) throw new FormatException("JSON get_pc thiếu data.item");
        var itemId = Long(item, "item_id") ?? 0;
        if (itemId <= 0) throw new FormatException("JSON get_pc thiếu data.item.item_id");

        var categoryIds = new List<long>();
        if (TryArr(item, "categories", out var cats))
            foreach (var c in cats.EnumerateArray())
                if (c.ValueKind == JsonValueKind.Object && Long(c, "catid") is long id && id > 0) categoryIds.Add(id);

        var (hasVariations, tiers) = ReadTiers(item);

        var models = new List<ListingModel>();
        if (TryArr(item, "models", out var ms))
            foreach (var m in ms.EnumerateArray())
            {
                if (m.ValueKind != JsonValueKind.Object) continue;
                var before = Micro(m, "price_before_discount") ?? 0;
                var raw = before > 0 ? before : Micro(m, "price") ?? 0;
                var tierIndex = TryObj(m, "extinfo", out var ext) && TryArr(ext, "tier_index", out var ti)
                    ? ti.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var v) ? v : -1).ToArray()
                    : [];
                models.Add(new ListingModel(Long(m, "model_id") ?? 0, tierIndex, Vnd(raw)));
            }

        var images = new List<string>();
        if (TryObj(data, "product_images", out var pi) && TryArr(pi, "images", out var imgs))
            foreach (var x in imgs.EnumerateArray())
                if (x.ValueKind == JsonValueKind.String && x.GetString() is { Length: > 0 } s) images.Add(s);

        var attributes = new List<ListingAttribute>();
        if (TryArr(item, "attributes", out var attrs))
            foreach (var a in attrs.EnumerateArray())
            {
                if (a.ValueKind != JsonValueKind.Object || Long(a, "id") is not long aid || aid <= 0) continue;
                attributes.Add(new ListingAttribute(aid, Text(a, "name"), Text(a, "value"), Long(a, "val_id") ?? 0));
            }

        var sizeChart = Str(item, "size_chart");
        return new ShopeeListing(
            itemId, Long(item, "shop_id") ?? 0, Text(item, "title"), ReadDescription(data, item),
            categoryIds.Count > 0 ? categoryIds[^1] : 0, categoryIds.Count > 0 ? categoryIds[0] : 0, categoryIds,
            hasVariations, tiers, models, images, attributes,
            string.IsNullOrWhiteSpace(sizeChart) ? null : sizeChart);
    }

    private static (bool HasVariations, List<ListingTier> Tiers) ReadTiers(JsonElement item)
    {
        var tiers = new List<ListingTier>();
        if (TryArr(item, "tier_variations", out var tvs))
            foreach (var t in tvs.EnumerateArray())
            {
                if (t.ValueKind != JsonValueKind.Object) continue;
                var options = TryArr(t, "options", out var os)
                    ? os.EnumerateArray().Select(o => o.ValueKind == JsonValueKind.String ? MassUploadText.Clean(o.GetString()) : "").ToList()
                    : [];
                var images = new List<string?>();
                if (TryArr(t, "images", out var ims))
                    foreach (var im in ims.EnumerateArray())
                        images.Add(im.ValueKind == JsonValueKind.String && im.GetString() is { Length: > 0 } s ? s : null);
                while (images.Count < options.Count) images.Add(null);
                if (images.Count > options.Count) images.RemoveRange(options.Count, images.Count - options.Count);
                tiers.Add(new ListingTier(Text(t, "name"), options, images));
            }

        // SP không biến thể: Shopee trả 1 nhóm tên rỗng với đúng 1 lựa chọn rỗng (hoặc không có nhóm nào).
        var none = tiers.Count == 0
                   || (tiers.Count == 1 && string.IsNullOrWhiteSpace(tiers[0].Name)
                       && tiers[0].Options.Count <= 1 && tiers[0].Options.All(string.IsNullOrWhiteSpace));
        return none ? (false, []) : (true, tiers);
    }

    private static string ReadDescription(JsonElement data, JsonElement item)
    {
        string? text = null;
        if (TryObj(data, "product_description", out var pd) && TryArr(pd, "paragraph_list", out var pl))
            text = JoinParagraphs(pl);
        else if (TryObj(item, "rich_text_description", out var rt) && TryArr(rt, "paragraph_list", out var rpl))
            text = JoinParagraphs(rpl);
        if (string.IsNullOrWhiteSpace(text)) text = Str(item, "description");
        return MassUploadText.Clean(text).Trim();
    }

    private static string JoinParagraphs(JsonElement list)
    {
        var lines = new List<string>();
        foreach (var p in list.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object) continue;
            switch (Long(p, "type"))
            {
                case ParagraphText:
                    lines.Add(Str(p, "text") ?? "");
                    break;
                case ParagraphEmptyLines:
                    var n = Long(p, "empty_paragraph_count") ?? 1;
                    for (var i = 0; i < Math.Clamp(n, 1, 10); i++) lines.Add("");
                    break;
                // type 1 (ảnh) và loại lạ: bỏ — mô tả trong file mẫu chỉ nhận chữ.
            }
        }
        return lines.Any(l => l.Trim().Length > 0) ? string.Join("\n", lines) : "";
    }

    private static long Vnd(decimal micro) =>
        (long)Math.Round(micro / PriceDivisor, MidpointRounding.AwayFromZero);

    /// <summary>Giá micro-VND: số nguyên, số thực / mũ (<c>2.0E10</c>) hay chuỗi số đều đọc được; không đọc được → null.</summary>
    private static decimal? Micro(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number)
        {
            if (v.TryGetDecimal(out var d)) return d;
            return v.TryGetDouble(out var x) && double.IsFinite(x) && Math.Abs(x) < 7.9e27 ? (decimal)x : null;
        }
        if (v.ValueKind == JsonValueKind.String
            && decimal.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) return s;
        return null;
    }

    /// <summary>Chuỗi đã làm sạch (bỏ ký tự điều khiển, NFC) — trước mọi phép đo độ dài ở mapper.</summary>
    private static string Text(JsonElement e, string name) => MassUploadText.Clean(Str(e, name));

    private static bool TryObj(JsonElement e, string name, out JsonElement value) =>
        e.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object;

    private static bool TryArr(JsonElement e, string name, out JsonElement value) =>
        e.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Array;

    private static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : null;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
