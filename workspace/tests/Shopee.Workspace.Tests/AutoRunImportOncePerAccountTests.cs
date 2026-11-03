using Shopee.Workspace.Modules.Workspace;

namespace Shopee.Workspace.Tests;

/// <summary>Auto Run: từng shop Import (1 shop) rồi Update, lần lượt — không còn Import nhiều shop một lần.</summary>
public sealed class AutoRunPerShopImportTests
{
    private sealed record FakeShop(string Name, bool ImportDone);

    private static readonly FakeShop[] ThreeShops =
    [
        new("alina99.store", ImportDone: false),
        new("shop9x.store", ImportDone: true),
        new("six6shop.store", ImportDone: false),
    ];

    [Fact]
    public async Task RunsEveryShopStepInOrder()
    {
        var events = new List<string>();

        await AutoRunViewModel.RunAccountShopsAsync(
            ThreeShops,
            shopStep: (shop, _) => { events.Add("step:" + shop.Name); return Task.FromResult(true); },
            CancellationToken.None);

        Assert.Equal(["step:alina99.store", "step:shop9x.store", "step:six6shop.store"], events);
    }

    [Fact]
    public async Task ShopStepFalse_StopsLoop()
    {
        var events = new List<string>();

        await AutoRunViewModel.RunAccountShopsAsync(
            ThreeShops,
            shopStep: (shop, _) =>
            {
                events.Add(shop.Name);
                return Task.FromResult(shop.Name != "shop9x.store");
            },
            CancellationToken.None);

        Assert.Equal(["alina99.store", "shop9x.store"], events);
    }

    [Fact]
    public async Task Cancelled_SkipsRemainingShops()
    {
        using var cts = new CancellationTokenSource();
        var events = new List<string>();

        await AutoRunViewModel.RunAccountShopsAsync(
            ThreeShops,
            shopStep: (shop, _) =>
            {
                events.Add(shop.Name);
                if (shop.Name == "alina99.store") cts.Cancel();
                return Task.FromResult(true);
            },
            cts.Token);

        Assert.Equal(["alina99.store"], events);
    }
}
