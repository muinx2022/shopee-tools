using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shopee.Hub;

/// <summary>
/// Lớp chặn RIÊNG TƯ phía Hub cho <c>harvested_products.raw</c> (GĐ7 — extension tự đẩy Hub bằng token chung, không còn chỉ
/// app C# gửi): dù client nào gửi, các khối riêng tư đã biết KHÔNG được vào DB:
/// <list type="bullet">
/// <item><c>data.account</c> (tài khoản Shopee đang đăng nhập) → <c>{}</c>;</item>
/// <item><c>data.product_shipping</c> → chỉ còn <c>shipping_fee_info.ship_from_location</c> (bỏ <c>delivery_address</c> = địa
/// chỉ + toạ độ người xem, phí / kênh giao theo địa chỉ đó); không phải object → <c>null</c>;</item>
/// <item><c>data.shop_vouchers</c> (có <c>signature</c>, <c>customer_reference_id</c> của người xem) → bỏ.</item>
/// </list>
/// Cùng luật với phần riêng tư của <c>HarvestPayloadCleaner</c> (client C#) và <c>pdp-clean.js</c> (extension) — client đúng
/// luật thì raw đã sạch, Hub KHÔNG ghi lại chuỗi (raw + <c>raw_hash</c> của client giữ nguyên từng byte, hash trùng vẫn là
/// "SP không đổi"). Chỉ khi phải cắt Hub mới ghi lại raw và tính lại <c>raw_hash</c> = SHA-256 (hex thường) của chuỗi đã cắt
/// — nội dung đã khác bản client băm. Raw có KHOÁ TRÙNG mà cần cắt → không cắt chắc chắn được → người gọi từ chối mục.
/// </summary>
internal static class HarvestRawGuard
{
    /// <summary>Raw + hash sẽ ghi; <see cref="Scrubbed"/> = Hub đã phải cắt.</summary>
    internal readonly record struct Prepared(string Raw, string Hash, bool Scrubbed);

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
        MaxDepth = 64,
    };

    private static readonly JsonDocumentOptions DocOptions = new() { MaxDepth = 64 };

    /// <summary>Raw đã qua <c>ProductDb.ValidateHarvested</c> (parse được, là object). NÉM <see cref="JsonException"/> /
    /// <see cref="ArgumentException"/> (khoá trùng) / <see cref="InvalidOperationException"/> khi phải cắt mà không cắt được.</summary>
    internal static Prepared Prepare(string raw, string clientHash)
    {
        if (!NeedsScrub(raw)) return new(raw, clientHash, false);
        var root = JsonNode.Parse(raw, null, DocOptions)!.AsObject();
        // Khoá trùng ở gốc / trong data: JsonObject chỉ phát hiện khi chạm tới — chạm hết các khoá để nổ ngay tại đây.
        _ = root.Count;
        if (root["data"] is JsonObject data)
        {
            _ = data.Count;
            if (data.ContainsKey("account")) data["account"] = new JsonObject();
            data.Remove("shop_vouchers");
            if (data.ContainsKey("product_shipping")) data["product_shipping"] = KeepShipFrom(data["product_shipping"]);
        }
        var text = root.ToJsonString(WriteOptions);
        return new(text, Sha256Hex(text), true);
    }

    /// <summary>Raw có khối riêng tư cần cắt không — duyệt MỌI lần xuất hiện của khoá (kể cả khoá trùng: JsonDocument liệt kê
    /// đủ, không chỉ bản cuối như khi tra theo tên).</summary>
    internal static bool NeedsScrub(string raw)
    {
        using var doc = JsonDocument.Parse(raw, DocOptions);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
        foreach (var top in doc.RootElement.EnumerateObject())
        {
            if (top.Name != "data" || top.Value.ValueKind != JsonValueKind.Object) continue;
            foreach (var p in top.Value.EnumerateObject())
            {
                switch (p.Name)
                {
                    case "account" when !IsEmptyObject(p.Value):
                    case "shop_vouchers":
                    case "product_shipping" when !IsShipFromOnly(p.Value):
                        return true;
                }
            }
        }
        return false;
    }

    private static bool IsEmptyObject(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object && !e.EnumerateObject().Any();

    /// <summary><c>null</c>, hoặc object chỉ có (tối đa) một <c>shipping_fee_info</c> chỉ chứa (tối đa) một <c>ship_from_location</c>.</summary>
    private static bool IsShipFromOnly(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Null) return true;
        if (e.ValueKind != JsonValueKind.Object) return false;
        var props = e.EnumerateObject().ToList();
        if (props.Count == 0) return true;
        if (props.Count != 1 || props[0].Name != "shipping_fee_info" || props[0].Value.ValueKind != JsonValueKind.Object) return false;
        var inner = props[0].Value.EnumerateObject().ToList();
        return inner.Count == 0 || (inner.Count == 1 && inner[0].Name == "ship_from_location");
    }

    private static JsonNode? KeepShipFrom(JsonNode? shipping)
    {
        if (shipping is not JsonObject s) return null;
        var kept = new JsonObject();
        if (s["shipping_fee_info"] is JsonObject fee && fee.TryGetPropertyValue("ship_from_location", out var from))
            kept["shipping_fee_info"] = new JsonObject { ["ship_from_location"] = from?.DeepClone() };
        return kept;
    }

    private static string Sha256Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
