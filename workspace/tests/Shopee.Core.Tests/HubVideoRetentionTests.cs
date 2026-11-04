using Shopee.Core.Coordination;
using Shopee.Core.Scrape;

namespace Shopee.Core.Tests;

public sealed class HubVideoRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 8, 0, 0, TimeSpan.Zero);

    private static FileManifestEntry E(string name, DateTimeOffset mtime) => new() { Name = name, Mtime = mtime };

    [Theory]
    [InlineData("videos/ABC.mp4")]
    [InlineData("videos/a_b.mp4")]
    [InlineData("videos/_.mp4")]
    [InlineData("videos/video.mp4")]
    public void IsVideoName_Nhan(string name) => Assert.True(HubVideoRetention.IsVideoName(name));

    [Theory]
    [InlineData("config/accounts.json")]
    [InlineData("cookies/x.json")]
    [InlineData("images/default-update.jpg")]
    [InlineData("videos/../config/accounts.json")]
    [InlineData("videos/a/b.mp4")]
    [InlineData("Videos/ABC.mp4")]
    [InlineData("videos/ABC.MP4")]
    [InlineData("")]
    [InlineData("videos/a..b.mp4")]
    public void IsVideoName_TuChoi(string name) => Assert.False(HubVideoRetention.IsVideoName(name));

    [Fact]
    public void IsExpired_Dung30NgayThiXoa_Thieu1GiayVaKhongNgayThiGiu()
    {
        Assert.Equal(TimeSpan.FromDays(30), HubVideoRetention.MaxAge);
        Assert.True(HubVideoRetention.IsExpired(Now - HubVideoRetention.MaxAge, Now));
        Assert.False(HubVideoRetention.IsExpired(Now - HubVideoRetention.MaxAge + TimeSpan.FromSeconds(1), Now));
        Assert.False(HubVideoRetention.IsExpired(default, Now));
    }

    [Fact]
    public void ExpiredNames_ChiLayVideoQuaHan()
    {
        var names = HubVideoRetention.ExpiredNames(
        [
            E("videos/OLD.mp4", Now - HubVideoRetention.MaxAge),
            E("videos/NEW.mp4", Now - HubVideoRetention.MaxAge + TimeSpan.FromSeconds(1)),
            E("videos/NONE.mp4", default),
            E("config/accounts.json", Now - HubVideoRetention.MaxAge),
            E("videos/ABC.MP4", Now - HubVideoRetention.MaxAge),
        ], Now);

        Assert.Equal(new[] { "videos/OLD.mp4" }, names);
    }

    [Fact]
    public async Task SweepExpired_KhongXoaConfig_IOExceptionVanXoaTenSau()
    {
        var called = new List<string>();
        var n = await HubVideoMaintenance.SweepExpiredAsync(
            [
                E("config/accounts.json", Now - TimeSpan.FromDays(40)),
                E("videos/A.mp4", Now - TimeSpan.FromDays(31)),
                E("videos/B.mp4", Now - TimeSpan.FromDays(31)),
                E("images/default-update.jpg", Now - TimeSpan.FromDays(40)),
            ],
            Now,
            (name, _) =>
            {
                called.Add(name);
                if (name == "videos/A.mp4") throw new IOException("đĩa đầy");
                return Task.FromResult(true);
            },
            CancellationToken.None);

        Assert.Equal(new[] { "videos/A.mp4", "videos/B.mp4" }, called);
        Assert.Equal(1, n);
    }

    [Fact]
    public async Task SweepExpired_Huy_NemTiepVaKhongXoaTenSau()
    {
        var called = new List<string>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            HubVideoMaintenance.SweepExpiredAsync(
                [
                    E("videos/A.mp4", Now - TimeSpan.FromDays(40)),
                    E("videos/B.mp4", Now - TimeSpan.FromDays(40)),
                ],
                Now,
                (name, _) =>
                {
                    called.Add(name);
                    throw new OperationCanceledException();
                },
                CancellationToken.None));

        Assert.Equal(new[] { "videos/A.mp4" }, called);
    }

    [Fact]
    public async Task DeleteAllVideos_ChiXoaVideoKeCaChuaQuaHan()
    {
        var called = new List<string>();
        var n = await HubVideoMaintenance.DeleteAllVideosAsync(
            [
                E("videos/ABC.mp4", Now),
                E("config/accounts.json", Now - TimeSpan.FromDays(40)),
                E("videos/a_b.mp4", default),
            ],
            (name, _) =>
            {
                called.Add(name);
                return Task.FromResult(name != "videos/ABC.mp4");
            },
            CancellationToken.None);

        Assert.Equal(new[] { "videos/ABC.mp4", "videos/a_b.mp4" }, called);
        Assert.Equal(1, n);
    }

    [Fact]
    public async Task RunExclusive_VaoTruoc_SweepKhongXoaKhiChuaNhaKhoa()
    {
        var order = new List<string>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var exclusive = HubVideoMaintenance.RunExclusiveAsync(async ct =>
        {
            lock (order) order.Add("exclusive");
            entered.TrySetResult();
            await Task.Delay(200, ct);
            lock (order) order.Add("exclusive-xong");
        }, CancellationToken.None);

        await entered.Task;

        var sweep = HubVideoMaintenance.SweepExpiredAsync(
            [E("videos/A.mp4", Now - TimeSpan.FromDays(40))],
            Now,
            (_, _) =>
            {
                lock (order) order.Add("delete");
                return Task.FromResult(true);
            },
            CancellationToken.None);

        await exclusive;
        var n = await sweep;

        Assert.Equal(1, n);
        Assert.Equal(new[] { "exclusive", "exclusive-xong", "delete" }, order);
    }
}
