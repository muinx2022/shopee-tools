using System;
using System.IO;
using ShopeeBackend.Models;
using ShopeeBackend.Services;
using Xunit;

namespace ShopeeBackend.Tests;

public class ImageStoragePathTests
{
    [Fact]
    public void GetProductImageDirectory_ReturnsExpectedPathStructure()
    {
        // Arrange
        GlobalState.ImageStorageFolder = @"d:\shopee-images";
        var product = new ScrapedProduct
        {
            ItemId = "123456789",
            ScrapedAt = new DateTime(2026, 10, 14, 15, 30, 0)
        };

        // Act
        string dir = ImageDownloadService.GetProductImageDirectory(product);

        // Assert
        string expected = Path.Combine(@"d:\shopee-images", "14-10-2026", "123456789");
        Assert.Equal(expected, dir);
    }

    [Fact]
    public void GetProductImageDirectory_FallbackToIdWhenItemIdIsEmptyOrZero()
    {
        // Arrange
        GlobalState.ImageStorageFolder = @"d:\shopee-images";
        var product = new ScrapedProduct
        {
            ItemId = "0",
            Id = "shop_99999",
            ScrapedAt = new DateTime(2026, 1, 5)
        };

        // Act
        string dir = ImageDownloadService.GetProductImageDirectory(product);

        // Assert
        string expected = Path.Combine(@"d:\shopee-images", "05-01-2026", "shop_99999");
        Assert.Equal(expected, dir);
    }

    [Fact]
    public void GetLocalImagePath_ReturnsStandardExpectedPath()
    {
        // Arrange
        GlobalState.ImageStorageFolder = @"d:\shopee-images";
        var product = new ScrapedProduct
        {
            ItemId = "123456789",
            ScrapedAt = new DateTime(2026, 10, 14, 15, 30, 0)
        };

        // Act
        string path = ImageDownloadService.GetLocalImagePath(product);

        // Assert: When file does not exist on disk, standardPath is returned
        string expected = Path.Combine(@"d:\shopee-images", "14-10-2026", "123456789", "123456789.jpg");
        Assert.Equal(expected, path);
    }

    [Fact]
    public void DataManager_GetImageStorageFolder_DefaultsToDShopeeImages()
    {
        // Act
        string folder = DataManager.GetImageStorageFolder();

        // Assert
        Assert.Equal(@"d:\shopee-images", folder);
    }

    [Fact]
    public void DataManager_GetImageStorageFolder_MigratesLegacyPhanTichShopee()
    {
        // Arrange: manually insert legacy path to sqlite settings
        DataManager.SetSetting("ImageStorageFolder", @"e:\images\phan-tich-shopee");

        // Act
        string folder = DataManager.GetImageStorageFolder();

        // Assert: must automatically update to d:\shopee-images
        Assert.Equal(@"d:\shopee-images", folder);
        Assert.Equal(@"d:\shopee-images", DataManager.GetSetting("ImageStorageFolder"));
    }
}
