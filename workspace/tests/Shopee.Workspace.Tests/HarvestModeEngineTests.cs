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
        /// <summary>Kết quả bước của link mẫu (kiểm chứng phiên); null = ném lỗi.</summary>
        public Func<string, HarvestStepResult?> Sample { get; set; } = _ => null;
        public List<string> Probes { get; } = [];
        public Task<HarvestStepResult> ProbeSampleAsync(string link, int rowNumber, int? tabId, CancellationToken cancellationToken)
        {
            Probes.Add(link);
            return Sample(link) is { } r ? Task.FromResult(r) : throw new InvalidOperationException("mẫu hỏng");
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
    public async Task Processor_SinkRejectsItem_RowFailedAndContinues_NotStopJob()
    {
        var x = Proc(new HarvestItemRejectedException("JSON PDP không xử lý được: depth 80"));
        x.Cfg.LastCompletedRow = 6;
        x.Cfg.NextRunRow = 7;
        x.P.Book.AddPdpError(5, "77", "pdp 5");   // chuỗi pdpError đang chờ: lỗi dòng khác cắt chuỗi → ghi

        var o = await Handle(x, Step(ok: true), 7);

        Assert.Equal((HarvestRowNext.Done, false), (o.Next, o.Saved));   // sang dòng kế, không Stop
        Assert.NotEqual("error", x.Cfg.RunnerPhase);
        Assert.False(Shopee.Core.Harvest.HarvestSinkFailure.Is(x.Cfg.LastRunnerMessage));
        var failed = x.Cfg.SnapshotFailedRows();
        Assert.Equal(new[] { 5, 7 }, failed.Select(r => r.Row).OrderBy(r => r));
        Assert.Contains(failed, r => r.Row == 7 && r.Reason.Contains("không xử lý được"));
        Assert.Equal(6, x.Cfg.LastCompletedRow);                           // sổ "đã lưu" không đụng
        Assert.Contains(x.Log, l => l.Contains("Dòng 7"));
        Assert.Equal(ScrapeFailureAction.None,
            ScrapeFailurePolicy.Decide(ScrapeRunner.Classify(x.Cfg).Errored, false, ScrapeRunner.Classify(x.Cfg).Reason));
    }

    private sealed class FakeFailureSink : IHarvestFailureSink
    {
        public List<(string Target, string[] Keys, string Reason)> Calls { get; } = [];
        public Exception? Fail { get; set; }
        public string? LastRunId { get; private set; }
        public Task RecordPermanentFailureAsync(IReadOnlyList<string> itemKeys, string reason, string runId, CancellationToken cancellationToken)
        {
            if (Fail is not null) throw Fail;
            LastRunId = runId;
            Calls.Add(("*", itemKeys.ToArray(), reason));   // GĐ6: tập lỗi toàn cục, không còn target
            return Task.CompletedTask;
        }
    }

    private static (HarvestRowProcessor P, InstanceConfig Cfg, FakeSink Sink, FakeHost Host, List<string> Log) ProcF(
        FakeFailureSink failures, Exception? sinkFails = null, HarvestPdpBreaker? breaker = null, Func<string?>? sampleLink = null)
    {
        var cfg = Cfg();
        cfg.HubAccountId = "bs1";
        var sink = new FakeSink(sinkFails);
        var log = new List<string>();
        var p = new HarvestRowProcessor(cfg, new HarvestRunOptions(sink, "shop1", 3, RecordDone: true, FailureSink: failures,
                SampleLink: sampleLink),
            breaker ?? new HarvestPdpBreaker(3), log.Add);
        return (p, cfg, sink, new FakeHost(), log);
    }

    /// <summary>Bảng phân loại: notProduct / JSON bị từ chối vì nội dung / mã pdpError đã xác nhận SP chết → VĨNH VIỄN (ghi tập
    /// lỗi); hết giờ / thiếu JSON / pdpError chưa xác nhận / captcha / proxy / traffic / lỗi chỗ lưu → TẠM (không ghi).</summary>
    [Fact]
    public async Task Processor_LoiVinhVien_GhiTapLoi_LoiTam_KhongGhi()
    {
        var f = new FakeFailureSink();
        var x = ProcF(f);
        await Handle(x, Step(notProduct: true, message: "không phải trang SP"), 3);
        Assert.Empty(f.Calls);                                                               // notProduct: chờ xem có phải chuỗi
        await Handle(x, Step(message: "hết giờ chờ get_pc"), 4);                            // tạm — cắt chuỗi → dòng 3 ghi
        Assert.Equal(("*", "id:1490438686.26393229156"), (f.Calls[0].Target, Assert.Single(f.Calls[0].Keys)));
        Assert.False(string.IsNullOrEmpty(f.LastRunId));

        await Handle(x, Step(pdpError: "90309999", message: "pdp"), 5);                     // pdpError chưa xác nhận → tạm
        await Handle(x, Step(captcha: true), 6);                                             // tạm
        await Handle(x, Step(proxy: true), 7);
        await Handle(x, Step(traffic: true), 8);
        Assert.Single(f.Calls);

        var rejected = new FakeFailureSink();
        var y = ProcF(rejected, new HarvestItemRejectedException("JSON PDP không xử lý được"));
        await Handle(y, Step(ok: true), 9);                                                  // JSON bị từ chối vì nội dung
        Assert.Contains("id:1490438686.26393229156", Assert.Single(rejected.Calls).Keys);

        var infra = new FakeFailureSink();
        var z = ProcF(infra, new IOException("disk full"));
        await Handle(z, Step(ok: true), 10);                                                 // lỗi chỗ lưu → tạm
        Assert.Empty(infra.Calls);

        // Mã pdpError đã bật cầu dao rồi tái diễn trước khi có dòng lưu được → SP chết → vĩnh viễn.
        var dead = new FakeFailureSink();
        var breaker = new HarvestPdpBreaker(1);
        var w = ProcF(dead, breaker: breaker);
        await Handle(w, Step(pdpError: "4", message: "pdp"), 11);                            // bật cầu dao (tạm)
        Assert.Empty(dead.Calls);
        await Handle(w, Step(pdpError: "4", message: "pdp"), 12);                            // tái diễn → SP chết
        Assert.Single(dead.Calls);
    }

    [Fact]
    public async Task Processor_GhiTapLoiHong_ChiLog_DongVanLaLoiDongThuong()
    {
        var f = new FakeFailureSink { Fail = new IOException("db khoá") };
        var x = ProcF(f);
        await Handle(x, Step(notProduct: true), 3);
        var o = await Handle(x, Step(ok: true), 4);                                          // cắt chuỗi → ghi dòng 3 (hỏng)
        Assert.Equal(HarvestRowNext.Done, o.Next);
        Assert.Contains(x.Log, l => l.Contains("không ghi được vào danh sách lỗi vĩnh viễn"));
        Assert.Contains(x.Cfg.SnapshotFailedRows(), r => r.Row == 3);
    }

    /// <summary>Phản biện vòng 13: tài khoản bị đăng xuất → trang /buyer/login (extension báo loginRequired) — KHÔNG phải SP lỗi:
    /// dừng khối, dòng không vào sổ lỗi / tập lỗi, tài khoản đi đường lỗi tạm (Cooldown), không dừng cả job.</summary>
    [Fact]
    public async Task Processor_MatDangNhap_DungKhoi_KhongGhiLoi_TaiKhoanNghi()
    {
        var f = new FakeFailureSink();
        var x = ProcF(f);
        await Handle(x, Step(notProduct: true), 3);                                          // đang chờ trong chuỗi
        var login = Step(message: "login") with { LoginRequired = true };
        var o = await Handle(x, login, 4);

        Assert.Equal(HarvestRowNext.Stop, o.Next);
        Assert.Equal(HarvestStepAction.LoginRequired, o.Action);
        Assert.Empty(f.Calls);
        Assert.Empty(x.Cfg.SnapshotFailedRows());
        Assert.Empty(x.P.PendingNotProductRows);
        Assert.Contains("mất đăng nhập", x.Cfg.LastRunnerMessage);
        var chunk = ScrapeRunner.Classify(x.Cfg);
        Assert.Equal(ScrapeFailureAction.Cooldown, ScrapeFailurePolicy.Decide(chunk.Errored, chunk.IsCaptcha, chunk.Reason));
        // Extension trả cờ loginRequired → Parse đọc được.
        using var doc = JsonDocument.Parse("{\"loginRequired\":true,\"notProduct\":false,\"message\":\"m\"}");
        Assert.True(HarvestStepResult.Parse(doc.RootElement, 1, "u").LoginRequired);
    }

    [Fact]
    public async Task Processor_ChuoiNotProductLienTiep_KhongCoMau_ChayTiep_KhongGhiLoiCaChuoi()
    {
        var f = new FakeFailureSink();
        var x = ProcF(f);
        await Handle(x, Step(notProduct: true), 3);
        await Handle(x, Step(notProduct: true), 4);
        var o = await Handle(x, Step(notProduct: true), 5);
        Assert.Equal(HarvestRowNext.Done, o.Next);                                           // không có mẫu → KHÔNG dừng khối
        Assert.Empty(x.Host.Probes);
        Assert.Empty(f.Calls);                                                               // KHÔNG vào tập lỗi vĩnh viễn
        Assert.Equal(new[] { 3, 4, 5 }, x.Cfg.SnapshotFailedRows().Select(r => r.Row));      // sổ bỏ qua của LƯỢT (lỗi tạm)
        Assert.All(x.Cfg.SnapshotFailedRows(), r => Assert.Equal(HarvestRowProcessor.UnresolvedReason, r.Reason));
        Assert.Equal(5, x.Cfg.LastCompletedRow);                                             // tính là đã duyệt
        Assert.Single(x.Log, l => l.Contains("Chưa có link mẫu để kiểm phiên"));
        await Handle(x, Step(notProduct: true), 6);                                          // phần còn lại của chuỗi: không log lại
        Assert.Single(x.Log, l => l.Contains("Chưa có link mẫu để kiểm phiên"));
        Assert.Contains(x.Cfg.SnapshotFailedRows(), r => r.Row == 6);
        Assert.Equal(6, x.Cfg.LastCompletedRow);
        Assert.Empty(f.Calls);
        await Handle(x, Step(ok: true), 7);                                                  // lưu được → có mẫu cho chuỗi sau
        x.Host.Sample = _ => Step(ok: true);
        for (var r = 8; r <= 10; r++) await Handle(x, Step(notProduct: true), r);
        Assert.Single(x.Host.Probes);
        Assert.Equal(3, f.Calls.Count);

        // Chuỗi chưa tới ngưỡng rồi bị cắt bằng một dòng lưu được → 2 dòng là lỗi thật; hết khối cũng xả.
        var g = new FakeFailureSink();
        var y = ProcF(g);
        await Handle(y, Step(notProduct: true), 3);
        await Handle(y, Step(notProduct: true), 4);
        await Handle(y, Step(ok: true), 5);
        Assert.Equal(2, g.Calls.Count);
        Assert.Equal(new[] { 3, 4 }, y.Cfg.SnapshotFailedRows().Select(r => r.Row).OrderBy(r => r));
        await Handle(y, Step(notProduct: true), 6);
        await y.P.FlushPendingNotProductAsync(CancellationToken.None);
        Assert.Equal(3, g.Calls.Count);
    }

    // ── Kiểm chứng phiên bằng link mẫu khi cầu dao notProduct nhảy ──

    private static readonly HarvestStepResult SampleOk = Step(ok: true);

    /// <summary>Link chết thật đứng liền nhau (shop nguồn bị khoá): link mẫu (dòng vừa lưu được) lấy được JSON → tài khoản ổn →
    /// chuỗi là lỗi thật (ghi sổ + tập lỗi), khối chạy tiếp, bộ đếm chuỗi về 0. Link mẫu không tính tiến độ / số đếm.</summary>
    [Fact]
    public async Task KiemChungPhien_MauSong_GhiChuoi_ChayTiep()
    {
        var f = new FakeFailureSink();
        var x = ProcF(f);
        x.Host.Sample = _ => SampleOk;
        await Handle(x, Step(ok: true), 2);                                                   // dòng 2 lưu được → link mẫu
        var savedBefore = x.Sink.Saved.Count;
        var lastDone = x.Cfg.LastCompletedRow;
        await Handle(x, Step(notProduct: true), 3);
        await Handle(x, Step(notProduct: true), 4);
        var o = await Handle(x, Step(notProduct: true), 5);

        Assert.Equal(HarvestRowNext.Done, o.Next);
        Assert.Equal(["https://shopee.vn/product/1490438686/26393229156"], x.Host.Probes);   // link đã làm sạch của dòng 2
        Assert.Equal(new[] { 3, 4, 5 }, x.Cfg.SnapshotFailedRows().Select(r => r.Row).OrderBy(r => r));
        Assert.Equal(3, f.Calls.Count);
        Assert.Empty(x.P.PendingNotProductRows);
        Assert.Equal(savedBefore, x.Sink.Saved.Count);                                        // mẫu không lưu dòng
        Assert.Equal(5, x.Cfg.LastCompletedRow);                                              // đóng chuỗi: vá đi tiếp từ sau chuỗi
        Assert.Equal(6, x.Cfg.NextRunRow);
        await Handle(x, Step(notProduct: true), 6);                                           // phiên đã chứng minh: GIỮ CHỜ tới lần kiểm kế
        Assert.Equal([6], x.P.PendingNotProductRows);
        Assert.Equal(3, f.Calls.Count);
        Assert.Single(x.Host.Probes);
        Assert.Equal(5, x.Cfg.LastCompletedRow);                                              // chưa bảo lãnh → chưa tiến
        await Handle(x, Step(ok: true), 7);                                                   // chuỗi bị cắt bằng dòng lưu được → xả
        Assert.Equal(4, f.Calls.Count);
    }

    [Theory]
    [InlineData("notProduct")]
    [InlineData("login")]
    [InlineData("throw")]
    public async Task KiemChungPhien_MauHong_DungKhoi_KhongGhiGi(string kind)
    {
        var f = new FakeFailureSink();
        var x = ProcF(f, sampleLink: () => "https://shopee.vn/product/1/2");                 // không lưu được dòng nào → mẫu dự phòng
        x.Host.Sample = kind switch
        {
            "notProduct" => _ => Step(notProduct: true),
            "login" => _ => Step() with { LoginRequired = true },
            _ => _ => null,
        };
        await Handle(x, Step(notProduct: true), 3);
        await Handle(x, Step(notProduct: true), 4);
        var o = await Handle(x, Step(notProduct: true), 5);

        Assert.Equal(HarvestRowNext.Stop, o.Next);
        Assert.Equal(["https://shopee.vn/product/1/2"], x.Host.Probes);
        Assert.Empty(f.Calls);
        Assert.Equal(new[] { 3, 4, 5 }, x.Cfg.SnapshotFailedRows().Select(r => r.Row));      // sổ bỏ qua của lượt, không tập lỗi
        Assert.Contains("link mẫu", x.Cfg.LastRunnerMessage);
        var chunk = ScrapeRunner.Classify(x.Cfg);
        Assert.Equal(ScrapeFailureAction.Cooldown, ScrapeFailurePolicy.Decide(chunk.Errored, chunk.IsCaptcha, chunk.Reason));
    }

    /// <summary>Phản biện vòng 15–16: chuỗi đã chứng minh phiên ổn KHÔNG kiểm sau mỗi N dòng, nhưng kiểm lại sau mỗi
    /// ProvenChainRecheckEvery dòng (giữ chờ, mẫu sống mới xả); chuỗi mới sau một dòng khác chen vào thì kiểm lại từ đầu.</summary>
    [Fact]
    public async Task KiemChungPhien_ChuoiDaiKiemLaiMoiKDong_ChuoiMoiSauDongKhacThiKiemLai()
    {
        var f = new FakeFailureSink();
        var x = ProcF(f, sampleLink: () => "https://shopee.vn/product/1/2");
        x.Host.Sample = _ => SampleOk;
        var row = 3;
        for (var i = 0; i < 20; i++)
            Assert.Equal(HarvestRowNext.Done, (await Handle(x, Step(notProduct: true), row++)).Next);
        Assert.Equal(2, x.Host.Probes.Count);                                                // dòng 5 (3 dòng), dòng 15 (+10 dòng)
        Assert.Equal(13, f.Calls.Count);                                                     // 7 dòng cuối còn giữ chờ
        Assert.Equal(7, x.P.PendingNotProductRows.Count);
        Assert.Equal(15, x.Cfg.LastCompletedRow);

        for (var chain = 0; chain < 4; chain++)                                               // chuỗi mới sau một dòng lưu được
        {
            await Handle(x, Step(ok: true), row++);
            for (var i = 0; i < HarvestStepPolicy.NotProductBreakerThreshold; i++)
                Assert.Equal(HarvestRowNext.Done, (await Handle(x, Step(notProduct: true), row++)).Next);
        }
        Assert.Equal(6, x.Host.Probes.Count);                                                // 2 của chuỗi dài + 1 mỗi chuỗi mới
    }

    /// <summary>Link mẫu gặp captcha: y như dòng thường gặp captcha — chờ giải tay một lần; không giải được → dừng như captcha
    /// (cách ly tài khoản); giải được → mở lại mẫu, mẫu sống thì chuỗi là lỗi thật.</summary>
    [Fact]
    public async Task KiemChungPhien_MauGapCaptcha_NhuDongThuong()
    {
        var f = new FakeFailureSink();
        var x = ProcF(f, sampleLink: () => "https://shopee.vn/product/1/2");
        x.Host.Sample = _ => Step(captcha: true);
        await Handle(x, Step(notProduct: true), 3);
        await Handle(x, Step(notProduct: true), 4);
        var o = await Handle(x, Step(notProduct: true), 5);
        Assert.Equal(HarvestRowNext.Stop, o.Next);
        Assert.Equal(1, x.Host.CaptchaChecks);
        Assert.Empty(f.Calls);
        var chunk = ScrapeRunner.Classify(x.Cfg);
        Assert.True(chunk.IsCaptcha);
        Assert.Equal(ScrapeFailureAction.Quarantine, ScrapeFailurePolicy.Decide(chunk.Errored, chunk.IsCaptcha, chunk.Reason));
        var row = Step(captcha: true);   // đối chiếu: dòng thường gặp captcha không giải được cũng ra cách ly
        var y = Proc();
        await Handle(y, row, 3);
        var c2 = ScrapeRunner.Classify(y.Cfg);
        Assert.Equal(ScrapeFailureAction.Quarantine, ScrapeFailurePolicy.Decide(c2.Errored, c2.IsCaptcha, c2.Reason));

        var g = new FakeFailureSink();
        var z = ProcF(g, sampleLink: () => "https://shopee.vn/product/1/2");
        var n = 0;
        z.Host.Sample = _ => ++n == 1 ? Step(captcha: true) : Step(ok: true);
        z.Host.Captcha = new(true, false, false, false, null, 5, null);                      // giải được
        await Handle(z, Step(notProduct: true), 3);
        await Handle(z, Step(notProduct: true), 4);
        Assert.Equal(HarvestRowNext.Done, (await Handle(z, Step(notProduct: true), 5)).Next);
        Assert.Equal(2, z.Host.Probes.Count);
        Assert.Equal(3, g.Calls.Count);
    }

    /// <summary>Tên tài khoản / ex.Message có chữ captcha / verify / proxy KHÔNG được làm Classify phân loại sai thông điệp dừng.</summary>
    [Fact]
    public async Task ThongDiepDung_KhongChenTenTaiKhoanVaoChuoiPhanLoai()
    {
        var x = ProcF(new FakeFailureSink());
        x.Cfg.Label = "shop verify captcha proxy";
        await Handle(x, Step() with { LoginRequired = true }, 3);
        var c = ScrapeRunner.Classify(x.Cfg);
        Assert.Equal(ScrapeFailureAction.Cooldown, ScrapeFailurePolicy.Decide(c.Errored, c.IsCaptcha, c.Reason));
        Assert.Contains(x.Log, l => l.Contains("shop verify captcha proxy"));               // tên vẫn có ở log riêng

        var y = ProcF(new FakeFailureSink(), sampleLink: () => "https://shopee.vn/product/1/2");
        y.Cfg.Label = "tk proxy";
        y.Host.Sample = _ => throw new InvalidOperationException("proxy verify captcha chết");
        await Handle(y, Step(notProduct: true), 3);
        await Handle(y, Step(notProduct: true), 4);
        await Handle(y, Step(notProduct: true), 5);
        var c2 = ScrapeRunner.Classify(y.Cfg);
        Assert.Equal(ScrapeFailureAction.Cooldown, ScrapeFailurePolicy.Decide(c2.Errored, c2.IsCaptcha, c2.Reason));
        Assert.Equal("…-…", HarvestStepPolicy.SafeForClassify("PROXY-Verify"));
    }

    // ── Vòng dòng cả khối (HarvestRunnerLoop.RunRowsAsync) với host + IO giả ──

    /// <summary>IO giả: bước theo kịch bản từng dòng; "kiểm trước link kế" mô phỏng extension: tab không ở trang SP (dòng trước
    /// không lưu được) → ok:false "URL hiện tại không phải link SP".</summary>
    private sealed class FakeIo(Func<int, HarvestStepResult> stepOf) : IHarvestLoopIo
    {
        public int CurrentLinkIndex { get; set; }
        public DateTimeOffset Now => Received;
        public List<int> Checks { get; } = [];
        /// <summary>Chữ overlay app đẩy lên trang + chữ trạng thái của từng bước (để kiểm số dòng hiển thị).</summary>
        public List<string> Overlays { get; } = [];
        public List<string> StatusTexts { get; } = [];
        private HarvestStepResult? _last;
        public Task<HarvestStepResult> ExecuteStepAsync(string link, int rowNumber, string statusText, int? tabId, CancellationToken cancellationToken)
        {
            StatusTexts.Add(statusText);
            _last = stepOf(rowNumber);
            return Task.FromResult(_last);
        }
        public Task PushDisplayStateAsync(int linkIndex, int? tabId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShowOverlayAsync(int? tabId, string text, CancellationToken cancellationToken)
        {
            Overlays.Add(text);
            return Task.CompletedTask;
        }
        public Task SimulateRestAsync(string pageUrl, int delayMs, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DelayAsync(int ms, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<RunnerExtensionRpc.BeforeNextLinkCheckResult> CheckBeforeNextLinkAsync(int tabId, int rowNumber, string sku, CancellationToken cancellationToken)
        {
            Checks.Add(rowNumber);
            var onProduct = _last is { HarvestOk: true };
            return Task.FromResult(onProduct
                ? new RunnerExtensionRpc.BeforeNextLinkCheckResult(true, false, false, false, null, tabId, null)
                : new RunnerExtensionRpc.BeforeNextLinkCheckResult(false, false, false, false, "URL hien tai khong phai link san pham Shopee", tabId, null));
        }
    }

    private static async Task<(bool Done, InstanceConfig Cfg, FakeFailureSink Failures, FakeIo Io, FakeHost Host)> RunBlock(
        Func<int, HarvestStepResult> stepOf, int from, int to, Func<FakeHost, FakeHost>? setupHost = null)
    {
        var f = new FakeFailureSink();
        var x = ProcF(f, sampleLink: () => null);
        setupHost?.Invoke(x.Host);
        var io = new FakeIo(stepOf);
        var items = Enumerable.Range(from, to - from + 1)
            .Select(r => new Shopee.Core.Scrape.SheetLinkItem(r, new Dictionary<string, object?>(), $"https://shopee.vn/sp-i.1.{r}"))
            .ToList();
        var done = await HarvestRunnerLoop.RunRowsAsync(items, x.Cfg, x.P, x.Host, io,
            Shopee.Core.Scrape.ScrapeRestWindow.Resolve(1, 1), "hdr", to, "Data",
            new System.Runtime.CompilerServices.StrongBox<int?>(5), x.Log.Add, () => { }, CancellationToken.None);
        return (done, x.Cfg, f, io, x.Host);
    }

    // ── Số dòng HIỂN THỊ = số dòng Excel thật (dải Từ dòng / Đến dòng nhập theo Excel); engine vẫn chạy dòng nén ──

    /// <summary>Sheet một cột link ở các dòng Excel cho sẵn (<paramref name="header"/> → dòng 1 là "Link"), đọc bằng bộ đọc Harvest.</summary>
    private static async Task<HarvestWorkbookRange> SheetAt(bool header, params int[] excelRows)
    {
        var path = Path.Combine(Path.GetTempPath(), "harvest-rows-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            using (var wb = new ClosedXML.Excel.XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Data");
                if (header) ws.Cell(1, 1).Value = "Link";
                foreach (var r in excelRows) ws.Cell(r, 1).Value = $"https://shopee.vn/sp-i.1.{r}";
                wb.SaveAs(path);
            }
            return await HarvestWorkbookReader.ReadSheetAsync(path, "Data", CancellationToken.None);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    /// <summary>Dòng trống giữa sheet (có tiêu đề) và file không tiêu đề: overlay "✓ Đã xong dòng N", chữ trạng thái, log lỗi dòng
    /// hiện số dòng Excel; sổ bỏ qua / LastCompletedRow vẫn là dòng nén.</summary>
    [Theory]
    [InlineData(true, new[] { 2, 3, 4, 5, 7, 8 }, 7)]     // dòng 6 trống → từ dòng nén 5 lệch 1
    [InlineData(false, new[] { 1, 2, 4, 5 }, 4)]          // không tiêu đề, dòng 3 trống
    public async Task SoDongHienThi_LaSoDongExcel_DongTrongGiua_VaKhongTieuDe(bool header, int[] excelRows, int deadExcel)
    {
        var full = await SheetAt(header, excelRows);
        var show = HarvestRowRange.ExcelRowMap(full);
        var deadDense = Array.IndexOf(excelRows, deadExcel) + 1;
        var f = new FakeFailureSink();
        var cfg = Cfg();
        cfg.HubAccountId = "bs1";
        var log = new List<string>();
        var p = new HarvestRowProcessor(cfg, new HarvestRunOptions(new FakeSink(null), "shop1", 3, RecordDone: true, FailureSink: f,
            SampleLink: () => null, DisplayRow: show), new HarvestPdpBreaker(3), log.Add);
        var io = new FakeIo(row => row == deadDense ? Step(notProduct: true) : Step(ok: true));
        var items = full.Links!.Items
            .Select(it => new Shopee.Core.Scrape.SheetLinkItem(it.RowNumber, new Dictionary<string, object?>(), it.Link)).ToList();

        var done = await HarvestRunnerLoop.RunRowsAsync(items, cfg, p, new FakeHost(), io,
            Shopee.Core.Scrape.ScrapeRestWindow.Resolve(1, 1), "hdr", full.DenseEnd, "Data",
            new System.Runtime.CompilerServices.StrongBox<int?>(5), log.Add, () => { }, CancellationToken.None);

        Assert.True(done);
        var last = excelRows[^1];
        Assert.Contains(io.Overlays, o => o.Contains($"✓ Đã xong dòng {last}", StringComparison.Ordinal));
        Assert.Contains(io.Overlays, o => o.Contains($"Dòng {deadExcel}: không lấy được dữ liệu", StringComparison.Ordinal));
        Assert.Contains(io.StatusTexts, s => s.EndsWith($"— dòng {deadExcel}.", StringComparison.Ordinal));
        Assert.Contains(log, l => l.StartsWith($"Dòng {deadExcel}:", StringComparison.Ordinal));
        // Không chỗ hiển thị nào còn số dòng nén của các dòng lệch.
        Assert.DoesNotContain(io.Overlays, o => o.Contains($"✓ Đã xong dòng {full.DenseEnd}", StringComparison.Ordinal) && full.DenseEnd != last);
        Assert.DoesNotContain(log, l => l.StartsWith($"Dòng {deadDense}:", StringComparison.Ordinal));
        // Bên trong vẫn dòng nén.
        Assert.Contains(cfg.SnapshotFailedRows(), x => x.Row == deadDense);
        Assert.Equal(full.DenseEnd, cfg.LastCompletedRow);
    }

    /// <summary>Cột "Trạng thái" của bảng Tiến trình: Harvest đổi dòng nén → dòng Excel; app Scrape (không có tuỳ chọn Harvest) giữ số.</summary>
    [Fact]
    public void TrangThaiDong_HarvestHienSoDongExcel_ScrapeGiuNguyen()
    {
        var cfg = Cfg();
        cfg.CurrentRow = 5;
        cfg.RunnerPhase = "opening";
        var opts = new HarvestRunOptions(new FakeSink(null), "s", DisplayRow: d => d + 2);
        Assert.Equal("Đang mở dòng 7…", ScrapeRunner.RowStatus(cfg, opts));
        Assert.Equal("Đang mở dòng 5…", ScrapeRunner.RowStatus(cfg));
        cfg.RunnerPhase = "waiting";
        cfg.LastCompletedRow = 4;
        Assert.Equal("Xong dòng 6 · chờ link kế…", ScrapeRunner.RowStatus(cfg, opts));
    }

    private static ScrapeFailureAction AccountAction(InstanceConfig cfg)
    {
        var c = ScrapeRunner.Classify(cfg);
        return ScrapeFailurePolicy.Decide(c.Errored, c.IsCaptcha, c.Reason);
    }

    /// <summary>Phản biện vòng 14 (mục 1): MỘT link chết giữa khối — trước đây bước kiểm trước link kế báo "URL không phải SP" →
    /// dừng khối, tài khoản bị cho nghỉ, dòng chết không vào tập lỗi. Nay: khối chạy hết, dòng vào tập lỗi, không loại tài khoản.</summary>
    [Fact]
    public async Task VongKhoi_MotLinkChetGiuaKhoi_ChayHet_DongVaoTapLoi_KhongLoaiTaiKhoan()
    {
        var r = await RunBlock(row => row == 4 ? Step(notProduct: true) : Step(ok: true), 2, 6);
        Assert.True(r.Done);
        Assert.DoesNotContain(4, r.Io.Checks);                                               // không kiểm sau dòng không lưu được
        Assert.Equal(["id:1.4"], Assert.Single(r.Failures.Calls).Keys);
        Assert.Contains(r.Cfg.SnapshotFailedRows(), x => x.Row == 4);
        Assert.Equal(ScrapeFailureAction.None, AccountAction(r.Cfg));
        Assert.Equal(6, r.Cfg.LastCompletedRow);
    }

    [Fact]
    public async Task VongKhoi_BaLinkChetLienTiep_KiemChungBangLinkMau_MauSong_ChayHet()
    {
        var r = await RunBlock(row => row is >= 3 and <= 5 ? Step(notProduct: true) : Step(ok: true), 2, 7,
            h => { h.Sample = _ => Step(ok: true); return h; });
        Assert.True(r.Done);
        Assert.Equal(["https://shopee.vn/product/1/2"], r.Host.Probes);                     // link đã làm sạch của dòng 2 (lưu được)
        Assert.Equal(3, r.Failures.Calls.Count);
        Assert.Equal(ScrapeFailureAction.None, AccountAction(r.Cfg));
        Assert.Equal(7, r.Cfg.LastCompletedRow);
    }

    [Fact]
    public async Task VongKhoi_BaLinkChetLienTiep_MauChet_DungKhoi_KhongGhiGi()
    {
        var r = await RunBlock(row => row is >= 3 and <= 5 ? Step(notProduct: true) : Step(ok: true), 2, 7,
            h => { h.Sample = _ => Step(notProduct: true); return h; });
        Assert.False(r.Done);
        Assert.Single(r.Host.Probes);
        Assert.Empty(r.Failures.Calls);
        Assert.Equal(ScrapeFailureAction.Cooldown, AccountAction(r.Cfg));
        Assert.Equal(5, r.Cfg.LastCompletedRow);                                             // chuỗi tính là đã duyệt — vá đi tiếp từ 6
    }

    [Fact]
    public async Task VongKhoi_LinkChetCuoiKhoi_XaChuoiKhiHetKhoi()
    {
        var r = await RunBlock(row => row == 6 ? Step(notProduct: true) : Step(ok: true), 2, 6);
        Assert.True(r.Done);
        Assert.Equal(["id:1.6"], Assert.Single(r.Failures.Calls).Keys);
        Assert.Equal(6, r.Cfg.LastCompletedRow);
    }

    /// <summary>Mô phỏng ScrapeRunner: chạy khối, khối dừng thì cho tài khoản đó nghỉ và VÁ phần còn lại từ LastCompletedRow + 1
    /// bằng tài khoản mới (bộ xử lý + config mới) cho tới khi phủ hết khối. Trả số lần chạy (phiên Brave), số tài khoản bị cho
    /// nghỉ / cách ly, số lần mở link mẫu, các dòng đã lưu, các khoá vào tập lỗi.</summary>
    private sealed record PatchResult(int Runs, int AccountsOut, int Quarantined, int Probes, List<int> Saved, List<string> FailedKeys,
        List<int> SkippedRows)
    {
        public void Deconstruct(out int runs, out int accountsOut, out int probes, out List<int> saved, out List<string> failedKeys)
            => (runs, accountsOut, probes, saved, failedKeys) = (Runs, AccountsOut, Probes, Saved, FailedKeys);
    }

    private static async Task<PatchResult> RunWithPatching(
        Func<int, HarvestStepResult> stepOf, int from, int to, Func<string, HarvestStepResult?> sample, Func<string?> sampleLink,
        RunnerExtensionRpc.BeforeNextLinkCheckResult? captchaAnswer = null)
    {
        var failures = new FakeFailureSink();
        var saved = new List<int>();
        var skipped = new List<int>();
        int runs = 0, out_ = 0, quarantined = 0, probes = 0, next = from;
        while (next <= to)
        {
            Assert.True(++runs <= 50, "vòng vá không tiến");
            var x = ProcF(failures, sampleLink: sampleLink);
            x.Host.Sample = sample;
            if (captchaAnswer is not null) x.Host.Captcha = captchaAnswer;
            var io = new FakeIo(stepOf);
            var items = Enumerable.Range(next, to - next + 1)
                .Select(r => new Shopee.Core.Scrape.SheetLinkItem(r, new Dictionary<string, object?>(), $"https://shopee.vn/sp-i.1.{r}"))
                .ToList();
            var done = await HarvestRunnerLoop.RunRowsAsync(items, x.Cfg, x.P, x.Host, io,
                Shopee.Core.Scrape.ScrapeRestWindow.Resolve(1, 1), "hdr", to, "Data",
                new System.Runtime.CompilerServices.StrongBox<int?>(5), x.Log.Add, () => { }, CancellationToken.None);
            probes += x.Host.Probes.Count;
            saved.AddRange(x.Sink.Saved.Select(i => i.RowNo));
            skipped.AddRange(x.Cfg.SnapshotFailedRows().Select(r => r.Row));
            // Như ScrapeRunner: kẹp LastCompletedRow vào khối; khối chưa phủ (lỗi, HOẶC không lỗi mà LastCompletedRow < to) →
            // tài khoản đó bị loại khỏi lượt vá kế (nghỉ / cách ly), phần còn lại vá bằng tài khoản khác.
            var lastDone = Shopee.Core.Scrape.ScrapeChunkMath.ClampLastDone(x.Cfg.LastCompletedRow, next, to);
            var action = AccountAction(x.Cfg);
            if (action == ScrapeFailureAction.Quarantine) quarantined++;
            if (!done || lastDone < to) out_++;
            Assert.True(lastDone >= next, $"lần vá {runs} không tiến (dừng ở {lastDone}, bắt đầu {next})");
            next = lastDone + 1;
        }
        return new PatchResult(runs, out_, quarantined, probes, saved, failures.Calls.SelectMany(c => c.Keys).ToList(), skipped);
    }

    /// <summary>Phản biện vòng 15: khối 2–30, dòng 3–22 chết (shop nguồn bị khoá), link mẫu sống — trước đây khối dừng đúng một chỗ ở
    /// mọi lần vá (~30 phiên Brave, ~30 tài khoản bị loại). Nay: một lượt đi hết, một lần mở mẫu, không loại tài khoản nào.</summary>
    [Fact]
    public async Task VongKhoi_CumChet20Dong_MauSong_DiHetKhoi_HaiLanKiem()
    {
        var r = await RunWithPatching(row => row is >= 3 and <= 22 ? Step(notProduct: true) : Step(ok: true), 2, 30,
            _ => Step(ok: true), () => null);
        Assert.Equal(1, r.Runs);
        Assert.Equal(0, r.AccountsOut);
        Assert.Equal(2, r.Probes);                                                           // lần đầu + một lần kiểm lại sau 10 dòng
        Assert.Equal([2, .. Enumerable.Range(23, 8)], r.Saved.Order());                     // dòng sống phía sau đều lấy được
        Assert.Equal(20, r.FailedKeys.Count);
    }

    /// <summary>Phản biện vòng 16: cụm 30 dòng chết, mẫu sống → kiểm 3 lần (dòng thứ 3, +10, +10), ghi đủ 30 dòng.</summary>
    [Fact]
    public async Task VongKhoi_CumChet30Dong_MauSong_KiemBaLan_GhiDu()
    {
        var r = await RunWithPatching(row => row is >= 3 and <= 32 ? Step(notProduct: true) : Step(ok: true), 2, 36,
            _ => Step(ok: true), () => null);
        Assert.Equal(1, r.Runs);
        Assert.Equal(3, r.Probes);
        Assert.Equal(30, r.FailedKeys.Count);
        Assert.Equal([2, 33, 34, 35, 36], r.Saved.Order());
    }

    /// <summary>Cụm 30 dòng chết, mẫu sống ở lần đầu rồi HỎNG từ lần thứ 2 (phiên hỏng giữa chuỗi mà Shopee không đẩy về trang đăng
    /// nhập) → chỉ 3 dòng được lần kiểm thứ nhất bảo lãnh vào tập lỗi; các dòng sau đó vào sổ bỏ qua, KHÔNG vào tập lỗi.</summary>
    [Fact]
    public async Task VongKhoi_CumChet30Dong_MauHongLanHai_DongSauLanKiemDauKhongVaoTapLoi()
    {
        var probes = 0;
        var r = await RunWithPatching(row => row is >= 3 and <= 32 ? Step(notProduct: true) : Step(ok: true), 2, 36,
            _ => ++probes == 1 ? Step(ok: true) : Step(notProduct: true), () => "https://shopee.vn/product/1/2");
        Assert.Contains("id:1.3", r.FailedKeys);                                             // 3 dòng được lần kiểm thứ nhất bảo lãnh
        Assert.Contains("id:1.5", r.FailedKeys);
        for (var row = 6; row <= 30; row++)                                                  // dòng sau lần kiểm thứ nhất: không bảo lãnh
            Assert.DoesNotContain($"id:1.{row}", r.FailedKeys);
        // (31–32: lần vá cuối gặp 2 dòng chết rồi một dòng LƯU ĐƯỢC cắt chuỗi → lỗi dòng thật như luật chuỗi < N.)
        Assert.Contains(6, r.SkippedRows);
        Assert.Contains(15, r.SkippedRows);
        Assert.Equal([2, 33, 34, 35, 36], r.Saved.Order());                                  // vá vẫn đi hết khối
    }

    /// <summary>Không có link mẫu (máy mới): không chứng minh được phiên hỏng → KHÔNG dừng khối: cụm 20 dòng chết đi qua trong một
    /// lượt, không đụng tài khoản nào, dòng chết không vào tập lỗi (lượt sau thử lại), dòng sống phía sau được lưu.</summary>
    [Fact]
    public async Task VongKhoi_CumChet20Dong_KhongCoMau_MotLanChay_KhongDungTaiKhoan()
    {
        // Khối bắt đầu ngay ở cụm chết (chưa lưu được dòng nào) + máy chưa từng lưu SP → không có link mẫu nào.
        var r = await RunWithPatching(row => row is >= 3 and <= 22 ? Step(notProduct: true) : Step(ok: true), 3, 30,
            _ => null, () => null);
        Assert.Equal(1, r.Runs);
        Assert.Equal(0, r.AccountsOut);
        Assert.Equal(0, r.Probes);
        Assert.Equal(Enumerable.Range(23, 8), r.Saved.Order());
        Assert.Empty(r.FailedKeys);
    }

    /// <summary>Nghiệm thu vòng 16: link mẫu gặp captcha không giải được — khối dừng, tài khoản bị CÁCH LY (như dòng thường gặp
    /// captcha), lần vá đi tiếp từ sau chuỗi (chuỗi tính đã duyệt), các dòng của chuỗi KHÔNG vào tập lỗi mà vào sổ bỏ qua của lượt.</summary>
    [Fact]
    public async Task VongKhoi_MauGapCaptchaKhongGiai_CachLy_VaDiTiepSauChuoi()
    {
        var r = await RunWithPatching(row => row is >= 3 and <= 5 ? Step(notProduct: true) : Step(ok: true), 2, 10,
            _ => Step(captcha: true), () => "https://shopee.vn/product/1/2");                 // FakeHost.Captcha mặc định: không giải được
        Assert.Equal(2, r.Runs);
        Assert.Equal(1, r.Quarantined);
        Assert.Equal(1, r.AccountsOut);
        Assert.Equal([2, 6, 7, 8, 9, 10], r.Saved.Order());                                  // lần vá đi tiếp từ dòng 6
        Assert.Empty(r.FailedKeys);
        Assert.Equal([3, 4, 5], r.SkippedRows.Order());
    }

    /// <summary>Cụm không có mẫu: sổ bỏ qua của lượt có các dòng đó (tổng kết cuối job hiện "BỎ QUA n dòng"), tập lỗi rỗng.</summary>
    [Fact]
    public async Task VongKhoi_KhongCoMau_DongChuoiVaoSoBoQua_TapLoiRong()
    {
        var r = await RunWithPatching(row => row is >= 3 and <= 7 ? Step(notProduct: true) : Step(ok: true), 3, 10,
            _ => null, () => null);
        Assert.Equal(1, r.Runs);
        Assert.Equal([3, 4, 5, 6, 7], r.SkippedRows.Order());
        Assert.Empty(r.FailedKeys);
    }

    /// <summary>Dòng pdpError đang chờ bị AdvanceTo vượt qua khi chuỗi notProduct dừng khối → phải vào sổ bỏ qua (trước đây bị bỏ
    /// ở nhánh dừng → mất dấu, tổng kết báo hoàn thành sai).</summary>
    [Fact]
    public async Task ChuoiDungKhoi_DongPdpErrorDangCho_VaoSoBoQua()
    {
        var x = ProcF(new FakeFailureSink(), sampleLink: () => "https://shopee.vn/product/1/2");
        x.Host.Sample = _ => Step(notProduct: true);                                         // mẫu hỏng → dừng
        await Handle(x, Step(pdpError: "77", message: "pdp 2"), 2);                          // pdpError đang chờ
        await Handle(x, Step(notProduct: true), 3);
        await Handle(x, Step(notProduct: true), 4);
        var o = await Handle(x, Step(notProduct: true), 5);
        Assert.Equal(HarvestRowNext.Stop, o.Next);
        Assert.Equal(5, x.Cfg.LastCompletedRow);
        Assert.Equal(new[] { 2, 3, 4, 5 }, x.Cfg.SnapshotFailedRows().Select(r => r.Row).OrderBy(r => r));
        Assert.Empty(x.P.Book.PendingPdpRows);
    }

    [Fact]
    public async Task VongKhoi_MauHong_Cum5Dong_HaiLanChay_MotTaiKhoanNghi()
    {
        var r = await RunWithPatching(row => row is >= 3 and <= 7 ? Step(notProduct: true) : Step(ok: true), 2, 10,
            _ => Step(notProduct: true), () => "https://shopee.vn/product/1/2");
        Assert.Equal(2, r.Runs);
        Assert.Equal(1, r.AccountsOut);
        Assert.Equal(1, r.Probes);
        Assert.Equal([2, 8, 9, 10], r.Saved.Order());
        Assert.DoesNotContain("id:1.3", r.FailedKeys);                                       // chuỗi dừng (3–5) không vào tập lỗi
    }

    /// <summary>Huỷ giữa khối: không báo xong cả khối.</summary>
    [Fact]
    public async Task VongKhoi_Huy_KhongBaoXongCaKhoi()
    {
        using var cts = new CancellationTokenSource();
        var x = ProcF(new FakeFailureSink());
        var io = new FakeIo(row => { if (row == 4) cts.Cancel(); return Step(ok: true); });
        var items = Enumerable.Range(2, 5)
            .Select(r => new Shopee.Core.Scrape.SheetLinkItem(r, new Dictionary<string, object?>(), $"https://shopee.vn/sp-i.1.{r}"))
            .ToList();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HarvestRunnerLoop.RunRowsAsync(items, x.Cfg, x.P, x.Host, io,
            Shopee.Core.Scrape.ScrapeRestWindow.Resolve(1, 1), "hdr", 6, "Data",
            new System.Runtime.CompilerServices.StrongBox<int?>(5), x.Log.Add, () => { }, cts.Token));
        Assert.True(x.Cfg.LastCompletedRow < 6, $"LastCompletedRow={x.Cfg.LastCompletedRow}");
    }

    // ── GĐ9: lượt ảnh gốc trong khoảng nghỉ giữa 2 SP ──

    /// <summary>IO giả có ĐỒNG HỒ CHẠY (nghỉ / chờ cộng thời gian) + lượt ảnh theo kịch bản; ghi thứ tự sự kiện.</summary>
    private sealed class ImageIo(Func<int, HarvestStepResult> stepOf) : IHarvestLoopIo
    {
        public DateTimeOffset Clock = Received;
        public int CurrentLinkIndex { get; set; }
        public DateTimeOffset Now => Clock;
        public List<string> Events { get; } = [];
        public List<int> Budgets { get; } = [];
        public List<DateTimeOffset> StepTimes { get; } = [];
        public Func<int, CancellationToken, Task<SourceImageRound?>> Images { get; set; } =
            (_, _) => Task.FromResult<SourceImageRound?>(null);
        public Func<int?> Missing { get; set; } = () => null;
        public Func<RunnerExtensionRpc.BeforeNextLinkCheckResult>? Check { get; set; }

        public Task<HarvestStepResult> ExecuteStepAsync(string link, int rowNumber, string statusText, int? tabId, CancellationToken cancellationToken)
        {
            Events.Add($"step:{rowNumber}");
            StepTimes.Add(Clock);
            return Task.FromResult(stepOf(rowNumber));
        }
        public Task PushDisplayStateAsync(int linkIndex, int? tabId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShowOverlayAsync(int? tabId, string text, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SimulateRestAsync(string pageUrl, int delayMs, CancellationToken cancellationToken)
        {
            Events.Add("rest");
            Clock = Clock.AddMilliseconds(delayMs);
            return Task.CompletedTask;
        }
        public Task<RunnerExtensionRpc.BeforeNextLinkCheckResult> CheckBeforeNextLinkAsync(int tabId, int rowNumber, string sku, CancellationToken cancellationToken)
        {
            Events.Add("check");
            return Task.FromResult(Check?.Invoke() ?? new RunnerExtensionRpc.BeforeNextLinkCheckResult(true, false, false, false, null, tabId, null));
        }
        public Task DelayAsync(int ms, CancellationToken cancellationToken)
        {
            Events.Add($"delay:{ms}");
            Clock = Clock.AddMilliseconds(ms);
            return Task.CompletedTask;
        }
        public async Task<SourceImageRound?> RunSourceImagesAsync(int budgetMs, CancellationToken cancellationToken)
        {
            Budgets.Add(budgetMs);
            Events.Add("images:start");
            var r = await Images(budgetMs, cancellationToken);
            Events.Add("images:end");
            return r;
        }
        public Task<int?> SourceImagesMissingAsync(CancellationToken cancellationToken)
        {
            Events.Add("missing");
            return Task.FromResult(Missing());
        }
    }

    private static SourceImageRound ImgRound(int pushed = 0, int existed = 0, int failed = 0, int missing = 0, string? stoppedBy = null,
        string? hubFullCode = null) =>
        new("r1", 0, 0, pushed, existed, failed, 0, 0, stoppedBy, hubFullCode, null, 0, missing, []);

    private static async Task<(bool Done, ImageIo Io, InstanceConfig Cfg, List<string> Log)> RunImageBlock(
        Action<ImageIo> setup, int restSeconds, int from = 2, int to = 3, CancellationToken ct = default)
    {
        var x = ProcF(new FakeFailureSink());
        var io = new ImageIo(_ => Step(ok: true));
        setup(io);
        var items = Enumerable.Range(from, to - from + 1)
            .Select(r => new Shopee.Core.Scrape.SheetLinkItem(r, new Dictionary<string, object?>(), $"https://shopee.vn/sp-i.1.{r}"))
            .ToList();
        var done = await HarvestRunnerLoop.RunRowsAsync(items, x.Cfg, x.P, x.Host, io,
            Shopee.Core.Scrape.ScrapeRestWindow.Resolve(restSeconds, restSeconds), "hdr", to, "Data",
            new System.Runtime.CompilerServices.StrongBox<int?>(5), x.Log.Add, () => { }, ct);
        return (done, io, x.Cfg, x.Log);
    }

    /// <summary>Nghỉ 60 s → lượt ảnh ngân sách 45 s, khởi động TRƯỚC phần nghỉ, chạy song song với nghỉ + kiểm captcha, được chờ xong
    /// TRƯỚC khi chờ nốt tới mốc link kế; mốc link kế không đổi; có số → 1 dòng log. Hết khối: hỏi số còn chờ (không có → thôi).</summary>
    [Fact]
    public async Task AnhGoc_ChaySongSongTrongKhoangNghi_DungNganSach_ChoXongTruocLinkKe()
    {
        var tcs = new TaskCompletionSource<SourceImageRound?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (done, io, _, log) = await RunImageBlock(io =>
        {
            io.Images = (_, _) => tcs.Task;
            io.Check = () =>
            {
                tcs.SetResult(ImgRound(pushed: 8, existed: 2, failed: 1, missing: 30));   // lượt ảnh xong trong lúc kiểm captcha
                return new RunnerExtensionRpc.BeforeNextLinkCheckResult(true, false, false, false, null, 5, null);
            };
        }, restSeconds: 60);

        Assert.True(done);
        Assert.Equal([45_000], io.Budgets);
        Assert.Equal(["step:2", "images:start", "rest", "check", "images:end", "delay:30000", "step:3", "missing"], io.Events);
        Assert.Equal(Received.AddSeconds(60), io.StepTimes[1]);   // mốc mở link kế không đổi
        Assert.Contains("Ảnh gốc: đẩy 8 · đã có 2 · lỗi 1 · còn thiếu 30", log);
    }

    /// <summary>Lượt ảnh chạy quá mốc link kế (chậm): link kế mở NGAY sau khi lượt xong, không cộng thêm chờ.</summary>
    [Fact]
    public async Task AnhGoc_ChayQuaMoc_KhongChoThem()
    {
        ImageIo? io0 = null;
        var (done, io, _, _) = await RunImageBlock(io =>
        {
            io0 = io;
            io.Images = (_, _) =>
            {
                io0!.Clock = io0.Clock.AddSeconds(70);
                return Task.FromResult<SourceImageRound?>(ImgRound());
            };
        }, restSeconds: 60);
        Assert.True(done);
        Assert.DoesNotContain(io.Events, e => e.StartsWith("delay:", StringComparison.Ordinal));
        Assert.Equal(Received.AddSeconds(70 + 30), io.StepTimes[1]);
    }

    /// <summary>Không chạy lượt sau dòng cuối khối; nghỉ quá ngắn (ngân sách &lt; 10 s) thì chỉ hỏi số còn chờ; lượt không có số thì
    /// không log.</summary>
    [Fact]
    public async Task AnhGoc_KhongChaySauDongCuoi_VaKhiNghiQuaNgan()
    {
        var (done, io, _, log) = await RunImageBlock(_ => { }, restSeconds: 60, from: 2, to: 5);
        Assert.True(done);
        Assert.Equal([45_000, 45_000, 45_000], io.Budgets);   // 4 dòng → 3 khoảng nghỉ
        Assert.Equal(["step:5", "missing"], io.Events[^2..]);
        Assert.DoesNotContain(log, l => l.Contains("Ảnh gốc", StringComparison.Ordinal));

        var (done2, io2, _, _) = await RunImageBlock(_ => { }, restSeconds: 24);   // 24 s − 15 s = 9 s < 10 s
        Assert.True(done2);
        Assert.Empty(io2.Budgets);
        Assert.Equal(2, io2.Events.Count(e => e == "missing"));          // khoảng nghỉ ngắn + hết khối
        var (_, io3, _, _) = await RunImageBlock(_ => { }, restSeconds: 25);
        Assert.Equal([10_000], io3.Budgets);
    }

    /// <summary>Nghỉ quá ngắn mà hàng đợi còn ảnh → cảnh báo MỘT lần mỗi khối; hết khối còn ảnh → một lượt dọn 30 s; dọn chưa hết →
    /// cảnh báo "hết khối".</summary>
    [Fact]
    public async Task AnhGoc_NghiNgan_CanhBaoMotLan_HetKhoi_DonMotLuot()
    {
        var (done, io, _, log) = await RunImageBlock(io => io.Missing = () => 7, restSeconds: 20, from: 2, to: 5);
        Assert.True(done);
        Assert.Equal(1, log.Count(l => l == SourceImageRound.PendingWarning(7, SourceImageRound.WhyShortRest)));
        Assert.Equal([HarvestRunnerLoop.BlockEndImageBudgetMs], io.Budgets);       // chỉ lượt dọn cuối khối
        Assert.Equal(["step:5", "missing", "images:start", "images:end"], io.Events[^4..]);
        Assert.Contains(SourceImageRound.PendingWarning(7, SourceImageRound.WhyBlockEnd), log);   // lượt dọn không có kết quả

        var (_, io2, _, log2) = await RunImageBlock(io =>
        {
            io.Missing = () => 12;
            io.Images = (b, _) => Task.FromResult<SourceImageRound?>(b == HarvestRunnerLoop.BlockEndImageBudgetMs ? ImgRound(pushed: 9, missing: 3) : null);
        }, restSeconds: 60);
        Assert.Equal([45_000, HarvestRunnerLoop.BlockEndImageBudgetMs], io2.Budgets);
        Assert.Contains("Ảnh gốc: đẩy 9 · đã có 0 · lỗi 0 · còn thiếu 3", log2);
        Assert.Contains(SourceImageRound.PendingWarning(3, SourceImageRound.WhyBlockEnd), log2);

        var (_, io3, _, log3) = await RunImageBlock(io =>
        {
            io.Missing = () => 4;
            io.Images = (_, _) => Task.FromResult<SourceImageRound?>(ImgRound(pushed: 4, missing: 0));
        }, restSeconds: 60);
        Assert.DoesNotContain(log3, l => l.Contains("chưa đẩy", StringComparison.Ordinal));      // dọn hết → không cảnh báo
        Assert.Equal(2, io3.Budgets.Count);
    }

    /// <summary>Lượt ảnh ném (đồng bộ lẫn bất đồng bộ) → dòng vẫn tính lưu, khối vẫn xong; Hub hết chỗ → log cảnh báo.</summary>
    [Fact]
    public async Task AnhGoc_Nem_KhongHongDong_HubHetCho_LogCanhBao()
    {
        var (done, _, cfg, _) = await RunImageBlock(io => io.Images = (_, _) => throw new InvalidOperationException("CDP đứt"), 60, 2, 4);
        Assert.True(done);
        Assert.Equal(3, cfg.HarvestSavedRows);

        var (done2, _, cfg2, _) = await RunImageBlock(io => io.Images = async (_, _) =>
        {
            await Task.Yield();
            throw new InvalidOperationException("lỗi muộn");
        }, 60, 2, 4);
        Assert.True(done2);
        Assert.Equal(3, cfg2.HarvestSavedRows);

        var (done3, _, _, _) = await RunImageBlock(io =>
        {
            io.Missing = () => throw new InvalidOperationException("status hỏng");
            io.Images = (_, _) => throw new InvalidOperationException("x");
        }, 20, 2, 4);
        Assert.True(done3);

        var (_, _, _, log) = await RunImageBlock(io => io.Images = (_, _) =>
            Task.FromResult<SourceImageRound?>(ImgRound(pushed: 1, stoppedBy: "hubFull", hubFullCode: "disk-low")), 60);
        Assert.Contains(log, l => l.StartsWith("⚠ Hub hết chỗ (disk-low) — dừng đẩy ảnh", StringComparison.Ordinal));
    }

    /// <summary>Dừng sớm vì captcha trước link kế trong lúc lượt ảnh còn chạy: vòng trả false NGAY (không chờ lượt ảnh), lỗi muộn của
    /// lượt ảnh được quan sát (không thành UnobservedTaskException).</summary>
    [Fact]
    public async Task AnhGoc_DungSomViCaptcha_KhongChoLuotAnh_LoiMuonDuocQuanSat()
    {
        var tcs = new TaskCompletionSource<SourceImageRound?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unobserved = 0;
        EventHandler<UnobservedTaskExceptionEventArgs> onUnobserved = (_, e) =>
        {
            if (e.Exception.InnerExceptions.Any(x => x.Message == "lượt ảnh hỏng muộn")) Interlocked.Increment(ref unobserved);
        };
        TaskScheduler.UnobservedTaskException += onUnobserved;
        try
        {
            var run = RunImageBlock(io =>
            {
                io.Images = (_, _) => tcs.Task;
                io.Check = () => new RunnerExtensionRpc.BeforeNextLinkCheckResult(false, true, false, false, "captcha", 5, null);
            }, restSeconds: 60);
            var finished = await Task.WhenAny(run, Task.Delay(5_000));
            Assert.Same(run, finished);
            var (done, io, cfg, _) = await run;
            Assert.False(done);
            Assert.True(cfg.CaptchaError);
            Assert.DoesNotContain("images:end", io.Events);

            tcs.SetException(new InvalidOperationException("lượt ảnh hỏng muộn"));
            await Task.Delay(50);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.Equal(0, unobserved);
        }
        finally { TaskScheduler.UnobservedTaskException -= onUnobserved; }
    }

    /// <summary>Dừng job trong lúc chờ lượt ảnh → huỷ lan ra (không nuốt).</summary>
    [Fact]
    public async Task AnhGoc_HuyJob_LanRa()
    {
        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunImageBlock(io => io.Images = async (_, ct) =>
        {
            cts.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        }, 60, ct: cts.Token));
    }

    /// <summary>RPC treo THẬT (start không trả lời, bỏ qua token) với driver thật: vòng dòng vẫn mở link kế, chỉ trễ tối đa grace sau
    /// hạn chót của lượt ảnh.</summary>
    [Fact]
    public async Task AnhGoc_RpcTreo_LinkKeKhongTreQuaGrace()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (done, io, _, _) = await RunImageBlock(io => io.Images = (_, ct) => SourceImageRoundDriver.RunAsync(
            (_, _, _) => new TaskCompletionSource<SourceImageStart?>().Task,     // treo, không nghe token
            _ => new TaskCompletionSource<SourceImageStatus?>().Task,
            (_, _) => Task.CompletedTask, () => DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMilliseconds(300),
            new HarvestSourceImageProgress(), "P", ct, grace: TimeSpan.FromMilliseconds(200), minStartMs: 100), restSeconds: 60);
        sw.Stop();
        Assert.True(done);
        Assert.Equal(2, io.StepTimes.Count);
        Assert.True(sw.ElapsedMilliseconds < 3_000, $"{sw.ElapsedMilliseconds} ms");
    }

    // ── GĐ9: điều một lượt ảnh phía app (SourceImageRoundDriver) ──

    private sealed class DriverFake
    {
        public DateTimeOffset Clock = Received;
        public List<bool> StartClears { get; } = [];
        public List<int> StartBudgets { get; } = [];
        public int Polls;
        public SourceImageStart? Start = new(true, "r1", null);
        public Func<int, SourceImageStatus?> Status = _ => null;

        public Task<SourceImageRound?> Run(HarvestSourceImageProgress? progress, int budgetMs = 45_000, string key = "P1",
            TimeSpan lateStart = default) =>
            SourceImageRoundDriver.RunAsync(
                (clear, budget, _) => { StartClears.Add(clear); StartBudgets.Add(budget); return Task.FromResult(Start); },
                _ => Task.FromResult(Status(++Polls)),
                (d, _) => { Clock += d; return Task.CompletedTask; },
                () => Clock + lateStart, Clock.AddMilliseconds(budgetMs), progress, key, CancellationToken.None);
    }

    private static SourceImageStatus St(bool running, string id, int pushed, int missing = 0, string? stoppedBy = null) =>
        new(running, ImgRound(pushed: pushed, missing: missing, stoppedBy: stoppedBy) with { RoundId = id });

    [Fact]
    public async Task Driver_HoiToiKhiXong_GhiDanSo_KhongCongHaiLan()
    {
        var p = new HarvestSourceImageProgress();
        var f = new DriverFake { Status = n => n < 3 ? St(true, "r1", n) : St(false, "r1", 5, missing: 7) };
        var r = await f.Run(p);
        Assert.Equal(5, r!.Pushed);
        Assert.Equal(3, f.Polls);
        Assert.Equal(5, p.Pushed);
        Assert.Equal(7, p.Missing);
        Assert.Equal([45_000], f.StartBudgets);
        await f.Run(p);
        Assert.Equal(5, p.Pushed);             // cùng lượt r1 báo lại → không cộng
    }

    /// <summary>Hạn chót tính từ ĐẦU khoảng nghỉ: start trễ thì ngân sách gửi extension là phần còn lại; quá hạn chót + grace thì thôi
    /// hỏi, giữ số giữa chừng.</summary>
    [Fact]
    public async Task Driver_HanChotTuDauKhoangNghi_ThoiHoiSauGrace_GiuSoGiuaChung()
    {
        var p = new HarvestSourceImageProgress();
        var f = new DriverFake { Status = n => St(true, "r1", n) };
        var r = await f.Run(p, budgetMs: 20_000);
        // 20 s + 5 s grace, mỗi lần hỏi cách 2 s → 13 lần.
        Assert.Equal(13, f.Polls);
        Assert.Equal(13, r!.Pushed);
        Assert.Equal(13, p.Pushed);

        var late = new DriverFake { Status = _ => St(false, "r1", 1) };
        await late.Run(null, budgetMs: 45_000, lateStart: TimeSpan.FromSeconds(12));   // start bắt đầu trễ 12 s sau đầu nghỉ
        Assert.Equal([33_000], late.StartBudgets);
        var tooLate = new DriverFake();
        Assert.Null(await tooLate.Run(null, budgetMs: 45_000, lateStart: TimeSpan.FromSeconds(41)));
        Assert.Empty(tooLate.StartClears);                                              // còn < 5 s → không bắt đầu
    }

    /// <summary>Status treo THẬT (không trả lời, bỏ qua token) sau một lần báo giữa chừng: driver trả về đúng hạn, giữ số đã thấy.</summary>
    [Fact]
    public async Task Driver_StatusTreo_TraVeDungHan_GiuSoGiuaChung()
    {
        var p = new HarvestSourceImageProgress();
        var polls = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = await SourceImageRoundDriver.RunAsync(
            (_, _, _) => Task.FromResult<SourceImageStart?>(new SourceImageStart(true, "r1", null)),
            _ => ++polls == 1 ? Task.FromResult<SourceImageStatus?>(St(true, "r1", 4)) : new TaskCompletionSource<SourceImageStatus?>().Task,
            (_, _) => Task.CompletedTask, () => DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMilliseconds(300),
            p, "P", CancellationToken.None, grace: TimeSpan.FromMilliseconds(200), minStartMs: 100);
        sw.Stop();
        Assert.Equal(4, r!.Pushed);
        Assert.Equal(4, p.Pushed);
        Assert.True(sw.ElapsedMilliseconds < 2_000, $"{sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Driver_LuotKhacDangChay_TheoDoiLuotDo_LuotLaBoQua()
    {
        var f = new DriverFake { Start = new SourceImageStart(false, "r0", "Đang chạy"), Status = _ => St(false, "r0", 2) };
        Assert.Equal(2, (await f.Run(null))!.Pushed);

        var g = new DriverFake { Status = _ => St(false, "khac", 9) };   // trạng thái của lượt khác (SW khởi động lại)
        Assert.Null(await g.Run(new HarvestSourceImageProgress()));
    }

    /// <summary>Thẻ "bỏ mốc Hub hết chỗ" chỉ dùng hết khi extension đã nhận (bắt đầu lượt / báo đã bỏ mốc); RPC hỏng → lần sau gửi lại.</summary>
    [Fact]
    public async Task Driver_ClearHubFull_ChiTieuKhiExtensionNhan()
    {
        var p = new HarvestSourceImageProgress();
        var f = new DriverFake { Start = null, Status = _ => St(false, "r1", 0) };
        await f.Run(p);                                          // RPC hỏng
        f.Start = new SourceImageStart(false, null, "x", Unsupported: true);
        await f.Run(p);                                          // extension cũ
        f.Start = new SourceImageStart(true, "r1", null);
        await f.Run(p);                                          // nhận
        await f.Run(p);
        Assert.Equal([true, true, true, false], f.StartClears);

        p.ResetHubFull();
        f.Start = new SourceImageStart(false, "r9", "Đang chạy lượt khác", HubFullCleared: true);
        await f.Run(p);                                          // không bắt đầu nhưng đã bỏ mốc
        f.Start = new SourceImageStart(true, "r1", null);
        await f.Run(p);
        Assert.Equal([true, true, true, false, true, false], f.StartClears);
    }

    [Fact]
    public async Task Driver_ExtensionCu_TuChoi_LoiRpc_HubHetCho()
    {
        var unsupported = new DriverFake { Start = new SourceImageStart(false, null, "x", Unsupported: true) };
        Assert.Null(await unsupported.Run(new HarvestSourceImageProgress()));
        Assert.Equal(0, unsupported.Polls);

        var p = new HarvestSourceImageProgress();
        var refused = new DriverFake { Start = new SourceImageStart(false, null, "Extension chưa được cấp quyền truy cập https://hub") };
        Assert.Null(await refused.Run(p));
        Assert.Equal("Ảnh gốc: Extension chưa được cấp quyền truy cập https://hub", p.LastError);

        var rpcDown = new DriverFake { Start = null };
        Assert.Null(await rpcDown.Run(p));

        var throwing = new DriverFake { Status = _ => throw new InvalidOperationException("CDP đứt") };
        Assert.Null(await throwing.Run(p));

        var full = new HarvestSourceImageProgress();
        full.Apply("P1", ImgRound(stoppedBy: "hubFull", hubFullCode: "disk-low"));
        var skipped = new DriverFake { Status = _ => St(false, "r1", 1) };
        Assert.Null(await skipped.Run(full));
        Assert.Empty(skipped.StartClears);   // Hub hết chỗ → không gọi extension
        full.ResetHubFull();
        Assert.Equal(1, (await skipped.Run(full))!.Pushed);
        Assert.Equal([true], skipped.StartClears);
    }

    /// <summary>Hỏi riêng số còn chờ: ghi vào Missing; treo / lỗi → null đúng hạn.</summary>
    [Fact]
    public async Task Driver_RefreshMissing()
    {
        var p = new HarvestSourceImageProgress();
        var n = await SourceImageRoundDriver.RefreshMissingAsync(
            _ => Task.FromResult<SourceImageStatus?>(new SourceImageStatus(false, null, 17)), p, "P", CancellationToken.None);
        Assert.Equal(17, n);
        Assert.Equal(17, p.Missing);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(await SourceImageRoundDriver.RefreshMissingAsync(
            _ => new TaskCompletionSource<SourceImageStatus?>().Task, p, "P", CancellationToken.None, TimeSpan.FromMilliseconds(100)));
        Assert.True(sw.ElapsedMilliseconds < 2_000);
        Assert.Null(await SourceImageRoundDriver.RefreshMissingAsync(_ => throw new InvalidOperationException("x"), p, "P", CancellationToken.None));
        Assert.Equal(17, p.Missing);
    }

    /// <summary>Lần mở LẠI link mẫu sau khi giải captcha ném lỗi → bắt như lần đầu, thông điệp đã làm sạch cho Classify.</summary>
    [Fact]
    public async Task KiemChungPhien_MoLaiMauSauCaptchaNem_BatVaLamSach()
    {
        var x = ProcF(new FakeFailureSink(), sampleLink: () => "https://shopee.vn/product/1/2");
        var n = 0;
        x.Host.Sample = _ => ++n == 1 ? Step(captcha: true) : throw new InvalidOperationException("proxy chết captcha");
        x.Host.Captcha = new(true, false, false, false, null, 5, null);
        await Handle(x, Step(notProduct: true), 3);
        await Handle(x, Step(notProduct: true), 4);
        var o = await Handle(x, Step(notProduct: true), 5);
        Assert.Equal(HarvestRowNext.Stop, o.Next);
        Assert.Equal(2, x.Host.Probes.Count);
        Assert.Equal(ScrapeFailureAction.Cooldown, AccountAction(x.Cfg));
        Assert.Contains("mở lại link mẫu lỗi", x.Cfg.LastRunnerMessage);
    }

    /// <summary>Mục 2: dòng cuối khối lỗi (hết giờ) — duyệt hết khối thì báo xong cả khối (ScrapeRunner không vá lại đúng dòng lỗi
    /// tới khi kẹt 3 lần); dòng lỗi không vào done nên lượt sau tự lấy lại.</summary>
    [Fact]
    public async Task VongKhoi_DongCuoiLoi_VanBaoXongCaKhoi()
    {
        var r = await RunBlock(row => row == 6 ? Step(message: "hết giờ chờ get_pc") : Step(ok: true), 2, 6);
        Assert.True(r.Done);
        Assert.Equal(6, r.Cfg.LastCompletedRow);
        Assert.Equal(7, r.Cfg.NextRunRow);
        Assert.Contains(r.Cfg.SnapshotFailedRows(), x => x.Row == 6);
        Assert.Empty(r.Failures.Calls);                                                      // lỗi tạm: không vào tập lỗi
        Assert.Equal(ScrapeFailureAction.None, AccountAction(r.Cfg));
    }

    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, true, false)]     // loginRequired dù ok
    [InlineData(true, false, false, false)]   // ok mà thiếu JSON
    [InlineData(false, false, true, false)]
    public void MauChungMinhPhien(bool ok, bool login, bool json, bool proves)
    {
        var r = Step(ok: ok) with { LoginRequired = login, Json = json ? "{\"error\":null}" : null };
        Assert.Equal(proves, HarvestStepPolicy.SampleProvesSession(r));
    }

    /// <summary>Phản biện vòng 14: mã đã xác nhận "SP chết" GIỮ tới hết phiên dù sau đó có dòng lưu được (quên đi thì mỗi cụm SP
    /// chết lại bật cầu dao → đổi IP rồi đổi tài khoản vô ích).</summary>
    [Fact]
    public void CauDao_LuuDuoc_VanNhoMaDaXacNhanSPChet()
    {
        var b = new HarvestPdpBreaker(1);
        var err = Step(pdpError: "4");
        Assert.Equal(HarvestStepAction.PdpBreakerTrip, HarvestStepPolicy.Decide(err, b).Action);
        Assert.Equal(HarvestStepAction.RowFailed, HarvestStepPolicy.Decide(err, b).Action);   // tái diễn → nhớ "SP chết"
        Assert.Equal(HarvestStepAction.Save, HarvestStepPolicy.Decide(Step(ok: true), b).Action);
        Assert.Contains("4", b.ConfirmedDeadCodes);
        Assert.Equal(HarvestStepAction.RowFailed, HarvestStepPolicy.Decide(err, b).Action);
    }

    [Theory]
    [InlineData(true, false, false, null, true)]       // notProduct
    [InlineData(false, false, false, "4", true)]       // mã pdpError đi vào RowFailed = mã đã xác nhận SP chết
    [InlineData(false, false, false, null, false)]     // hết giờ / thiếu JSON / RPC
    [InlineData(false, true, false, null, false)]      // ok (không phải lỗi)
    public void PhanLoai_LoiVinhVien(bool notProduct, bool ok, bool unused, string? pdp, bool permanent)
    {
        _ = unused;
        var step = Step(ok: ok, notProduct: notProduct, pdpError: pdp);
        Assert.Equal(permanent, HarvestStepPolicy.IsPermanentRowFailure(HarvestStepAction.RowFailed, step));
        Assert.False(HarvestStepPolicy.IsPermanentRowFailure(HarvestStepAction.PdpError, step));
        Assert.False(HarvestStepPolicy.IsPermanentRowFailure(HarvestStepAction.Captcha, step));
    }

    [Fact]
    public async Task RowBook_SinkRejects_ReturnsError_KeepsPendingPdpForRowFailed()
    {
        var cfg = Cfg();
        var book = new HarvestRowBook(cfg);
        book.AddPdpError(5, "77", "pdp");

        var err = await book.SaveAsync(new FakeSink(new HarvestItemRejectedException("x")), Item(6), CancellationToken.None);

        Assert.IsType<HarvestItemRejectedException>(err);
        Assert.Equal(new[] { 5 }, book.PendingPdpRows);   // không bỏ như lỗi hạ tầng: caller RowFailed sẽ ghi
        Assert.Null(cfg.LastCompletedRow);
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
        Assert.Empty(x.Cfg.SnapshotFailedRows());                     // notProduct chờ xem có phải chuỗi không
        await x.P.FlushPendingNotProductAsync(CancellationToken.None);   // hết khối → lỗi dòng thật
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
