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
    [InlineData("Please confirm again if you want to clear, the products cannot be recovered after clearing. Cancel Confirm")]   // câu thật 03/11
    [InlineData("PLEASE CONFIRM AGAIN\nIF YOU  WANT TO CLEAR, the products cannot be recovered")]   // hoa thường + xuống dòng/nhiều khoảng trắng
    public void SecondConfirm_RecognisedByPhrase(string text) => Assert.True(BigSellerDraftClearer.IsClearAllSecondConfirmText(text));

    [Theory]
    [InlineData("The task of clearing all filtered products cannot be stopped. Are you sure? Cancel Confirm")]   // câu hộp một
    [InlineData("Are you sure you want to delete the selected products? Cancel Confirm")]
    [InlineData("Please confirm again if you want to delete")]   // thiếu "to clear"
    [InlineData("")]
    [InlineData(null)]
    public void NotSecondConfirm_SoCallerCancels(string? text) => Assert.False(BigSellerDraftClearer.IsClearAllSecondConfirmText(text));

    [Fact]
    public void NewConfirmBox_SkipsFirstBoxStillClosing()
    {
        const string first = "The task of clearing all filtered products cannot be stopped.";
        const string second = "Please confirm again if you want to clear, the products cannot be recovered after clearing.";
        Assert.Equal(-1, BigSellerDraftClearer.FindNewConfirmBoxIndex(new string?[] { first }, first));             // hộp một đang đóng
        Assert.Equal(-1, BigSellerDraftClearer.FindNewConfirmBoxIndex(new string?[] { "  " + first + " ", "" }, first));
        Assert.Equal(-1, BigSellerDraftClearer.FindNewConfirmBoxIndex(System.Array.Empty<string?>(), first));
        Assert.Equal(1, BigSellerDraftClearer.FindNewConfirmBoxIndex(new string?[] { first, second }, first));     // hai hộp cùng có trong DOM
        Assert.Equal(0, BigSellerDraftClearer.FindNewConfirmBoxIndex(new string?[] { second }, first));
        Assert.Equal(0, BigSellerDraftClearer.FindNewConfirmBoxIndex(new string?[] { "Hộp lạ" }, first));          // hộp lạ vẫn được chọn để caller Cancel
    }

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

    // ── Giao diện tiếng Việt (song song tiếng Anh) ─────────────────────────────────────────────

    // Câu hộp 1/hộp 2 nguyên văn (Anh: DOM thật 03/11; Việt: user dán 03/11).
    private const string EnBox1 = "The task of clearing all filtered products cannot be stopped. Are you sure? Cancel Confirm";
    private const string EnBox2 = "Please confirm again if you want to clear, the products cannot be recovered after clearing. Cancel Confirm";
    private const string ViBox1 = "Thao tác này sẽ xóa tất cả các sản phẩm đã lọc và không thể chấm dứt nhiệm vụ xóa. Sản phẩm sau khi bị xóa không thể khôi phục, bạn có chắc chắn xóa hay không? Hủy Đồng ý";
    private const string ViBox2 = "Vui lòng xác nhận lại nếu bạn muốn xóa sạch, các sản phẩm không thể khôi phục sau khi xóa. Hủy Đồng ý";

    [Theory]
    [InlineData("Clear")]
    [InlineData("Xóa sạch")]
    [InlineData("Xoá sạch")]                 // dấu cũ (oá) vẫn khớp
    [InlineData("  xóa   sạch \n")]          // khoảng trắng/xuống dòng/hoa thường
    [InlineData("XÓA SẠCH")]
    [InlineData("Xo\u0301a sa\u0323ch")]      // NFD (dấu rời) vẫn khớp nhờ NFC + bỏ dấu
    [InlineData("Xóa\u00A0sạch")]            // khoảng trắng không ngắt
    public void ClearMenuItem_BothLanguages_Match(string text) => Assert.True(BigSellerDraftClearer.IsClearMenuText(text));

    [Theory]
    [InlineData("Xóa")]                       // "Delete" tiếng Việt: xoá SP đang chọn, TUYỆT ĐỐI không khớp
    [InlineData("Xoá")]
    [InlineData("Delete")]
    [InlineData("Chỉnh sửa hình ảnh")]
    [InlineData("Chỉnh sửa danh mục")]
    [InlineData("Xóa sạch bộ lọc")]           // chứa "Xóa sạch" nhưng không phải nguyên chuỗi
    [InlineData("Clear Filters")]
    public void ClearMenuItem_OtherItems_DoNotMatch(string text) => Assert.False(BigSellerDraftClearer.IsClearMenuText(text));

    [Theory]
    [InlineData(EnBox1)]
    [InlineData(ViBox1)]
    [InlineData("THAO TÁC NÀY SẼ XOÁ TẤT CẢ CÁC SẢN PHẨM ĐÃ LỌC")]   // hoa thường + dấu cũ
    public void ClearAllConfirm_BothLanguages_Recognised(string text) => Assert.True(BigSellerDraftClearer.IsClearAllConfirmText(text));

    [Theory]
    [InlineData(EnBox2)]
    [InlineData(ViBox2)]
    [InlineData("Thao tác này sẽ xóa các sản phẩm đã chọn. Hủy Đồng ý")]   // hộp xoá SP đang chọn
    public void ClearAllConfirm_CrossBoxOrOther_NotRecognised(string text) => Assert.False(BigSellerDraftClearer.IsClearAllConfirmText(text));

    [Theory]
    [InlineData(EnBox2)]
    [InlineData(ViBox2)]
    [InlineData("VUI LÒNG XÁC NHẬN LẠI NẾU BẠN MUỐN XOÁ SẠCH, các sản phẩm")]   // hoa thường + dấu cũ
    public void SecondConfirm_BothLanguages_Recognised(string text) => Assert.True(BigSellerDraftClearer.IsClearAllSecondConfirmText(text));

    [Theory]
    [InlineData(EnBox1)]
    [InlineData(ViBox1)]
    [InlineData("Vui lòng xác nhận lại nếu bạn muốn xóa")]   // thiếu "sạch"
    public void SecondConfirm_CrossBoxOrOther_NotRecognised(string text) => Assert.False(BigSellerDraftClearer.IsClearAllSecondConfirmText(text));

    [Theory]
    [InlineData("Confirm", true)]
    [InlineData("Đồng ý", true)]
    [InlineData("  ĐỒNG   Ý \n", true)]
    [InlineData("Dong y", true)]              // bỏ dấu hoàn toàn vẫn khớp (so không dấu)
    [InlineData("Hủy", false)]
    [InlineData("Cancel", false)]
    [InlineData("Đồng ý xóa", false)]         // chứa nhưng không nguyên chuỗi
    public void ConfirmButton_BothLanguages(string text, bool expected) => Assert.Equal(expected, BigSellerDraftClearer.IsConfirmButtonText(text));

    [Theory]
    [InlineData("Cancel", true)]
    [InlineData("Hủy", true)]
    [InlineData("Huỷ", true)]
    [InlineData("Confirm", false)]
    [InlineData("Đồng ý", false)]
    [InlineData("Hủy bỏ", false)]
    [InlineData(null, false)]
    public void CancelButton_BothLanguages(string? text, bool expected) => Assert.Equal(expected, BigSellerDraftClearer.IsCancelButtonText(text));

    [Theory]
    [InlineData("Bulk Actions", true)]
    [InlineData("Thao tác hàng loạt", true)]
    [InlineData(" THAO TÁC  HÀNG LOẠT ", true)]
    [InlineData("Chỉnh sửa hàng loạt", false)]   // nút cùng thanh: Bulk Edit
    [InlineData("Tải lên", false)]
    [InlineData("Nhập", false)]
    [InlineData("Thêm sản phẩm", false)]
    [InlineData("Bulk Edit", false)]
    public void BulkButton_BothLanguages(string text, bool expected) => Assert.Equal(expected, BigSellerDraftClearer.IsBulkButtonText(text));

    [Fact]
    public void NewConfirmBox_Vietnamese_SkipsFirstBoxStillClosing()
    {
        Assert.Equal(-1, BigSellerDraftClearer.FindNewConfirmBoxIndex(new string?[] { ViBox1 }, ViBox1));             // hộp một đang đóng
        Assert.Equal(1, BigSellerDraftClearer.FindNewConfirmBoxIndex(new string?[] { ViBox1, ViBox2 }, ViBox1));     // hai hộp cùng có trong DOM
        Assert.Equal(0, BigSellerDraftClearer.FindNewConfirmBoxIndex(new string?[] { ViBox2 }, ViBox1));
    }

    [Fact]
    public void ButtonDiagnostics_MentionsBothLanguageButtonTexts()
    {
        var line = BigSellerDraftClearer.FormatButtonDiagnostics(0, 0, new string?[] { "Thao tác hàng loạt" });
        Assert.Contains("Bulk Actions/Thao tác hàng loạt", line);
    }

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

    // ── Chẩn đoán khi không thấy nút Bulk Actions ──────────────────────────────────────────────

    [Fact]
    public void ButtonDiagnostics_CountsAndCapsAt15Buttons_SquashesAndTruncates()
    {
        var texts = new List<string?> { "  Bulk\n Actions ", "", null, new string('x', 100) };
        for (var i = 0; i < 20; i++) texts.Add("b" + i);
        var line = BigSellerDraftClearer.FormatButtonDiagnostics(3, 1, texts);
        Assert.Contains("khớp 3 phần tử, đang hiện 1", line);
        Assert.Contains("24 button đang hiện", line);
        Assert.Contains("[Bulk Actions | (trống) | (trống) | " + new string('x', 40) + "… | b0 |", line);
        Assert.Contains("b10", line);     // phần tử thứ 14
        Assert.DoesNotContain("b11", line);   // phần tử thứ 16 trở đi bị cắt
        Assert.EndsWith("(+9 nữa)", line);
    }

    // ── Trang nháp rỗng: bỏ qua Clear ──

    [Theory]
    [InlineData(true, 0, true)]      // có dấu "Không có dữ liệu" + 0 dòng → bỏ qua Clear
    [InlineData(false, 0, false)]    // 0 dòng mà KHÔNG có dấu (đang tải...) → vẫn Clear
    [InlineData(true, 3, false)]     // có dấu mà vẫn đếm được dòng (đọc lệch) → vẫn Clear
    [InlineData(false, 3, false)]    // có dòng → Clear
    public void ShouldSkipClear_OnlyWhenEmptyMarkerAndZeroRows(bool marker, int rows, bool expected) =>
        Assert.Equal(expected, BigSellerDraftClearer.ShouldSkipClear(marker, rows));

    [Fact]
    public void PageEmptyResult_DefaultsFalse_SoOtherNothingToClearPathsKeepOldMeaning()
    {
        Assert.False(new DraftClearResult(DraftClearStatus.NothingToClear, "x").PageEmpty);
        Assert.True(new DraftClearResult(DraftClearStatus.NothingToClear, "x", PageEmpty: true).PageEmpty);
    }
}
