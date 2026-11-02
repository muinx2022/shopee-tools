using System;
using System.Collections.Generic;
using System.IO;
using Shopee.Core.Infrastructure;
using Shopee.Core.Scrape;
using Xunit;

namespace Shopee.Core.Tests;

public class ScrapeMultiStoreTests
{
    [Fact]
    public void FailedSyncStore_AddRemoveAndClear_WorksCorrectly()
    {
        var store = ScrapeMultiFailedSyncStore.Shared;
        store.Clear();
        Assert.Equal(0, store.Count);

        var item1 = new FailedSyncItem
        {
            AccountId = "acc_1",
            AccountName = "Account 1",
            Row = 10,
            Url = "https://shopee.vn/product1",
            JsonData = "{\"item_id\": 111}",
            Error = "401006 expired"
        };

        store.AddOrUpdate(item1);
        Assert.Equal(1, store.Count);

        var items = store.GetAll();
        Assert.Single(items);
        Assert.Equal("acc_1", items[0].AccountId);
        Assert.Equal(10, items[0].Row);

        // Update same account and row with new error
        item1.Error = "New Error";
        store.AddOrUpdate(item1);
        Assert.Equal(1, store.Count);
        Assert.Equal("New Error", store.GetAll()[0].Error);

        // Add second item
        var item2 = new FailedSyncItem
        {
            AccountId = "acc_2",
            AccountName = "Account 2",
            Row = 15,
            Url = "https://shopee.vn/product2",
            JsonData = "{\"item_id\": 222}",
            Error = "Network error"
        };
        store.AddOrUpdate(item2);
        Assert.Equal(2, store.Count);

        // Remove by accountId and row
        var removed = store.Remove("acc_1", 10);
        Assert.True(removed);
        Assert.Equal(1, store.Count);
        Assert.Equal("acc_2", store.GetAll()[0].AccountId);

        // Clear
        store.Clear();
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void FailedSyncStore_CountFor_CountsPerAccount()
    {
        var store = ScrapeMultiFailedSyncStore.Shared;
        store.Clear();
        try
        {
            Assert.Equal(0, store.CountFor("acc_1"));

            store.AddOrUpdate(new FailedSyncItem { AccountId = "acc_1", Row = 1 });
            store.AddOrUpdate(new FailedSyncItem { AccountId = "acc_1", Row = 2 });
            store.AddOrUpdate(new FailedSyncItem { AccountId = "acc_2", Row = 1 });

            Assert.Equal(2, store.CountFor("acc_1"));
            Assert.Equal(1, store.CountFor("acc_2"));
            Assert.Equal(0, store.CountFor("acc_3"));
            Assert.Equal(0, store.CountFor("ACC_1"));

            store.Remove("acc_1", 1);
            Assert.Equal(1, store.CountFor("acc_1"));
        }
        finally
        {
            store.Clear();
        }
    }

    [Fact]
    public void ConfigStore_SaveAndLoad_PreservesValues()
    {
        var store = ScrapeMultiConfigStore.Shared;
        var config = new ScrapeMultiConfig
        {
            StartRow = 5,
            EndRow = 500,
            MaxProcess = 8,
            FrameSize = 12,
            RestMinSeconds = 100,
            RestMaxSeconds = 200,
            SelectedAccountIds = ["acc_a", "acc_b"]
        };

        store.Save(config);

        var loaded = store.Load();
        Assert.Equal(5, loaded.StartRow);
        Assert.Equal(500, loaded.EndRow);
        Assert.Equal(8, loaded.MaxProcess);
        Assert.Equal(12, loaded.FrameSize);
        Assert.Equal(100, loaded.RestMinSeconds);
        Assert.Equal(200, loaded.RestMaxSeconds);
        Assert.Equal(2, loaded.SelectedAccountIds.Count);
        Assert.Contains("acc_a", loaded.SelectedAccountIds);
        Assert.Contains("acc_b", loaded.SelectedAccountIds);
    }
}
