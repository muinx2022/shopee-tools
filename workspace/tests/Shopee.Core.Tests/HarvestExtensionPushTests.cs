using System.Text.Json;
using Shopee.Core.Harvest;
using static Shopee.Core.Tests.HarvestTestData;

namespace Shopee.Core.Tests;

/// <summary>
/// GĐ7 — extension tự đẩy Hub: app chỉ ghi <c>done</c> (không đưa JSON vào outbox) khi extension báo Hub đã nhận
/// (<see cref="ExtensionPushedHarvestSink"/> → <see cref="HarvestOutbox.RecordPushedByExtensionAsync"/>); đọc kết quả gửi Hub
/// của extension (<see cref="HarvestHubPush"/>).
/// </summary>
public sealed class HarvestExtensionPushTests : IDisposable
{
    private readonly string _dir = NewTempDir();
    private string DbPath => Path.Combine(_dir, "outbox.db");

    public void Dispose() => TryDelete(_dir);

    private static readonly HarvestHubAccess Hub = new("https://hub.example.com", "bi-mat", "MAY-1");

    private static HarvestedItem Sp(long itemId, int row, bool recordDone = true) =>
        Item("", row, json: SyntheticJson(itemId)) with
        {
            SourceLink = $"https://shopee.vn/sp-i.77.{itemId}", ShopeeShopId = 77, RecordDone = recordDone,
            Url = $"https://shopee.vn/product/77/{itemId}",
        };

    [Fact]
    public async Task ExtensionDaLuuHub_GhiDone_GoTapLoi_LinkMau_KhongVaoHangDoi()
    {
        var box = new HarvestOutbox(DbPath);
        await box.RecordPermanentFailureAsync(["id:77.1001"], "notProduct", "lượt-1", CancellationToken.None);
        var sink = new ExtensionPushedHarvestSink(box, () => Hub);
        await sink.EnsureReadyAsync(CancellationToken.None);

        await sink.SaveAsync(Sp(1001, 4), CancellationToken.None);

        Assert.Equal(new HashSet<string> { "id:77.1001" }, box.DoneKeys());
        Assert.Empty(box.FailedKeys(minRuns: 1));
        Assert.Equal("https://shopee.vn/product/77/1001", box.LastSavedLink());
        Assert.Equal(new HarvestOutboxStats(0, 0, 0, 0, 1), box.GetStats());   // outbox KHÔNG nhận mục mới, đếm 1 SP ext đẩy
        Assert.Empty(box.TakeDue(50, long.MaxValue));
    }

    /// <summary>Ba số Hub trên màn Harvest: "đã nhận" = outbox app đẩy + SP extension tự đẩy (bộ đếm bền ext_saved).</summary>
    [Fact]
    public async Task DaNhan_GomCaSpExtensionTuDay_BenQuaLanMoLai()
    {
        var box = new HarvestOutbox(DbPath);
        await box.SaveAsync(Sp(2001, 2), CancellationToken.None);
        box.Complete([(box.TakeDue(50, long.MaxValue).Single(), true)]);          // app đẩy 1
        var sink = new ExtensionPushedHarvestSink(box, () => Hub);
        await sink.SaveAsync(Sp(2002, 3), CancellationToken.None);
        await sink.SaveAsync(Sp(2003, 4, recordDone: false), CancellationToken.None);   // vẫn là SP Hub đã nhận
        var st = new HarvestOutbox(DbPath).GetStats();                             // mở lại DB
        Assert.Equal((1L, 2L, 3L), (st.PushedTotal, st.ExtensionSavedTotal, st.HubReceivedTotal));
        Assert.Equal("Hub: đã nhận 3 · app chờ đẩy 0 · lỗi 0", HarvestOverlayText.HubLine(st.HubReceivedTotal, st.Pending, st.Failed));
    }

    [Fact]
    public async Task RecordDoneFalse_KhongGhiDone()
    {
        var box = new HarvestOutbox(DbPath);
        await new ExtensionPushedHarvestSink(box, () => Hub).SaveAsync(Sp(1002, 4, recordDone: false), CancellationToken.None);
        Assert.Empty(box.DoneKeys());
    }

    /// <summary>SP từng bị Hub từ chối qua outbox (pending failed) → DoneKeys coi là "chưa có". Extension vừa đẩy được bản mới
    /// → mục lỗi cũ hết ý nghĩa, phải gỡ, không thì lượt sau lấy lại SP này mãi.</summary>
    [Fact]
    public async Task MucOutboxLoiCuCuaCungSP_BiGo_SPTinhLaDaCo()
    {
        var box = new HarvestOutbox(DbPath);
        await box.SaveAsync(Sp(1003, 2), CancellationToken.None);
        await box.SaveAsync(Sp(1004, 3), CancellationToken.None);
        var due = box.TakeDue(50, long.MaxValue);
        box.MarkFailed(due.Select(e => (e, "raw-error")).ToList());
        Assert.Empty(box.DoneKeys());

        await new ExtensionPushedHarvestSink(box, () => Hub).SaveAsync(Sp(1003, 2), CancellationToken.None);

        Assert.Equal(new HashSet<string> { "id:77.1003" }, box.DoneKeys());
        Assert.Equal(1, box.GetStats().Failed);   // mục lỗi của SP khác giữ nguyên
    }

    [Fact]
    public async Task ChuaNoiHub_EnsureReady_Nem_TruocKhiMoBrave()
    {
        var box = new HarvestOutbox(DbPath);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ExtensionPushedHarvestSink(box, () => null).EnsureReadyAsync(CancellationToken.None));
        Assert.Contains("chưa nối Hub", ex.Message);
    }

    [Fact]
    public void HubAccess_ToString_KhongLoToken()
    {
        Assert.DoesNotContain("bi-mat", Hub.ToString());
        Assert.Contains("hub.example.com", Hub.ToString());
    }

    [Theory]
    [InlineData("{\"hub\":{\"status\":\"saved\",\"reason\":null,\"applied\":true}}", "saved", null, true)]
    [InlineData("{\"hub\":{\"status\":\"queued\",\"reason\":\"Hub tạm lỗi (HTTP 503)\",\"applied\":false}}", "queued", "Hub tạm lỗi (HTTP 503)", false)]
    [InlineData("{\"hub\":{\"status\":\"rejected\",\"reason\":\"raw-error\"}}", "rejected", "raw-error", false)]
    [InlineData("{\"hub\":{\"status\":\"invalid\",\"reason\":\"khoá trùng\"}}", "invalid", "khoá trùng", false)]
    [InlineData("{\"hub\":{\"status\":\"failed\",\"reason\":\"đầy\"}}", "failed", "đầy", false)]
    [InlineData("{\"hub\":{\"status\":\"la-hoac\"}}", "failed", null, false)]   // chữ lạ → coi như failed (an toàn: dừng job)
    public void HubPush_Parse(string json, string status, string? reason, bool applied)
    {
        using var doc = JsonDocument.Parse(json);
        var p = HarvestHubPush.Parse(doc.RootElement)!;
        Assert.Equal((status, reason, applied), (p.Status, p.Reason, p.Applied));
        Assert.Equal(status == "saved", p.IsSaved);
    }

    [Fact]
    public void HubPush_Vang_LaNull_VaStepResultDocDuoc()
    {
        using var none = JsonDocument.Parse("{\"ok\":true}");
        Assert.Null(HarvestHubPush.Parse(none.RootElement));
        using var doc = JsonDocument.Parse("{\"ok\":true,\"itemId\":\"5\",\"json\":\"{}\",\"hub\":{\"status\":\"saved\",\"applied\":true}}");
        var step = HarvestStepResult.Parse(doc.RootElement, 1, "u");
        Assert.True(step.HarvestOk);
        Assert.True(step.HubPush!.IsSaved);
    }
}
