using Shopee.Core.Scrape;

namespace Shopee.Core.Tests;

/// <summary>
/// <see cref="NeedLoginStreak"/> — chuỗi login-first liên tiếp của job Lead. Reset DUY NHẤT khi có dòng cào xong
/// thật; chunk lỗi proxy / không cào được gì không reset (bản cũ reset nhầm → job đá phiên chạy mãi, đốt tk Shopee).
/// </summary>
public sealed class NeedLoginStreakTests
{
    [Fact]
    public void ThreeNeedLogins_Stop()
    {
        var s = new NeedLoginStreak(3);
        Assert.False(s.RecordNeedLogin(out var a));
        Assert.False(s.RecordNeedLogin(out var b));
        Assert.True(s.RecordNeedLogin(out var c));
        Assert.Equal((1, 2, 3), (a, b, c));
    }

    [Fact]
    public void CompletedRow_ResetsStreak()
    {
        var s = new NeedLoginStreak(3);
        s.RecordNeedLogin(out _);
        s.RecordNeedLogin(out _);
        s.RecordRowCompleted();
        Assert.Equal(0, s.Count);
        Assert.False(s.RecordNeedLogin(out var n));
        Assert.Equal(1, n);
        Assert.False(s.RecordNeedLogin(out _));
        Assert.True(s.RecordNeedLogin(out _));
    }

    [Fact]
    public void KeepsStoppingOnceThresholdPassed()
    {
        var s = new NeedLoginStreak(3);
        for (var i = 0; i < 3; i++) s.RecordNeedLogin(out _);
        Assert.True(s.RecordNeedLogin(out var n));
        Assert.Equal(4, n);
    }
}
