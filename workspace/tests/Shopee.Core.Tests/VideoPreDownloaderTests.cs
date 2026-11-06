using Shopee.Core.Scrape;

namespace Shopee.Core.Tests;

public class VideoPreDownloaderTests
{
    private static string TempDir() 
    {
        var dir = Path.Combine(Path.GetTempPath(), "videopredownloader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void FindMissing_FileExists_NotInResult()
    {
        var folder = TempDir();
        var sku = "B12345";
        var localPath = SkuVideoSync.LocalPath(folder, sku);
        File.WriteAllText(localPath, "fake");
        
        var missing = VideoPreDownloader.FindMissing(folder, [sku]);
        Assert.Empty(missing);
        
        Directory.Delete(folder, true);
    }

    [Fact]
    public void FindMissing_FileMissing_InResult()
    {
        var folder = TempDir();
        var sku = "B12345";
        
        var missing = VideoPreDownloader.FindMissing(folder, [sku]);
        Assert.Single(missing);
        Assert.Contains(sku, missing[0].RemoteName, StringComparison.OrdinalIgnoreCase);
        
        Directory.Delete(folder, true);
    }

    [Fact]
    public void FindMissing_DedupSameSku()
    {
        var folder = TempDir();
        var sku = "B12345";
        
        var missing = VideoPreDownloader.FindMissing(folder, [sku, sku, sku]);
        Assert.Single(missing);
        
        Directory.Delete(folder, true);
    }

    [Fact]
    public void FindMissing_NullOrEmptySku_Skipped()
    {
        var folder = TempDir();
        
        var missing = VideoPreDownloader.FindMissing(folder, [null, "", "  ", null]);
        Assert.Empty(missing);
        
        Directory.Delete(folder, true);
    }

    [Fact]
    public void DownloadResult_PropertiesMatch()
    {
        var result = new DownloadResult(1, 2, 3);
        Assert.Equal(1, result.Downloaded);
        Assert.Equal(2, result.Failed);
        Assert.Equal(3, result.NotOnHub);
    }

    [Fact]
    public void FindMissing_InNotFoundStore_Skipped()
    {
        var folder = TempDir();
        var sku = "NOTFOUND_SKU_123";
        var remote = SkuVideoSync.RemoteName(sku);

        try
        {
            VideoNotFoundStore.Shared.Add(remote);
            var missing = VideoPreDownloader.FindMissing(folder, [sku]);
            Assert.Empty(missing);
        }
        finally
        {
            VideoNotFoundStore.Shared.Remove(remote);
            Directory.Delete(folder, true);
        }
    }
}
