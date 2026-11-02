using Shopee.Core.BigSeller;

namespace Shopee.Core.Tests;

/// <summary>
/// Quyết định thuần của lượt Import: quét thêm vòng không (<see cref="ImportScanPolicy.ShouldRescan"/>) và trang crawl
/// đã xác nhận chưa (<see cref="ImportScanPolicy.IsPageConfirmed"/>). Lỗi cũ: tab/trang tải chậm ra "0 khớp" rồi bấm
/// Next bỏ sót SP, và không có vòng quét lại.
/// </summary>
public sealed class ImportScanPolicyTests
{
    [Fact]
    public void ShouldRescan_StillMissing_AfterBatchSent_Rescans()
    {
        Assert.True(ImportScanPolicy.ShouldRescan(1, 3, allImported: false, sentThisPass: true, hasUnconfirmed: false));
        Assert.True(ImportScanPolicy.ShouldRescan(2, 3, allImported: false, sentThisPass: true, hasUnconfirmed: false));
    }

    [Fact]
    public void ShouldRescan_StillMissing_WithUnconfirmedPart_Rescans()
    {
        Assert.True(ImportScanPolicy.ShouldRescan(1, 3, allImported: false, sentThisPass: false, hasUnconfirmed: true));
    }

    [Fact]
    public void ShouldRescan_NothingSentAndNothingUnconfirmed_Stops()
    {
        // Quét lại cũng không ra thêm gì → dừng, khỏi quét vô ích.
        Assert.False(ImportScanPolicy.ShouldRescan(1, 3, allImported: false, sentThisPass: false, hasUnconfirmed: false));
        Assert.False(ImportScanPolicy.ShouldRescan(2, 3, allImported: false, sentThisPass: false, hasUnconfirmed: false));
    }

    [Fact]
    public void ShouldRescan_ThirdPass_NeverScansAgain()
    {
        Assert.False(ImportScanPolicy.ShouldRescan(3, 3, allImported: false, sentThisPass: true, hasUnconfirmed: true));
        Assert.False(ImportScanPolicy.ShouldRescan(ImportScanPolicy.MaxPasses, ImportScanPolicy.MaxPasses, false, true, true));
    }

    [Fact]
    public void ShouldRescan_AllImported_Stops()
    {
        Assert.False(ImportScanPolicy.ShouldRescan(1, 3, allImported: true, sentThisPass: true, hasUnconfirmed: true));
    }

    [Fact]
    public void MaxPasses_IsThree()
    {
        Assert.Equal(3, ImportScanPolicy.MaxPasses);
    }

    // ── IsPageConfirmed ──

    [Fact]
    public void IsPageConfirmed_NoNewerResponse_IsNotConfirmed()
    {
        // DOM còn dòng trang cũ khớp response cũ → vẫn KHÔNG được xác nhận nếu chưa có response sau thao tác.
        Assert.False(ImportScanPolicy.IsPageConfirmed(false, new[] { "a.jpg" }, 1, new[] { "a.jpg" }));
    }

    [Fact]
    public void IsPageConfirmed_UnreadableResponse_IsNotConfirmed()
    {
        Assert.False(ImportScanPolicy.IsPageConfirmed(true, null, 1, new[] { "a.jpg" }));
    }

    [Fact]
    public void IsPageConfirmed_DomKeysBelongToResponse_IsConfirmed()
    {
        Assert.True(ImportScanPolicy.IsPageConfirmed(true, new[] { "a.jpg", "b.jpg", "c.jpg" }, 2, new[] { "a.jpg", "c.jpg" }));
    }

    [Fact]
    public void IsPageConfirmed_DomStillHasOldRows_IsNotConfirmed()
    {
        Assert.False(ImportScanPolicy.IsPageConfirmed(true, new[] { "new1.jpg" }, 2, new[] { "old1.jpg", "new1.jpg" }));
    }

    [Fact]
    public void IsPageConfirmed_NonEmptyResponseButDomEmpty_IsNotConfirmed()
    {
        // Đang tải: response đã về nhưng bảng chưa render → chưa được kết luận "0 khớp".
        Assert.False(ImportScanPolicy.IsPageConfirmed(true, new[] { "a.jpg" }, 0, Array.Empty<string>()));
    }

    // ── Phanh: trang liên tiếp chưa xác nhận, không có SP khớp, không có dữ liệu trang mới ──

    [Fact]
    public void Streak_IncrementsOnlyOnUnconfirmedNoMatchNoNewData()
    {
        Assert.Equal(1, ImportScanPolicy.NextUnconfirmedNoMatchStreak(0, pageConfirmed: false, hadMatch: false, noNewPageData: true));
        Assert.Equal(3, ImportScanPolicy.NextUnconfirmedNoMatchStreak(2, pageConfirmed: false, hadMatch: false, noNewPageData: true));
    }

    [Fact]
    public void Streak_ResetsWhenConfirmedOrHasMatchOrHasNewData()
    {
        Assert.Equal(0, ImportScanPolicy.NextUnconfirmedNoMatchStreak(2, pageConfirmed: true, hadMatch: false, noNewPageData: true));
        Assert.Equal(0, ImportScanPolicy.NextUnconfirmedNoMatchStreak(2, pageConfirmed: false, hadMatch: true, noNewPageData: true));
        Assert.Equal(0, ImportScanPolicy.NextUnconfirmedNoMatchStreak(2, pageConfirmed: false, hadMatch: false, noNewPageData: false));
    }

    [Fact]
    public void ShouldAbandonTab_OnlyAtThreeConsecutive()
    {
        Assert.False(ImportScanPolicy.ShouldAbandonTab(0));
        Assert.False(ImportScanPolicy.ShouldAbandonTab(2));
        Assert.True(ImportScanPolicy.ShouldAbandonTab(3));
        Assert.True(ImportScanPolicy.ShouldAbandonTab(4));
        Assert.Equal(3, ImportScanPolicy.MaxUnconfirmedNoMatchPages);
    }

    [Fact]
    public void Streak_ThreeUnconfirmedPagesWithMatchInBetween_DoesNotAbandon()
    {
        // 2 trang không dữ liệu → có SP khớp (reset) → 2 trang nữa: chưa đủ 3 liên tiếp.
        var s = 0;
        s = ImportScanPolicy.NextUnconfirmedNoMatchStreak(s, false, false, true);
        s = ImportScanPolicy.NextUnconfirmedNoMatchStreak(s, false, false, true);
        s = ImportScanPolicy.NextUnconfirmedNoMatchStreak(s, false, true, true);
        s = ImportScanPolicy.NextUnconfirmedNoMatchStreak(s, false, false, true);
        s = ImportScanPolicy.NextUnconfirmedNoMatchStreak(s, false, false, true);
        Assert.False(ImportScanPolicy.ShouldAbandonTab(s));
        s = ImportScanPolicy.NextUnconfirmedNoMatchStreak(s, false, false, true);
        Assert.True(ImportScanPolicy.ShouldAbandonTab(s));
    }

    [Fact]
    public void Streak_ThreePagesEachWithOneForeignImage_DoesNotAbandon()
    {
        // Mỗi trang response có dữ liệu, DOM giao với response, chỉ lạ 1 ảnh → chưa xác nhận nhưng KHÔNG phải "không có dữ liệu".
        var s = 0;
        for (var page = 0; page < 3; page++)
        {
            var response = new[] { "a.jpg", "b.jpg", "c.jpg" };
            var dom = new[] { "a.jpg", "b.jpg", $"foreign{page}.jpg" };
            Assert.False(ImportScanPolicy.IsPageConfirmed(true, response, 3, dom));
            var noNew = ImportScanPolicy.HasNoNewPageData(true, response, dom);
            Assert.False(noNew);
            s = ImportScanPolicy.NextUnconfirmedNoMatchStreak(s, false, false, noNew);
        }
        Assert.False(ImportScanPolicy.ShouldAbandonTab(s));
    }

    [Fact]
    public void Streak_ThreePagesWithNoNewResponse_Abandons()
    {
        var s = 0;
        for (var page = 0; page < 3; page++)
        {
            var noNew = ImportScanPolicy.HasNoNewPageData(false, new[] { "a.jpg" }, new[] { "a.jpg" });
            Assert.True(noNew);
            s = ImportScanPolicy.NextUnconfirmedNoMatchStreak(s, false, false, noNew);
        }
        Assert.True(ImportScanPolicy.ShouldAbandonTab(s));
    }

    [Fact]
    public void HasNoNewPageData_Cases()
    {
        Assert.True(ImportScanPolicy.HasNoNewPageData(false, new[] { "a" }, new[] { "a" }));            // không response mới
        Assert.True(ImportScanPolicy.HasNoNewPageData(true, null, new[] { "a" }));                      // response không đọc được
        Assert.True(ImportScanPolicy.HasNoNewPageData(true, new[] { "a" }, new[] { "x", "y" }));        // không giao
        Assert.True(ImportScanPolicy.HasNoNewPageData(true, new[] { "a" }, Array.Empty<string>()));     // DOM không có ảnh
        Assert.False(ImportScanPolicy.HasNoNewPageData(true, new[] { "a", "b" }, new[] { "x", "b" })); // giao 1 khoá
    }
    // ── Chẩn đoán chưa xác nhận ──

    [Fact]
    public void DescribeUnconfirmed_ReportsCountsAndAtMostFiveForeignKeys()
    {
        var text = ImportScanPolicy.DescribeUnconfirmed(
            true, new[] { "a", "b" }, 9, new[] { "a", "x1", "x2", "x3", "x4", "x5", "x6", "x7" });
        Assert.Contains("newer=True", text);
        Assert.Contains("khoá response=2", text);
        Assert.Contains("dòng DOM=9", text);
        Assert.Contains("khoá ảnh DOM=8", text);
        Assert.Contains("x1, x2, x3, x4, x5", text);
        Assert.DoesNotContain("x6", text);
        Assert.DoesNotContain("[a", text);   // "a" thuộc response nên không phải khoá lạ
    }

    [Fact]
    public void DescribeUnconfirmed_NullResponse_SaysNull()
    {
        var text = ImportScanPolicy.DescribeUnconfirmed(false, null, 0, Array.Empty<string>());
        Assert.Contains("newer=False", text);
        Assert.Contains("khoá response=null", text);
    }

    [Fact]
    public void IsPageConfirmed_EmptyResponse_NeedsEmptyDom()
    {
        Assert.True(ImportScanPolicy.IsPageConfirmed(true, Array.Empty<string>(), 0, Array.Empty<string>()));
        Assert.False(ImportScanPolicy.IsPageConfirmed(true, Array.Empty<string>(), 3, new[] { "old.jpg" }));
    }
}
