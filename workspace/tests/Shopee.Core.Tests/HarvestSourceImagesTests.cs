using System.Text.Json;
using Shopee.Core.Harvest;

namespace Shopee.Core.Tests;

/// <summary>GĐ9 — số liệu ảnh gốc phía app: đọc kết quả lượt của extension, cộng dồn nhiều cửa sổ / nhiều lần báo cùng lượt, cờ Hub hết
/// chỗ, ngân sách lượt theo khoảng nghỉ, dòng log.</summary>
public sealed class HarvestSourceImagesTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static SourceImageRound Round(string id, int pushed = 0, int existed = 0, int failed = 0, int queueIds = 0,
        string? stoppedBy = null, string? hubFullCode = null, string? lastError = null, int dropped = 0) =>
        new(id, 0, 0, pushed, existed, failed, 0, dropped, stoppedBy, hubFullCode, lastError, 0, queueIds, []);

    [Fact]
    public void Parse_DayDu()
    {
        var r = SourceImageRound.Parse(J("""
            {"roundId":"r1","startedAt":1,"endedAt":2,"checked":30,"present":10,"pushed":8,"existed":2,"failed":1,"skipped":3,
             "dropped":1,"stoppedBy":"hubFull","hubFullCode":"disk-low","lastError":"x",
             "errors":[{"itemId":"9","id":"vn-1","reason":"CDN trả HTTP 404"},{"itemId":"9","id":null,"reason":"bỏ"}],
             "queue":{"jobs":4,"ids":30}}
            """))!;
        Assert.Equal("r1", r.RoundId);
        Assert.Equal((30, 10, 8, 2, 1, 3, 1), (r.Checked, r.Present, r.Pushed, r.Existed, r.Failed, r.Skipped, r.Dropped));
        Assert.True(r.HubFull);
        Assert.Equal("disk-low", r.HubFullCode);
        Assert.Equal((4, 30), (r.QueueJobs, r.QueueIds));
        Assert.Equal(2, r.Errors.Count);
        Assert.Equal(new SourceImageRoundError("9", "vn-1", "CDN trả HTTP 404"), r.Errors[0]);
        Assert.Null(r.Errors[1].ImageId);
    }

    [Fact]
    public void Parse_ThieuTruong_VaKhongHopLe()
    {
        var r = SourceImageRound.Parse(J("""{"roundId":"r2","pushed":"x","failed":-3,"queue":null,"errors":"lạ","stoppedBy":null}"""))!;
        Assert.Equal((0, 0, 0, 0), (r.Pushed, r.Failed, r.QueueIds, r.QueueJobs));
        Assert.Null(r.StoppedBy);
        Assert.Empty(r.Errors);
        Assert.Null(SourceImageRound.Parse(J("""{"pushed":3}""")));     // thiếu roundId
        Assert.Null(SourceImageRound.Parse(J("[]")));
        Assert.Null(SourceImageRound.Parse(J("null")));
    }

    [Fact]
    public void ParseStartVaStatus()
    {
        Assert.Equal(new SourceImageStart(true, "r1", null), SourceImageStart.Parse(J("""{"started":true,"roundId":"r1"}""")));
        Assert.Equal(new SourceImageStart(false, "r0", "Đang chạy"), SourceImageStart.Parse(J("""{"started":false,"roundId":"r0","reason":"Đang chạy"}""")));
        Assert.True(SourceImageStart.Parse(J("""{"unsupported":true}""")).Unsupported);
        Assert.False(SourceImageStart.Parse(J("1")).Started);
        Assert.Equal(new SourceImageStart(false, "r0", null, HubFullCleared: true),
            SourceImageStart.Parse(J("""{"started":false,"roundId":"r0","hubFullCleared":true}""")));

        var st = SourceImageStatus.Parse(J("""{"running":true,"round":{"roundId":"r1","pushed":2},"queue":{"jobs":1,"ids":5},"hubFullUntil":0}"""))!;
        Assert.True(st.Running);
        Assert.Equal(2, st.Round!.Pushed);
        Assert.Equal(5, st.QueueIds);                         // không có profiles → queue.ids
        // Có profiles → chỉ hàng đợi của app ("auto"), không tính việc bấm tay.
        Assert.Equal(7, SourceImageStatus.Parse(J("""{"queue":{"ids":9},"profiles":{"auto":{"jobs":2,"ids":7},"manual":{"jobs":1,"ids":2}}}"""))!.QueueIds);
        Assert.Equal(0, SourceImageStatus.Parse(J("""{"queue":{"ids":2},"profiles":{"manual":{"jobs":1,"ids":2}}}"""))!.QueueIds);
        var none = SourceImageStatus.Parse(J("""{"running":false,"round":null}"""))!;
        Assert.False(none.Running);
        Assert.Null(none.Round);
        Assert.Null(SourceImageStatus.Parse(J("\"x\"")));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(24_999, 0)]        // còn 9,999 s < 10 s → không chạy
    [InlineData(25_000, 10_000)]
    [InlineData(60_000, 45_000)]
    [InlineData(115_000, 100_000)]
    [InlineData(240_000, 100_000)] // trần 100 s
    [InlineData(-5, 0)]
    public void Budget(int restMs, int expected) => Assert.Equal(expected, HarvestImageBudget.For(restMs));

    [Fact]
    public void Progress_CongDonNhieuCuaSo_ConThieuLaSoMoiNhatTungCuaSo()
    {
        var p = new HarvestSourceImageProgress();
        var changed = 0;
        p.Changed += () => changed++;
        p.Apply("A", Round("a1", pushed: 5, existed: 1, failed: 1, queueIds: 20));
        p.Apply("B", Round("b1", pushed: 3, queueIds: 7));
        p.Apply("A", Round("a2", pushed: 4, failed: 2, queueIds: 12));
        Assert.Equal(12, p.Pushed);
        Assert.Equal(1, p.Existed);
        Assert.Equal(3, p.Failed);
        Assert.Equal(19, p.Missing);   // A mới nhất 12 + B 7
        Assert.Equal(3, changed);
        Assert.Equal(new HarvestSourceImageSnapshot(12, 1, 3, 19, null, null), p.Snapshot());
    }

    [Fact]
    public void Progress_CungLuotBaoLai_KhongCongHaiLan_ChiCongPhanTang()
    {
        var p = new HarvestSourceImageProgress();
        p.Apply("A", Round("r1", pushed: 3, failed: 1, queueIds: 9));
        p.Apply("A", Round("r1", pushed: 3, failed: 1, queueIds: 9));   // báo lại y nguyên
        Assert.Equal(3, p.Pushed);
        p.Apply("A", Round("r1", pushed: 5, failed: 1, queueIds: 6));   // giữa chừng → cuối lượt
        Assert.Equal(5, p.Pushed);
        Assert.Equal(1, p.Failed);
        Assert.Equal(6, p.Missing);
        p.Apply("B", Round("r1", pushed: 2));                           // cùng roundId nhưng cửa sổ khác → lượt khác
        Assert.Equal(7, p.Pushed);
    }

    [Fact]
    public void Progress_HubFull_ResetVaXoaMocExtension()
    {
        var p = new HarvestSourceImageProgress();
        Assert.Equal(0, p.HubFullClearPending("A"));   // lượt đầu của profile trong phiên app → bỏ mốc cũ của extension
        Assert.Equal(0, p.HubFullClearPending("A"));   // chưa đánh dấu (RPC chưa tới extension) → vẫn còn thẻ
        p.MarkHubFullCleared("A", 0);
        Assert.Null(p.HubFullClearPending("A"));
        p.Apply("A", Round("r1", stoppedBy: "hubFull", hubFullCode: "source-quota-exceeded"));
        Assert.Equal("source-quota-exceeded", p.HubFullCode);
        p.Apply("A", Round("r2"));                                     // lượt sau không còn hubFull vẫn giữ cờ tới khi Reset
        Assert.Equal("source-quota-exceeded", p.HubFullCode);
        p.ResetHubFull();
        Assert.Null(p.HubFullCode);
        Assert.Equal(1, p.HubFullClearPending("A"));
        Assert.Equal(1, p.HubFullClearPending("B"));
        p.MarkHubFullCleared("A", 1);
        Assert.Null(p.HubFullClearPending("A"));
        p.MarkHubFullCleared("A", 0);                                  // thế hệ cũ đến muộn không lùi mốc
        Assert.Null(p.HubFullClearPending("A"));
        p.Apply("B", Round("r3", stoppedBy: "hubFull"));
        Assert.Equal("hub-full", p.HubFullCode);
    }

    [Fact]
    public void Progress_SetMissing_VaDropped()
    {
        var p = new HarvestSourceImageProgress();
        p.Apply("A", Round("r1", queueIds: 10, dropped: 1));
        p.SetMissing("B", 6);                                          // B chưa có lượt nào (nghỉ ngắn / hết khối)
        Assert.Equal(16, p.Missing);
        p.SetMissing("A", 2);
        Assert.Equal(8, p.Missing);
        p.Apply("A", Round("r1", queueIds: 2, dropped: 2));
        p.Apply("A", Round("r2", dropped: 1));
        Assert.Equal(3, p.Dropped);
        Assert.Equal(3, p.Snapshot().Dropped);
    }

    [Fact]
    public void PendingWarning()
    {
        Assert.Equal("⚠ Ảnh gốc: còn 7 ảnh chưa đẩy (hết khối) — sẽ đẩy ở khoảng nghỉ sau.",
            SourceImageRound.PendingWarning(7, SourceImageRound.WhyBlockEnd));
        Assert.Contains("(nghỉ giữa link quá ngắn)", SourceImageRound.PendingWarning(1, SourceImageRound.WhyShortRest));
    }

    [Fact]
    public void Progress_LoiGanNhat()
    {
        var p = new HarvestSourceImageProgress();
        p.Apply("A", Round("r1", lastError: "CDN trả WebP"));
        Assert.Equal("CDN trả WebP", p.LastError);
        p.Apply("A", Round("r2"));
        Assert.Equal("CDN trả WebP", p.LastError);                    // lượt không lỗi không xoá lỗi gần nhất
        var withErrors = Round("r3") with { Errors = [new SourceImageRoundError("1", "x", "Hub từ chối ảnh (HTTP 400 corrupt-image)")] };
        p.Apply("A", withErrors);
        Assert.Equal("Hub từ chối ảnh (HTTP 400 corrupt-image)", p.LastError);
        p.NoteError("Ảnh gốc: extension chưa được cấp quyền");
        Assert.Equal("Ảnh gốc: extension chưa được cấp quyền", p.LastError);
    }

    [Fact]
    public void Progress_ThreadSafe()
    {
        var p = new HarvestSourceImageProgress();
        Parallel.For(0, 200, i => p.Apply("K" + (i % 8), Round("r" + i, pushed: 1, queueIds: 1)));
        Assert.Equal(200, p.Pushed);
        Assert.Equal(8, p.Missing);
    }

    [Fact]
    public void LogLine()
    {
        Assert.Null(Round("r").LogLine());
        Assert.Equal("Ảnh gốc: đẩy 8 · đã có 2 · lỗi 1 · còn thiếu 30", Round("r", 8, 2, 1, 30).LogLine());
        Assert.Equal("Ảnh gốc: đẩy 0 · đã có 0 · lỗi 0 · còn thiếu 3 · bỏ 1 SP", Round("r", queueIds: 3, dropped: 1).LogLine());
        Assert.StartsWith("⚠ Hub hết chỗ (source-quota-exceeded) — dừng đẩy ảnh gốc",
            Round("r", 1, stoppedBy: "hubFull", hubFullCode: "source-quota-exceeded").LogLine());
        Assert.Equal("⚠ Ảnh gốc: không đẩy được — Hub từ chối token (HTTP 401).",
            Round("r", stoppedBy: "auth", lastError: "Hub từ chối token (HTTP 401)").LogLine());
    }
}
