using System.Text.RegularExpressions;

namespace Shopee.Core.Harvest;

/// <summary>
/// Khoá SP cho tập "đã lấy xong" của app Harvest. Thứ tự tách id (mọi query/fragment/www/http(s)/hoa thường):
/// <list type="number">
/// <item>path dạng SEO <c>…-i.{shop}.{item}</c> (cặp CUỐI) hoặc <c>/product/{shop}/{item}</c>;</item>
/// <item>cặp tham số <c>shopid</c> + <c>itemid</c> ở NGAY query của link (vd <c>shopee.vn/product?shopid=1&amp;itemid=2</c>);</item>
/// <item>giá trị tham số TRÔNG NHƯ URL (sau khi URL-decode, kể cả decode lồng nhau — <c>s.shopee.vn/an_redir?origin_link=…</c>,
/// <c>shopee.vn/universal-link?redir=…</c>): tách lại đệ quy theo đúng các luật trên.</item>
/// </list>
/// KHÔNG quét cả chuỗi query: <c>search?keyword=ao-i.5.6</c> hay <c>shop?shopid=7&amp;redir=x%26itemid%3D5</c> không phải link SP.
/// Có id → <c>id:{shopId}.{itemId}</c>. Không tách được → <c>url:{host thường}{path bỏ / cuối}[?query]</c>, query GIỮ (tham số
/// sắp theo tên, bỏ tham số theo dõi đã biết — <see cref="TrackingParam"/>) để hai SP khác nhau chỉ khác query không bị gộp làm
/// một. Không phải URL → <c>txt:</c> + chuỗi đã trim. Rỗng → null. Hàm thuần.
/// </summary>
public static class HarvestItemKey
{
    // Neo đầu như HarvestLinks / extension ("…Wi.2.4G…" không thành id); lấy cặp id CUỐI.
    private static readonly Regex SeoIds = new(@"(?:^|[/\-])i\.(\d+)\.(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ProductIds = new(@"/product/(\d+)/(\d+)(?=/|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Digits = new(@"^\d+$", RegexOptions.Compiled);

    private const int MaxDepth = 5;

    public static string FromIds(long shopId, long itemId) => $"id:{shopId}.{itemId}";

    public static string? FromLink(string? link)
    {
        if (string.IsNullOrWhiteSpace(link)) return null;
        var t = link.Trim();
        if (TryParse(t) is not { } uri) return "txt:" + t;
        return IdsOf(uri, 0) ?? UrlKey(uri);
    }

    /// <summary>Khoá <c>url:</c> của link (bỏ qua việc tách id) — null nếu không phải URL.</summary>
    public static string? UrlKeyOf(string? link) =>
        string.IsNullOrWhiteSpace(link) || TryParse(link.Trim()) is not { } uri ? null : UrlKey(uri);

    private static Uri? TryParse(string t)
    {
        var withScheme = t.Contains("://", StringComparison.Ordinal) ? t : "https://" + t.TrimStart('/');
        return Uri.TryCreate(withScheme, UriKind.Absolute, out var uri)
               && uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host) && uri.Host.Contains('.')
            ? uri
            : null;
    }

    private static string UrlKey(Uri uri)
    {
        var key = $"url:{uri.Host.ToLowerInvariant()}{Uri.UnescapeDataString(uri.AbsolutePath).TrimEnd('/')}";
        var kept = KeptQuery(uri.Query.TrimStart('?'));
        return kept.Length == 0 ? key : key + "?" + kept;
    }

    private static string? IdsOf(Uri uri, int depth)
    {
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        var seo = SeoIds.Matches(path);
        if (seo.Count > 0) return Ids(seo[^1].Groups[1].Value, seo[^1].Groups[2].Value);
        var product = ProductIds.Matches(path);
        if (product.Count > 0) return Ids(product[^1].Groups[1].Value, product[^1].Groups[2].Value);

        var parameters = Params(uri.Query.TrimStart('?'));
        string? shop = null, item = null;
        foreach (var (name, value) in parameters)
        {
            if (name is "shopid" or "shop_id" && Digits.IsMatch(value)) shop ??= value;
            if (name is "itemid" or "item_id" && Digits.IsMatch(value)) item ??= value;
        }
        if (shop is not null && item is not null) return Ids(shop, item);
        if (depth >= MaxDepth) return null;

        // Link lồng: CHỈ giá trị trông như URL (decode tới khi hết thay đổi).
        foreach (var (_, raw) in parameters)
        {
            var value = raw;
            for (var i = 0; i < MaxDepth && value.Contains('%'); i++)
            {
                var decoded = Decode(value);
                if (decoded == value) break;
                value = decoded;
            }
            if (!LooksLikeUrl(value) || TryParse(value) is not { } nested) continue;
            if (IdsOf(nested, depth + 1) is { } found) return found;
        }
        return null;
    }

    private static bool LooksLikeUrl(string v) =>
        v.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || v.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || v.StartsWith("//", StringComparison.Ordinal);

    private static List<(string Name, string Value)> Params(string query)
    {
        var list = new List<(string, string)>();
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var name = Decode(eq < 0 ? pair : pair[..eq]).Trim().ToLowerInvariant();
            if (name.Length == 0) continue;
            list.Add((name, eq < 0 ? "" : Decode(pair[(eq + 1)..])));
        }
        return list;
    }

    private static string Decode(string s)
    {
        try { return Uri.UnescapeDataString(s.Replace('+', ' ')); }
        catch (UriFormatException) { return s; }
    }

    /// <summary>Tham số theo dõi (không đổi SP) — bỏ khỏi khoá <c>url:</c>.</summary>
    internal static bool TrackingParam(string name)
    {
        var n = name.ToLowerInvariant();
        return n.StartsWith("utm_", StringComparison.Ordinal) || n.StartsWith("af_", StringComparison.Ordinal)
            || n is "sp_atk" or "xptdk" or "smtt" or "gclid" or "fbclid" or "gbraid" or "wbraid" or "uls_trackid"
                or "mmp_pid" or "publish_id" or "share_channel_code" or "is_from_login" or "deep_and_deferred";
    }

    private static string KeptQuery(string query) =>
        string.Join("&", Params(query).Where(p => !TrackingParam(p.Name))
            .OrderBy(p => p.Name, StringComparer.Ordinal).ThenBy(p => p.Value, StringComparer.Ordinal)
            .Select(p => p.Name + "=" + p.Value));

    private static string? Ids(string shop, string item) =>
        long.TryParse(shop, out var s) && long.TryParse(item, out var i) ? FromIds(s, i) : null;

    /// <summary>
    /// Khoá ghi vào tập khi lưu xong (hoặc lỗi vĩnh viễn) một dòng — theo SP THẬT trong JSON:
    /// <list type="bullet">
    /// <item>khoá id từ JSON (<paramref name="itemId"/> + <paramref name="shopeeShopId"/>) nếu có;</item>
    /// <item>khoá suy từ link GỐC của dòng (chính thứ lúc dựng lại sẽ so): khoá <c>id:</c> chỉ ghi khi KHỚP SP trong JSON
    /// (item id, và shop id nếu JSON có); không khớp (link chuyển hướng sang SP khác) → ghi khoá <c>url:</c> của link thay vì
    /// một <c>id:</c> sai — dòng đó không bị lấy lặp mà SP của link không bị coi là đã có. Link không tách được id → khoá
    /// <c>url:</c>/<c>txt:</c> của nó.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<string> ForSaved(string? sourceLink, long itemId, long? shopeeShopId)
    {
        var keys = new List<string>(3);
        var linkKey = FromLink(sourceLink);
        if (linkKey is not null)
        {
            if (!linkKey.StartsWith("id:", StringComparison.Ordinal) || IdKeyMatches(linkKey, itemId, shopeeShopId))
                keys.Add(linkKey);
            else if (UrlKeyOf(sourceLink) is { } urlKey)
                keys.Add(urlKey);
        }
        if (shopeeShopId is > 0 && itemId > 0)
        {
            var fromJson = FromIds(shopeeShopId.Value, itemId);
            if (!keys.Contains(fromJson)) keys.Add(fromJson);
        }
        return keys;
    }

    private static bool IdKeyMatches(string idKey, long itemId, long? shopeeShopId)
    {
        var dot = idKey.LastIndexOf('.');
        if (dot < 0 || !long.TryParse(idKey[(dot + 1)..], out var item) || item != itemId) return false;
        return shopeeShopId is not > 0 || (long.TryParse(idKey[3..dot], out var shop) && shop == shopeeShopId);
    }
}

/// <summary>Nguồn link của app Harvest: một file Excel do chính app chọn, mỗi sheet một đơn vị chạy. Không có khái niệm tài
/// khoản hay shop đăng bán ở đây — việc chọn shop để đăng SP là của app đăng SP.</summary>
public static class HarvestSource
{
    /// <summary>Hằng định danh nguồn Harvest gửi Hub ở <c>account_id</c> (cột <c>harvested_product_sources.account_id</c>);
    /// <c>sheet</c> = tên sheet trong file Excel. Cũng là "tài khoản" của kho tiến độ <c>harvest-progress.json</c>.</summary>
    public const string AccountId = "harvest";

    /// <summary>Khoá tiến độ của một (file, sheet): đường dẫn đầy đủ đã chuẩn hoá (chữ thường, dấu <c>\</c>) + <c>|</c> + tên
    /// sheet. Cùng file mở bằng đường dẫn tương đối / khác hoa thường ra cùng khoá; hai file trùng tên sheet ra khoá khác.</summary>
    public static string SheetKey(string filePath, string sheet) => $"{NormalizePath(filePath)}|{(sheet ?? "").Trim()}";

    public static string NormalizePath(string filePath)
    {
        var p = (filePath ?? "").Trim();
        if (p.Length == 0) return "";
        try { p = Path.GetFullPath(p); } catch { /* đường dẫn hỏng → giữ nguyên chuỗi */ }
        return p.Replace('/', '\\').ToLowerInvariant();
    }

    /// <summary>itemId của khoá <c>id:{shop}.{item}</c> (<see cref="HarvestItemKey"/>); khoá <c>url:</c> / <c>txt:</c> → null
    /// (không hỏi Hub được).</summary>
    public static long? ItemIdOfKey(string? key)
    {
        if (key is null || !key.StartsWith("id:", StringComparison.Ordinal)) return null;
        var dot = key.LastIndexOf('.');
        return dot > 3 && long.TryParse(key.AsSpan(dot + 1), out var id) && id > 0 ? id : null;
    }
}
