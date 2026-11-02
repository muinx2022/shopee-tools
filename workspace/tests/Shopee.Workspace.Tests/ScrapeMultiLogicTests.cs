using System.Collections.Generic;
using Shopee.Workspace.Modules.Scrape;
using Shopee.Workspace.Modules.Workspace;
using Xunit;

namespace Shopee.Workspace.Tests;

public sealed class ScrapeMultiLogicTests
{
    [Fact]
    public void LoginFailureTracker_MarkAndClear()
    {
        var t = new LoginFailureTracker();
        Assert.False(t.IsFailed("a"));
        t.MarkFailed("a");
        Assert.True(t.IsFailed("A"));
        Assert.False(t.IsFailed("b"));
        t.Clear();
        Assert.False(t.IsFailed("a"));
    }

    [Fact]
    public void LoginFailureTracker_TryLogOnce_OnlyOncePerFailedAccount()
    {
        var t = new LoginFailureTracker();
        Assert.False(t.TryLogOnce("a"));   // chưa lỗi → không log
        t.MarkFailed("a");
        Assert.True(t.TryLogOnce("a"));
        Assert.False(t.TryLogOnce("a"));
        t.Clear();
        t.MarkFailed("a");
        Assert.True(t.TryLogOnce("a"));    // lượt chạy mới → log lại 1 lần
    }

    [Fact]
    public void Selection_PendingWinsOverPrevious_AndNewAccountsDefaultSelected()
    {
        var pending = new HashSet<string>(new[] { "a" }, System.StringComparer.OrdinalIgnoreCase);
        var prev = new Dictionary<string, bool> { ["a"] = false, ["b"] = true };
        Assert.True(ScrapeMultiSelection.Resolve("A", pending, prev));
        Assert.False(ScrapeMultiSelection.Resolve("b", pending, prev));

        Assert.False(ScrapeMultiSelection.Resolve("a", null, prev));
        Assert.True(ScrapeMultiSelection.Resolve("b", null, prev));
        Assert.True(ScrapeMultiSelection.Resolve("new", null, prev));
    }

    [Fact]
    public void JobOutcome_Factories()
    {
        Assert.Equal(ScrapeJobStatus.Completed, ScrapeJobOutcome.Completed().Status);
        Assert.Equal(ScrapeJobStatus.Stopped, ScrapeJobOutcome.Stopped().Status);
        var f = ScrapeJobOutcome.Failed("x");
        Assert.Equal(ScrapeJobStatus.Failed, f.Status);
        Assert.Equal("x", f.Reason);
        Assert.Equal(ScrapeJobStatus.NotStarted, ScrapeJobOutcome.NotStarted("y").Status);
    }
}
