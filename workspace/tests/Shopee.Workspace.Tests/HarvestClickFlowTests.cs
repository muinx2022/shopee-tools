using System.Text.Json;
using OpenMultiBraveLauncherV3;
using Shopee.Core.Harvest;
using Shopee.Modules.MultiBrave;

namespace Shopee.Workspace.Tests;

/// <summary>
/// GĐ7 — app điều khiển extension kiểu BigSeller: <see cref="HarvestClickStep"/> (chờ hộp sẵn sàng → bấm chuột thật đúng toạ
/// độ nút → chờ kết quả, bấm lại khi cú bấm trượt) qua IO giả; và <see cref="HarvestRowProcessor"/> ánh xạ kết quả gửi Hub
/// của extension sang luật dòng GĐ5–GĐ6 (chỉ ghi done khi Hub đã nhận).
/// </summary>
public sealed class HarvestClickFlowTests
{
    private const string Link = "https://shopee.vn/product/1490438686/26393229156";
    private static readonly HarvestHubAccess Hub = new("https://hub.example.com", "tok-app", "MAY-1");

    private static HarvestClickRequest Req(int? tab = 5) =>
        new(Link, 12, "Đang lấy dữ liệu dòng 12…", "P1", tab, [90309999L], "harvest", "Shop A",
            "https://shopee.vn/Ao-i.1490438686.26393229156?sp_atk=x", "run-1", Hub);

    private static JsonElement Json(string s)
    {
        using var doc = JsonDocument.Parse(s);
        return doc.RootElement.Clone();
    }

    private static HarvestClickPrepare Ready(double x = 1000, double y = 740, double w = 276, double h = 31, double vw = 1280, double vh = 800,
        double? zoom = 1, double? scale = 1) =>
        new(true, 7, x, y, w, h, vw, vh, Link + "?x=1", "26393229156", null, null, zoom, scale, 1.25);

    private const string SavedResult =
        "{\"ok\":true,\"harvestOk\":true,\"itemId\":\"26393229156\",\"shopId\":\"1490438686\",\"json\":\"{\\\"error\\\":null}\"," +
        "\"source\":\"hook\",\"tabId\":7,\"pageUrl\":\"" + Link + "\",\"capturedAt\":1700000000123," +
        "\"hub\":{\"status\":\"saved\",\"reason\":null,\"applied\":true}}";

    /// <summary>IO giả: đồng hồ ảo (DelayAsync đẩy giờ), kịch bản poll theo thời điểm, ghi lại mọi cú bấm.</summary>
    private sealed class FakeIo : IHarvestClickIo
    {
        public DateTimeOffset Now { get; private set; } = new(2026, 11, 18, 9, 0, 0, TimeSpan.FromHours(7));
        public HarvestClickPrepare Prepare { get; set; } = Ready();
        public HarvestClickPrepare Rect { get; set; } = Ready(x: 900, y: 700);
        public Func<FakeIo, HarvestClickPoll> Poll { get; set; } = _ => new("running", null);
        public List<string> Payloads { get; } = [];
        public List<(string? Url, double X, double Y, DateTimeOffset At)> Clicks { get; } = [];
        public List<string> PollTokens { get; } = [];
        public int RectCalls { get; private set; }
        public int PollFailures { get; set; }

        public Task<HarvestClickPrepare> PrepareAsync(string payloadJson, CancellationToken cancellationToken)
        {
            Payloads.Add(payloadJson);
            return Task.FromResult(Prepare);
        }
        public Exception? ClickFails { get; set; }
        public Task ClickAsync(string? pageUrl, double x, double y, CancellationToken cancellationToken)
        {
            if (ClickFails is not null) throw ClickFails;
            Clicks.Add((pageUrl, x, y, Now));
            return Task.CompletedTask;
        }
        public Task<HarvestClickPoll> PollAsync(string token, CancellationToken cancellationToken)
        {
            PollTokens.Add(token);
            if (PollFailures > 0) { PollFailures--; throw new InvalidOperationException("SW không trả lời"); }
            return Task.FromResult(Poll(this));
        }
        public Task<HarvestClickPrepare> ButtonRectAsync(int tabId, string token, CancellationToken cancellationToken)
        {
            RectCalls++;
            return Task.FromResult(Rect);
        }
        public Task DelayAsync(int ms, CancellationToken cancellationToken)
        {
            Now = Now.AddMilliseconds(ms);
            return Task.CompletedTask;
        }
        public double SinceFirstClick => Clicks.Count == 0 ? 0 : (Now - Clicks[0].At).TotalSeconds;
    }

    private static Task<HarvestStepResult> Run(FakeIo io) => HarvestClickStep.RunAsync(io, Req(), new Random(7), CancellationToken.None);

    [Fact]
    public async Task HopSanSang_BamDungGiuaNut_ChoKetQua_DocKetQuaGuiHub()
    {
        var io = new FakeIo { Poll = f => f.SinceFirstClick >= 3 ? new("done", Json(SavedResult)) : new("running", null) };
        var r = await Run(io);

        var click = Assert.Single(io.Clicks);
        Assert.Equal(Link + "?x=1", click.Url);                                     // bấm trên đúng tab trang SP hộp báo
        Assert.InRange(click.X, 1000 + 276 * 0.3, 1000 + 276 * 0.7);              // trong 40% giữa nút, không trượt ra trang
        Assert.InRange(click.Y, 740 + 31 * 0.3, 740 + 31 * 0.7);
        Assert.True(r.HarvestOk);
        Assert.Equal(26393229156L, r.ItemId);
        Assert.True(r.HubPush!.IsSaved);
        Assert.Equal(7, r.TabId);

        // Ngữ cảnh truyền extension: nguồn + cấu hình Hub của app; token lượt bấm khớp lúc hỏi kết quả.
        using var p = JsonDocument.Parse(Assert.Single(io.Payloads));
        var ctx = p.RootElement.GetProperty("context");
        Assert.Equal("harvest", ctx.GetProperty("accountId").GetString());
        Assert.Equal("Shop A", ctx.GetProperty("sheet").GetString());
        Assert.Equal(12, ctx.GetProperty("rowNo").GetInt32());
        Assert.Equal(Link, ctx.GetProperty("url").GetString());
        Assert.Equal("https://shopee.vn/Ao-i.1490438686.26393229156?sp_atk=x", ctx.GetProperty("sourceUrl").GetString());
        Assert.Equal("run-1", ctx.GetProperty("runId").GetString());
        Assert.Equal("https://hub.example.com", ctx.GetProperty("hub").GetProperty("url").GetString());
        Assert.Equal("tok-app", ctx.GetProperty("hub").GetProperty("token").GetString());
        Assert.Equal("MAY-1", ctx.GetProperty("hub").GetProperty("machineId").GetString());
        Assert.Equal(90309999L, p.RootElement.GetProperty("blockErrorCodes")[0].GetInt64());
        var token = p.RootElement.GetProperty("token").GetString();
        Assert.All(io.PollTokens, t => Assert.Equal(token, t));
    }

    [Fact]
    public async Task KhongCoHopDeBam_TraKetQuaExtensionDaChay_KhongBam()
    {
        var io = new FakeIo { Prepare = new(false, 7, 0, 0, 0, 0, 0, 0, null, null, Json("{\"loginRequired\":true,\"message\":\"m\",\"tabId\":7}"), null) };
        var r = await Run(io);
        Assert.Empty(io.Clicks);
        Assert.True(r.LoginRequired);
        Assert.False(r.HarvestOk);
    }

    [Fact]
    public async Task KhongToiDuocExtension_LoiNoSW_DeMoLaiProfile()
    {
        var io = new FakeIo { Prepare = HarvestClickPrepare.NotReady("No SW: lượt bấm không gửi được tới extension (x).") };
        var r = await Run(io);
        Assert.Empty(io.Clicks);
        Assert.StartsWith("No SW", r.Message);
    }

    [Fact]
    public async Task CuBamTruot_HoiLaiToaDo_BamLanHai_RoiXong()
    {
        var io = new FakeIo
        {
            Poll = f => f.Clicks.Count < 2 ? new("pending", null) : new("done", Json(SavedResult)),
        };
        var r = await Run(io);
        Assert.Equal(2, io.Clicks.Count);
        Assert.Equal(1, io.RectCalls);
        Assert.True((io.Clicks[1].At - io.Clicks[0].At) >= HarvestClickStep.ClickAckTimeout);   // chờ đủ hạn rồi mới bấm lại
        Assert.InRange(io.Clicks[1].X, 900 + 276 * 0.3, 900 + 276 * 0.7);                      // toạ độ MỚI hộp báo
        Assert.True(r.HubPush!.IsSaved);
    }

    [Fact]
    public async Task BamHaiLanVanKhongNhan_LoiTam_KhongBamThem()
    {
        var io = new FakeIo { Poll = _ => new("pending", null) };
        var r = await Run(io);
        Assert.Equal(HarvestClickStep.MaxClicks, io.Clicks.Count);
        Assert.False(r.HarvestOk);
        Assert.Contains("không nhận", r.Message);
        Assert.DoesNotContain("No SW", r.Message);
    }

    [Fact]
    public async Task HopKhongConSanSangKhiBamLai_LoiTam()
    {
        var io = new FakeIo { Poll = _ => new("pending", null), Rect = HarvestClickPrepare.NotReady("busy") };
        var r = await Run(io);
        Assert.Single(io.Clicks);
        Assert.Contains("không còn sẵn sàng", r.Message);
    }

    [Theory]
    [InlineData(1100, 740, 276, 31)]   // tràn mép phải viewport 1280
    [InlineData(1000, 790, 276, 31)]   // tràn mép dưới viewport 800
    [InlineData(1000, -5, 276, 31)]
    [InlineData(1000, 740, 0, 31)]     // nút ẩn
    public async Task NutNgoaiKhungNhin_KhongBam(double x, double y, double w, double h)
    {
        var io = new FakeIo { Prepare = Ready(x, y, w, h) };
        var r = await Run(io);
        Assert.Empty(io.Clicks);
        Assert.Contains("ngoài khung nhìn", r.Message);
    }

    [Fact]
    public async Task BamQuaCdpLoi_LoiTam_KhongNem()
    {
        var io = new FakeIo { ClickFails = new InvalidOperationException("không thấy tab trang SP trên CDP") };
        var r = await Run(io);
        Assert.False(r.HarvestOk);
        Assert.Contains("Không bấm được nút", r.Message);
        Assert.Empty(io.PollTokens);
    }

    [Theory]
    [InlineData(1.25, 1.0, "zoom 125%")]
    [InlineData(0.9, 1.0, "zoom 90%")]
    [InlineData(1.0, 1.5, "thu phóng 150%")]
    public async Task TrangDangPhongTo_KhongBam_BaoRo_TinhLaBamTruot(double zoom, double scale, string expect)
    {
        var io = new FakeIo { Prepare = Ready(zoom: zoom, scale: scale) };
        var r = await Run(io);
        Assert.Empty(io.Clicks);
        Assert.True(r.ClickMissed);
        Assert.Contains(expect, r.Message);
        Assert.DoesNotMatch("(?i)captcha|verify|proxy", r.Message);
    }

    [Fact]
    public async Task ExtensionKhongBaoZoom_VanBamDuoc()
    {
        var io = new FakeIo { Prepare = Ready(zoom: null, scale: null), Poll = _ => new("done", Json(SavedResult)) };
        Assert.True((await Run(io)).HarvestOk);
        Assert.Single(io.Clicks);
    }

    [Fact]
    public async Task CacLoaiBamTruot_DanhDauClickMissed_LoiKhac_Khong()
    {
        Assert.True((await Run(new FakeIo { Poll = _ => new("pending", null) })).ClickMissed);                 // bấm 2 lần không nhận
        Assert.True((await Run(new FakeIo { Prepare = Ready(x: 1100) })).ClickMissed);                         // ngoài khung nhìn
        Assert.True((await Run(new FakeIo { ClickFails = new InvalidOperationException("x") })).ClickMissed);  // CDP bấm lỗi
        Assert.True((await Run(new FakeIo { Poll = _ => new("pending", null), Rect = HarvestClickPrepare.NotReady("covered: nút bị che") })).ClickMissed);
        Assert.False((await Run(new FakeIo { Poll = _ => new("unknown", null) })).ClickMissed);
        Assert.False((await Run(new FakeIo { Poll = _ => new("running", null) })).ClickMissed);                // quá giờ chờ kết quả
        Assert.False((await Run(new FakeIo { Prepare = HarvestClickPrepare.NotReady("No SW: x") })).ClickMissed);
    }

    [Fact]
    public async Task ExtensionMatLuotBam_Unknown_LoiTam()
    {
        var io = new FakeIo { Poll = _ => new("unknown", null) };
        var r = await Run(io);
        Assert.False(r.HarvestOk);
        Assert.Contains("không còn giữ lượt bấm", r.Message);
    }

    [Fact]
    public async Task HoiKetQuaLoiLienTiep_NoSW_LoiLe_ThiBoQua()
    {
        var io = new FakeIo { PollFailures = HarvestClickStep.MaxPollErrors };
        var r = await Run(io);
        Assert.StartsWith("No SW", r.Message);

        var io2 = new FakeIo { PollFailures = 2, Poll = _ => new("done", Json(SavedResult)) };
        Assert.True((await Run(io2)).HarvestOk);
    }

    [Fact]
    public async Task QuaTranChoKetQua_LoiTam()
    {
        var io = new FakeIo { Poll = _ => new("running", null) };
        var r = await Run(io);
        Assert.Contains("Quá 600s", r.Message);
        Assert.True(io.SinceFirstClick >= HarvestClickStep.ResultTimeout.TotalSeconds);
        Assert.Single(io.Clicks);
    }

    // ── Bộ xử lý dòng: kết quả gửi Hub của extension → luật dòng ─────────────────────────────────────────────────────

    private sealed class RecSink : IHarvestSink
    {
        public List<HarvestedItem> Saved { get; } = [];
        public Task EnsureReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(HarvestedItem item, CancellationToken cancellationToken) { Saved.Add(item); return Task.CompletedTask; }
    }

    private sealed class RecFailures : IHarvestFailureSink
    {
        public List<(string[] Keys, string Reason)> Calls { get; } = [];
        public Task RecordPermanentFailureAsync(IReadOnlyList<string> itemKeys, string reason, string runId, CancellationToken cancellationToken)
        {
            Calls.Add((itemKeys.ToArray(), reason));
            return Task.CompletedTask;
        }
    }

    private sealed class Host : IHarvestRowHost
    {
        public Task ReportAsync(int? tabId, CancellationToken cancellationToken) => Task.CompletedTask;
        public void Progress() { }
        public Task<bool> IsCdpReachableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<RunnerExtensionRpc.BeforeNextLinkCheckResult> CheckCaptchaAsync(int tabId, int rowNumber, string sku, CancellationToken cancellationToken) =>
            Task.FromResult(new RunnerExtensionRpc.BeforeNextLinkCheckResult(false, true, false, false, null, 1, null));
        public Task<HarvestStepResult> ProbeSampleAsync(string link, int rowNumber, int? tabId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("không dùng");
    }

    private static (HarvestRowProcessor P, InstanceConfig Cfg, RecSink Sink, RecFailures Failures) Proc(bool extensionPush = true)
    {
        var cfg = new InstanceConfig { StartRow = 2, EndRow = 20, NextRunRow = 2, Mode = RunnerMode.Harvest, HubAccountId = "harvest" };
        var sink = new RecSink();
        var failures = new RecFailures();
        var options = new HarvestRunOptions(sink, "", RecordDone: true, FailureSink: failures, RunId: "run-1",
            ExtensionPush: extensionPush ? new HarvestExtensionPush(() => Hub) : null);
        return (new HarvestRowProcessor(cfg, options, new HarvestPdpBreaker(3), _ => { }), cfg, sink, failures);
    }

    private static HarvestStepResult Captured(HarvestHubPush? push) =>
        new(true, false, false, false, false, false, false, false, "ok", 7, Link, 26393229156L, 1490438686L,
            "{\"error\":null}", "hook", null, null, 1_700_000_000_123, false, push);

    private static Task<HarvestRowOutcome> Handle(HarvestRowProcessor p, HarvestStepResult step, int row = 6) =>
        p.HandleAsync(step, new HarvestRowInput(row, "Shop A", "https://shopee.vn/Ao-i.1490438686.26393229156", Link, "SKU", 7,
            DateTimeOffset.Now), new Host(), CancellationToken.None);

    private static ScrapeFailureAction Action(InstanceConfig cfg)
    {
        var c = ScrapeRunner.Classify(cfg);
        return ScrapeFailurePolicy.Decide(c.Errored, c.IsCaptcha, c.Reason);
    }

    [Fact]
    public async Task HubDaNhan_GhiDoneQuaSink_TinhDongXong()
    {
        var x = Proc();
        var o = await Handle(x.P, Captured(new(HarvestHubPush.Saved, null, true)));
        Assert.Equal((HarvestRowNext.Done, true), (o.Next, o.Saved));
        var item = Assert.Single(x.Sink.Saved);
        Assert.True(item.RecordDone);
        Assert.Equal(("harvest", "Shop A", 6, 26393229156L), (item.AccountId, item.Sheet, item.RowNo, item.ItemId));
        Assert.Equal(6, x.Cfg.LastCompletedRow);
        Assert.Empty(x.Cfg.SnapshotFailedRows());
    }

    [Fact]
    public async Task HubChuaNhan_VaoHangDoiExtension_KhongGhiDone_LoiTam_ChayTiep()
    {
        var x = Proc();
        var o = await Handle(x.P, Captured(new(HarvestHubPush.Queued, "Hub tạm lỗi (HTTP 503 pg-unavailable)", false)));
        Assert.Equal((HarvestRowNext.Done, false), (o.Next, o.Saved));
        Assert.Empty(x.Sink.Saved);                                         // KHÔNG done
        Assert.Empty(x.Failures.Calls);                                     // KHÔNG lỗi vĩnh viễn
        var f = Assert.Single(x.Cfg.SnapshotFailedRows());
        Assert.Equal(6, f.Row);
        Assert.Contains("hàng đợi của extension", f.Reason);
    }

    [Fact]
    public async Task HubTuChoi_LoiTam_KhongDone_KhongVinhVien()
    {
        var x = Proc();
        var o = await Handle(x.P, Captured(new(HarvestHubPush.Rejected, "raw-error", false)));
        Assert.Equal(HarvestRowNext.Done, o.Next);
        Assert.Empty(x.Sink.Saved);
        Assert.Empty(x.Failures.Calls);
        Assert.Contains("Hub từ chối", Assert.Single(x.Cfg.SnapshotFailedRows()).Reason);
    }

    [Fact]
    public async Task JsonKhongXuLyDuoc_LoiVinhVien_LuatHaiLuot()
    {
        var x = Proc();
        await Handle(x.P, Captured(new(HarvestHubPush.Invalid, "JSON PDP có khoá trùng", false)));
        Assert.Empty(x.Sink.Saved);
        var call = Assert.Single(x.Failures.Calls);
        Assert.Contains("id:1490438686.26393229156", call.Keys);
        Assert.Contains("không xử lý được", call.Reason);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData(null)]   // extension không báo kết quả gửi Hub (bản cũ)
    public async Task KhongGuiDuocMaCungKhongGiuDuoc_DungJobNhuLoiChoLuu(string? status)
    {
        var x = Proc();
        var o = await Handle(x.P, Captured(status is null ? null : new(status, "Hàng đợi gửi Hub đã đầy", false)));
        Assert.Equal(HarvestRowNext.Stop, o.Next);
        Assert.Empty(x.Sink.Saved);
        Assert.True(HarvestSinkFailure.Is(x.Cfg.LastRunnerMessage));
        Assert.Equal(ScrapeFailureAction.StopJob, Action(x.Cfg));
        Assert.NotEqual(6, x.Cfg.LastCompletedRow);                          // dòng không tính xong
    }

    private static HarvestStepResult Miss() =>
        HarvestStepResult.Failure("Bấm nút \"Lấy dữ liệu\" 2 lần mà extension không nhận", 7, Link) with { ClickMissed = true };

    [Fact]
    public async Task CauDaoBamTruot_3DongLienTiep_DungJob_KhongBoDong()
    {
        var x = Proc();
        Assert.Equal(HarvestRowNext.Done, (await Handle(x.P, Miss(), 6)).Next);
        Assert.Equal(HarvestRowNext.Done, (await Handle(x.P, Miss(), 7)).Next);   // dưới ngưỡng: lỗi dòng tạm
        Assert.Equal(2, x.Cfg.SnapshotFailedRows().Count);
        var o = await Handle(x.P, Miss(), 8);
        Assert.Equal(HarvestRowNext.Stop, o.Next);
        Assert.True(HarvestPanelClickFailure.Is(x.Cfg.LastRunnerMessage));
        Assert.Contains("3 dòng liên tiếp", x.Cfg.LastRunnerMessage);
        Assert.Contains("zoom", x.Cfg.LastRunnerMessage);
        Assert.Equal(ScrapeFailureAction.StopJob, Action(x.Cfg));
        Assert.DoesNotContain(x.Cfg.SnapshotFailedRows(), f => f.Row == 8);       // dòng dừng không vào sổ bỏ qua
        var reason = ScrapeRunner.Classify(x.Cfg).Reason;
        Assert.DoesNotMatch("(?i)captcha|verify|proxy", reason);
        Assert.Equal(reason[..120], ScrapeFailurePolicy.FleetWideReason(reason)[..120]);
    }

    [Fact]
    public async Task CauDaoBamTruot_DongKhacChenVao_DemLai()
    {
        var x = Proc();
        await Handle(x.P, Miss(), 6);
        await Handle(x.P, Miss(), 7);
        await Handle(x.P, Captured(new(HarvestHubPush.Saved, null, true)), 8);        // dòng bấm trúng cắt chuỗi
        Assert.Equal(HarvestRowNext.Done, (await Handle(x.P, Miss(), 9)).Next);
        Assert.Equal(HarvestRowNext.Done, (await Handle(x.P, Miss(), 10)).Next);
        Assert.Equal(HarvestRowNext.Done, (await Handle(x.P, HarvestStepResult.Failure("hết giờ", 7, Link), 11)).Next);   // lỗi khác cũng cắt
        Assert.Equal(HarvestRowNext.Done, (await Handle(x.P, Miss(), 12)).Next);
    }

    [Fact]
    public async Task AppChuaNoiHub_LoiChoLuu_DungJob()
    {
        var x = Proc();
        var o = await Handle(x.P, HarvestStepResult.Failure(HarvestSinkFailure.Prefix + "app chưa nối Hub — …", 7, Link));
        Assert.Equal(HarvestRowNext.Stop, o.Next);
        Assert.Equal(ScrapeFailureAction.StopJob, Action(x.Cfg));
        Assert.Empty(x.Cfg.SnapshotFailedRows());
    }

    [Fact]
    public async Task LuongCu_KhongExtensionPush_HubPushBiBoQua()
    {
        var x = Proc(extensionPush: false);
        var o = await Handle(x.P, Captured(new(HarvestHubPush.Queued, "x", false)));
        Assert.True(o.Saved);                                                // luồng outbox cũ: sink lưu như trước
        Assert.Single(x.Sink.Saved);
    }

    [Fact]
    public void Payload_CoToken_KhongLoQuaToString()
    {
        var req = Req();
        Assert.DoesNotContain("tok-app", req.Hub.ToString());
        Assert.Contains("tok-app", HarvestClickStep.BuildPayload(req, "t"));   // payload BẮT BUỘC có token (extension cần để gửi)
    }
}
