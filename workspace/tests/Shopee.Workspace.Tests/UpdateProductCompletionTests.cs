using Shopee.Workspace.Modules.UpdateProduct;

namespace Shopee.Workspace.Tests;

/// <summary>
/// <see cref="UpdateProductViewModel.ShouldMarkCompleted"/>: shop chỉ được ghi "completed" khi Import/Update XONG THẬT.
/// Lỗi cũ: hết lượt (import 3/10, update 3/100) vẫn bị ghi xong nên lần chạy sau bỏ qua, sản phẩm sót im lặng.
/// </summary>
public sealed class UpdateProductCompletionTests
{
    [Theory]
    [InlineData(UpdateKind.Import)]
    [InlineData(UpdateKind.Update)]
    public void Complete_WhenRunnerReportsComplete(UpdateKind kind)
    {
        Assert.True(UpdateProductViewModel.ShouldMarkCompleted(kind, listingLimitReached: false, runComplete: true));
    }

    [Theory]
    [InlineData(UpdateKind.Import)]
    [InlineData(UpdateKind.Update)]
    public void NotComplete_WhenRunnerReportsIncomplete(UpdateKind kind)
    {
        Assert.False(UpdateProductViewModel.ShouldMarkCompleted(kind, listingLimitReached: false, runComplete: false));
    }

    [Theory]
    [InlineData(UpdateKind.Import)]
    [InlineData(UpdateKind.Update)]
    [InlineData(UpdateKind.Rewrite)]
    public void ListingLimit_AlwaysCompletes_SoAutoRunMovesToNextShop(UpdateKind kind)
    {
        Assert.True(UpdateProductViewModel.ShouldMarkCompleted(kind, listingLimitReached: true, runComplete: false));
    }

    // Listing limit chốt từ TOAST (câu chữ đoán) → đi theo runComplete như thường: chưa đủ thì "stopped", lượt sau thử lại.
    [Theory]
    [InlineData(UpdateKind.Import)]
    [InlineData(UpdateKind.Update)]
    public void ListingLimitFromToast_NotComplete_WhenRunIncomplete(UpdateKind kind)
    {
        Assert.False(UpdateProductViewModel.ShouldMarkCompleted(kind, listingLimitReached: true, runComplete: false, listingLimitFromToast: true));
    }

    [Theory]
    [InlineData(UpdateKind.Import)]
    [InlineData(UpdateKind.Update)]
    public void ListingLimitFromToast_Complete_WhenRunActuallyComplete(UpdateKind kind)
    {
        Assert.True(UpdateProductViewModel.ShouldMarkCompleted(kind, listingLimitReached: true, runComplete: true, listingLimitFromToast: true));
    }

    [Theory]
    [InlineData(true, false, true)]    // hộp thoại đã xác nhận → completed
    [InlineData(true, true, false)]    // chỉ toast → không tính completed
    [InlineData(false, false, false)]  // không chạm giới hạn
    [InlineData(false, true, false)]
    public void ListingLimitCountsAsCompleted_OnlyForDialogSource(bool reached, bool fromToast, bool expected)
    {
        Assert.Equal(expected, UpdateProductViewModel.ListingLimitCountsAsCompleted(reached, fromToast));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Rewrite_KeepsOldBehaviour_AlwaysCompletes(bool runComplete)
    {
        Assert.True(UpdateProductViewModel.ShouldMarkCompleted(UpdateKind.Rewrite, listingLimitReached: false, runComplete: runComplete));
    }
}
