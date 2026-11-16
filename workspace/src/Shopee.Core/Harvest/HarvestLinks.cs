using System.Text.RegularExpressions;

namespace Shopee.Core.Harvest;

/// <summary>Làm sạch link sản phẩm trước khi giao bước Harvest cho extension.</summary>
public static class HarvestLinks
{
    // Neo ĐẦU như extension (pdp-core.extractProductIds: "(?:^|[/\-])i\.(\d+)\.(\d+)") để tên SP kiểu "…Wi.2.4G…"
    // không bị đọc thành id (P-20261115-11) — nhưng KHÔNG neo đuôi: mục đích chính là cắt đuôi lạ dính sau id
    // (vd "…-i.1.2%7DD"), thứ mà extension (neo cả đuôi) sẽ báo notProduct.
    private static readonly Regex AnchoredIds = new(@"(?:^|[/\-])i\.(\d+)\.(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Bản neo đầu của <see cref="Scrape.ShopeeApiDataHelper.CanonicalProductUrl"/>: link SEO
    /// <c>…-i.{shop}.{item}…</c> → <c>{scheme}://{host}/product/{shop}/{item}</c>. Lấy cặp id CUỐI CÙNG (id thật luôn
    /// ở cuối slug). Không nhận ra (link <c>/product/…</c>, link rút gọn, chuỗi không phải URL) → trả link gốc đã trim.
    /// Null/rỗng → chuỗi rỗng.
    /// </summary>
    public static string CleanProductUrl(string? link)
    {
        if (string.IsNullOrWhiteSpace(link)) return "";
        var trimmed = link.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return trimmed;

        var matches = AnchoredIds.Matches(uri.AbsolutePath);
        if (matches.Count == 0) return trimmed;
        var m = matches[^1];
        return $"{uri.Scheme}://{uri.Host}/product/{m.Groups[1].Value}/{m.Groups[2].Value}";
    }
}
