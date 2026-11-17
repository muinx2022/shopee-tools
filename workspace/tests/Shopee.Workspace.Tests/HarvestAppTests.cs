using Shopee.Core.Coordination;
using Shopee.Core.Harvest;
using Shopee.Core.Scrape;
using Shopee.Modules.MultiBrave;
using Shopee.Workspace.Infrastructure;

namespace Shopee.Workspace.Tests;

/// <summary>
/// GĐ5 — exe Shopee Harvest: vai trò + đối số, kho tiến độ/cấu hình RIÊNG theo vai trò, profile Brave tách khỏi Scrape,
/// mutex chặn 2 bản, runtime outbox (sink báo UI + Nudge, đóng app có giới hạn thời gian). Các vai trò cũ giữ nguyên.
/// GĐ6 — Harvest chạy theo file Excel tự chọn, không BigSeller: tập đã xong toàn cục, màn chọn file / sheet, shell 3 tab.
/// </summary>
[Collection("Sequential")]
public sealed class HarvestAppTests
{
    // ── Vai trò / đối số ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Initialize_RoleHarvest_ConfiguresCorrectly()
    {
        try
        {
            WorkspaceAppConfig.Initialize(WorkspaceRole.Harvest);

            Assert.True(WorkspaceAppConfig.IsHarvest);
            Assert.True(WorkspaceAppConfig.IsScrapeOnly);     // dùng lại màn scrape-only
            Assert.True(WorkspaceAppConfig.ShowScrape);
            Assert.False(WorkspaceAppConfig.ShowImportUpdate);
            Assert.False(WorkspaceAppConfig.IsImportUpdateOnly);
            Assert.Equal("Shopee Harvest", WorkspaceAppConfig.WindowTitle);
            Assert.EndsWith("/Assets/harvest-icon.ico", WorkspaceAppConfig.WindowIcon);
        }
        finally { WorkspaceAppConfig.Initialize(WorkspaceRole.Full); }
    }

    [Theory]
    [InlineData(WorkspaceRole.Full)]
    [InlineData(WorkspaceRole.Scrape)]
    [InlineData(WorkspaceRole.ImportUpdate)]
    public void VaiTroCu_KhongPhaiHarvest(WorkspaceRole role)
    {
        try
        {
            WorkspaceAppConfig.Initialize(role);
            Assert.False(WorkspaceAppConfig.IsHarvest);
            Assert.DoesNotContain("harvest", WorkspaceAppConfig.WindowIcon);
            Assert.NotEqual("Shopee Harvest", WorkspaceAppConfig.WindowTitle);
        }
        finally { WorkspaceAppConfig.Initialize(WorkspaceRole.Full); }
    }

    /// <summary>ShopeeWorkspace.exe không gói extension shopee-harvest → không có đối số vào vai trò Harvest; chỉ
    /// ShopeeHarvest.exe (gọi <c>Program.Run(WorkspaceRole.Harvest)</c>) mới vào được.</summary>
    [Theory]
    [InlineData("--harvest")]
    [InlineData("--HARVEST")]
    [InlineData("--role harvest")]
    [InlineData("--role Harvest")]
    [InlineData("--foo --harvest")]
    [InlineData("--role")]                   // thiếu giá trị
    [InlineData("harvest")]                  // thiếu --
    public void InitializeFromArgs_KhongVaoHarvest_VeFull(string commandLine)
    {
        WorkspaceAppConfig.InitializeFromArgs(commandLine.Split(' '));
        Assert.Equal(WorkspaceRole.Full, WorkspaceAppConfig.Role);
    }

    // ── Kho theo vai trò ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoleStores_Harvest_DungKhoRieng_VaCoordOpHarvest()
    {
        var (progress, targets, op) = RoleStores.For(WorkspaceRole.Harvest);
        Assert.Same(ScrapeProgressStore.Harvest, progress);
        Assert.Same(ScrapeTargetConfigStore.Harvest, targets);
        Assert.Equal(CoordOp.Harvest, op);
        Assert.NotSame(ScrapeProgressStore.Shared, progress);
        Assert.NotSame(ScrapeTargetConfigStore.Shared, targets);
    }

    [Theory]
    [InlineData(WorkspaceRole.Full)]
    [InlineData(WorkspaceRole.Scrape)]
    [InlineData(WorkspaceRole.ImportUpdate)]
    public void RoleStores_VaiTroCu_DungKhoShared(WorkspaceRole role)
    {
        var (progress, targets, op) = RoleStores.For(role);
        Assert.Same(ScrapeProgressStore.Shared, progress);
        Assert.Same(ScrapeTargetConfigStore.Shared, targets);
        Assert.Equal(CoordOp.Scrape, op);
    }

    [Fact]
    public void RoleStores_DocTheoVaiTroHienHanh()
    {
        try
        {
            WorkspaceAppConfig.Initialize(WorkspaceRole.Harvest);
            Assert.Same(ScrapeProgressStore.Harvest, RoleStores.ScrapeProgress);
            Assert.Same(ScrapeTargetConfigStore.Harvest, RoleStores.ScrapeTargets);

            WorkspaceAppConfig.Initialize(WorkspaceRole.Scrape);
            Assert.Same(ScrapeProgressStore.Shared, RoleStores.ScrapeProgress);
            Assert.Same(ScrapeTargetConfigStore.Shared, RoleStores.ScrapeTargets);
        }
        finally { WorkspaceAppConfig.Initialize(WorkspaceRole.Full); }
    }

    // ── Profile Brave tách biệt ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ProfileHarvest_NamDuoiHarvestProfiles_KhacScrape()
    {
        Assert.Equal("profiles", MultiBraveRuntime.ProfileSubRoot(RunnerMode.Scrape));
        Assert.Equal("harvest-profiles", MultiBraveRuntime.ProfileSubRoot(RunnerMode.Harvest));

        Assert.Equal(Path.Combine("harvest-profiles", "acc-1"), ScrapeRunner.ProfileRelativePathOverride(RunnerMode.Harvest, "acc-1"));
        // Scrape: không ghi đè → InstanceConfig tự đặt "profiles/<id>" như cũ.
        Assert.Null(ScrapeRunner.ProfileRelativePathOverride(RunnerMode.Scrape, "acc-1"));
        var cfg = new OpenMultiBraveLauncherV3.InstanceConfig { Id = "acc-1" };
        cfg.EnsureProfileRelativePath();
        Assert.Equal(Path.Combine("profiles", "acc-1"), cfg.ProfileRelativePath);
        Assert.NotEqual(cfg.ProfileRelativePath, ScrapeRunner.ProfileRelativePathOverride(RunnerMode.Harvest, "acc-1"));
    }

    // ── Chặn 2 bản ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MutexName_RiengHarvest_OnDinh_TheoThuMucDuLieu()
    {
        var a = HarvestSingleInstance.MutexName(@"C:\Users\u1\AppData\Roaming\ShopeeBackend\Workspace");
        Assert.StartsWith(@"Global\ShopeeHarvest-", a);
        Assert.Equal(a, HarvestSingleInstance.MutexName(@"c:\users\U1\AppData\Roaming\ShopeeBackend\Workspace\"));
        Assert.NotEqual(a, HarvestSingleInstance.MutexName(@"C:\Users\u2\AppData\Roaming\ShopeeBackend\Workspace"));
        Assert.DoesNotContain("Scrape", a, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryAcquire_BanThuHaiBiChan_BanDauNhaThiMoLaiDuoc()
    {
        var name = @"Local\ShopeeHarvest-test-" + Guid.NewGuid().ToString("N");
        var first = HarvestSingleInstance.TryAcquire(name);
        Assert.NotNull(first);
        try
        {
            Assert.Null(HarvestSingleInstance.TryAcquire(name));
            // Tên khác (vd một app khác) không bị chặn.
            using var other = HarvestSingleInstance.TryAcquire(name + "-other");
            Assert.NotNull(other);
        }
        finally { first!.Dispose(); }

        using var again = HarvestSingleInstance.TryAcquire(name);
        Assert.NotNull(again);
    }

    // ── Runtime outbox ──────────────────────────────────────────────────────────────────────────────────────────

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "harvest-app-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static HarvestedItem Item(long itemId, int row) =>
        new("acc-1", "shop-1", "Shop1", row, $"https://shopee.vn/product/1/{itemId}", $"https://shopee.vn/x-i.1.{itemId}",
            itemId, 77,
            "{\"error\":null,\"data\":{\"item\":{\"item_id\":" + itemId + ",\"shop_id\":77,\"title\":\"SP\"},\"product_images\":{\"images\":[\"vn-a\"],\"video\":null}}}",
            "hook", DateTimeOffset.Now);

    [Fact]
    public async Task Runtime_SinkLuuXong_VaoOutbox_VaBaoStatsChanged()
    {
        var dir = NewTempDir();
        try
        {
            var rt = new HarvestRuntime(Path.Combine(dir, "outbox.db"), () => null);
            var changed = 0;
            rt.StatsChanged += () => Interlocked.Increment(ref changed);

            await rt.Sink.EnsureReadyAsync(CancellationToken.None);
            await rt.Sink.SaveAsync(Item(101, 2), CancellationToken.None);

            Assert.True(changed >= 1);
            var st = await rt.GetStatsAsync();
            Assert.Equal(1, st.Pending);
            Assert.Equal(0, st.Failed);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Runtime_Shutdown_KhongCoHub_TraVeTrongGioiHan_GiuMucChoDay()
    {
        var dir = NewTempDir();
        try
        {
            var rt = new HarvestRuntime(Path.Combine(dir, "outbox.db"), () => null);
            rt.Worker.Start();
            await rt.Sink.SaveAsync(Item(202, 3), CancellationToken.None);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var empty = await rt.ShutdownAsync(TimeSpan.FromSeconds(2));
            sw.Stop();

            Assert.False(empty);                                    // Hub chưa nối → mục vẫn nằm bền trong outbox
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"đóng mất {sw.Elapsed}");
            Assert.Equal(1, (await rt.GetStatsAsync()).Pending);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ── Workbook đang bị app Update ghi: không phải lỗi tài khoản ──

    [Fact]
    public void KhoaUpdate_DungJob_KhongCooldownTaiKhoan_LoiTkThuongVanCooldown()
    {
        var msg = HarvestWorkbookLockedException.MessageFor(@"D:\wb\nguon.xlsx");
        Assert.True(HarvestWorkbookLockedException.Is(msg));
        Assert.Contains("(nguon.xlsx)", msg);
        Assert.Equal(ScrapeFailureAction.StopJob, ScrapeFailurePolicy.Decide(true, false, msg));
        // Phiên Brave bắt ngoại lệ của vòng dòng → phase "error" + thông điệp; Classify giữ NGUYÊN thông điệp (có tiền tố).
        var chunk = ScrapeRunner.Classify(new OpenMultiBraveLauncherV3.InstanceConfig { RunnerPhase = "error", LastRunnerMessage = msg });
        Assert.True(chunk.Errored);
        Assert.False(chunk.IsCaptcha);
        Assert.Equal(msg, chunk.Reason);
        Assert.Equal(ScrapeFailureAction.StopJob, ScrapeFailurePolicy.Decide(chunk.Errored, chunk.IsCaptcha, chunk.Reason));
        Assert.StartsWith(HarvestWorkbookLockedException.Prefix, ScrapeFailurePolicy.FleetWideReason(msg));
        Assert.Equal(ScrapeFailureAction.Cooldown, ScrapeFailurePolicy.Decide(true, false, "Lỗi proxy"));
        Assert.False(HarvestWorkbookLockedException.Is("App Update đang ghi"));
    }

    // ── Tập SP đã lấy xong (outbox, TOÀN CỤC theo khoá SP — GĐ6) ──

    private static HarvestedItem ItemFor(string sourceLink, long itemId, int row, string sheet = "Shop1", long? shop = null) =>
        new(HarvestSource.AccountId, "", sheet, row, HarvestLinks.CleanProductUrl(sourceLink), sourceLink, itemId, shop,
            "{\"error\":null,\"data\":{\"item\":{\"item_id\":" + itemId + ",\"shop_id\":77,\"title\":\"SP\"},\"product_images\":{\"images\":[\"vn-a\"],\"video\":null}}}",
            "hook", DateTimeOffset.Now, RecordDone: true);

    [Fact]
    public async Task Outbox_LuuXong_GhiKhoaToanCuc_ResetXoaTheoKhoa()
    {
        var dir = NewTempDir();
        try
        {
            var ob = new HarvestOutbox(Path.Combine(dir, "outbox.db"));
            await ob.SaveAsync(ItemFor("https://shopee.vn/ao-i.10.100", 100, 2, "Shop A"), CancellationToken.None);
            await ob.SaveAsync(ItemFor("https://shp.ee/abc", 200, 3, "Shop A", shop: 77), CancellationToken.None);   // link rút gọn: thêm khoá từ JSON
            await ob.SaveAsync(ItemFor("https://shopee.vn/product/10/300", 300, 5, "Shop B"), CancellationToken.None);
            await ob.SaveAsync(ItemFor("https://shopee.vn/product/10/400", 400, 9) with { RecordDone = false }, CancellationToken.None);   // không theo dõi

            Assert.Equal(new HashSet<string> { "id:10.100", "url:shp.ee/abc", "id:77.200", "id:10.300" }, ob.DoneKeys());
            Assert.Equal(2, ob.ClearDone(["id:10.100", "url:shp.ee/abc", "id:khong-co"]));
            Assert.Equal(new HashSet<string> { "id:77.200", "id:10.300" }, ob.DoneKeys());
            Assert.Equal(4, (await ob.GetStatsAsync()).Pending);                     // dữ liệu SP chờ đẩy Hub giữ nguyên
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ── Màn Harvest: quét file + đếm theo tập toàn cục + Hub; Reset theo sheet ──

    private sealed class YesDialogs : Services.IDialogService
    {
        public List<string> Asked { get; } = [];
        public Task<bool> ConfirmAsync(string text, string caption = "", Services.DialogIcon icon = Services.DialogIcon.Question)
        {
            Asked.Add(text);
            return Task.FromResult(true);
        }
        public Task InfoAsync(string text, string caption = "", Services.DialogIcon icon = Services.DialogIcon.Info) => Task.CompletedTask;
        public void Notify(string text, string caption = "", Services.DialogIcon icon = Services.DialogIcon.Info) { }
    }

    private static string Workbook(string dir, string name, params (string Sheet, string[] Links)[] sheets)
    {
        var path = Path.Combine(dir, name);
        using var wb = new ClosedXML.Excel.XLWorkbook();
        foreach (var (sheet, links) in sheets)
        {
            var ws = wb.Worksheets.Add(sheet);
            ws.Cell(1, 1).Value = "Link";
            ws.Cell(1, 6).Value = "Tên";
            for (var i = 0; i < links.Length; i++) { ws.Cell(i + 2, 1).Value = links[i]; ws.Cell(i + 2, 6).Value = "SP"; }
        }
        wb.SaveAs(path);
        return path;
    }

    /// <summary>Hai sheet cùng chứa SP 1: lưu ở "Shop A" thì "Shop B" cũng tính là đã có; SP 3 Hub báo đã có (máy khác lấy) → đã có;
    /// Hub lỗi → chỉ tính cục bộ, có ghi chú. Reset chỉ các sheet đã tick: gỡ khoá SP của sheet đó (toàn cục).</summary>
    [Fact]
    public async Task ManHarvest_DemDaCoToanCucVaHub_ResetTheoSheetDaTick()
    {
        var dir = NewTempDir();
        var oldDialogs = Dialogs.Service;
        var dialogs = new YesDialogs();
        try
        {
            WorkspaceAppConfig.Initialize(WorkspaceRole.Harvest);
            var file = Workbook(dir, "links.xlsx",
                ("Shop A", ["https://shopee.vn/a-i.77.1", "https://shopee.vn/b-i.77.2"]),
                ("Shop B", ["https://shopee.vn/product/77/1", "https://shopee.vn/c-i.77.3"]));
            var rt = new HarvestRuntime(Path.Combine(dir, "outbox.db"), () => null);
            var hubIds = new List<long>();
            rt.KnownQuery = (ids, days, _) =>
            {
                hubIds.AddRange(ids);
                return Task.FromResult<IReadOnlyDictionary<long, DateTimeOffset>>(
                    ids.Where(i => i == 3).ToDictionary(i => i, _ => DateTimeOffset.UtcNow));
            };
            HarvestRuntime.SetCurrentForTest(rt);
            Dialogs.Service = dialogs;
            await rt.Sink.SaveAsync(ItemFor("https://shopee.vn/a-i.77.1", 1, 2, "Shop A"), CancellationToken.None);

            var settings = new HarvestSettingsStore(Path.Combine(dir, "harvest-settings.json"));
            settings.Current.WorkbookPath = file;
            var vm = new Modules.Harvest.HarvestViewModel(new Modules.Scrape.ScrapeViewModel(), settings);
            await vm.RefreshCommand.ExecuteAsync(null);

            Assert.Equal(["Shop A", "Shop B"], vm.Sheets.Select(s => s.Name));
            Assert.All(vm.Sheets, s => Assert.True(s.IsChecked));                  // mặc định tick hết
            var a = vm.Sheets[0];
            var b = vm.Sheets[1];
            Assert.Equal((2, 1, 1, 0), (a.TotalLinks, a.HaveCount, a.TodoCount, a.SkippedCount));
            Assert.Equal((2, 2, 0), (b.TotalLinks, b.HaveCount, b.TodoCount));       // SP 1 lấy ở Shop A + SP 3 Hub đã có
            Assert.DoesNotContain(1L, hubIds);                                       // SP đã có trên máy khỏi hỏi Hub
            Assert.Equal("", vm.HubNote);

            b.IsChecked = false;                                                     // bỏ tick → nhớ qua lần mở sau
            Assert.Contains(b.Key, new HarvestSettingsStore(settings.FilePath).Current.UncheckedSheetKeys);
            await vm.ResetCommand.ExecuteAsync(null);
            Assert.Single(dialogs.Asked);
            Assert.Empty(rt.Outbox.DoneKeys());                                      // khoá SP 1 của Shop A đã gỡ (toàn cục)
            a = vm.Sheets[0];
            b = vm.Sheets[1];
            Assert.Equal((0, 2), (a.HaveCount, a.TodoCount));
            Assert.Equal((1, 1), (b.HaveCount, b.TodoCount));                        // còn SP 3 nhờ Hub

            rt.KnownQuery = (_, _, _) => throw new System.Net.Http.HttpRequestException("Hub sập");
            await vm.RefreshCommand.ExecuteAsync(null);
            Assert.Contains("Hub sập", vm.HubNote);
            Assert.Equal((0, 2), (vm.Sheets[1].HaveCount, vm.Sheets[1].TodoCount));  // Hub lỗi: chỉ dựa vào cục bộ

            // Ô số nhập ngoài khoảng → ô hiện ngay giá trị đã kẹp (không hiện 0 mà chạy 30), lưu đúng giá trị đó.
            vm.MaxAgeDays = 0;
            Assert.Equal(HarvestPlanning.DefaultMaxAgeDays, vm.MaxAgeDays);
            vm.MaxAgeDays = 99_999;
            Assert.Equal(Shopee.Core.Coordination.HarvestedKnownLimits.MaxAgeDays, vm.MaxAgeDays);
            vm.MaxAgeDays = 7;
            vm.Processes = 0;
            vm.RowsPerAccount = -3;
            vm.RestMinSeconds = 300;
            vm.RestMaxSeconds = 10;                                                  // max < min → kéo lên bằng min
            Assert.Equal((7, 1, 1, 300, 300), (vm.MaxAgeDays, vm.Processes, vm.RowsPerAccount, vm.RestMinSeconds, vm.RestMaxSeconds));
            var saved = new HarvestSettingsStore(settings.FilePath).Current;
            Assert.Equal((7, 1, 1, 300, 300), (saved.MaxAgeDays, saved.Processes, saved.RowsPerAccount, saved.RestMinSeconds, saved.RestMaxSeconds));
        }
        finally
        {
            Dialogs.Service = oldDialogs;
            HarvestRuntime.SetCurrentForTest(null);
            WorkspaceAppConfig.Initialize(WorkspaceRole.Full);
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    // ── Vòng sheet của một lượt Harvest (phản biện GĐ6) ──

    private static readonly IReadOnlyList<Modules.Scrape.HarvestSheetJob> BaSheet =
        [new(@"C:\f.xlsx", "Shop A"), new(@"C:\f.xlsx", "Shop B"), new(@"C:\f.xlsx", "Shop C")];

    [Fact]
    public async Task VongSheet_ChayDungThuTu_ChungRunId_LoiDocSheetBoQuaSheetDo()
    {
        var calls = new List<(string Sheet, string RunId)>();
        var changed = new List<string>();
        var done = await Modules.Scrape.ScrapeViewModel.RunSheetsAsync(BaSheet, "run-1", (job, runId) =>
        {
            calls.Add((job.Sheet, runId));
            // Shop A đọc lỗi (sheet hỏng) → bỏ riêng sheet đó.
            return Task.FromResult(job.Sheet == "Shop A"
                ? Modules.Scrape.ScrapeViewModel.EndAfterReadError(new InvalidOperationException("Không tìm thấy sheet"))
                : Modules.Scrape.ScrapeViewModel.EndAfterRun(jobFatal: false, cancelled: false));
        }, CancellationToken.None, new Modules.Scrape.HarvestRunCallbacks { Changed = j => changed.Add(j.Sheet) });

        Assert.Equal(3, done);
        Assert.Equal(["Shop A", "Shop B", "Shop C"], calls.Select(c => c.Sheet));
        Assert.All(calls, c => Assert.Equal("run-1", c.RunId));                     // mọi sheet dùng CHUNG một lượt
        Assert.Equal(["Shop A", "Shop B", "Shop C"], changed);
    }

    [Fact]
    public async Task VongSheet_LoiHaTangToanCucHoacFileBiKhoa_DungCaLuot()
    {
        foreach (var stop in new[]
                 {
                     Modules.Scrape.ScrapeViewModel.EndAfterRun(jobFatal: true, cancelled: false),          // key proxy chết / chỗ lưu hỏng
                     Modules.Scrape.ScrapeViewModel.EndAfterReadError(new HarvestWorkbookLockedException(@"C:\f.xlsx")),
                 })
        {
            var calls = new List<string>();
            var done = await Modules.Scrape.ScrapeViewModel.RunSheetsAsync(BaSheet, "r", (job, _) =>
            {
                calls.Add(job.Sheet);
                return Task.FromResult(job.Sheet == "Shop B" ? stop : Modules.Scrape.ScrapeViewModel.HarvestSheetEnd.Next);
            }, CancellationToken.None);
            Assert.Equal(["Shop A", "Shop B"], calls);                                // Shop C không chạy
            Assert.Equal(2, done);
        }
        Assert.Equal(Modules.Scrape.ScrapeViewModel.HarvestSheetEnd.StopRun,
            Modules.Scrape.ScrapeViewModel.EndAfterRun(jobFatal: false, cancelled: true));
        Assert.Equal(Modules.Scrape.ScrapeViewModel.HarvestSheetEnd.Next,
            Modules.Scrape.ScrapeViewModel.EndAfterRun(jobFatal: false, cancelled: false));
    }

    [Fact]
    public async Task VongSheet_HuyThiDungNgay_KhongChaySheetSau()
    {
        using var cts = new CancellationTokenSource();
        var calls = new List<string>();
        var done = await Modules.Scrape.ScrapeViewModel.RunSheetsAsync(BaSheet, "r", (job, _) =>
        {
            calls.Add(job.Sheet);
            cts.Cancel();                                                            // bấm Dừng giữa sheet đầu
            return Task.FromResult(Modules.Scrape.ScrapeViewModel.HarvestSheetEnd.Next);
        }, cts.Token);
        Assert.Equal(["Shop A"], calls);
        Assert.Equal(1, done);

        calls.Clear();
        Assert.Equal(0, await Modules.Scrape.ScrapeViewModel.RunSheetsAsync(BaSheet, "r", (job, _) =>
        {
            calls.Add(job.Sheet);
            return Task.FromResult(Modules.Scrape.ScrapeViewModel.HarvestSheetEnd.Next);
        }, cts.Token));                                                              // đã huỷ trước khi bắt đầu
        Assert.Empty(calls);
    }

    // ── Nối dây (đọc mã nguồn): các điểm gọi không bị gỡ âm thầm ──

    private static string Src(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ShopeeWorkspace.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, relative));
    }

    private static string Between(string text, string from, string to)
    {
        var i = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(i >= 0, $"không thấy: {from}");
        var j = text.IndexOf(to, i + from.Length, StringComparison.Ordinal);
        Assert.True(j > i, $"không thấy sau đó: {to}");
        return text[i..j];
    }

    [Fact]
    public void NoiDay_JobHarvest_DungLaiTuTapToanCucVaHub_GhiDoneLoi_KhongDungTienDoKhiHuySom()
    {
        var job = Src(Path.Combine("src", "Shopee.Workspace", "Modules", "Scrape", "ScrapeViewModel.Harvest.cs"));
        var body = Between(job, "private async Task<HarvestSheetEnd> HarvestSheetBodyAsync", "catch (OperationCanceledException)");
        Assert.Contains("rt.Outbox.DoneKeys(HarvestPlanning.DoneSince(DateTimeOffset.UtcNow, settings.MaxAgeDays))", body);
        Assert.Contains("HarvestPlanning.AskHubAsync(rt.KnownQuery,", body);
        Assert.Contains("store.ReplaceProgress(acct, key, label, totalRows, plan.Completed, plan.Skipped);", body);
        Assert.Contains("RecordDone: true,", body);
        Assert.Contains("FailureSink: rt.Outbox,", body);
        Assert.Contains("RunId: runId,", body);
        Assert.Contains("bigSellerAccountId: acct", body);                         // account_id gửi Hub = hằng "harvest"
        Assert.Contains("mode: RunnerMode.Harvest", body);
        Assert.Contains("plan.Remaining", body);
        Assert.DoesNotContain("BeginFresh(", body);                                 // Chạy không xoá tập đã có
        Assert.Contains("if (progressTouched)", Between(job, "catch (OperationCanceledException)", "finally"));
        Assert.Equal("harvest", HarvestSource.AccountId);
    }

    /// <summary>Đường Scrape (tài khoản BigSeller) không còn nhánh Harvest nào; engine ghi tập đã xong theo cờ RecordDone.</summary>
    [Fact]
    public void NoiDay_DuongScrape_KhongConNhanhHarvest()
    {
        var vm = Src(Path.Combine("src", "Shopee.Workspace", "Modules", "Scrape", "ScrapeViewModel.cs"));
        var run = Between(vm, "private async Task RunOneJobAsync", "/// <summary>Tạo + phóng 1 job");
        foreach (var harvestCode in new[] { "IsHarvest", "HarvestRuntime", "HarvestWorkbook", "HarvestDone", "var harvest", "RunnerMode.Harvest" })
        {
            Assert.DoesNotContain(harvestCode, run);
            Assert.DoesNotContain(harvestCode, Between(vm, "private bool StartJob", "/// <summary>true nếu đang có job LIVE"));
        }
        Assert.DoesNotContain("IsHarvest", Between(vm, "private static bool ValidateTarget", "[RelayCommand(CanExecute = nameof(IsBusy))]"));
        var proc = Src(Path.Combine("src", "Shopee.Module.MultiBrave", "Engine", "HarvestRowProcessor.cs"));
        Assert.Contains("_options.RecordDone);", Between(proc, "var harvested = new HarvestedItem(", "var saveError"));
        var loop = Src(Path.Combine("src", "Shopee.Module.MultiBrave", "Engine", "HarvestRunnerLoop.cs"));
        var flushLine = System.Text.RegularExpressions.Regex.Match(loop, @"^[ \t]+await processor\.FlushPendingNotProductAsync\(cancellationToken\)",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        Assert.True(flushLine.Success, "phải GỌI (không comment) FlushPendingNotProductAsync");
        Assert.True(flushLine.Index < loop.IndexOf("processor.CompleteBlock(totalLinks, sheet);", StringComparison.Ordinal),
            "hết khối phải xả chuỗi notProduct chưa tới ngưỡng TRƯỚC CompleteBlock");
        Assert.DoesNotContain("BigSeller", Between(loop, "var accountHeader =", ";"));
        var app = Src(Path.Combine("src", "Shopee.Workspace", "App.xaml.cs"));
        var hooks = Between(app, "private void RunShutdownHooks()", "private static void HookBindingTrace()");
        Assert.Contains("BigSellerStore.Shared.Save()", Between(hooks, "if (!WorkspaceAppConfig.IsHarvest)", "}"));
    }

    [Fact]
    public void NoiDay_HarvestDocLinkQuaBoDocRieng_KhongQuaScrapeWorkbookOpen()
    {
        var loop = Src(Path.Combine("src", "Shopee.Module.MultiBrave", "Engine", "HarvestRunnerLoop.cs"));
        Assert.Contains("HarvestWorkbookReader.TotalDataRowsAsync(workbookPath, sheet, ct)", loop);
        Assert.Contains("HarvestWorkbookReader.FetchLinksAsync(workbookPath, sheet, startRow, endRow, ct)", loop);
        Assert.Contains("endRow = useHubData", loop);
        Assert.Contains("var fetch = useHubData", loop);
        Assert.DoesNotContain("ScrapeWorkbook.FetchLinks(", loop);
        Assert.DoesNotContain("ScrapeWorkbook.TotalDataRows(", loop);
        Assert.DoesNotContain("Thread.Sleep", Src(Path.Combine("src", "Shopee.Core", "Harvest", "HarvestWorkbookReader.cs")));
    }

    // ── Shell: Harvest không có gì của BigSeller; Scrape giữ nguyên tab + danh sách tài khoản ──

    private static readonly object AppGate = new();

    private static void OnSta(Action body)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                lock (AppGate)
                {
                    if (System.Windows.Application.Current is null)
                    {
                        try { new App().InitializeComponent(); }
                        catch (InvalidOperationException) { /* lớp test khác vừa tạo App */ }
                    }
                }
                body();
            }
            catch (Exception ex) { error = ex; }
            finally { WorkspaceAppConfig.Initialize(WorkspaceRole.Full); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new AggregateException(error);
    }

    /// <summary>Mọi chữ người dùng nhìn thấy trên một cây giao diện: TextBlock (cả Run), nội dung chữ, tooltip, tiêu đề cột, ô nhập.</summary>
    private static List<string> VisibleTexts(System.Windows.DependencyObject root)
    {
        var texts = new List<string>();
        var seen = new HashSet<System.Windows.DependencyObject>();
        var stack = new Stack<System.Windows.DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            if (!seen.Add(d)) continue;
            if (d is System.Windows.Controls.TextBlock tb) texts.Add(tb.Text);
            if (d is System.Windows.Controls.ContentControl { Content: string s }) texts.Add(s);
            if (d is System.Windows.FrameworkElement { ToolTip: string tip }) texts.Add(tip);
            if (d is System.Windows.Controls.DataGrid grid)
                foreach (var col in grid.Columns) texts.Add(col.Header?.ToString() ?? "");
            if (d is System.Windows.Controls.TextBox box) texts.Add(box.Text);
            foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(d).OfType<System.Windows.DependencyObject>()) stack.Push(child);
            if (d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D)
                for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
                    stack.Push(System.Windows.Media.VisualTreeHelper.GetChild(d, i));
        }
        return texts;
    }

    private static List<string> RibbonTexts(ViewModels.ShellViewModel shell)
    {
        var texts = new List<string>();
        foreach (var tab in shell.Tabs)
        {
            texts.Add(tab.Title);
            foreach (var g in tab.Groups)
            {
                texts.Add(g.Title);
                foreach (var item in g.Items)
                {
                    if (item is ViewModels.RibbonScreenItem s) { texts.Add(s.Title); texts.Add(s.ToolTip ?? ""); }
                    if (item is ViewModels.RibbonActionItem a) { texts.Add(a.Title); texts.Add(a.ToolTip ?? ""); }
                }
            }
        }
        return texts;
    }

    [Fact]
    public void ShellHarvest_BaTab_KhongCoBigSeller_ManChinhChonFileExcel()
    {
        OnSta(() =>
        {
            WorkspaceAppConfig.Initialize(WorkspaceRole.Harvest);
            var shell = new ViewModels.ShellViewModel();
            Assert.Equal(["Lấy dữ liệu", "Tài khoản & Proxy", "Cài đặt"], shell.Tabs.Select(t => t.Title));
            Assert.DoesNotContain(RibbonTexts(shell), s => s.Contains("BigSeller", StringComparison.OrdinalIgnoreCase));
            Assert.False(shell.ShowBigSellerStatus);
            Assert.Throws<InvalidOperationException>(() => shell.BigSeller);
            Assert.NotNull(shell.Harvest);
            Assert.Same(shell.Harvest, shell.CurrentScreen);

            var view = new Modules.Harvest.HarvestView { DataContext = shell.Harvest };
            view.Measure(new System.Windows.Size(1500, 1000));
            view.Arrange(new System.Windows.Rect(0, 0, 1500, 1000));
            view.UpdateLayout();
            var texts = VisibleTexts(view);
            Assert.Contains("Chọn file Excel", texts);
            Assert.Contains("Lấy lại nếu dữ liệu cũ hơn", texts);
            Assert.Contains("Đẩy lại mục lỗi", texts);
            Assert.Contains("Chạy / Tiếp tục", texts);
            Assert.DoesNotContain(texts, s => s.Contains("BigSeller", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void ShellScrape_VanDuTab_VaDanhSachTaiKhoan()
    {
        OnSta(() =>
        {
            WorkspaceAppConfig.Initialize(WorkspaceRole.Scrape);
            var shell = new ViewModels.ShellViewModel();
            Assert.Equal(["Workspace", "Cấu hình BigSeller", "Tài khoản & Proxy", "Cài đặt"], shell.Tabs.Select(t => t.Title));
            Assert.Contains("Scrape Multi", RibbonTexts(shell));
            Assert.Null(shell.Harvest);
            Assert.True(shell.ShowBigSellerStatus);
            Assert.Same(shell.Workspace, shell.CurrentScreen);
            var count = Shopee.Core.BigSeller.BigSellerStore.Shared.Accounts.Count;
            Assert.Equal(count, shell.Workspace.Accounts.Count);
            Assert.Equal(count, shell.Workspace.Scrape.ScrapeTargets.Count);
            Assert.Equal(count, shell.BigSeller.Items.Count);
        });
    }

    // ── Bộ đếm tổng kết cuối khối qua watchdog mở lại ─────────────────────────────────────────────────────────

    [Fact]
    public void TongKet_KhoiChayTuDau_DatLaiBoDem_MoLaiGiuaKhoi_GiuSoDaDem()
    {
        var cfg = new OpenMultiBraveLauncherV3.InstanceConfig { StartRow = 10, EndRow = 20 };
        cfg.HarvestSavedRows = 7;
        cfg.HarvestFailedRows = 2;

        OpenMultiBraveLauncherV3.HarvestRunnerLoop.BeginBlockTally(cfg, 15);   // watchdog mở lại từ dòng 15
        Assert.Equal((7, 2), (cfg.HarvestSavedRows, cfg.HarvestFailedRows));
        Assert.Equal(10, OpenMultiBraveLauncherV3.HarvestRunnerLoop.BlockStartRow(cfg, 15));

        OpenMultiBraveLauncherV3.HarvestRunnerLoop.BeginBlockTally(cfg, 10);   // khối mới chạy từ đầu
        Assert.Equal((0, 0), (cfg.HarvestSavedRows, cfg.HarvestFailedRows));
    }

    // ── Dòng Hub của bảng tổng kết cuối khối: lỗi/chậm không làm hỏng kết thúc khối ─────────────────────────────

    [Fact]
    public async Task HubStatus_KhongCungCap_Null()
    {
        Assert.Null(await OpenMultiBraveLauncherV3.HarvestRunnerLoop.TryHubStatusAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task HubStatus_TraChu_DungChuDo()
    {
        var s = await OpenMultiBraveLauncherV3.HarvestRunnerLoop.TryHubStatusAsync(
            _ => Task.FromResult<string?>("Hub: đã nhận 1 · chờ đẩy 0 · lỗi 0"), CancellationToken.None);
        Assert.Equal("Hub: đã nhận 1 · chờ đẩy 0 · lỗi 0", s);
    }

    [Fact]
    public async Task HubStatus_Nem_Null()
    {
        var s = await OpenMultiBraveLauncherV3.HarvestRunnerLoop.TryHubStatusAsync(
            _ => throw new IOException("outbox hỏng"), CancellationToken.None);
        Assert.Null(s);
    }

    [Fact]
    public async Task HubStatus_Cham_QuaHan_Null_KhongCho()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var s = await OpenMultiBraveLauncherV3.HarvestRunnerLoop.TryHubStatusAsync(
            async _ => { await Task.Delay(TimeSpan.FromSeconds(30)); return "muộn"; },
            CancellationToken.None, timeout: TimeSpan.FromMilliseconds(100));
        Assert.Null(s);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task HubStatus_JobBiHuy_NemHuy()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            OpenMultiBraveLauncherV3.HarvestRunnerLoop.TryHubStatusAsync(
                async ct => { await Task.Delay(1000, ct); return "x"; }, cts.Token));
    }

    // ── Bộ làm mới 3 số không kẹt số cũ ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresher_YeuCauDenGiuaLuot_DuocChayBu()
    {
        var runs = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var r = new CoalescingRefresher(async () =>
        {
            if (Interlocked.Increment(ref runs) == 1) await gate.Task;   // lượt 1 bị giữ lại giữa chừng
        });

        var first = r.RequestAsync();
        var second = r.RequestAsync();     // đến lúc lượt 1 đang chạy → phải được chạy bù, không mất
        var third = r.RequestAsync();      // gộp với yêu cầu 2
        gate.SetResult();
        await Task.WhenAll(first, second, third);

        Assert.Equal(2, Volatile.Read(ref runs));
    }

    [Fact]
    public async Task Refresher_YeuCauDenKhiVuaXongChuaNhaCo_KhongBiMat()
    {
        var runs = 0;
        var r = new CoalescingRefresher(() => { Interlocked.Increment(ref runs); return Task.CompletedTask; });
        Task? late = null;
        r.AfterDrainForTest = () =>
        {
            if (late is null) late = r.RequestAsync();   // thấy cờ bận → bỏ về; lượt đang chạy phải gánh bù
        };

        await r.RequestAsync();
        await late!;

        Assert.Equal(2, runs);
    }

    [Fact]
    public async Task Refresher_YeuCauLienTiepSauKhiXong_MoiYeuCauDeuChay()
    {
        var runs = 0;
        var r = new CoalescingRefresher(() => { Interlocked.Increment(ref runs); return Task.CompletedTask; });

        await r.RequestAsync();
        await r.RequestAsync();

        Assert.Equal(2, runs);
    }

    [Fact]
    public async Task Refresher_LuotLoi_TuThuLaiSau()
    {
        var runs = 0;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var r = new CoalescingRefresher(() =>
        {
            if (Interlocked.Increment(ref runs) == 1) throw new IOException("outbox bận");
            done.TrySetResult();
            return Task.CompletedTask;
        }, retryDelay: TimeSpan.FromMilliseconds(20));

        await r.RequestAsync();            // lỗi → không ném ra ngoài

        var finished = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(done.Task, finished);  // không có yêu cầu mới vẫn tự đọc lại
        Assert.Equal(2, Volatile.Read(ref runs));
    }
}
