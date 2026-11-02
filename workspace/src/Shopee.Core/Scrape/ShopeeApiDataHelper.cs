using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Shopee.Core.Scrape;

/// <summary>
/// Hỗ trợ giải nén dữ liệu sản phẩm Shopee PDP mà BigSeller extension lưu trữ trong window.localStorage ("shopee-api-data-...").
/// Dữ liệu là chuỗi Base64 chứa GZip payload của URL-encoded JSON.
/// </summary>
public static class ShopeeApiDataHelper
{
    public static string? TryDecompress(string? base64Gzip)
    {
        if (string.IsNullOrWhiteSpace(base64Gzip)) return null;
        try
        {
            var trimmed = base64Gzip.Trim();
            // Bỏ dấu nháy kép bọc quanh nếu chuỗi được serialize qua JSON
            if (trimmed.Length >= 2 && trimmed.StartsWith("\"") && trimmed.EndsWith("\""))
            {
                trimmed = trimmed.Substring(1, trimmed.Length - 2);
            }

            var bytes = Convert.FromBase64String(trimmed);
            using var ms = new MemoryStream(bytes);
            using var gz = new GZipStream(ms, CompressionMode.Decompress);
            using var reader = new StreamReader(gz, Encoding.UTF8);
            var unescaped = reader.ReadToEnd();
            return Uri.UnescapeDataString(unescaped);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Trích xuất Shopee Item ID (ID sản phẩm) từ URL của Shopee (hỗ trợ nhiều format: i.{shopId}.{itemId}, /product/..., query params, v.v.).
    /// </summary>
    public static string? TryExtractShopeeItemId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        // 1. Format tiêu chuẩn của Shopee: i.<shopId>.<itemId>
        var m = Regex.Match(url, @"i\.\d+\.(\d+)", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value;

        // 2. Format /product/<shopId>/<itemId>
        m = Regex.Match(url, @"/product/\d+/(\d+)", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value;

        // 3. Query param: itemid=<itemId> hoặc item_id=<itemId>
        m = Regex.Match(url, @"[?&](?:item_?id|itemId)=(\d+)", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value;

        // 4. Format /pdp/<itemId>
        m = Regex.Match(url, @"/pdp/(?:item/)?(\d+)", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value;

        // 5. Chuỗi 9-16 chữ số ở cuối URL
        m = Regex.Match(url, @"[\./\-](\d{9,16})(?:[?#&/]|$)");
        if (m.Success) return m.Groups[1].Value;

        return null;
    }

    /// <summary>
    /// Kiểm tra dữ liệu JSON decompressed từ Shopee PDP có khớp với Item ID chỉ định hay không.
    /// </summary>
    public static bool MatchesItemId(string? json, string? itemId)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(itemId)) return false;
        return json.Contains(itemId, StringComparison.OrdinalIgnoreCase);
    }
}
