using UpdateProduct;

namespace Shopee.Workspace.Tests;

/// <summary>
/// <see cref="UpdateTargetTracker.RecordProductLimitToast"/>: toast nghi vượt giới hạn chỉ dừng shop khi có ĐỦ ngưỡng SP KHÁC NHAU
/// (item id), nguồn toast, hộp thoại thắng, thread-safe giữa các lane.
/// </summary>
public sealed class UpdateTargetTrackerTests
{
    private static UpdateTargetTracker NewTracker(Action? onLimit = null)
        => new(new Dictionary<string, WorkbookRecord>(), onListingLimitReached: onLimit);

    [Fact]
    public void Threshold_IsTwoDistinctProducts()
        => Assert.Equal(2, UpdateTargetTracker.ListingLimitToastThreshold);

    [Fact]
    public void OneItem_RepeatedManyTimes_IsNeverDecided()
    {
        var fired = 0;
        var t = NewTracker(() => Interlocked.Increment(ref fired));
        for (var i = 0; i < 10; i++)
        {
            var (distinct, decided) = t.RecordProductLimitToast("A");
            Assert.Equal(1, distinct);
            Assert.False(decided);
        }
        Assert.False(t.IsListingLimitReached);
        Assert.False(t.IsListingLimitFromToast);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void TwoDistinctItems_Decide_FromToast_FiringCallbackOnce()
    {
        var fired = 0;
        var t = NewTracker(() => Interlocked.Increment(ref fired));

        var first = t.RecordProductLimitToast("A");
        Assert.Equal((1, false), first);
        Assert.False(t.IsListingLimitReached);

        var second = t.RecordProductLimitToast("B");
        Assert.Equal((2, true), second);
        Assert.True(t.IsListingLimitReached);
        Assert.True(t.IsListingLimitFromToast);
        Assert.Equal(1, fired);

        // Toast thêm (SP thứ 3, hoặc lặp SP cũ) không bắn callback lần nữa.
        Assert.Equal((3, true), t.RecordProductLimitToast("C"));
        Assert.Equal((3, true), t.RecordProductLimitToast("A"));
        Assert.Equal(1, fired);
    }

    [Fact]
    public void SameItemTwice_ThenOtherItem_CountsDistinctOnly()
    {
        var t = NewTracker();
        Assert.Equal((1, false), t.RecordProductLimitToast("A"));
        Assert.Equal((1, false), t.RecordProductLimitToast("A"));
        Assert.False(t.IsListingLimitReached);
        Assert.Equal((2, true), t.RecordProductLimitToast("B"));
        Assert.True(t.IsListingLimitReached);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingItemId_IsNotCounted(string? id)
    {
        var t = NewTracker();
        Assert.Equal((0, false), t.RecordProductLimitToast(id));
        Assert.Equal((0, false), t.RecordProductLimitToast(id));
        Assert.Equal((1, false), t.RecordProductLimitToast("A"));
        Assert.Equal((1, false), t.RecordProductLimitToast(null));
        Assert.False(t.IsListingLimitReached);
    }

    [Fact]
    public void ItemIds_AreCaseSensitive()
    {
        var t = NewTracker();
        t.RecordProductLimitToast("abc");
        Assert.True(t.RecordProductLimitToast("ABC").Decided);
    }

    // ── Nguồn: toast vs hộp thoại; hộp thoại thắng ────────────────────────────────────────────────────────────
    [Fact]
    public void DialogOnly_IsNotFromToast()
    {
        var t = NewTracker();
        t.MarkListingLimitReached();
        Assert.True(t.IsListingLimitReached);
        Assert.False(t.IsListingLimitFromToast);
    }

    [Fact]
    public void ToastDecidedFirst_ThenDialog_DialogWins()
    {
        var t = NewTracker();
        t.RecordProductLimitToast("A");
        t.RecordProductLimitToast("B");
        Assert.True(t.IsListingLimitFromToast);
        t.MarkListingLimitReached();   // hộp thoại đến sau → nâng nguồn lên "đã xác nhận"
        Assert.True(t.IsListingLimitReached);
        Assert.False(t.IsListingLimitFromToast);
    }

    [Fact]
    public void DialogFirst_ThenToastDecides_DialogStillWins()
    {
        var fired = 0;
        var t = NewTracker(() => Interlocked.Increment(ref fired));
        t.MarkListingLimitReached();
        t.RecordProductLimitToast("A");
        t.RecordProductLimitToast("B");
        Assert.True(t.IsListingLimitReached);
        Assert.False(t.IsListingLimitFromToast);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void ToastBelowThreshold_ThenDialog_IsFromDialog()
    {
        var t = NewTracker();
        t.RecordProductLimitToast("A");
        Assert.False(t.IsListingLimitReached);
        t.MarkListingLimitReached();
        Assert.True(t.IsListingLimitReached);
        Assert.False(t.IsListingLimitFromToast);
    }

    // ── Thread-safe ──────────────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Parallel_SameItem_NeverDecides()
    {
        var fired = 0;
        var t = NewTracker(() => Interlocked.Increment(ref fired));
        Parallel.For(0, 500, new ParallelOptions { MaxDegreeOfParallelism = 16 }, _ => t.RecordProductLimitToast("A"));
        Assert.False(t.IsListingLimitReached);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void Parallel_ManyItems_DecidesExactlyOnce()
    {
        for (var round = 0; round < 50; round++)
        {
            var fired = 0;
            var t = NewTracker(() => Interlocked.Increment(ref fired));
            Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i => t.RecordProductLimitToast($"item{i}"));
            Assert.True(t.IsListingLimitReached);
            Assert.True(t.IsListingLimitFromToast);
            Assert.Equal(1, fired);
        }
    }

    [Fact]
    public async Task Parallel_TwoLanesTwoItems_AlwaysDecidedAfterBoth()
    {
        for (var round = 0; round < 200; round++)
        {
            var fired = 0;
            var t = NewTracker(() => Interlocked.Increment(ref fired));
            var a = Task.Run(() => t.RecordProductLimitToast("A"));
            var b = Task.Run(() => t.RecordProductLimitToast("B"));
            await Task.WhenAll(a, b);
            Assert.True(t.IsListingLimitReached);
            Assert.Equal(1, fired);
            // Lane nào ghi nhận SAU cùng phải thấy đã chốt.
            Assert.True((await a).Decided || (await b).Decided);
        }
    }
}
