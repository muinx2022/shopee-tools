using System.Text.Json;
using Shopee.Core.Harvest;

namespace Shopee.Core.Tests;

/// <summary>Dữ liệu dùng chung cho test GĐ4 (outbox / worker / cleaner / parser): fixture PDP thật + thư mục tạm.</summary>
internal static class HarvestTestData
{
    public static readonly string[] Fixtures =
    [
        "pdp-1tang-4model-1anh.json",
        "pdp-1tang-70model-4capdm.json",
        "pdp-2tang-8model.json",
        "pdp-danurl-1tang-6model-anhsg.json",
        "pdp-khong-bien-the.json",
        "pdp-motaanh-brand-voucher.json",
        "pdp-video-1tang-11model-2capdm.json",
    ];

    public static string FixturePath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Fixtures", "pdp", name);
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(dir.FullName, "tests", "Shopee.Core.Tests", "Fixtures", "pdp", name);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(name);
    }

    public static string Fixture(string name) => File.ReadAllText(FixturePath(name));

    public static long ItemIdOf(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("data").GetProperty("item").GetProperty("item_id").GetInt64();
    }

    public static readonly DateTimeOffset T0 = new(2026, 11, 16, 8, 0, 0, TimeSpan.FromHours(7));

    /// <summary>Mục engine giao cho sink, JSON = fixture <paramref name="fixture"/>.</summary>
    public static HarvestedItem Item(string fixture, int row, DateTimeOffset? at = null, string acct = "acc-1",
        string sheet = "Shop1", string? json = null)
    {
        json ??= Fixture(fixture);
        var id = ItemIdOf(json);
        return new HarvestedItem(acct, "shop-1", sheet, row, $"https://shopee.vn/product/1/{id}",
            $"https://shopee.vn/San-pham-i.1.{id}?sp_atk=goc", id, null, json, "hook", at ?? T0);
    }

    /// <summary>PDP tối giản hợp lệ với itemId tuỳ ý — cho test cần nhiều SP khác nhau hơn 7 fixture.</summary>
    public static string SyntheticJson(long itemId) =>
        "{\"error\":null,\"data\":{\"item\":{\"item_id\":" + itemId + ",\"shop_id\":77,\"title\":\"SP " + itemId
        + "\"},\"product_images\":{\"images\":[\"vn-a\"],\"video\":null}}}";

    public static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "harvest-outbox-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, true); } catch { /* file tạm, bỏ qua */ }
    }
}
