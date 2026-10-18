using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ShopeeBackend.Models;
using Xunit;

namespace ShopeeBackend.Tests;

public class CategorySelectionTests
{
    [Fact]
    public void CategoryInfo_IsSelected_DefaultIsFalse()
    {
        var cat = new CategoryInfo();
        Assert.False(cat.IsSelected);
    }

    [Fact]
    public void CategoryInfo_IsSelected_TriggersPropertyChanged()
    {
        var cat = new CategoryInfo();
        var changedProps = new List<string>();
        cat.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null)
                changedProps.Add(e.PropertyName);
        };

        cat.IsSelected = true;

        Assert.True(cat.IsSelected);
        Assert.Contains(nameof(CategoryInfo.IsSelected), changedProps);
    }

    [Fact]
    public void CategoryInfo_IsSelected_SameValue_DoesNotTriggerPropertyChanged()
    {
        var cat = new CategoryInfo();
        var count = 0;
        cat.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(CategoryInfo.IsSelected))
                count++;
        };

        cat.IsSelected = false; // Same as default

        Assert.Equal(0, count);
    }

    [Fact]
    public void CategoryFilter_ReturnsOnlyCheckedCategories()
    {
        var categories = new List<CategoryInfo>
        {
            new CategoryInfo { Name = "Cat 1", Url = "https://shopee.vn/cat1", IsSelected = true },
            new CategoryInfo { Name = "Cat 2", Url = "https://shopee.vn/cat2", IsSelected = false },
            new CategoryInfo { Name = "Cat 3", Url = "https://shopee.vn/cat3", IsSelected = true },
            new CategoryInfo { Name = "Cat 4", Url = "https://shopee.vn/cat4", IsSelected = false },
        };

        var selected = categories.Where(c => c.IsSelected).ToList();

        Assert.Equal(2, selected.Count);
        Assert.Equal("Cat 1", selected[0].Name);
        Assert.Equal("Cat 3", selected[1].Name);
    }

    [Fact]
    public void CategoryFilter_WhenNoneChecked_ReturnsEmpty()
    {
        var categories = new List<CategoryInfo>
        {
            new CategoryInfo { Name = "Cat 1", Url = "https://shopee.vn/cat1", IsSelected = false },
            new CategoryInfo { Name = "Cat 2", Url = "https://shopee.vn/cat2", IsSelected = false },
        };

        var selected = categories.Where(c => c.IsSelected).ToList();

        Assert.Empty(selected);
    }

    [Fact]
    public void CategoryToggleAll_SelectsAll_WhenNotAllSelected()
    {
        var categories = new List<CategoryInfo>
        {
            new CategoryInfo { Name = "Cat 1", IsSelected = true },
            new CategoryInfo { Name = "Cat 2", IsSelected = false },
            new CategoryInfo { Name = "Cat 3", IsSelected = false },
        };

        bool targetState = !categories.All(c => c.IsSelected);
        foreach (var cat in categories)
        {
            cat.IsSelected = targetState;
        }

        Assert.True(targetState);
        Assert.All(categories, c => Assert.True(c.IsSelected));
    }

    [Fact]
    public void CategoryToggleAll_DeselectsAll_WhenAllAreAlreadySelected()
    {
        var categories = new List<CategoryInfo>
        {
            new CategoryInfo { Name = "Cat 1", IsSelected = true },
            new CategoryInfo { Name = "Cat 2", IsSelected = true },
            new CategoryInfo { Name = "Cat 3", IsSelected = true },
        };

        bool targetState = !categories.All(c => c.IsSelected);
        foreach (var cat in categories)
        {
            cat.IsSelected = targetState;
        }

        Assert.False(targetState);
        Assert.All(categories, c => Assert.False(c.IsSelected));
    }
}
