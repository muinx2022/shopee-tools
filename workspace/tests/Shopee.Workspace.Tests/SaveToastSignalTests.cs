using Shopee.Core.BigSeller;
using UpdateProduct;

namespace Shopee.Workspace.Tests;

/// <summary>
/// Bước Lưu của Update: thứ tự hộp thoại lỗi / toast (<see cref="BigSellerSaveSuccessHelper.PickFailureSignal"/>) và luật hai suất
/// log của dump toast (<see cref="BigSellerMaterialCenterCleaner.LogSaveToastDump"/>).
/// </summary>
public sealed class SaveToastSignalTests
{
    // ── Thứ tự: hộp thoại thắng toast ─────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("Upload failed: listing limit", "Số sản phẩm vượt giới hạn", "Dialog")]
    [InlineData("Upload failed: listing limit", null, "Dialog")]
    [InlineData(null, "Số sản phẩm vượt giới hạn", "Toast")]
    [InlineData(null, null, "None")]
    public void PickFailureSignal_DialogBeatsToast(string? dialog, string? toast, string expected)
        => Assert.Equal(expected, BigSellerSaveSuccessHelper.PickFailureSignal(dialog, toast).ToString());

    // ── Dump: hai suất riêng ──────────────────────────────────────────────────────────────────────────────────
    private sealed class Slots
    {
        public bool ContentTaken, EmptyTaken;
        public int ContentAsked, EmptyAsked;
        public readonly List<string> Lines = new();

        public Func<bool> Content => () => { ContentAsked++; if (ContentTaken) return false; ContentTaken = true; return true; };
        public Func<bool> Empty => () => { EmptyAsked++; if (EmptyTaken) return false; EmptyTaken = true; return true; };
        public void Run(IReadOnlyList<RecordedToast> all, long? since, string lead = "Lưu hết giờ, không xác nhận được")
            => BigSellerMaterialCenterCleaner.LogSaveToastDump(all, since, lead, Lines.Add, Content, Empty);
    }

    private static RecordedToast T(string text, long ts, string kind = "error") => new(text, ts, kind);

    [Fact]
    public void EmptyBuffer_LogsEmptyLineOnce_AndNeverAsksContentSlot()
    {
        var s = new Slots();
        s.Run([], 100);
        Assert.Single(s.Lines);
        Assert.Contains("không bắt được toast nào", s.Lines[0]);
        Assert.Equal(0, s.ContentAsked);

        s.Run([], 100);   // lần 2 cùng lane: suất "rỗng" đã dùng
        Assert.Single(s.Lines);
        Assert.Equal(0, s.ContentAsked);
    }

    [Fact]
    public void ContentPresent_LogsOnce_AndNeverAsksEmptySlot()
    {
        var s = new Slots();
        var all = new[] { T("Lỗi X", 150), T("Lỗi Y", 160, "warning") };
        s.Run(all, 100);
        Assert.Single(s.Lines);
        Assert.Contains("[error] Lỗi X", s.Lines[0]);
        Assert.Contains("[warning] Lỗi Y", s.Lines[0]);
        Assert.Contains("2 toast", s.Lines[0]);
        Assert.Equal(0, s.EmptyAsked);

        s.Run(all, 100);
        Assert.Single(s.Lines);
    }

    [Fact]
    public void EmptyLine_DoesNotConsumeContentSlot()
    {
        var s = new Slots();
        s.Run([], 100);   // lần 1: không toast → dòng rỗng
        Assert.False(s.ContentTaken);

        s.Run([T("Lỗi thật", 200)], 100);   // lần 2: có toast → vẫn log được nội dung
        Assert.Equal(2, s.Lines.Count);
        Assert.Contains("[error] Lỗi thật", s.Lines[1]);
        Assert.True(s.ContentTaken);
    }

    [Fact]
    public void ContentLogged_ThenLaterEmpty_StillGetsOwnEmptyLine()
    {
        var s = new Slots();
        s.Run([T("Lỗi thật", 200)], 100);
        s.Run([], 100);
        Assert.Equal(2, s.Lines.Count);
        Assert.Contains("không bắt được toast nào", s.Lines[1]);
    }

    [Fact]
    public void OnlyToastsBeforeSince_CountAsEmpty()
    {
        var s = new Slots();
        s.Run([T("Toast cũ", 50)], 100);
        Assert.Single(s.Lines);
        Assert.Contains("không bắt được toast nào", s.Lines[0]);
        Assert.DoesNotContain("Toast cũ", s.Lines[0]);
        Assert.Equal(0, s.ContentAsked);
    }

    [Fact]
    public void AlertSourcedEntries_AreDumpedWithTheirKind()
    {
        var s = new Slots();
        s.Run([T("Banner báo lỗi", 150, "alert:error")], 100);
        Assert.Contains("[alert:error] Banner báo lỗi", s.Lines[0]);
    }

    [Fact]
    public void NoSinceTs_ListsWholeBuffer_AndSaysSo()
    {
        var s = new Slots();
        s.Run([T("Toast rất cũ", 1)], null);
        Assert.Contains("Toast rất cũ", s.Lines[0]);
        Assert.Contains("không lấy được mốc bấm Lưu", s.Lines[0]);
    }

    [Fact]
    public void Lead_IsPassedThrough_NotHardcodedAsTimeout()
    {
        var s = new Slots();
        s.Run([T("X", 150)], 100, "Lưu thất bại (modal lỗi, không phải giới hạn số SP)");
        Assert.Contains("Lưu thất bại (modal lỗi", s.Lines[0]);
        Assert.DoesNotContain("hết giờ", s.Lines[0]);

        var s2 = new Slots();
        s2.Run([], 100, "Lưu thất bại (modal lỗi, không phải giới hạn số SP)");
        Assert.DoesNotContain("hết giờ", s2.Lines[0]);
    }
}
