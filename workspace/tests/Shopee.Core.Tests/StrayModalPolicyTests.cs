using Shopee.Core.BigSeller;

namespace Shopee.Core.Tests;

/// <summary>
/// <see cref="StrayModalPolicy.ShouldCloseStrayModal"/>: hộp không phải việc đang cần thì đóng.
/// Cờ Finish gồm cả nút «Finish» và «Hoàn thành» — caller gộp hai chữ đó thành một cờ trước khi gọi.
/// </summary>
public sealed class StrayModalPolicyTests
{
    [Fact]
    public void NoImport_NoShopCheckbox_NoFinish_Closes()
    {
        Assert.True(StrayModalPolicy.ShouldCloseStrayModal(
            hasImportToStoresButton: false,
            hasShopCheckbox: false,
            hasFinishButton: false,
            isListingLimit: false));
    }

    [Fact]
    public void HasImportToStoresButton_Keeps()
    {
        Assert.False(StrayModalPolicy.ShouldCloseStrayModal(
            hasImportToStoresButton: true,
            hasShopCheckbox: false,
            hasFinishButton: false,
            isListingLimit: false));
    }

    [Fact]
    public void HasShopCheckbox_Keeps()
    {
        Assert.False(StrayModalPolicy.ShouldCloseStrayModal(
            hasImportToStoresButton: false,
            hasShopCheckbox: true,
            hasFinishButton: false,
            isListingLimit: false));
    }

    [Fact]
    public void HasFinishOrHoanThanh_Keeps()
    {
        Assert.False(StrayModalPolicy.ShouldCloseStrayModal(
            hasImportToStoresButton: false,
            hasShopCheckbox: false,
            hasFinishButton: true,
            isListingLimit: false));
    }

    [Fact]
    public void ListingLimitFlag_DoesNotClose()
    {
        Assert.False(StrayModalPolicy.ShouldCloseStrayModal(
            hasImportToStoresButton: false,
            hasShopCheckbox: false,
            hasFinishButton: false,
            isListingLimit: true));
    }
}
