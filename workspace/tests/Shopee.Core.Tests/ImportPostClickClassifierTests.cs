using Shopee.Core.BigSeller;
using Shopee.Core.Cdp;

namespace Shopee.Core.Tests;

/// <summary>
/// <see cref="ImportPostClickClassifier.Classify"/>: sau khi modal import đóng, lô chỉ được coi là ĐÃ GỬI khi URL còn
/// ở Crawl List và không còn hộp lạ. Lỗi cũ: modal đóng là tính đã gửi dù hộp lỗi hiện sau đó hay phiên rớt về login.
/// </summary>
public sealed class ImportPostClickClassifierTests
{
    private static readonly OpenModalInfo DraftBox = new("Đã thêm vào Hộp nháp. Xem Hộp nháp", HasDraftBoxLink: true);

    [Fact]
    public void NoModalsAndOnCrawl_IsSent()
    {
        var v = ImportPostClickClassifier.Classify(true, []);
        Assert.True(v.Sent);
    }

    [Fact]
    public void OnlyKnownDraftBox_IsSent()
    {
        Assert.True(ImportPostClickClassifier.Classify(true, [DraftBox]).Sent);
        Assert.True(ImportPostClickClassifier.Classify(true, [DraftBox, DraftBox]).Sent);
    }

    [Fact]
    public void UrlNotOnCrawl_IsUncertain_EvenWithNoModals()
    {
        var v = ImportPostClickClassifier.Classify(false, []);
        Assert.False(v.Sent);
        Assert.Contains("Crawl List", v.Reason);
    }

    [Fact]
    public void UrlNotOnCrawl_IsUncertain_EvenWithKnownBox()
    {
        Assert.False(ImportPostClickClassifier.Classify(false, [DraftBox]).Sent);
    }

    [Fact]
    public void UnknownBox_IsUncertain_AndReasonCarriesBoxText()
    {
        var v = ImportPostClickClassifier.Classify(true, [new OpenModalInfo("Import failed: shop quota exceeded", false)]);
        Assert.False(v.Sent);
        Assert.Contains("Import failed: shop quota exceeded", v.Reason);
    }

    [Fact]
    public void UnknownBoxAlongsideKnownBox_IsUncertain()
    {
        Assert.False(ImportPostClickClassifier.Classify(true, [DraftBox, new OpenModalInfo("Lỗi lạ", false)]).Sent);
    }

    [Fact]
    public void ErrorToast_IsUncertain_EvenWithKnownBoxOrNoModal()
    {
        var toast = new[] { "Import thất bại, vui lòng thử lại" };
        var v = ImportPostClickClassifier.Classify(true, [], toast);
        Assert.False(v.Sent);
        Assert.Contains("Import thất bại", v.Reason);
        Assert.False(ImportPostClickClassifier.Classify(true, [DraftBox], toast).Sent);
    }

    [Fact]
    public void NoToast_NullOrEmpty_IsIgnored()
    {
        Assert.True(ImportPostClickClassifier.Classify(true, [], null).Sent);
        Assert.True(ImportPostClickClassifier.Classify(true, [], []).Sent);
    }

    // ── Điều hướng (FrameNavigated) ──

    [Fact]
    public void Navigated_WithoutKnownBox_IsUncertain()
    {
        var v = ImportPostClickClassifier.Classify(true, [], null, navigated: true);
        Assert.False(v.Sent);
        Assert.Contains("điều hướng", v.Reason);
    }

    [Fact]
    public void Navigated_WithKnownBox_IsSent()
    {
        Assert.True(ImportPostClickClassifier.Classify(true, [DraftBox], null, navigated: true).Sent);
    }

    [Fact]
    public void NotNavigated_NoBox_IsSent()
    {
        Assert.True(ImportPostClickClassifier.Classify(true, [], null, navigated: false).Sent);
    }

    // ── Hộp kết quả kèm lỗi: cả hai chiều ──

    [Theory]
    [InlineData("Thành công 3, Thất bại 2. Xem Hộp nháp")]
    [InlineData("Failed: 2")]
    [InlineData("failed 10")]
    [InlineData("20 failed")]
    [InlineData("Lỗi khi thêm vào Hộp nháp")]
    [InlineData("LỖI")]
    [InlineData("An error occurred")]
    [InlineData("Thành công 0 Thất bại 5")]   // số 0 của nhãn "Thành công" không được đọc nhầm cho "Thất bại"
    [InlineData("Thành công: 0 Thất bại: 5")]
    [InlineData("Thành công 0, Thất bại 5")]
    [InlineData("Thất bại 5, Thành công 0")]
    [InlineData("Thành công 5, thất bại 0, lỗi mạng")]   // 0 chỉ miễn cho "thất bại", "lỗi" vẫn là lỗi
    public void KnownBoxWithErrorWords_IsUncertain(string text)
    {
        Assert.True(ImportPostClickClassifier.BoxReportsError(text));
        var v = ImportPostClickClassifier.Classify(true, [new OpenModalInfo(text, HasDraftBoxLink: true)]);
        Assert.False(v.Sent);
        Assert.Contains("lỗi/thất bại", v.Reason);
    }

    [Theory]
    [InlineData("Thất bại 0")]
    [InlineData("Thất bại: 0")]
    [InlineData("Thất bại 0, Thành công 5")]
    [InlineData("Thành công 5, Thất bại 0")]
    [InlineData("Thành công 5 Thất bại 0")]
    [InlineData("Thất bại 0. Xem chi tiết")]
    [InlineData("THẤT BẠI：0")]
    [InlineData("0 failed")]
    [InlineData("Failed: 0")]
    [InlineData("Thành công 5, Thất bại 0. Xem Hộp nháp")]
    [InlineData("Đã thêm vào Hộp nháp")]
    [InlineData("")]
    public void KnownBoxWithZeroFailureOrNoErrorWords_IsSent(string text)
    {
        Assert.False(ImportPostClickClassifier.BoxReportsError(text));
        Assert.True(ImportPostClickClassifier.Classify(true, [new OpenModalInfo(text, HasDraftBoxLink: true)]).Sent);
    }

    [Fact]
    public void Trim_CutsAt300Chars()
    {
        Assert.Equal(new string('y', 300) + "…", ImportPostClickClassifier.Trim(new string('y', 301)));
        Assert.Equal("ngắn", ImportPostClickClassifier.Trim("  ngắn "));
        Assert.Equal("", ImportPostClickClassifier.Trim(null));
    }

    [Fact]
    public void UnknownBoxText_IsTruncatedToAbout300Chars()
    {
        var v = ImportPostClickClassifier.Classify(true, [new OpenModalInfo(new string('x', 1000), false)]);
        Assert.False(v.Sent);
        Assert.Contains(new string('x', ImportPostClickClassifier.MaxBoxTextLength), v.Reason);
        Assert.DoesNotContain(new string('x', ImportPostClickClassifier.MaxBoxTextLength + 1), v.Reason);
    }
}

/// <summary>
/// <see cref="CdpErrors.IsRealNavigationError(string?)"/>: tập HẸP chỉ nhận lỗi "trang điều hướng làm context chết".
/// "Target closed"/"WebSocket"/... (Brave chết, CDP đứt) KHÔNG phải điều hướng thật dù vẫn là lỗi tạm thời.
/// </summary>
public sealed class CdpRealNavigationErrorTests
{
    [Theory]
    [InlineData("Execution context was destroyed, most likely because of a navigation.")]
    [InlineData("Execution context was destroyed")]
    [InlineData("... most likely because of a navigation")]
    public void RealNavigation_IsRecognized(string message)
    {
        Assert.True(CdpErrors.IsRealNavigationError(message));
    }

    [Theory]
    [InlineData("Target closed")]
    [InlineData("Target page, context or browser has been closed")]   // chuỗi lỗi THẬT của Playwright khi Brave/tab chết
    [InlineData("Page.EvaluateAsync: Target page, context or browser has been closed")]
    [InlineData("The remote party closed the WebSocket connection")]
    [InlineData("Inspected target navigated or closed")]
    [InlineData("Cannot find context with specified id")]
    [InlineData("Timeout 10000ms exceeded.")]
    [InlineData("")]
    [InlineData(null)]
    public void NotRealNavigation_IsRejected(string? message)
    {
        Assert.False(CdpErrors.IsRealNavigationError(message));
    }

    [Theory]
    [InlineData("Target closed")]
    [InlineData("The remote party closed the WebSocket connection")]
    public void TransientNavigation_StillAcceptsThemForOtherCallers(string message)
    {
        // CdpErrors.IsTransientNavigationError KHÔNG đổi: chỗ khác vẫn dựa vào nó.
        Assert.True(CdpErrors.IsTransientNavigationError(message));
    }
}
