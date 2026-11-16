using System.Text.Json;
using OpenMultiBraveLauncherV3;
using Shopee.Core.Harvest;
using Shopee.Modules.MultiBrave;

namespace Shopee.Workspace.Tests;

/// <summary>
/// Engine MultiBrave chế độ Harvest (GĐ3): các điểm tham số hoá theo chế độ (extension, probe, cờ Brave, đọc tiến độ),
/// RPC bước harvest không gọi chồng, sổ dòng (sink ném → không tính dòng). Mặc định Scrape phải giữ NGUYÊN hằng cũ.
/// Không đổi <see cref="MultiBraveRuntime.Mode"/> toàn cục (test chạy song song) — dùng các overload nhận chế độ.
/// </summary>
public sealed class HarvestModeEngineTests
{
    // ── Hồ sơ chế độ / probe ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ScrapeProfile_KeepsLegacyConstantsVerbatim()
    {
        var p = RunnerModeProfile.For(RunnerMode.Scrape);
        Assert.Equal("shopee-scrape", p.ExtensionFolder);
        Assert.Equal("Shopee Data Runner", p.ExtensionDisplayName);
        Assert.Equal("hasScrapeStep", p.ProbeFlag);
        // Biểu thức probe cũ (RunnerExtensionRpc trước GĐ3) — từng chữ.
        Assert.Equal(
            "({ hasScrapeStep: typeof globalThis.__launcherExecuteScrapeStep === 'function' && typeof globalThis.__launcherApplyFormConfig === 'function' })",
            RunnerExtensionRpc.BuildServiceWorkerMethodExpression("probe", "null", p));
        Assert.Equal(RunnerMode.Scrape, MultiBraveRuntime.Mode);   // mặc định process
        Assert.Same(RunnerModeProfile.Scrape, RunnerModeProfile.Current);
    }

    [Fact]
    public void HarvestProfile_UsesHarvestExtensionAndProbe()
    {
        var p = RunnerModeProfile.For(RunnerMode.Harvest);
        Assert.Equal("shopee-harvest", p.ExtensionFolder);
        Assert.Equal("Shopee Harvest Runner", p.ExtensionDisplayName);
        Assert.Equal("hasHarvestStep", p.ProbeFlag);
        var probe = RunnerExtensionRpc.BuildServiceWorkerMethodExpression("probe", "null", p);
        Assert.Contains("hasHarvestStep:", probe);
        Assert.Contains("__launcherExecuteHarvestStep", probe);
        Assert.DoesNotContain("hasScrapeStep", probe);
    }

    [Fact]
    public void HarvestProfile_MatchesExtensionManifestAndBackgroundProbe()
    {
        var dir = RunnerExtensionPaths.ResolveLoadDirectory(RunnerMode.Harvest);
        Assert.NotNull(dir);
        Assert.EndsWith(Path.Combine("extensions", "shopee-harvest"), dir!);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!, "manifest.json")));
        Assert.Equal(RunnerModeProfile.Harvest.ExtensionDisplayName, manifest.RootElement.GetProperty("name").GetString());
        var background = File.ReadAllText(Path.Combine(dir!, "background.js"));
        Assert.Contains("hasHarvestStep:", background);                       // probe qua popup trả đúng cờ này
        Assert.Contains("globalThis.__launcherExecuteHarvestStep =", background);

        Assert.EndsWith(Path.Combine("extensions", "shopee-scrape"), RunnerExtensionPaths.ResolveLoadDirectory(RunnerMode.Scrape)!);
        Assert.NotEqual(
            RunnerExtensionPaths.TryGetLoadedExtensionId(RunnerMode.Scrape),
            RunnerExtensionPaths.TryGetLoadedExtensionId(RunnerMode.Harvest));
    }

    private static JsonElement Eval(object value) =>
        JsonSerializer.SerializeToElement(new { result = new { type = "object", value } });

    [Fact]
    public void ReadyProbe_AcceptsOnlyTheModesFlag()
    {
        var harvestSw = Eval(new { hasHarvestStep = true });
        var scrapeSw = Eval(new { hasScrapeStep = true });
        Assert.True(RunnerExtensionRpc.IsReadyProbeResult(harvestSw, RunnerModeProfile.Harvest.ProbeFlag));
        Assert.False(RunnerExtensionRpc.IsReadyProbeResult(scrapeSw, RunnerModeProfile.Harvest.ProbeFlag));
        Assert.True(RunnerExtensionRpc.IsReadyProbeResult(scrapeSw, RunnerModeProfile.Scrape.ProbeFlag));
        Assert.False(RunnerExtensionRpc.IsReadyProbeResult(harvestSw, RunnerModeProfile.Scrape.ProbeFlag));
        Assert.False(RunnerExtensionRpc.IsReadyProbeResult(Eval(new { hasHarvestStep = false }), "hasHarvestStep"));
    }

    // ── RPC bước harvest ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HarvestStepExpressions_CallHarvestHook_PopupHasNoSixSecondRace()
    {
        var sw = RunnerExtensionRpc.BuildServiceWorkerMethodExpression("executeHarvestStep", "P", RunnerModeProfile.Harvest);
        Assert.Equal("(async () => globalThis.__launcherExecuteHarvestStep(P))()", sw);

        var popup = RunnerExtensionRpc.BuildPopupLongInvokeExpression("executeHarvestStep", "P");
        Assert.Contains("method:\"executeHarvestStep\"", popup);
        Assert.DoesNotContain("6000", popup);
        Assert.DoesNotContain("setTimeout", popup);
        Assert.DoesNotContain("Promise.race", popup);
        Assert.True(RunnerExtensionRpc.HarvestStepReceiveTimeout > TimeSpan.FromSeconds(540));   // > trần bước của extension
    }

    [Fact]
    public void HarvestStepPayload_OmitsBlockCodesByDefault_NoProgressKey()
    {
        using var doc = JsonDocument.Parse(RunnerExtensionRpc.BuildHarvestStepPayload("https://shopee.vn/product/1/2", 5, "st", "P1", 9, null));
        var root = doc.RootElement;
        Assert.Equal("https://shopee.vn/product/1/2", root.GetProperty("link").GetString());
        Assert.Equal(5, root.GetProperty("rowNumber").GetInt32());
        Assert.Equal(9, root.GetProperty("tabId").GetInt32());
        Assert.False(root.TryGetProperty("blockErrorCodes", out _));   // để extension dùng mặc định [90309999]
        Assert.False(root.TryGetProperty("lastCompletedRow", out _));

        using var withCodes = JsonDocument.Parse(RunnerExtensionRpc.BuildHarvestStepPayload("l", 1, "", "", null, [90309999L, 1L]));
        Assert.Equal(new[] { 90309999L, 1L }, withCodes.RootElement.GetProperty("blockErrorCodes").EnumerateArray().Select(e => e.GetInt64()));
        Assert.Equal(JsonValueKind.Null, withCodes.RootElement.GetProperty("tabId").ValueKind);
    }

    private static JsonElement EvalException(string description) =>
        JsonSerializer.SerializeToElement(new { exceptionDetails = new { text = "Uncaught", exception = new { description } } });

    [Theory]
    // Chắc chắn CHƯA chạy bước → được gửi lại.
    [InlineData("TypeError: globalThis.__launcherExecuteHarvestStep is not a function", "NotDelivered")]
    [InlineData("Error: Could not establish connection. Receiving end does not exist.", "NotDelivered")]
    [InlineData("Error: POPUP_CONTEXT_DEAD: chrome.runtime không khả dụng", "NotDelivered")]
    [InlineData("Cannot find context with specified id", "NotDelivered")]
    // Có thể bước ĐÃ chạy → KHÔNG gửi lại.
    [InlineData("Target closed", "SentNoAnswer")]
    [InlineData("Error: SW_NO_RESPONSE: service worker không phản hồi sendMessage", "SentNoAnswer")]
    [InlineData("Error: Extension chưa hỗ trợ executeHarvestStep.", "SentNoAnswer")]
    public void ClassifyEvalResult_RetriesOnlyWhenSurelyNotDelivered(string error, string expected) =>
        Assert.Equal(expected, RunnerExtensionRpc.ClassifyHarvestEvalResult(EvalException(error)).Kind.ToString());

    [Fact]
    public void ClassifyEvalResult_ObjectValueAnswered_OtherwiseSentNoAnswer()
    {
        var ok = RunnerExtensionRpc.ClassifyHarvestEvalResult(Eval(new { harvestOk = true, itemId = "1" }));
        Assert.Equal(RunnerExtensionRpc.HarvestDelivery.Answered, ok.Kind);
        Assert.True(HarvestStepResult.Parse(ok.Value, null, null).HarvestOk);

        var undefinedValue = JsonSerializer.SerializeToElement(new { result = new { type = "undefined" } });
        Assert.Equal(RunnerExtensionRpc.HarvestDelivery.SentNoAnswer, RunnerExtensionRpc.ClassifyHarvestEvalResult(undefinedValue).Kind);
    }

    // ── Cờ Brave theo chế độ ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BraveArgs_Scrape_KeepsBigSellerBypassAndRequiresProductCopy()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            BraveProfileManager.BuildBraveArguments(9222, @"C:\p", "http://1.2.3.4:5", null, sourceUserData: null));
        Assert.Contains("Product Copy", ex.Message);

        var src = NewSourceUserData(("pc", "Product Copy"));
        try
        {
            var args = BraveProfileManager.BuildBraveArguments(9222, @"C:\p", "http://1.2.3.4:5", null, src);
            Assert.Contains("--proxy-bypass-list=*.bigseller.com;bigseller.com;*.bigseller.pro;bigseller.pro", args);
            Assert.Contains("shopee-scrape", args);
            Assert.DoesNotContain("shopee-harvest", args);
        }
        finally { Directory.Delete(src, recursive: true); }
    }

    [Fact]
    public void BraveArgs_Harvest_NoBypass_NoProductCopyRequirement_LoadsHarvestExtension()
    {
        var args = BraveProfileManager.BuildBraveArguments(
            9222, @"C:\p", "http://1.2.3.4:5", null, sourceUserData: null, mode: RunnerMode.Harvest);
        Assert.Contains("--proxy-server=http://1.2.3.4:5", args);
        Assert.DoesNotContain("--proxy-bypass-list", args);
        Assert.Contains("shopee-harvest", args);
        Assert.DoesNotContain("shopee-scrape", args);
    }

    [Fact]
    public void UserExtensions_HarvestSkipsProductCopyAndRunners_ScrapeUnchanged()
    {
        var src = NewSourceUserData(("pc", "Product Copy"), ("hv", "Shopee Harvest Runner"), ("ab", "AdBlock"));
        try
        {
            var scrape = BraveProfileManager.CollectUserExtensions(src).Select(e => e.name).OrderBy(n => n).ToList();
            Assert.Equal(new[] { "AdBlock", "Product Copy", "Shopee Harvest Runner" }, scrape);

            var harvest = BraveProfileManager.CollectUserExtensions(src, RunnerMode.Harvest).Select(e => e.name).ToList();
            Assert.Equal(new[] { "AdBlock" }, harvest);
        }
        finally { Directory.Delete(src, recursive: true); }
    }

    private static string NewSourceUserData(params (string id, string name)[] exts)
    {
        var root = Path.Combine(Path.GetTempPath(), "harvest-test-" + Guid.NewGuid().ToString("N"));
        foreach (var (id, name) in exts)
        {
            var dir = Path.Combine(root, "Default", "Extensions", id, "1.0_0");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(new { name, manifest_version = 3 }));
        }
        return root;
    }

    // ── Đọc tiến độ extension chỉ từ extension của chế độ ───────────────────────────────────────────────────────

    [Fact]
    public void ProgressReader_HarvestOnlyReadsHarvestExtensionStorage()
    {
        var root = Path.Combine(Path.GetTempPath(), "harvest-prog-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteState(root, "scrapeextid", 50, DateTime.UtcNow);                    // scrape ghi gần nhất, dòng cao hơn
            WriteState(root, "harvestextid", 10, DateTime.UtcNow.AddMinutes(-5));

            Assert.True(ExtensionProgressReader.TryRead(new DirectoryInfo(root), out var any, onlyExtensionId: null));
            Assert.Equal(50, any.LastCompletedRow);                                 // Scrape: hành vi cũ — chọn bản ghi gần nhất

            Assert.True(ExtensionProgressReader.TryRead(new DirectoryInfo(root), out var only, onlyExtensionId: "harvestextid"));
            Assert.Equal(10, only.LastCompletedRow);

            Assert.False(ExtensionProgressReader.TryRead(new DirectoryInfo(root), out _, onlyExtensionId: ""));   // không phân giải được ID → không đọc
        }
        finally { Directory.Delete(root, recursive: true); }

        Assert.Null(ExtensionProgressReader.OnlyExtensionIdForMode(RunnerMode.Scrape));
        Assert.Equal(RunnerExtensionPaths.TryGetLoadedExtensionId(RunnerMode.Harvest), ExtensionProgressReader.OnlyExtensionIdForMode(RunnerMode.Harvest));
    }

    private static void WriteState(string root, string extId, int lastCompletedRow, DateTime writeUtc)
    {
        var dir = Path.Combine(root, "Default", "Local Extension Settings", extId);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "000003.log");
        var json = JsonSerializer.Serialize(new { sheetName = "Data", startRow = 2, endRow = 60, lastCompletedRow, running = true, phase = "waiting" });
        File.WriteAllText(file, new string('x', 200) + "runnerState" + json);
        File.SetLastWriteTimeUtc(file, writeUtc);
    }

    // ── Cấu hình / runner ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InstanceConfig_ModeDefaultsToScrape_AndIsNotPersisted()
    {
        var cfg = new InstanceConfig();
        Assert.Equal(RunnerMode.Scrape, cfg.Mode);
        cfg.Mode = RunnerMode.Harvest;
        Assert.DoesNotContain("\"Mode\"", JsonSerializer.Serialize(cfg));
    }

    [Fact]
    public void ScrapeRunner_RefusesModeDifferentFromProcess()
    {
        // Process mặc định Scrape → runner Harvest bị chặn (sẽ nạp nhầm extension).
        Assert.Throws<InvalidOperationException>(() =>
            new ScrapeRunner("", "", braveExe: "brave.exe", mode: RunnerMode.Harvest));
        Assert.Equal(RunnerMode.Scrape, new ScrapeRunner("", "", braveExe: "brave.exe").Mode);
    }

    // ── Sổ dòng: sink rồi mới tính dòng ────────────────────────────────────────────────────────────────────────

    private sealed class FakeSink(Exception? fail = null) : IHarvestSink
    {
        public List<HarvestedItem> Saved { get; } = [];
        public Exception? NotReady { get; init; }
        public int EnsureCalls { get; private set; }
        public Task EnsureReadyAsync(CancellationToken cancellationToken)
        {
            EnsureCalls++;
            if (NotReady is not null) throw NotReady;
            return Task.CompletedTask;
        }
        public Task SaveAsync(HarvestedItem item, CancellationToken cancellationToken)
        {
            if (fail is not null) throw fail;
            Saved.Add(item);
            return Task.CompletedTask;
        }
    }

    private static HarvestedItem Item(int row) =>
        new("bs1", "shop1", "Data", row, "https://shopee.vn/product/1/2", "https://shopee.vn/a-i.1.2%7DD", 2, 1, "{}", "hook", DateTimeOffset.Now);

    private static InstanceConfig Cfg() => new() { StartRow = 2, EndRow = 20, NextRunRow = 2, Mode = RunnerMode.Harvest };

    [Fact]
    public async Task RowBook_SinkThrows_RowNotCountedNorSkipped()
    {
        var cfg = Cfg();
        cfg.LastCompletedRow = 4;
        cfg.NextRunRow = 5;
        var book = new HarvestRowBook(cfg);
        book.AddPdpError(5, "77", "pdp");

        var err = await book.SaveAsync(new FakeSink(new IOException("disk full")), Item(6), CancellationToken.None);

        Assert.IsType<IOException>(err);
        Assert.Equal(4, cfg.LastCompletedRow);
        Assert.Equal(5, cfg.NextRunRow);
        Assert.Empty(cfg.SnapshotFailedRows());   // không vào sổ bỏ qua: dòng 5, 6 được làm lại
        Assert.Empty(book.PendingPdpRows);
    }

    [Fact]
    public async Task RowBook_SinkOk_CountsRow_FlushesPendingPdp_RemovesStaleFailure()
    {
        var cfg = Cfg();
        cfg.AddFailedRow(7, "lỗi trước khi đổi IP");
        var book = new HarvestRowBook(cfg);
        book.AddPdpError(5, "77", "pdp 5");
        book.AddPdpError(6, "77", "pdp 6");
        var sink = new FakeSink();

        Assert.Null(await book.SaveAsync(sink, Item(7), CancellationToken.None));

        Assert.Single(sink.Saved);
        Assert.Equal(7, cfg.LastCompletedRow);
        Assert.Equal(8, cfg.NextRunRow);
        Assert.Equal(new[] { 5, 6 }, cfg.SnapshotFailedRows().Select(x => x.Row).OrderBy(x => x));   // 7 đã gỡ
    }

    [Fact]
    public async Task RowBook_UserCancelDuringSave_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cfg = Cfg();
        var book = new HarvestRowBook(cfg);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            book.SaveAsync(new FakeSink(new OperationCanceledException()), Item(3), cts.Token));
        Assert.Null(cfg.LastCompletedRow);
    }

    [Fact]
    public void RowBook_BreakerDiscardsPending_RowFailedFlushes()
    {
        var cfg = Cfg();
        var book = new HarvestRowBook(cfg);
        book.AddPdpError(3, "77", "a");
        book.AddPdpError(4, "77", "b");
        book.DiscardPendingPdpRows();                 // cầu dao bật → các dòng được làm lại, không ghi sổ
        Assert.Empty(cfg.SnapshotFailedRows());

        book.AddPdpError(5, "77", "c");
        book.RowFailed(6, "notProduct");              // chuỗi bị cắt → dòng pdpError là lỗi thật
        Assert.Equal(new[] { 5, 6 }, cfg.SnapshotFailedRows().Select(x => x.Row));
    }

    [Fact]
    public void RowBook_PendingOfDifferentCode_IsFlushedAsRealRowError()
    {
        var cfg = Cfg();
        var book = new HarvestRowBook(cfg);
        book.AddPdpError(3, "1", "a");
        book.AddPdpError(4, "1", "b");
        book.AddPdpError(5, "2", "c");                // chuỗi mã 1 bị cắt → 3, 4 là lỗi dòng thật
        Assert.Equal(new[] { 3, 4 }, cfg.SnapshotFailedRows().Select(x => x.Row));
        Assert.Equal(new[] { 5 }, book.PendingPdpRows);
    }

    // ── Bộ xử lý kết quả bước (nối dây vòng Harvest) với host + sink giả ───────────────────────────────────────

    private sealed class FakeHost : IHarvestRowHost
    {
        public bool CdpReachable { get; set; } = true;
        public RunnerExtensionRpc.BeforeNextLinkCheckResult Captcha { get; set; } = new(false, true, false, false, null, 1, null);
        public int Reports { get; private set; }
        public int CaptchaChecks { get; private set; }
        public Exception? ReportFails { get; set; }

        public Task ReportAsync(int? tabId, CancellationToken cancellationToken)
        {
            Reports++;
            if (ReportFails is not null) throw ReportFails;
            return Task.CompletedTask;
        }
        public void Progress() { }
        public Task<bool> IsCdpReachableAsync(CancellationToken cancellationToken) => Task.FromResult(CdpReachable);
        public Task<RunnerExtensionRpc.BeforeNextLinkCheckResult> CheckCaptchaAsync(int tabId, int rowNumber, string sku, CancellationToken cancellationToken)
        {
            CaptchaChecks++;
            return Task.FromResult(Captcha);
        }
    }

    private static HarvestStepResult Step(
        bool ok = false, bool captcha = false, bool traffic = false, bool proxy = false, bool superseded = false,
        bool notProduct = false, string? pdpError = null, string? message = "m", long? capturedAt = null, int? tab = 5) =>
        new(ok, captcha, proxy, traffic, false, notProduct, superseded, superseded, message, tab, "u",
            ok ? 26393229156L : (long?)null, ok ? 1490438686L : (long?)null, ok ? "{\"error\":null}" : null, ok ? "hook" : null, pdpError,
            null, capturedAt);

    private static (HarvestRowProcessor P, InstanceConfig Cfg, FakeSink Sink, FakeHost Host, List<string> Log) Proc(
        Exception? sinkFails = null, int threshold = 3, HarvestPdpBreaker? breaker = null)
    {
        var cfg = Cfg();
        cfg.HubAccountId = "bs1";
        var sink = new FakeSink(sinkFails);
        var log = new List<string>();
        var p = new HarvestRowProcessor(cfg, new HarvestRunOptions(sink, "shop1", threshold), breaker ?? new HarvestPdpBreaker(threshold), log.Add);
        return (p, cfg, sink, new FakeHost(), log);
    }

    private static readonly DateTimeOffset Received = new(2026, 11, 16, 9, 0, 0, TimeSpan.FromHours(7));

    private static HarvestRowInput Row(int row, int? tab = 5) =>
        new(row, "Data", "https://shopee.vn/Ao-i.1490438686.26393229156%7DD", "https://shopee.vn/product/1490438686/26393229156", "SKU" + row, tab, Received);

    private static Task<HarvestRowOutcome> Handle((HarvestRowProcessor P, InstanceConfig Cfg, FakeSink Sink, FakeHost Host, List<string> Log) x, HarvestStepResult step, int row) =>
        x.P.HandleAsync(step, Row(row), x.Host, CancellationToken.None);

    [Fact]
    public async Task Processor_Save_BuildsItemWithSourceLinkAndCapturedAt_ThenCountsRow()
    {
        var x = Proc();
        var captured = Received.AddSeconds(-30);
        var o = await Handle(x, Step(ok: true, capturedAt: captured.ToUnixTimeMilliseconds()), 7);

        Assert.Equal((HarvestRowNext.Done, true), (o.Next, o.Saved));
        var item = Assert.Single(x.Sink.Saved);
        Assert.Equal(("bs1", "shop1", "Data", 7), (item.AccountId, item.ShopId, item.Sheet, item.RowNo));
        Assert.Equal("https://shopee.vn/product/1490438686/26393229156", item.Url);                 // link đã làm sạch
        Assert.Equal("https://shopee.vn/Ao-i.1490438686.26393229156%7DD", item.SourceLink);          // link gốc
        Assert.Equal((26393229156L, (long?)1490438686L, "hook"), (item.ItemId, item.ShopeeShopId, item.Source));
        Assert.Equal(captured, item.HarvestedAt);
        Assert.Equal(7, x.Cfg.LastCompletedRow);
        Assert.Equal(8, x.Cfg.NextRunRow);

        var y = Proc();
        await Handle(y, Step(ok: true), 7);
        Assert.Equal(Received, Assert.Single(y.Sink.Saved).HarvestedAt);   // không có capturedAt → giờ nhận kết quả
    }

    [Fact]
    public async Task Processor_SinkThrows_StopsJobAsInfraFailure_RowNotCounted()
    {
        var x = Proc(new IOException("disk full"));
        x.Cfg.LastCompletedRow = 6;
        x.Cfg.NextRunRow = 7;

        var o = await Handle(x, Step(ok: true), 7);

        Assert.Equal(HarvestRowNext.Stop, o.Next);
        Assert.False(o.Saved);
        Assert.Equal(6, x.Cfg.LastCompletedRow);
        Assert.Equal(7, x.Cfg.NextRunRow);
        Assert.Empty(x.Cfg.SnapshotFailedRows());
        Assert.Equal("error", x.Cfg.RunnerPhase);
        Assert.True(Shopee.Core.Harvest.HarvestSinkFailure.Is(x.Cfg.LastRunnerMessage));

        // ScrapeRunner: thông điệp này → chunk lỗi KHÔNG captcha → StopJob (nhả tk nguyên vẹn, không cooldown/kẹt/bỏ dòng).
        x.Cfg.LastRunnerMessage += " captcha verify proxy";   // chữ bất kỳ trong lỗi đĩa không được lái sang luật khác
        var chunk = ScrapeRunner.Classify(x.Cfg);
        Assert.True(chunk.Errored);
        Assert.False(chunk.IsCaptcha);
        Assert.False(chunk.NeedLogin);
        Assert.Equal(ScrapeFailureAction.StopJob, ScrapeFailurePolicy.Decide(chunk.Errored, chunk.IsCaptcha, chunk.Reason));
        Assert.Equal(chunk.Reason.Trim(), ScrapeFailurePolicy.FleetWideReason(chunk.Reason));
    }

    [Theory]
    [InlineData("error", "Lỗi proxy khi tải", ScrapeFailureAction.Cooldown)]
    [InlineData("paused", "🚫 Captcha dòng 3", ScrapeFailureAction.Quarantine)]
    [InlineData("error", "KEY_EXPIRED: key hết hạn", ScrapeFailureAction.StopJob)]
    [InlineData("finished", "Hoàn tất", ScrapeFailureAction.None)]
    public void Classify_ScrapeMessagesUnchanged(string phase, string message, ScrapeFailureAction expected)
    {
        var cfg = new InstanceConfig { RunnerPhase = phase, LastRunnerMessage = message };
        var chunk = ScrapeRunner.Classify(cfg);
        Assert.Equal(expected, ScrapeFailurePolicy.Decide(chunk.Errored, chunk.IsCaptcha, chunk.Reason));
    }

    [Fact]
    public async Task Processor_Superseded_RetriesOnceWithoutRecording_ThenStopsWithoutRecording()
    {
        var x = Proc();
        var first = await Handle(x, Step(superseded: true), 4);
        Assert.Equal(HarvestRowNext.RetryRow, first.Next);
        Assert.Empty(x.Cfg.SnapshotFailedRows());

        var second = await Handle(x, Step(superseded: true), 4);
        Assert.Equal(HarvestRowNext.Stop, second.Next);
        Assert.Equal("error", x.Cfg.RunnerPhase);
        Assert.Empty(x.Cfg.SnapshotFailedRows());
        Assert.Null(x.Cfg.LastCompletedRow);
        Assert.Empty(x.Sink.Saved);
    }

    [Fact]
    public async Task Processor_BreakerTrip_DiscardsPendingPdpRows_TrafficPath()
    {
        var x = Proc(threshold: 3);
        Assert.Equal(HarvestRowNext.Done, (await Handle(x, Step(pdpError: "77"), 3)).Next);
        Assert.Equal(HarvestRowNext.Done, (await Handle(x, Step(pdpError: "77"), 4)).Next);
        Assert.Empty(x.Cfg.SnapshotFailedRows());            // hoãn ghi
        var trip = await Handle(x, Step(pdpError: "77"), 5);

        Assert.Equal(HarvestRowNext.Stop, trip.Next);
        Assert.Equal(HarvestStepAction.PdpBreakerTrip, trip.Action);
        Assert.Equal("traffic-recheck", x.Cfg.RunnerPhase);  // lần đầu: đổi IP
        Assert.Empty(x.Cfg.SnapshotFailedRows());            // 3, 4, 5 làm lại sau khi đổi IP, không vào sổ bỏ qua
        Assert.Empty(x.P.Book.PendingPdpRows);
    }

    [Theory]
    [InlineData("traffic")]
    [InlineData("proxy")]
    public async Task Processor_TrafficOrProxy_DiscardsPendingPdpRows(string kind)
    {
        var x = Proc();
        await Handle(x, Step(pdpError: "77"), 3);
        var o = await Handle(x, kind == "traffic" ? Step(traffic: true) : Step(proxy: true), 4);
        Assert.Equal(HarvestRowNext.Stop, o.Next);
        Assert.Empty(x.Cfg.SnapshotFailedRows());
    }

    [Fact]
    public async Task Processor_CaptchaUnresolved_DiscardsPendingAndStops_SolvedRetriesRow()
    {
        var x = Proc();
        await Handle(x, Step(pdpError: "77"), 3);
        var o = await Handle(x, Step(captcha: true), 4);
        Assert.Equal(HarvestRowNext.Stop, o.Next);
        Assert.Equal(1, x.Host.CaptchaChecks);
        Assert.Empty(x.Cfg.SnapshotFailedRows());
        Assert.True(x.Cfg.CaptchaError);

        var y = Proc();
        y.Host.Captcha = new(true, false, false, true, null, 9, null);
        var solved = await Handle(y, Step(captcha: true), 4);
        Assert.Equal((HarvestRowNext.RetryRow, (int?)9), (solved.Next, solved.TabId));
        Assert.False(y.Cfg.CaptchaError);
        var again = await Handle(y, Step(captcha: true), 4);   // captcha lại đúng dòng đó → không chờ lần 2
        Assert.Equal(HarvestRowNext.Stop, again.Next);
        Assert.Equal(1, y.Host.CaptchaChecks);
    }

    [Fact]
    public async Task Processor_CompleteBlock_FlushesPendingPdpRows()
    {
        var x = Proc();
        await Handle(x, Step(pdpError: "77"), 3);
        await Handle(x, Step(pdpError: "77"), 4);
        x.P.CompleteBlock(2, "Data");
        Assert.Equal(new[] { 3, 4 }, x.Cfg.SnapshotFailedRows().Select(r => r.Row));
        Assert.Equal("finished", x.Cfg.RunnerPhase);
    }

    [Fact]
    public async Task Processor_TrippedCodeAfterIpChange_IsRowError_NotAnotherTrip()
    {
        var breaker = new HarvestPdpBreaker(2);
        var first = Proc(threshold: 2, breaker: breaker);
        await Handle(first, Step(pdpError: "9"), 3);
        Assert.Equal(HarvestStepAction.PdpBreakerTrip, (await Handle(first, Step(pdpError: "9"), 4)).Action);

        // Lượt mới (cùng phiên, sau khi đổi IP) — cầu dao truyền lại.
        var second = Proc(threshold: 2, breaker: breaker);
        var a = await Handle(second, Step(pdpError: "9"), 3);
        var b = await Handle(second, Step(pdpError: "9"), 4);
        Assert.Equal((HarvestRowNext.Done, HarvestStepAction.RowFailed), (a.Next, a.Action));
        Assert.Equal((HarvestRowNext.Done, HarvestStepAction.RowFailed), (b.Next, b.Action));
        Assert.Equal(new[] { 3, 4 }, second.Cfg.SnapshotFailedRows().Select(r => r.Row));
    }

    [Fact]
    public async Task Processor_RowFailed_RecordsRow_NoSwOrCdpDownThrowsForRelaunch()
    {
        var x = Proc();
        var o = await Handle(x, Step(notProduct: true, message: "không phải trang SP"), 3);
        Assert.Equal(HarvestRowNext.Done, o.Next);
        Assert.Equal(new[] { 3 }, x.Cfg.SnapshotFailedRows().Select(r => r.Row));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Handle(Proc(), Step(message: "No SW: bước harvest không gửi được"), 3));
        var down = Proc();
        down.Host.CdpReachable = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handle(down, Step(message: "Bước harvest quá 600s"), 3));
    }

    // ── RPC: thứ tự attach → đã gửi ─────────────────────────────────────────────────────────────────────────────

    private static readonly JsonElement AnsweredEval =
        JsonSerializer.SerializeToElement(new { result = new { type = "object", value = new { harvestOk = true } } });

    [Fact]
    public async Task SendTracked_AttachFailure_IsNotDelivered_EvaluateNeverCalled()
    {
        var evaluated = false;
        var throwing = await RunnerExtensionRpc.SendTrackedAsync(
            () => throw new InvalidOperationException("attach boom"),
            _ => Task.CompletedTask,
            _ => { evaluated = true; return Task.FromResult(AnsweredEval); },
            CancellationToken.None);
        Assert.Equal("NotDelivered", throwing.Kind.ToString());

        var empty = await RunnerExtensionRpc.SendTrackedAsync(
            () => Task.FromResult<string?>(null),
            _ => Task.CompletedTask,
            _ => { evaluated = true; return Task.FromResult(AnsweredEval); },
            CancellationToken.None);
        Assert.Equal("NotDelivered", empty.Kind.ToString());
        Assert.False(evaluated);
    }

    [Fact]
    public async Task SendTracked_OrderAttachEnableEvaluate_EnableErrorIgnored_EvaluateErrorIsSent()
    {
        var calls = new List<string>();
        var ok = await RunnerExtensionRpc.SendTrackedAsync(
            () => { calls.Add("attach"); return Task.FromResult<string?>("S1"); },
            sess => { calls.Add("enable:" + sess); throw new InvalidOperationException("enable boom"); },
            sess => { calls.Add("evaluate:" + sess); return Task.FromResult(AnsweredEval); },
            CancellationToken.None);
        Assert.Equal(new[] { "attach", "enable:S1", "evaluate:S1" }, calls);
        Assert.Equal("Answered", ok.Kind.ToString());

        var lost = await RunnerExtensionRpc.SendTrackedAsync(
            () => Task.FromResult<string?>("S1"),
            _ => Task.CompletedTask,
            _ => throw new TimeoutException("socket closed"),
            CancellationToken.None);
        Assert.Equal("SentNoAnswer", lost.Kind.ToString());     // có thể bước đã chạy → không gửi lại

        var timedOut = await RunnerExtensionRpc.SendTrackedAsync(
            () => Task.FromResult<string?>("S1"),
            _ => Task.CompletedTask,
            _ => throw new OperationCanceledException(),        // receive-timeout CDP (không phải người dùng huỷ)
            CancellationToken.None);
        Assert.Equal("SentNoAnswer", timedOut.Kind.ToString());
    }

    [Fact]
    public async Task SendTracked_UserCancel_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => RunnerExtensionRpc.SendTrackedAsync(
            () => Task.FromResult<string?>("S1"),
            _ => Task.CompletedTask,
            _ => throw new OperationCanceledException(cts.Token),
            cts.Token));
    }

    [Fact]
    public async Task Processor_StopReportFails_KeepsStopReasonAndPhase()
    {
        var x = Proc(new IOException("disk full"));
        var o = await Handle(x, Step(ok: true), 7);
        Assert.Equal(HarvestRowNext.Stop, o.Next);

        x.Host.ReportFails = new InvalidOperationException("No SW: popup chết");
        await x.P.ReportStopAsync(o, x.Host, CancellationToken.None);   // không ném

        Assert.Equal("error", x.Cfg.RunnerPhase);
        Assert.True(Shopee.Core.Harvest.HarvestSinkFailure.Is(x.Cfg.LastRunnerMessage));
        Assert.Equal(x.Cfg.LastRunnerMessage, x.Log[^1]);                 // lý do dừng vẫn được log

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        x.Host.ReportFails = new OperationCanceledException(cts.Token);
        await Assert.ThrowsAsync<OperationCanceledException>(() => x.P.ReportStopAsync(o, x.Host, cts.Token));
    }

    [Fact]
    public void FleetWideReason_SinkShortened_ScrapeStringsUnchanged()
    {
        var sink = Shopee.Core.Harvest.HarvestSinkFailure.Message(4, new IOException("line1\r\nline2 " + new string('y', 400)));
        var r = ScrapeFailurePolicy.FleetWideReason(sink);
        Assert.True(r.Length <= 120);
        Assert.DoesNotContain('\n', r);
        Assert.StartsWith(Shopee.Core.Harvest.HarvestSinkFailure.Prefix, r);

        Assert.Equal("key proxy hết hạn (KEY_EXPIRED) — gia hạn key rồi chạy lại", ScrapeFailurePolicy.FleetWideReason("KEY_EXPIRED blah"));
        Assert.Equal("key proxy không tồn tại (KEY_NOT_FOUND) — sửa key rồi chạy lại", ScrapeFailurePolicy.FleetWideReason("KEY_NOT_FOUND"));
        Assert.Equal("key proxy KiotProxy không dùng được — kiểm tra key rồi chạy lại", ScrapeFailurePolicy.FleetWideReason("whatever"));
    }

    // ── RPC: lỗi giao thức CDP trả cho chính lệnh evaluate ──────────────────────────────────────────────────────

    private static JsonElement ProtocolError(string message) =>
        JsonSerializer.SerializeToElement(new { id = 41, error = new { code = -32001, message } });

    [Theory]
    [InlineData("Session with given id not found.")]
    [InlineData("No target with given id found")]
    [InlineData("Target closed")]
    public async Task SendTracked_CdpRejectsEvaluate_IsNotDelivered(string error)
    {
        var o = await RunnerExtensionRpc.SendTrackedAsync(
            () => Task.FromResult<string?>("S1"), _ => Task.CompletedTask, _ => Task.FromResult(ProtocolError(error)),
            CancellationToken.None);
        Assert.Equal("NotDelivered", o.Kind.ToString());
    }

    [Fact]
    public async Task SendTracked_OtherProtocolError_OrSocketDropWhileWaiting_IsSentNoAnswer()
    {
        var other = await RunnerExtensionRpc.SendTrackedAsync(
            () => Task.FromResult<string?>("S1"), _ => Task.CompletedTask, _ => Task.FromResult(ProtocolError("Internal error")),
            CancellationToken.None);
        Assert.Equal("SentNoAnswer", other.Kind.ToString());

        // Socket đứt / target đóng TRONG LÚC chờ: CdpSession huỷ pending → ném (không có gói lỗi cho lệnh) → đã gửi.
        var dropped = await RunnerExtensionRpc.SendTrackedAsync(
            () => Task.FromResult<string?>("S1"), _ => Task.CompletedTask, _ => throw new TaskCanceledException(),
            CancellationToken.None);
        Assert.Equal("SentNoAnswer", dropped.Kind.ToString());
        var closedWhileWaiting = await RunnerExtensionRpc.SendTrackedAsync(
            () => Task.FromResult<string?>("S1"), _ => Task.CompletedTask, _ => throw new InvalidOperationException("Target closed"),
            CancellationToken.None);
        Assert.Equal("SentNoAnswer", closedWhileWaiting.Kind.ToString());
    }

    // ── Kiểm sink trước khi mở Brave ────────────────────────────────────────────────────────────────────────────

    private sealed class CountingPool : IScrapeAccountPool
    {
        public int Borrows { get; private set; }
        public Task<ScrapeAccountSpec?> BorrowAsync(CancellationToken ct) { Borrows++; return Task.FromResult<ScrapeAccountSpec?>(null); }
        public void Release(ScrapeAccountSpec spec) { }
        public AccountCooldown Cooldown(ScrapeAccountSpec spec) => default;
        public void Quarantine(ScrapeAccountSpec spec) { }
    }

    [Fact]
    public async Task RunAuto_SinkNotReady_StopsJobBeforeAnyWorker()
    {
        MultiBraveRuntime.SetMode(RunnerMode.Harvest);
        try
        {
            var sink = new FakeSink { NotReady = new IOException("database is locked") };
            var runner = new ScrapeRunner("", "", braveExe: "brave.exe", mode: RunnerMode.Harvest)
            {
                Harvest = new HarvestRunOptions(sink, "shop1"),
            };
            string? fatal = null;
            var slots = new List<string>();
            runner.JobFatal += r => fatal = r;
            runner.InstanceStatus += (key, _) => slots.Add(key);
            runner.SlotAssigned += (key, _, _) => slots.Add(key);
            var pool = new CountingPool();

            await runner.RunAutoAsync(pool, 3, [(2, 30)], 5, null, CancellationToken.None);

            Assert.Equal(1, sink.EnsureCalls);
            Assert.Equal(0, pool.Borrows);        // không mượn tài khoản → không worker nào chạy, không mở Brave
            Assert.Empty(slots);
            Assert.NotNull(fatal);
            Assert.True(Shopee.Core.Harvest.HarvestSinkFailure.Is(fatal));
            Assert.Contains("database is locked", fatal);
        }
        finally
        {
            MultiBraveRuntime.SetMode(RunnerMode.Scrape);
        }
    }

    // ── FetchShopeeApiJsonAsync chỉ dùng ở Scrape ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task FetchShopeeApiJson_ThrowsInHarvestMode()
    {
        // Đổi chế độ process tạm thời: các test khác của lớp này chạy tuần tự (cùng lớp), lớp khác không đọc giá trị này.
        MultiBraveRuntime.SetMode(RunnerMode.Harvest);
        try
        {
            var runner = new ScrapeRunner("", "", braveExe: "brave.exe", mode: RunnerMode.Harvest);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.FetchShopeeApiJsonAsync(
                new ScrapeAccountSpec("a", "a", "", false, "", "", "", "", false, "Data"),
                ["https://shopee.vn/product/1/2"], _ => { }, (_, _) => Task.CompletedTask, CancellationToken.None));
            Assert.Contains("chỉ dùng ở chế độ Scrape", ex.Message);
        }
        finally
        {
            MultiBraveRuntime.SetMode(RunnerMode.Scrape);
        }
    }
}
