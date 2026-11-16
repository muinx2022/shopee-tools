using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;

namespace Shopee.Core.Harvest;

/// <summary>
/// Làm sạch JSON <c>get_pc</c> trước khi lưu outbox / tính <c>raw_hash</c> / đẩy Hub (GĐ4):
/// <list type="number">
/// <item>Bỏ ký tự <c>\u0000</c> và surrogate UTF-16 LẺ (emoji bị cắt đôi) — cả dạng ký tự thô lẫn dạng escape
/// <c>\uXXXX</c> trong chuỗi JSON. Postgres (jsonb/text) không nhận, System.Text.Json cũng ném khi đọc.</item>
/// <item>Riêng tư (lớp an toàn thứ hai — extension đã cắt): <c>data.account</c> → <c>{}</c>; <c>data.product_shipping</c>
/// chỉ còn <c>shipping_fee_info.ship_from_location</c> (nên <c>delivery_address</c> — địa chỉ + toạ độ của tài khoản đang
/// đăng nhập — không bao giờ lọt).</item>
/// <item>Bỏ khối phụ thuộc NGƯỜI XEM / THỜI ĐIỂM (plan mục 1): <c>data.shop_vouchers</c>, <c>data.top_banner</c>,
/// <c>data.coin_info</c>, <c>data.product_review.liked</c>, kênh/phí/ngày giao trong <c>data.product_shipping</c>,
/// <c>data.product_price.final_price_info</c> + <c>final_price_vouchers</c> (giá sau voucher của người xem),
/// <c>data.price_breakdown</c>, <c>data.button_group</c> — để cùng một SP cào lại không đổi hash vô cớ. Giá, rating, số
/// đã bán GIỮ (đổi là thay đổi thật của SP).</item>
/// </list>
/// SỐ giữ NGUYÊN văn bản gốc (số &gt; 2^53, số thực như <c>rating_star</c>): JsonNode đọc từ JsonDocument ghi lại đúng
/// chuỗi số thô. Thứ tự khoá giữ nguyên; xuất gọn không thụt dòng, chữ Việt không escape.
/// </summary>
public static class HarvestPayloadCleaner
{
    /// <summary>Độ sâu tối đa của JSON PDP — CÙNG một giới hạn cho bộ đọc lẫn bộ ghi, và khớp mặc định 64 của
    /// System.Text.Json mà Hub dùng khi kiểm <c>raw</c> (sâu hơn thì Hub cũng từ chối). PDP thật sâu ~9 tầng.</summary>
    public const int MaxDepth = 64;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
        MaxDepth = MaxDepth,
    };

    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = false };
    private static readonly JsonDocumentOptions DocOptions = new() { MaxDepth = MaxDepth };

    /// <summary>JSON sạch từ JSON extension trả. NÉM <see cref="HarvestItemRejectedException"/> khi không xử lý được (không
    /// phải object JSON có <c>data.item</c>, sâu quá <see cref="MaxDepth"/>, khoá trùng…) — lỗi của RIÊNG SP này.</summary>
    public static string Clean(string json)
    {
        try
        {
            var text = StripInvalidJsonText(json ?? "");
            // Khoá trùng ở BẤT KỲ đâu → từ chối. JsonObject chỉ phát hiện khi khối bị chạm tới (lười); khối không chạm thì
            // đi thẳng ra chuỗi kèm khoá trùng, Postgres jsonb lặng lẽ giữ khoá sau — nên kiểm hết một lượt cho rõ ràng.
            using (var doc = JsonDocument.Parse(text, DocOptions))
                RejectDuplicateKeys(doc.RootElement);
            var root = JsonNode.Parse(text, NodeOptions, DocOptions);
            if (root is not JsonObject obj || obj["data"] is not JsonObject data || data["item"] is not JsonObject)
                throw new HarvestItemRejectedException("JSON PDP thiếu data.item");

            if (data.ContainsKey("account")) data["account"] = new JsonObject();
            data.Remove("shop_vouchers");
            data.Remove("top_banner");
            data.Remove("coin_info");
            data.Remove("price_breakdown");
            data.Remove("button_group");
            if (data["product_price"] is JsonObject price)
            {
                price.Remove("final_price_info");
                price.Remove("final_price_vouchers");
            }
            if (data["product_review"] is JsonObject review) review.Remove("liked");
            if (data.ContainsKey("product_shipping"))
                data["product_shipping"] = KeepShipFrom(data["product_shipping"]);
            return obj.ToJsonString(WriteOptions);
        }
        catch (HarvestItemRejectedException) { throw; }
        // JsonException: cú pháp hỏng / sâu quá MaxDepth (đọc). ArgumentException: JsonObject gặp khoá trùng khi dựng từ
        // điển (lười — có thể nổ ở bất kỳ lần truy cập nào). InvalidOperationException: bộ ghi vượt MaxDepth / surrogate.
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new HarvestItemRejectedException("JSON PDP không xử lý được: " + ex.Message, ex);
        }
    }

    private static void RejectDuplicateKeys(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in e.EnumerateObject())
                {
                    if (!seen.Add(p.Name)) throw new HarvestItemRejectedException($"JSON PDP có khoá trùng \"{p.Name}\"");
                    RejectDuplicateKeys(p.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var v in e.EnumerateArray()) RejectDuplicateKeys(v);
                break;
        }
    }

    /// <summary><c>data.product_shipping</c> → chỉ còn <c>{"shipping_fee_info":{"ship_from_location":…}}</c> (nơi gửi
    /// hàng là thuộc tính của SP). Danh sách GIỮ chứ không phải danh sách BỎ: khối này gần như toàn bộ phụ thuộc địa chỉ
    /// người xem, Shopee thêm trường mới cũng không lọt địa chỉ / làm đổi hash.</summary>
    private static JsonNode? KeepShipFrom(JsonNode? shipping)
    {
        if (shipping is not JsonObject s) return null;
        var kept = new JsonObject();
        if (s["shipping_fee_info"] is JsonObject fee && fee.TryGetPropertyValue("ship_from_location", out var from))
            kept["shipping_fee_info"] = new JsonObject { ["ship_from_location"] = from?.DeepClone() };
        return kept;
    }

    /// <summary><c>raw_hash</c> = SHA-256 (hex thường) của chuỗi UTF-8 đã làm sạch.</summary>
    public static string Hash(string cleanJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cleanJson))).ToLowerInvariant();

    /// <summary>Bỏ <c>\0</c> và surrogate lẻ khỏi một chuỗi văn bản thường (link, tên sheet…) — Postgres text không nhận.</summary>
    public static string StripInvalidText(string? s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        StringBuilder? sb = null;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            var bad = c == '\0'
                      || (char.IsHighSurrogate(c) && (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1])))
                      || (char.IsLowSurrogate(c) && (i == 0 || !char.IsHighSurrogate(s[i - 1])));
            if (bad) { sb ??= new StringBuilder(s, 0, i, s.Length); continue; }
            sb?.Append(c);
        }
        return sb?.ToString() ?? s;
    }

    /// <summary>Như <see cref="StripInvalidText"/> nhưng hiểu JSON: trong chuỗi JSON còn bỏ escape <c>\u0000</c> và
    /// escape surrogate lẻ (cặp <c>\uD8xx\uDCxx</c> hợp lệ giữ nguyên). Dấu <c>\\</c> được tính đúng (<c>\\u0000</c> là chữ
    /// thường, không phải escape).</summary>
    internal static string StripInvalidJsonText(string json)
    {
        var s = StripInvalidText(json);
        if (s.IndexOf("\\u", StringComparison.OrdinalIgnoreCase) < 0) return s;
        var sb = new StringBuilder(s.Length);
        var inString = false;
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (!inString)
            {
                if (c == '"') inString = true;
                sb.Append(c);
                i++;
                continue;
            }
            if (c == '"') { inString = false; sb.Append(c); i++; continue; }
            if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); i++; continue; }
            if (s[i + 1] != 'u' || !TryHex4(s, i + 2, out var code))
            {
                sb.Append(c).Append(s[i + 1]);   // escape khác (\" \\ \n …): chép nguyên 2 ký tự
                i += 2;
                continue;
            }
            if (code == 0) { i += 6; continue; }
            if (code is >= 0xD800 and <= 0xDBFF)
            {
                // Cao: giữ khi ngay sau là escape surrogate thấp; không thì bỏ.
                if (i + 7 < s.Length && s[i + 6] == '\\' && s[i + 7] == 'u' && TryHex4(s, i + 8, out var low)
                    && low is >= 0xDC00 and <= 0xDFFF)
                {
                    sb.Append(s, i, 12);
                    i += 12;
                }
                else i += 6;
                continue;
            }
            if (code is >= 0xDC00 and <= 0xDFFF) { i += 6; continue; }   // thấp đứng một mình
            sb.Append(s, i, 6);
            i += 6;
        }
        return sb.ToString();
    }

    private static bool TryHex4(string s, int start, out int value)
    {
        value = 0;
        if (start + 4 > s.Length) return false;
        for (var k = 0; k < 4; k++)
        {
            var h = s[start + k];
            int d = h switch
            {
                >= '0' and <= '9' => h - '0',
                >= 'a' and <= 'f' => h - 'a' + 10,
                >= 'A' and <= 'F' => h - 'A' + 10,
                _ => -1,
            };
            if (d < 0) return false;
            value = value * 16 + d;
        }
        return true;
    }
}
