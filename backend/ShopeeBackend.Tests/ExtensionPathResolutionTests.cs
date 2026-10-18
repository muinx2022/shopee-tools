using System.IO;
using ShopeeBackend.Services;
using Xunit;

namespace ShopeeBackend.Tests;

public class ExtensionPathResolutionTests
{
    [Fact]
    public void ExtensionPath_ResolvesExistingDirectoryWithManifest()
    {
        string path = SuiteSearchSession.ExtensionPath;
        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.True(Directory.Exists(path), $"ExtensionPath should exist: {path}");
        Assert.True(File.Exists(Path.Combine(path, "manifest.json")), $"manifest.json should exist in: {path}");
    }

    [Fact]
    public void ShopeeExtPath_ResolvesExistingDirectoryWithManifest()
    {
        string path = SuiteSearchSession.ShopeeExtPath;
        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.True(Directory.Exists(path), $"ShopeeExtPath should exist: {path}");
        Assert.True(File.Exists(Path.Combine(path, "manifest.json")), $"manifest.json should exist in: {path}");
    }
}
