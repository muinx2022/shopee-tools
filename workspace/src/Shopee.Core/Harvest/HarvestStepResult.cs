using System.Text.Json;

namespace Shopee.Core.Harvest;

/// <summary>
/// Kết quả MỘT bước <c>__launcherExecuteHarvestStep</c> của extension <c>shopee-harvest</c>
/// (<c>extensions/shopee-harvest/background.js</c>, hàm <c>harvestResult</c>). Mọi lối ra của extension đều có đủ
/// các cờ; thiếu trường (SW cũ, lỗi RPC phía C#) thì coi như false/null.
/// </summary>
public sealed record HarvestStepResult(
    bool HarvestOk,
    bool Captcha,
    bool ProxyError,
    bool TrafficError,
    bool Blocked,
    bool NotProduct,
    bool Aborted,
    bool Superseded,
    string? Message,
    int? TabId,
    string? PageUrl,
    long? ItemId,
    long? ShopId,
    string? Json,
    string? Source,
    string? PdpError,
    string? CapturedUrl = null,
    long? CapturedAtMs = null)
{
    /// <summary>Kết quả lỗi dựng phía C# (RPC không tới được / không có phản hồi) — mọi cờ false.</summary>
    public static HarvestStepResult Failure(string message, int? tabId, string? pageUrl) =>
        new(false, false, false, false, false, false, false, false, message, tabId, pageUrl,
            null, null, null, null, null);

    // Mốc hợp lệ của DateTimeOffset.FromUnixTimeMilliseconds (ngoài khoảng thì ném).
    private const long MaxUnixMs = 253_402_300_799_999;

    /// <summary>Giờ cào của bản ghi: <c>capturedAt</c> (ms epoch do extension đóng dấu lúc hook bắt get_pc — cùng máy,
    /// chính xác hơn) nếu có và hợp lệ; không thì <paramref name="receivedAt"/> (giờ C# nhận kết quả bước).</summary>
    public DateTimeOffset HarvestedAtOr(DateTimeOffset receivedAt) =>
        CapturedAtMs is > 0 and <= MaxUnixMs
            ? DateTimeOffset.FromUnixTimeMilliseconds(CapturedAtMs.Value).ToOffset(receivedAt.Offset)
            : receivedAt;

    /// <summary>Đọc object kết quả bước (giá trị <c>result.value</c> của Runtime.evaluate).
    /// <paramref name="fallbackTabId"/>/<paramref name="fallbackPageUrl"/> dùng khi extension không trả.</summary>
    public static HarvestStepResult Parse(JsonElement value, int? fallbackTabId, string? fallbackPageUrl)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return Failure("Extension trả kết quả không phải object.", fallbackTabId, fallbackPageUrl);

        // harvestOk là cờ riêng của bước harvest; bản cũ chỉ có ok/scrapeOk — đọc lần lượt, cái nào true là true.
        var ok = IsTrue(value, "harvestOk") || IsTrue(value, "ok") || IsTrue(value, "scrapeOk");
        var pageUrl = GetString(value, "pageUrl");
        return new HarvestStepResult(
            ok,
            IsTrue(value, "captcha"),
            IsTrue(value, "proxyError"),
            IsTrue(value, "trafficError"),
            IsTrue(value, "blocked"),
            IsTrue(value, "notProduct"),
            IsTrue(value, "aborted"),
            IsTrue(value, "superseded"),
            GetString(value, "message"),
            value.TryGetProperty("tabId", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out var tab)
                ? tab
                : fallbackTabId,
            string.IsNullOrWhiteSpace(pageUrl) ? fallbackPageUrl : pageUrl,
            GetLong(value, "itemId"),
            GetLong(value, "shopId"),
            GetJson(value, "json"),
            GetString(value, "source"),
            GetScalarText(value, "pdpError"),
            GetString(value, "capturedUrl"),
            GetLong(value, "capturedAt"));
    }

    private static bool IsTrue(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.True;

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    /// <summary>itemId/shopId: extension trả CHUỖI (lấy từ regex link) hoặc SỐ (lấy từ body) — nhận cả hai.</summary>
    private static long? GetLong(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var n)) return n;
        if (el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), out var s)) return s;
        return null;
    }

    /// <summary>JSON SP là chuỗi (body gốc đã cắt riêng tư); phòng xa nếu là object thì lấy nguyên văn.</summary>
    private static string? GetJson(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Object => el.GetRawText(),
            _ => null,
        };
    }

    /// <summary>pdpError là mã lỗi get_pc (số, đôi khi chuỗi) — giữ dạng chữ để log/so khớp.</summary>
    private static string? GetScalarText(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.String => string.IsNullOrWhiteSpace(el.GetString()) ? null : el.GetString(),
            _ => null,
        };
    }
}
