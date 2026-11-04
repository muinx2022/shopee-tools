using System.Text.Json;
using Shopee.Core.Coordination;
using Shopee.Core.Progress;

namespace Shopee.Core.Tests;

/// <summary>
/// Cờ <see cref="OpProgress.ListingLimit"/>: ApplyFinish bật/tắt, ApplyBegin tắt khi mở lượt mới,
/// JSON <c>false</c> không có field, <c>true</c> có, file thiếu field đọc ra <c>false</c>. Không đụng file tiến độ.
/// </summary>
public sealed class OpProgressListingLimitTests
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [Fact]
    public void ApplyFinish_BatCo_Va_TatCo()
    {
        var p = new OpProgress { Status = LedgerStatus.Running };

        OpProgressStore.ApplyFinish(p, completed: true, listingLimit: true);
        Assert.True(p.ListingLimit);
        Assert.Equal(LedgerStatus.Completed, p.Status);

        OpProgressStore.ApplyFinish(p, completed: false, listingLimit: false);
        Assert.False(p.ListingLimit);
        Assert.Equal(LedgerStatus.Stopped, p.Status);

        p.ListingLimit = true;
        OpProgressStore.ApplyFinish(p, completed: true, listingLimit: false);
        Assert.False(p.ListingLimit);
        Assert.Equal(LedgerStatus.Completed, p.Status);

        OpProgressStore.ApplyFinish(p, completed: false, listingLimit: true);
        Assert.True(p.ListingLimit);
        Assert.Equal(LedgerStatus.Stopped, p.Status);
    }

    [Fact]
    public void ApplyBegin_TatCoKhiMoLuotMoi()
    {
        var p = new OpProgress { Status = LedgerStatus.Completed, ListingLimit = true };

        OpProgressStore.ApplyBegin(p);

        Assert.False(p.ListingLimit);
        Assert.Equal(LedgerStatus.Running, p.Status);
        Assert.NotNull(p.LastRunAt);
    }

    [Fact]
    public void ListingLimit_JsonFalseKhongCoField_TrueCo_ThieuDocRaFalse()
    {
        var off = JsonSerializer.Serialize(new List<OpProgress> { new() { AccountId = "a", ListingLimit = false } }, Indented);
        Assert.DoesNotContain("ListingLimit", off);

        var on = JsonSerializer.Serialize(new List<OpProgress> { new() { AccountId = "a", ListingLimit = true } }, Indented);
        Assert.Contains("\"ListingLimit\": true", on);

        const string missing = """
            [
              {
                "AccountId": "a",
                "Sheet": "S",
                "Op": "update",
                "Status": "completed"
              }
            ]
            """;
        var read = JsonSerializer.Deserialize<List<OpProgress>>(missing, Indented);
        Assert.NotNull(read);
        Assert.Single(read!);
        Assert.False(read[0].ListingLimit);
    }

    [Fact]
    public void AnyListingLimit_DungCacKhoaSheetUngVien()
    {
        var update = new OpProgress
        {
            AccountId = "acc",
            Sheet = "shared:shop",
            Op = AssignmentOps.Update,
            ListingLimit = true,
        };
        var otherSheet = new OpProgress
        {
            AccountId = "acc",
            Sheet = "khac",
            Op = AssignmentOps.Update,
            ListingLimit = false,
        };
        var import = new OpProgress
        {
            AccountId = "acc",
            Sheet = "shared:shop",
            Op = AssignmentOps.Import,
            ListingLimit = true,
        };
        var items = new[] { update, otherSheet, import };

        Assert.True(OpProgressStore.AnyListingLimit(items, "acc", new[] { "SHARED:SHOP", "Sheet1", null, "  " }, AssignmentOps.Update));
        Assert.False(OpProgressStore.AnyListingLimit(items, "acc", new[] { "khac", "Sheet1" }, AssignmentOps.Update));
        Assert.True(OpProgressStore.AnyListingLimit(items, "acc", new[] { "shared:shop" }, AssignmentOps.Import));
        Assert.False(OpProgressStore.AnyListingLimit(items, "acc-khac", new[] { "shared:shop" }, AssignmentOps.Update));
        Assert.False(OpProgressStore.AnyListingLimit(items, "acc", new string?[] { null, "  " }, AssignmentOps.Update));
    }
}
