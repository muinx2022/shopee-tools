using Shopee.Core.BigSeller;
using Shopee.Core.Infrastructure;
using Shopee.Core.Progress;
using Shopee.Core.Scrape;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Shopee.Core.Tests;

public sealed class StoreReloadExternalChangeTests
{
    [Fact]
    public void BigSellerStore_ReloadIfExternalChange_NoChange_ReturnsFalse()
    {
        var store = BigSellerStore.Shared;
        store.Load();

        var reloaded = store.ReloadIfExternalChange();
        Assert.False(reloaded);
    }

    [Fact]
    public void BigSellerStore_ReloadIfExternalChange_WhenFileTouched_ReturnsTrueAndFiresChanged()
    {
        var store = BigSellerStore.Shared;
        var path = Path.Combine(SuitePaths.ModuleDir("shared"), "bigseller.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "[]");
        store.Load();

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(2));

        var changedFired = false;
        void OnChanged() => changedFired = true;

        store.Changed += OnChanged;
        try
        {
            var reloaded = store.ReloadIfExternalChange();
            Assert.True(reloaded);
            Assert.True(changedFired);
        }
        finally
        {
            store.Changed -= OnChanged;
        }
    }

    [Fact]
    public void ScrapeProgressStore_ReloadIfExternalChange_NoChange_ReturnsFalse()
    {
        var store = ScrapeProgressStore.Shared;
        store.Load();

        var reloaded = store.ReloadIfExternalChange();
        Assert.False(reloaded);
    }

    [Fact]
    public void ScrapeProgressStore_ReloadIfExternalChange_WhenFileTouched_ReturnsTrueAndFiresChanged()
    {
        var store = ScrapeProgressStore.Shared;
        var path = Path.Combine(SuitePaths.ModuleDir("shared"), "scrape-progress.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "[]");
        store.Load();

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(2));

        var changedFired = false;
        void OnChanged() => changedFired = true;

        store.Changed += OnChanged;
        try
        {
            var reloaded = store.ReloadIfExternalChange();
            Assert.True(reloaded);
            Assert.True(changedFired);
        }
        finally
        {
            store.Changed -= OnChanged;
        }
    }

    [Fact]
    public void OpProgressStore_ReloadIfExternalChange_NoChange_ReturnsFalse()
    {
        var store = OpProgressStore.Shared;
        store.Load();

        var reloaded = store.ReloadIfExternalChange();
        Assert.False(reloaded);
    }

    [Fact]
    public void OpProgressStore_ReloadIfExternalChange_WhenFileTouched_ReturnsTrueAndFiresChanged()
    {
        var store = OpProgressStore.Shared;
        var path = Path.Combine(SuitePaths.ModuleDir("shared"), "op-progress.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "[]");
        store.Load();

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(2));

        var changedFired = false;
        void OnChanged() => changedFired = true;

        store.Changed += OnChanged;
        try
        {
            var reloaded = store.ReloadIfExternalChange();
            Assert.True(reloaded);
            Assert.True(changedFired);
        }
        finally
        {
            store.Changed -= OnChanged;
        }
    }
}
