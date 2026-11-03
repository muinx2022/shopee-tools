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

    // ── Hộp tiến trình "Importing… Finish": đọc số Total / Successfully / Failed ──

    private const string FinalOk1 = "Importing Shopee Total: 1 Successfully: 1 Failed: 0 Draft Listing Finish";   // nguyên văn hộp thật (innerText gộp khoảng trắng)
    private const string FinalOk50 = "Importing Shopee Total: 50 Successfully: 50 Failed: 0 Draft Listing Finish";
    private const string RunningWithTotal = "Importing Shopee Total: 50 Importing... Finish";                        // nguyên văn từ log
    private const string RunningBare = "Importing Finish";                                                          // nguyên văn từ log

    private static ImportPostClickVerdict ClassifyBox(string text, int? expectedCount, bool draftLink = false) =>
        ImportPostClickClassifier.Classify(true, [new OpenModalInfo(text, draftLink)], expectedCount: expectedCount);

    [Theory]
    [InlineData(FinalOk1, 1, 1, 0)]
    [InlineData(FinalOk50, 50, 50, 0)]
    [InlineData(RunningWithTotal, 50, null, null)]
    [InlineData(RunningBare, null, null, null)]
    [InlineData("Importing Shopee Total: 1,234 Successfully: 1.200 Failed: 34 Finish", 1234, 1200, 34)]   // dấu phân cách nghìn
    [InlineData("importing shopee total：50 successfully：48 failed：2 finish", 50, 48, 2)]                   // hoa thường, ':' toàn chiều rộng
    [InlineData("Importing Shopee Total: 50 Successfully: 50 Failed: 0. Finish", 50, 50, 0)]                 // dấu chấm cuối câu không thuộc số
    [InlineData("Total:50 Success:3 Fail:1", 50, 3, 1)]
    [InlineData("", null, null, null)]
    public void ParseProgressCounts_ReadsThreeNumbers(string text, int? total, int? success, int? failed)
    {
        Assert.Equal(new ImportBoxCounts(total, success, failed), ImportPostClickClassifier.ParseProgressCounts(text));
    }

    [Theory]
    [InlineData(1, 1, 0, ImportBoxStatus.Succeeded)]
    [InlineData(50, 50, 0, ImportBoxStatus.Succeeded)]
    [InlineData(50, 0, 50, ImportBoxStatus.AllFailed)]
    [InlineData(50, 48, 2, ImportBoxStatus.PartialFailed)]
    [InlineData(50, 0, 3, ImportBoxStatus.Running)]      // 0 + 3 < 50: còn chạy dù đã có lỗi
    [InlineData(50, 30, 0, ImportBoxStatus.Running)]
    [InlineData(50, 0, 0, ImportBoxStatus.Running)]
    [InlineData(50, 50, 3, ImportBoxStatus.PartialFailed)]   // số vô lý (tổng > Total): không bao giờ là Succeeded
    public void GetStatus_FromCounts(int total, int success, int failed, ImportBoxStatus expected)
    {
        Assert.Equal(expected, ImportPostClickClassifier.GetStatus(new ImportBoxCounts(total, success, failed)));
    }

    [Theory]
    [InlineData(50, null, null)]
    [InlineData(null, 50, 0)]
    [InlineData(50, 50, null)]
    [InlineData(null, null, null)]
    public void GetStatus_MissingNumber_IsRunning(int? total, int? success, int? failed)
    {
        Assert.Equal(ImportBoxStatus.Running, ImportPostClickClassifier.GetStatus(new ImportBoxCounts(total, success, failed)));
    }

    [Theory]
    [InlineData(FinalOk1, 1, true)]
    [InlineData(FinalOk50, 50, true)]
    [InlineData(FinalOk50, null, false)]          // không biết số đã tick thì không so Total
    [InlineData(FinalOk1, 1, false)]              // HasDraftBoxLink không ảnh hưởng kết quả
    public void FinalSuccessBox_MatchingTickedCount_IsSent(string text, int? expected, bool draftLink)
    {
        Assert.True(ImportPostClickClassifier.IsImportProgressBox(text));
        var v = ClassifyBox(text, expected, draftLink);
        Assert.True(v.Sent);
        Assert.Equal(ImportVerdictKind.Sent, v.Kind);
    }

    [Theory]
    [InlineData(RunningWithTotal)]
    [InlineData(RunningBare)]
    [InlineData("Importing Shopee Total: 50 Successfully: 20 Failed: 0 Importing... Finish")]
    [InlineData("Importing Shopee Total: 50 Successfully: 0 Failed: 3 Importing... Finish")]   // đã có lỗi nhưng chưa xong: chờ trạng thái cuối
    public void RunningBox_IsNotSent_AndKeepsWaiting(string text)
    {
        Assert.True(ImportPostClickClassifier.IsImportProgressBox(text));
        var v = ClassifyBox(text, 50);
        Assert.False(v.Sent);
        Assert.Equal(ImportVerdictKind.InProgress, v.Kind);
        Assert.Contains("đang chạy", v.Reason);
    }

    [Fact]
    public void FailedAllBox_IsReportedFailure_AndReasonCarriesBoxText()
    {
        const string text = "Importing Shopee Total: 50 Successfully: 0 Failed: 50 Draft Listing Finish";
        var v = ClassifyBox(text, 50);
        Assert.False(v.Sent);
        Assert.Equal(ImportVerdictKind.ReportedFailure, v.Kind);
        Assert.Contains("cả lô", v.Reason);
        Assert.Contains(text, v.Reason);
    }

    [Fact]
    public void PartialFailedBox_IsReportedFailure_AndReasonCarriesBoxText()
    {
        const string text = "Importing Shopee Total: 50 Successfully: 48 Failed: 2 Draft Listing Finish";
        var v = ClassifyBox(text, 50);
        Assert.False(v.Sent);
        Assert.Equal(ImportVerdictKind.ReportedFailure, v.Kind);
        Assert.Contains("MỘT PHẦN", v.Reason);
        Assert.Contains(text, v.Reason);
    }

    [Theory]
    [InlineData("Importing Shopee Total: 49 Successfully: 49 Failed: 0 Draft Listing Finish", 50)]   // BigSeller nhận ít hơn số đã tick
    [InlineData("Importing Shopee Total: 2 Successfully: 2 Failed: 0 Draft Listing Finish", 1)]      // nhiều hơn
    [InlineData("Importing Shopee Total: 1 Successfully: 1 Failed: 0 Draft Listing Finish", 50)]
    public void FinalBox_TotalDiffersFromTicked_IsUncertain(string text, int ticked)
    {
        var v = ClassifyBox(text, ticked);
        Assert.False(v.Sent);
        Assert.Equal(ImportVerdictKind.Uncertain, v.Kind);
        Assert.Contains("khác số SP đã tick", v.Reason);
    }

    [Fact]
    public void BoxWithErrorWordButIncompleteNumbers_IsUncertainAtOnce()
    {
        var v = ClassifyBox("Importing Shopee Total: 50 Failed: 3 Finish", 50);
        Assert.False(v.Sent);
        Assert.Equal(ImportVerdictKind.Uncertain, v.Kind);
        Assert.Contains("tiến trình", v.Reason);
        Assert.False(ClassifyBox("Importing Error Finish", 50).Sent);
    }

    [Theory]
    [InlineData("Import failed: shop quota exceeded")]
    [InlineData("Finish")]
    [InlineData("Importing")]
    [InlineData("")]
    public void NotProgressBox_StaysUnknownBox_IsUncertain(string text)
    {
        Assert.False(ImportPostClickClassifier.IsImportProgressBox(text));
        var v = ClassifyBox(text, 50);
        Assert.False(v.Sent);
        Assert.Equal(ImportVerdictKind.Uncertain, v.Kind);
    }

    [Theory]
    [InlineData(FinalOk1)]
    [InlineData(RunningWithTotal)]
    public void ProgressBox_WithErrorToast_IsUncertain(string text)
    {
        var v = ImportPostClickClassifier.Classify(true, [new OpenModalInfo(text, false)], ["Import thất bại"], expectedCount: 1);
        Assert.False(v.Sent);
        Assert.Equal(ImportVerdictKind.Uncertain, v.Kind);
    }

    [Theory]
    [InlineData(FinalOk1)]
    [InlineData(RunningWithTotal)]
    public void ProgressBox_WithUrlNotOnCrawl_IsUncertain(string text)
    {
        var v = ImportPostClickClassifier.Classify(false, [new OpenModalInfo(text, false)], expectedCount: 1);
        Assert.False(v.Sent);
        Assert.Equal(ImportVerdictKind.Uncertain, v.Kind);
    }

    [Fact]
    public void FinalBox_Navigated_WithoutDraftLink_IsUncertain_ButWithDraftLink_IsSent()
    {
        Assert.False(ImportPostClickClassifier.Classify(true, [new OpenModalInfo(FinalOk1, false)], null, navigated: true, expectedCount: 1).Sent);
        Assert.True(ImportPostClickClassifier.Classify(true, [new OpenModalInfo(FinalOk1, true)], null, navigated: true, expectedCount: 1).Sent);
    }

    [Theory]
    [InlineData(FinalOk1)]
    [InlineData(RunningWithTotal)]
    public void ProgressBox_AlongsideUnknownBox_IsUncertain(string text)
    {
        var progress = new OpenModalInfo(text, false);
        var unknown = new OpenModalInfo("Hộp lạ", false);
        var a = ImportPostClickClassifier.Classify(true, [progress, unknown], expectedCount: 1);
        var b = ImportPostClickClassifier.Classify(true, [unknown, progress], expectedCount: 1);
        Assert.False(a.Sent);
        Assert.False(b.Sent);
        Assert.Equal(ImportVerdictKind.Uncertain, a.Kind);
        Assert.Equal(ImportVerdictKind.Uncertain, b.Kind);
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

    // ── Bằng chứng đã gửi (requireEvidence = lần xét cuối khi hết cửa sổ theo dõi) ──

    [Fact]
    public void RequireEvidence_NoBoxAtAll_IsUncertain()
    {
        var v = ImportPostClickClassifier.Classify(true, [], null, navigated: false, expectedCount: 50, requireEvidence: true);
        Assert.False(v.Sent);
        Assert.Contains("chưa có bằng chứng", v.Reason);
        // Không bật requireEvidence thì vẫn như cũ (đang trong cửa sổ theo dõi, chưa kết luận).
        Assert.True(ImportPostClickClassifier.Classify(true, [], null, navigated: false, expectedCount: 50).Sent);
    }

    [Fact]
    public void RequireEvidence_FinalProgressBoxOrDraftBox_IsSent()
    {
        Assert.True(ImportPostClickClassifier.Classify(true, [new OpenModalInfo(FinalOk50, true)], expectedCount: 50, requireEvidence: true).Sent);
        Assert.True(ImportPostClickClassifier.Classify(true, [DraftBox], expectedCount: 50, requireEvidence: true).Sent);
    }

    [Fact]
    public void RequireEvidence_DraftBoxWithError_StillUncertain()
    {
        var box = new OpenModalInfo("Hộp nháp: thất bại 3", true);
        Assert.False(ImportPostClickClassifier.Classify(true, [box], requireEvidence: true).Sent);
    }

    // ── Loại hộp đã hiện TRƯỚC khi bấm Import ──

    [Fact]
    public void ExcludeExistingModals_DropsPreExistingBoxByText_KeepsNewOnes()
    {
        var stale = new OpenModalInfo(FinalOk50, true);
        var fresh = new OpenModalInfo("Hộp lạ mới", false);
        var left = ImportPostClickClassifier.ExcludeExistingModals([stale, fresh], [FinalOk50]);
        Assert.Single(left);
        Assert.Same(fresh, left[0]);
    }

    [Fact]
    public void ExcludeExistingModals_StaleSuccessBoxNoLongerMakesNextBatchSent()
    {
        // Lô sau: chỉ còn hộp thành công cũ chưa đóng → sau khi loại không còn bằng chứng nào của lô này.
        var stale = new OpenModalInfo(FinalOk50, true);
        var visible = ImportPostClickClassifier.ExcludeExistingModals([stale], [FinalOk50]);
        Assert.Empty(visible);
        Assert.False(ImportPostClickClassifier.Classify(true, visible, expectedCount: 50, requireEvidence: true).Sent);
        // Không loại thì hộp cũ làm lô sau "đã gửi" oan — đúng lỗi cần chặn.
        Assert.True(ImportPostClickClassifier.Classify(true, [stale], expectedCount: 50, requireEvidence: true).Sent);
    }

    [Fact]
    public void ExcludeExistingModals_ExcludesOnlyAsManyAsExisted()
    {
        var a = new OpenModalInfo("X", false);
        var left = ImportPostClickClassifier.ExcludeExistingModals([a, a, a], ["X", "X"]);
        Assert.Single(left);
        Assert.Empty(ImportPostClickClassifier.ExcludeExistingModals([], ["X"]));
        Assert.Equal(2, ImportPostClickClassifier.ExcludeExistingModals([a, a], []).Count);
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
