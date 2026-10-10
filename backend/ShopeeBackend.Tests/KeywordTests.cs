using System;
using System.ComponentModel;
using ShopeeBackend.Models;
using Xunit;

namespace ShopeeBackend.Tests;

public class KeywordTests
{
    [Fact]
    public void KeywordInfo_Defaults_AreCorrect()
    {
        var kw = new KeywordInfo();
        Assert.Equal(string.Empty, kw.Keyword);
        Assert.Equal("Chờ quét", kw.Status);
        Assert.Equal(1, kw.CurrentPage);
        Assert.Equal(0, kw.TotalPages);
        Assert.Equal(0, kw.ProductCount);
        Assert.Null(kw.LastScrapedAt);
        Assert.Equal("Chưa quét", kw.ProgressDisplay);
    }

    [Fact]
    public void KeywordInfo_ProgressDisplay_ReflectsStatusAndPages()
    {
        var kw = new KeywordInfo { Keyword = "áo thun" };

        kw.Status = "Đang quét";
        kw.CurrentPage = 2;
        kw.ProductCount = 50;
        Assert.Equal("⟳ Đang quét: Trang 2 (50 SP)", kw.ProgressDisplay);

        kw.Status = "Hoàn thành";
        kw.ProductCount = 100;
        Assert.Equal("✔ Hoàn thành (100 SP)", kw.ProgressDisplay);

        kw.Status = "Tạm dừng";
        kw.CurrentPage = 4;
        kw.ProductCount = 80;
        Assert.Equal("⏸ Tạm dừng: Trang 4 (80 SP)", kw.ProgressDisplay);
    }

    [Fact]
    public void KeywordInfo_PropertyChanged_FiresProperly()
    {
        var kw = new KeywordInfo { Keyword = "giày" };
        var changedProps = new System.Collections.Generic.List<string>();
        kw.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null) changedProps.Add(e.PropertyName);
        };

        kw.CurrentPage = 3;
        kw.Status = "Đang quét";
        kw.ProductCount = 120;

        Assert.Contains(nameof(KeywordInfo.CurrentPage), changedProps);
        Assert.Contains(nameof(KeywordInfo.ProgressDisplay), changedProps);
        Assert.Contains(nameof(KeywordInfo.Status), changedProps);
        Assert.Contains(nameof(KeywordInfo.ProductCount), changedProps);
    }
}
