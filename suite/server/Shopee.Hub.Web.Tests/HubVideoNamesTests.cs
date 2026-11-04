using Shopee.Hub;
using Shopee.Hub.Web.Api;

namespace Shopee.Hub.Web.Tests;

public sealed class HubVideoNamesTests : IDisposable
{
    private readonly string _dataDir =
        Path.Combine(Path.GetTempPath(), "hub-video-names-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("videos/ABC.mp4", true)]
    [InlineData("videos/a_b.mp4", true)]
    [InlineData("videos/_.mp4", true)]
    [InlineData("videos/video.mp4", true)]
    [InlineData("config/accounts.json", false)]
    [InlineData("cookies/x.json", false)]
    [InlineData("images/default-update.jpg", false)]
    [InlineData("videos/../config/accounts.json", false)]
    [InlineData("videos/a/b.mp4", false)]
    [InlineData("Videos/ABC.mp4", false)]
    [InlineData("videos/ABC.MP4", false)]
    [InlineData("", false)]
    [InlineData("videos/a..b.mp4", false)]
    public void IsVideoName_DungLuat(string name, bool allowed)
        => Assert.Equal(allowed, HubVideoNames.IsVideoName(name));

    [Fact]
    public void DeleteFile_XoaVideo_GiuConfig()
    {
        using var db = new HubDatabase(_dataDir);
        Assert.True(db.PutFile("videos/ABC.mp4", new byte[] { 1, 2, 3 }, null, "test").Ok);
        Assert.True(db.PutFile("config/accounts.json", new byte[] { 4 }, null, "test").Ok);

        Assert.True(db.DeleteFile("videos/ABC.mp4"));

        var left = db.ListFiles().Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("videos/ABC.mp4", left);
        Assert.Contains("config/accounts.json", left);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dataDir, recursive: true); } catch { }
    }
}
