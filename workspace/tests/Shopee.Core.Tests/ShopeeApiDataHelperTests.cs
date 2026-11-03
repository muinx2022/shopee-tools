using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Shopee.Core.Scrape;
using Xunit;

namespace Shopee.Core.Tests;

public sealed class ShopeeApiDataHelperTests
{
    [Theory]
    [InlineData("https://shopee.vn/(%E1%BA%A2NH-TH%E1%BA%ACT)-%C3%81o-s%C6%A1-mi-th%C3%B4-form-r%E1%BB%99ng-1-t%C3%BAi-H%C3%A0n-Qu%E1%BB%91c-color-block-i.24190785.21987398019", "21987398019")]
    [InlineData("https://shopee.vn/(%E1%BA%A2NH-TH%E1%BA%ACT)-%C3%81o-s%C6%A1-mi-th%C3%B4-form-r%E1%BB%99ng-1-t%C3%BAi-H%C3%A0n-Qu%E1%BB%91c-color-block-i.24190785.21987398019?extraParams=%7B%22display_model_id%22%3A233264908472%2C%22model_selection_logic%22%3A3%7D", "21987398019")]
    [InlineData("https://shopee.vn/-H%C3%80NG-S%E1%BA%B4N-Ch%C3%A2n-v%C3%A1y-d%C3%A1ng-xo%C3%A8-ch%E1%BA%A5t-VOAN-NH%C4%82N-3-t%E1%BA%A7ng-c%E1%BA%A1p-chun-co-gi%C3%A3n-NHI%E1%BB%80U-M%C3%80U-si%C3%AAu-xinh-%C4%90anh-Cho-N%E1%BB%AF-i.24190785.54107239906", "54107239906")]
    [InlineData("https://shopee.vn/-H%C3%80NG-S%E1%BA%B4N-Ch%C3%A2n-v%C3%A1y-d%C3%A1ng-xo%C3%A8-ch%E1%BA%A5t-VOAN-NH%C4%82N-3-t%E1%BA%A7ng-c%E1%BA%A1p-chun-co-gi%C3%A3n-NHI%E1%BB%80U-M%C3%80U-si%C3%AAu-xinh-%C4%90anh-Cho-N%E1%BB%AF-i.24190785.54107239906?extraParams=%7B%22display_model_id%22%3A307456925747%2C%22model_selection_logic%22%3A3%7D", "54107239906")]
    [InlineData("https://shopee.vn/product/24190785/54107239906", "54107239906")]
    [InlineData("https://shopee.vn/product/24190785/54107239906?extraParams=xyz", "54107239906")]
    [InlineData("https://shopee.vn/pdp/54107239906", "54107239906")]
    [InlineData("https://shopee.vn/universal-link?itemid=54107239906", "54107239906")]
    [InlineData("https://shopee.vn/universal-link?item_id=54107239906&shop_id=24190785", "54107239906")]
    public void TryExtractShopeeItemId_ExtractsCorrectId(string url, string expectedId)
    {
        var actual = ShopeeApiDataHelper.TryExtractShopeeItemId(url);
        Assert.Equal(expectedId, actual);
    }

    [Fact]
    public void TryExtractShopeeItemId_ReturnsNullForInvalidUrl()
    {
        Assert.Null(ShopeeApiDataHelper.TryExtractShopeeItemId(null));
        Assert.Null(ShopeeApiDataHelper.TryExtractShopeeItemId(""));
        Assert.Null(ShopeeApiDataHelper.TryExtractShopeeItemId("   "));
        Assert.Null(ShopeeApiDataHelper.TryExtractShopeeItemId("https://google.com"));
    }

    [Fact]
    public void MatchesItemId_ChecksMatchingAndMismatching()
    {
        var jsonChanVay = """{"data":{"item":{"itemid":54107239906,"title":"Chân váy dài"}}}""";
        var jsonAoSomi = """{"data":{"item":{"itemid":21987398019,"title":"Áo sơ mi"}}}""";

        // Khớp đúng item id
        Assert.True(ShopeeApiDataHelper.MatchesItemId(jsonChanVay, "54107239906"));
        Assert.True(ShopeeApiDataHelper.MatchesItemId(jsonAoSomi, "21987398019"));

        // Lệch item id → KHÔNG khớp
        Assert.False(ShopeeApiDataHelper.MatchesItemId(jsonAoSomi, "54107239906"));
        Assert.False(ShopeeApiDataHelper.MatchesItemId(jsonChanVay, "21987398019"));

        // Null / rỗng
        Assert.False(ShopeeApiDataHelper.MatchesItemId(null, "54107239906"));
        Assert.False(ShopeeApiDataHelper.MatchesItemId(jsonChanVay, null));
    }

    [Fact]
    public void TryDecompress_RoundTrip()
    {
        var originalJson = """{"data":{"item":{"itemid":54107239906,"title":"[HÀNG SẴN] Chân váy dài"}}}""";
        var escaped = Uri.EscapeDataString(originalJson);
        var bytes = Encoding.UTF8.GetBytes(escaped);

        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            gz.Write(bytes, 0, bytes.Length);
        }

        var base64 = Convert.ToBase64String(ms.ToArray());
        var decompressed = ShopeeApiDataHelper.TryDecompress(base64);

        Assert.Equal(originalJson, decompressed);
    }

    [Theory]
    [InlineData("https://shopee.vn/Ten-SP-i.123.456", "https://shopee.vn/product/123/456")]
    [InlineData("https://shopee.vn/Ten-SP-i.123.456%7DD", "https://shopee.vn/product/123/456")]
    [InlineData("https://shopee.vn/product/123/456", null)]
    [InlineData("https://shopee.vn/search?q=abc", null)]
    [InlineData("", null)]
    public void CanonicalProductUrl_StripsJunkTail(string url, string? expected) =>
        Assert.Equal(expected, ShopeeApiDataHelper.CanonicalProductUrl(url));

    [Theory]
    [InlineData("https://shopee.vn/verify/captcha?x=1", true)]
    [InlineData("https://shopee.vn/buyer/login?next=a", true)]
    [InlineData("https://shopee.vn/verify/traffic/error", true)]
    [InlineData("https://shopee.vn/product/123/456", false)]
    [InlineData("", false)]
    public void IsBlockedPageUrl_DetectsLoginCaptcha(string url, bool expected) =>
        Assert.Equal(expected, ShopeeApiDataHelper.IsBlockedPageUrl(url));
}
