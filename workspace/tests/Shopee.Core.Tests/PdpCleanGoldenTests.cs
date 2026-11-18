using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Shopee.Core.Harvest;

namespace Shopee.Core.Tests;

/// <summary>
/// GĐ7: extension <c>shopee-harvest</c> tự làm sạch JSON rồi đẩy Hub bằng bản JS (<c>extensions/shopee-harvest/pdp-clean.js</c>)
/// — phải ra GIỐNG HỆT <see cref="HarvestPayloadCleaner"/> + <see cref="ShopeePdpParser"/> (cùng chuỗi sạch, cùng
/// <c>raw_hash</c>, cùng cột tóm tắt), không thì cùng một SP lấy bằng app và bằng extension cứ đổi hash qua lại trên Hub.
/// Hai bên cùng đối chiếu MỘT file kỳ vọng <c>Fixtures/pdp-clean/expected.json</c>: test này giữ bản C# đúng với file đó,
/// <c>tests/pdp-clean.test.mjs</c> giữ bản JS đúng với file đó. Đổi luật làm sạch thì sinh lại file (xem README extension).
/// </summary>
public sealed class PdpCleanGoldenTests
{
    private static string FixtureDir(string sub)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Fixtures", sub);
            if (Directory.Exists(candidate)) return candidate;
            candidate = Path.Combine(dir.FullName, "tests", "Shopee.Core.Tests", "Fixtures", sub);
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(sub);
    }

    /// <summary>Mở rộng ký hiệu trong cases.json — PHẢI giống hàm expand của test JS.</summary>
    private static string Expand(JsonObject c)
    {
        var s = c["input"]!.GetValue<string>();
        if (c["depth"] is JsonNode d)
        {
            var n = d.GetValue<int>();
            s = s.Replace("@@NEST@@", new string('[', n) + new string(']', n));
        }
        return Regex.Replace(s, @"\{\{U\+([0-9A-F]{4})\}\}",
            m => ((char)int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());
    }

    private static Dictionary<string, string> Inputs()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in Directory.GetFiles(FixtureDir("pdp"), "*.json"))
            map["fixture:" + Path.GetFileName(f)] = File.ReadAllText(f);
        foreach (var c in JsonNode.Parse(File.ReadAllText(Path.Combine(FixtureDir("pdp-clean"), "cases.json")))!.AsArray())
            map[c!["name"]!.GetValue<string>()] = Expand(c.AsObject());
        return map;
    }

    private static JsonArray Expected() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(FixtureDir("pdp-clean"), "expected.json")))!.AsArray();

    private static string? L(long? v) => v?.ToString(CultureInfo.InvariantCulture);

    /// <summary>Kết quả bản C# cho một đầu vào, cùng hình với một mục của expected.json.</summary>
    private static JsonObject Compute(string name, string input, bool keepClean)
    {
        var o = new JsonObject { ["name"] = name };
        string clean;
        try { clean = HarvestPayloadCleaner.Clean(input); }
        catch (HarvestItemRejectedException) { o["rejected"] = true; return o; }
        o["rejected"] = false;
        if (keepClean) o["clean"] = clean;
        o["hash"] = HarvestPayloadCleaner.Hash(clean);
        try
        {
            var s = ShopeePdpParser.Parse(clean);
            o["summary"] = new JsonObject
            {
                ["itemId"] = L(s.ItemId),
                ["shopId"] = L(s.ShopId),
                ["name"] = s.Name,
                ["catId"] = L(s.CatId),
                ["categoryIds"] = new JsonArray(s.CategoryIds.Select(x => (JsonNode?)L(x)).ToArray()),
                ["brandId"] = L(s.BrandId),
                ["priceMin"] = L(s.PriceMin),
                ["priceMax"] = L(s.PriceMax),
                ["priceBeforeDiscount"] = L(s.PriceBeforeDiscount),
                ["tierCount"] = s.TierCount,
                ["modelCount"] = s.ModelCount,
                ["imageUrls"] = new JsonArray(s.ImageUrls.Select(x => (JsonNode?)x).ToArray()),
                ["videoUrls"] = new JsonArray(s.VideoUrls.Select(x => (JsonNode?)x).ToArray()),
                ["sizeChartUrl"] = s.SizeChartUrl,
            };
        }
        catch (HarvestItemRejectedException) { o["summaryRejected"] = true; }
        return o;
    }

    [Fact]
    public void FileKyVong_PhuDu7FixtureVaMoiCaTongHop()
    {
        var expected = Expected();
        var inputs = Inputs();
        Assert.Equal(7, expected.Count(e => e!["name"]!.GetValue<string>().StartsWith("fixture:", StringComparison.Ordinal)));
        Assert.Equal(inputs.Count, expected.Count);
        Assert.Contains(expected, e => e!["rejected"]!.GetValue<bool>());
        Assert.Contains(expected, e => e!["summaryRejected"] is not null);
    }

    public static IEnumerable<object[]> Names() =>
        Expected().Select(e => new object[] { e!["name"]!.GetValue<string>() });

    /// <summary>CÔNG CỤ, không phải phép thử: đặt biến môi trường <c>HARVEST_PDP_GOLDEN_WRITE=1</c> rồi chạy riêng test này để
    /// sinh lại <c>expected.json</c> từ bản C# (sau khi CỐ Ý đổi luật làm sạch / thêm ca vào cases.json). Không đặt biến → không
    /// làm gì.</summary>
    [Fact]
    public void SinhLaiFileKyVong_ChiKhiDatBienMoiTruong()
    {
        if (Environment.GetEnvironmentVariable("HARVEST_PDP_GOLDEN_WRITE") != "1") return;
        var result = new JsonArray();
        foreach (var f in Directory.GetFiles(FixtureDir("pdp"), "*.json").OrderBy(x => x, StringComparer.Ordinal))
            result.Add(Compute("fixture:" + Path.GetFileName(f), File.ReadAllText(f), keepClean: false));
        foreach (var c in JsonNode.Parse(File.ReadAllText(Path.Combine(FixtureDir("pdp-clean"), "cases.json")))!.AsArray())
            result.Add(Compute(c!["name"]!.GetValue<string>(), Expand(c.AsObject()), keepClean: true));
        var opts = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default };
        File.WriteAllText(Path.Combine(FixtureDir("pdp-clean"), "expected.json"),
            result.ToJsonString(opts).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void CSharp_VanRaDungFileKyVong(string name)
    {
        var expected = Expected().Single(e => e!["name"]!.GetValue<string>() == name)!.AsObject();
        var actual = Compute(name, Inputs()[name], keepClean: expected.ContainsKey("clean"));
        Assert.True(JsonNode.DeepEquals(expected, actual),
            $"bản C# lệch file kỳ vọng ({name}) — đổi luật làm sạch thì sửa cả pdp-clean.js rồi sinh lại expected.json.\n" +
            $"kỳ vọng: {expected.ToJsonString()}\nthực tế: {actual.ToJsonString()}");
    }
}
