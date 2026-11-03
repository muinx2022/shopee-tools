using System.Collections.Generic;
using Shopee.Core.BigSeller;
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

    // ── Quyết định phiên khi sync lỗi: rớt mạng KHÔNG được coi là mất phiên ──

    [Theory]
    // (isSessionIssue, probe, inLock) → action
    [InlineData(false, BigSellerSessionProbe.Alive, false, ScrapeMultiSyncAction.Book)]        // phiên ổn, lỗi khác → không đăng nhập
    [InlineData(true, BigSellerSessionProbe.Alive, true, ScrapeMultiSyncAction.Repost)]        // trong khoá: đã có phiên mới → post lại
    [InlineData(false, BigSellerSessionProbe.Alive, true, ScrapeMultiSyncAction.Repost)]
    [InlineData(false, BigSellerSessionProbe.Rejected, false, ScrapeMultiSyncAction.Login)]
    [InlineData(true, BigSellerSessionProbe.Rejected, true, ScrapeMultiSyncAction.Login)]
    [InlineData(true, BigSellerSessionProbe.Unreachable, true, ScrapeMultiSyncAction.Login)]   // server đã báo 2001 + mạng chập → vẫn đăng nhập
    [InlineData(true, BigSellerSessionProbe.Unreachable, false, ScrapeMultiSyncAction.Login)]
    [InlineData(false, BigSellerSessionProbe.Unreachable, false, ScrapeMultiSyncAction.Book)]  // chỉ ghi sổ, không đăng nhập, không MarkFailed
    [InlineData(false, BigSellerSessionProbe.Unreachable, true, ScrapeMultiSyncAction.Book)]
    public void SessionDecision_AfterProbe(bool isSessionIssue, BigSellerSessionProbe probe, bool inLock, ScrapeMultiSyncAction expected) =>
        Assert.Equal(expected, ScrapeMultiSessionDecision.AfterProbe(isSessionIssue, probe, inLock));

    [Theory]
    // (loginSuccess, probe) → action
    [InlineData(true, BigSellerSessionProbe.Alive, ScrapeMultiSyncAction.Repost)]
    [InlineData(true, BigSellerSessionProbe.Rejected, ScrapeMultiSyncAction.MarkFailed)]
    [InlineData(true, BigSellerSessionProbe.Unreachable, ScrapeMultiSyncAction.Book)]
    [InlineData(false, BigSellerSessionProbe.Unreachable, ScrapeMultiSyncAction.Book)]         // đăng nhập hỏng vì mạng → không MarkFailed
    [InlineData(false, BigSellerSessionProbe.Rejected, ScrapeMultiSyncAction.MarkFailed)]
    [InlineData(false, BigSellerSessionProbe.Alive, ScrapeMultiSyncAction.Repost)]             // đăng nhập hỏng nhưng phiên dùng được → post lại
    public void SessionDecision_AfterLogin(bool loginSuccess, BigSellerSessionProbe probe, ScrapeMultiSyncAction expected) =>
        Assert.Equal(expected, ScrapeMultiSessionDecision.AfterLogin(loginSuccess, probe));
}
