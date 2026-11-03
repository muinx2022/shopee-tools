using UpdateProduct;

namespace Shopee.Workspace.Tests;

/// <summary>
/// Phần quyết định thuần của <see cref="BigSellerDraftClearer"/> (Clear nháp trước import): chỉ Confirm khi hộp đúng câu
/// "clearing all filtered products"; "về 0" cần BA lần đọc liên tiếp thấy 0 dòng; quyết định khi không thấy mục Clear; hộp confirm không hiện
/// mà bảng nháp còn dòng là Aborted; click Confirm lỗi mà hộp đã biến mất vẫn là đã Confirm (bảng import đã reset từ trước Confirm).
/// </summary>
public sealed class DraftClearerLogicTests
{
    [Theory]
    [InlineData("The task of clearing all filtered products cannot be stopped. Are you sure? Cancel Confirm")]
    [InlineData("Clear The task of  CLEARING ALL FILTERED\nPRODUCTS cannot be stopped")]   // hoa thường + xuống dòng/nhiều khoảng trắng
    public void ClearAllConfirm_RecognisedByPhrase(string text) => Assert.True(BigSellerDraftClearer.IsClearAllConfirmText(text));

    [Theory]
    [InlineData("Are you sure you want to delete the selected products? Cancel Confirm")]
    [InlineData("Clearing filters")]
    [InlineData("clearing all products")]       // thiếu "filtered"
    [InlineData("")]
    [InlineData(null)]
    public void OtherConfirmBox_IsNotClearAll_SoCallerCancels(string? text) => Assert.False(BigSellerDraftClearer.IsClearAllConfirmText(text));

    [Theory]
    [InlineData("Clear", true)]
    [InlineData("  clear \n", true)]
    [InlineData("Clear Filters", false)]
    [InlineData("Clear all", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ClearMenuItem_MustBeExactlyClear(string? text, bool expected) => Assert.Equal(expected, BigSellerDraftClearer.IsClearMenuText(text));

    [Theory]
    [InlineData("Confirm", true)]
    [InlineData(" confirm ", true)]
    [InlineData("Cancel", false)]
    [InlineData("Confirm all", false)]
    [InlineData(null, false)]
    public void ConfirmButton_MustBeExactlyConfirm(string? text, bool expected) => Assert.Equal(expected, BigSellerDraftClearer.IsConfirmButtonText(text));

    [Fact]
    public void EmptyNeedsThreeConsecutiveZeroReads()
    {
        Assert.Equal(3, BigSellerDraftClearer.RequiredEmptyReads);
        var streak = 0;
        streak = BigSellerDraftClearer.NextZeroStreak(streak, 0);
        Assert.False(BigSellerDraftClearer.IsEmptyConfirmed(streak));   // một lần 0 chưa đủ (bảng có thể đang tải)
        streak = BigSellerDraftClearer.NextZeroStreak(streak, 0);
        Assert.False(BigSellerDraftClearer.IsEmptyConfirmed(streak));   // hai lần cũng chưa
        streak = BigSellerDraftClearer.NextZeroStreak(streak, 0);
        Assert.True(BigSellerDraftClearer.IsEmptyConfirmed(streak));
    }

    [Fact]
    public void RowsBetweenZeroReads_ResetTheStreak()
    {
        var streak = BigSellerDraftClearer.NextZeroStreak(0, 0);
        streak = BigSellerDraftClearer.NextZeroStreak(streak, 0);
        streak = BigSellerDraftClearer.NextZeroStreak(streak, 12);
        Assert.Equal(0, streak);
        streak = BigSellerDraftClearer.NextZeroStreak(streak, 0);
        streak = BigSellerDraftClearer.NextZeroStreak(streak, 0);
        Assert.False(BigSellerDraftClearer.IsEmptyConfirmed(streak));   // chuỗi phải đếm lại từ đầu sau khi thấy dòng
    }

    // ── Không tìm được mục Clear ───────────────────────────────────────────────────────────────

    [Fact]
    public void MenuOpenedWithoutClear_TableEmpty_NothingToClear()
    {
        Assert.Equal(DraftClearStatus.NothingToClear, BigSellerDraftClearer.DecideWithoutClearItem(ClearMenuProbe.NoClearItem, tableEmpty: true));
    }

    [Fact]
    public void MenuOpenedWithoutClear_TableHasRows_Aborted()
    {
        // Menu không có "Clear" mà bảng nháp còn dòng: BigSeller đổi menu / lỗi giao diện, KHÔNG phải "không có gì để xoá" → không import vào nháp cũ.
        Assert.Equal(DraftClearStatus.Aborted, BigSellerDraftClearer.DecideWithoutClearItem(ClearMenuProbe.NoClearItem, tableEmpty: false));
    }

    [Theory]
    [InlineData("NoClearItem")]
    [InlineData("ButtonMissing")]
    [InlineData("MenuNotOpened")]
    [InlineData("ClearDisabled")]
    public void NoUsableClearItem_TableEmpty_NothingToClear_TableHasRows_Aborted(string probeName)
    {
        var probe = Enum.Parse<ClearMenuProbe>(probeName);   // enum internal → tham số test phải là kiểu public
        Assert.Equal(DraftClearStatus.NothingToClear, BigSellerDraftClearer.DecideWithoutClearItem(probe, tableEmpty: true));
        Assert.Equal(DraftClearStatus.Aborted, BigSellerDraftClearer.DecideWithoutClearItem(probe, tableEmpty: false));
    }

    // ── Hộp confirm không hiện sau khi bấm Clear ───────────────────────────────────────────────

    [Theory]
    [InlineData(0, true, "NothingToClear")]    // bảng rỗng trước Clear và đọc lại vẫn rỗng → không có gì để xoá
    [InlineData(0, false, "Aborted")]          // trước Clear 0 dòng nhưng đọc lại thấy dòng → bất thường
    [InlineData(5, true, "Aborted")]           // trước Clear còn dòng (đọc lại rỗng có thể do đang tải) → không coi là sạch
    [InlineData(5, false, "Aborted")]
    public void ConfirmBoxMissing_OnlyEmptyTableIsNothingToClear(int rowsBeforeClear, bool tableEmptyNow, string expectedName)
    {
        var expected = Enum.Parse<DraftClearStatus>(expectedName);   // enum internal → tham số test phải là kiểu public
        Assert.Equal(expected, BigSellerDraftClearer.DecideWhenConfirmBoxMissing(rowsBeforeClear, tableEmptyNow));
    }

    // ── Click Confirm ném lỗi ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false, false, true)]    // click bình thường, hộp đóng
    [InlineData(false, true, true)]     // click bình thường (hộp còn trong chớp mắt): vẫn là đã Confirm
    [InlineData(true, false, true)]     // click ném lỗi nhưng hộp đã biến mất → coi là đã Confirm (bảng đã reset từ trước: đi tiếp chờ nháp về 0)
    [InlineData(true, true, false)]     // click ném lỗi và hộp vẫn hiện → chưa Confirm: Aborted, "đã reset bảng nhưng chưa chắc đã xoá nháp"
    public void ConfirmClick_CountsAsDone(bool clickThrew, bool boxStillVisible, bool expected)
    {
        Assert.Equal(expected, BigSellerDraftClearer.ConfirmClickCountsAsDone(clickThrew, boxStillVisible));
    }

    // ── Toast nguyên văn ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void MergeToastTexts_DropsEmptyAndDuplicates_KeepsVerbatimWording()
    {
        var merged = BigSellerDraftClearer.MergeToastTexts(new string?[] { "No  data\nto clear", "", null, "No data to clear", "Network error" });
        Assert.Equal("No data to clear | Network error", merged);
    }

    [Fact]
    public void MergeToastTexts_NothingSeen_IsEmpty() => Assert.Equal("", BigSellerDraftClearer.MergeToastTexts(Array.Empty<string?>()));
}
