using Shopee.Core.BigSeller;

namespace Shopee.Core.Tests;

/// <summary>Import 1 shop / lần: tiến độ <see cref="ImportShopState"/>, chọn 1 shop trong hộp (<see cref="ImportShopMatcher.PickMany"/> với 1 tên),
/// phán tick Crawl List (<see cref="ImportBatchPlanner.TickedMatchesBatch"/>), và CheckedSetMatches.</summary>
public sealed class ImportSingleShopPlannerTests
{
    private static ImportShopState Shop(string key, params string[] ids) => new(key, key + ".store", ids);

    private static readonly string[] Labels = ["alina99.store", "shop9x.store", "six6shop.store", "Alina99 Store"];

    [Fact]
    public void ImportShopState_NeedsAndMarkDone()
    {
        var s = Shop("a", "1", "2", "3");
        Assert.True(s.Needs("1"));
        Assert.False(s.IsComplete);
        s.MarkDone(["1", "2"]);
        Assert.False(s.Needs("1"));
        Assert.True(s.Needs("3"));
        s.MarkDone(["3"]);
        Assert.True(s.IsComplete);
    }

    [Fact]
    public void ImportShopState_SeedDone_ReplacesAndIgnoresForeignIds()
    {
        var s = Shop("a", "1", "2");
        Assert.Equal(1, s.SeedDone(["1", "9"]));
        Assert.False(s.Needs("1"));
        Assert.True(s.Needs("2"));
        Assert.Equal(2, s.SeedDone(["1", "2", "x"]));
        Assert.True(s.IsComplete);
    }

    [Fact]
    public void PickMany_OneName_SameAsPick()
    {
        var pick = ImportShopMatcher.PickMany(Labels, ["shop9x.store"]);
        Assert.Null(pick.Error);
        Assert.Equal([ImportShopMatcher.Pick(Labels, "shop9x.store")], pick.Indices);
    }

    [Fact]
    public void PickMany_NoNames_IsRejected()
    {
        Assert.NotNull(ImportShopMatcher.PickMany(Labels, []).Error);
    }

    [Theory]
    [InlineData(new[] { true, false, true }, new[] { 0, 2 }, true)]
    [InlineData(new[] { true, true, true }, new[] { 0, 2 }, false)]
    [InlineData(new[] { false, true, false }, new[] { 1 }, true)]
    [InlineData(new[] { true, false }, new[] { 5 }, false)]
    public void CheckedSetMatches_RequiresExactSet(bool[] flags, int[] expected, bool match)
    {
        Assert.Equal(match, ImportShopMatcher.CheckedSetMatches(flags, expected));
    }

    [Fact]
    public void TickedMatchesBatch_ExactSet_IsTrue()
    {
        Assert.True(ImportBatchPlanner.TickedMatchesBatch(["1", "2", "3"], ["3", "1", "2"], 3));
    }

    [Theory]
    [InlineData(new[] { "1", "2" }, 2)]
    [InlineData(new[] { "1", "2", "3", "4" }, 4)]
    [InlineData(new[] { "1", "2", "9" }, 3)]
    [InlineData(new[] { "1", "2", "3" }, 4)]
    public void TickedMatchesBatch_AnyMismatch_IsFalse(string[] ticked, int rows)
    {
        Assert.False(ImportBatchPlanner.TickedMatchesBatch(["1", "2", "3"], ticked, rows));
    }

    [Fact]
    public void TickedMatchesBatch_NothingTicked_IsFalseForNonEmptyBatch()
    {
        Assert.False(ImportBatchPlanner.TickedMatchesBatch(["1"], [], 0));
    }

    [Theory]
    [InlineData(true, "", null, true)]
    [InlineData(false, "vxe-checkbox--icon vxe-icon-checkbox-checked", null, true)]
    [InlineData(false, "vxe-cell--checkbox is--checked", null, true)]
    [InlineData(false, "vxe-checkbox--icon", "true", true)]
    [InlineData(false, "vxe-checkbox--icon vxe-icon-checkbox-unchecked", null, false)]
    [InlineData(false, "", null, false)]
    public void VxeCheckboxLooksChecked_MultiSignal(bool inputChecked, string classNames, string? aria, bool expected)
    {
        Assert.Equal(expected, ImportBatchPlanner.VxeCheckboxLooksChecked(inputChecked, classNames, aria));
    }
}
