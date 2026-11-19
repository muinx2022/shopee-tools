using System.Net.Http;
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

    // ── GĐ7.1: dải Từ dòng / Đến dòng từng sheet ──

    /// <summary>Mặc định theo dòng dữ liệu đầu (2 có tiêu đề / 1 không), đếm theo dải, kẹp khi nhập, Đến &lt; Từ thì báo và giữ dải cũ,
    /// nhớ theo (file, sheet), job mang dải, áp dải cho sheet đã tick, Reset chỉ gỡ SP trong dải.</summary>
    [Fact]
    public async Task DaiDong_MacDinh_DemTheoDai_Kep_Nho_Job_ApNhieuSheet_ResetTrongDai()
    {
        var dir = NewTempDir();
        var oldDialogs = Dialogs.Service;
        var dialogs = new YesDialogs();
        try
        {
            WorkspaceAppConfig.Initialize(WorkspaceRole.Harvest);
            var links = Enumerable.Range(1, 6).Select(i => $"https://shopee.vn/a-i.77.{i}").ToArray();   // Excel dòng 2..7
            var file = Workbook(dir, "links.xlsx", ("Shop A", links), ("Shop B", links));
            using (var wb = new ClosedXML.Excel.XLWorkbook(file))
            {
                var ws = wb.Worksheets.Add("Khong tieu de");                       // dòng 1 đã là link
                for (var i = 1; i <= 3; i++) ws.Cell(i, 1).Value = $"https://shopee.vn/b-i.88.{i}";
                wb.Save();
            }
            var rt = new HarvestRuntime(Path.Combine(dir, "outbox.db"), () => null);
            HarvestRuntime.SetCurrentForTest(rt);
            Dialogs.Service = dialogs;
            await rt.Sink.SaveAsync(ItemFor(links[0], 1, 2, "Shop A"), CancellationToken.None);   // SP Excel dòng 2 đã lấy

            var settings = new HarvestSettingsStore(Path.Combine(dir, "harvest-settings.json"));
            settings.Current.WorkbookPath = file;
            var vm = new Modules.Harvest.HarvestViewModel(new Modules.Scrape.ScrapeViewModel(), settings, hub: new FakeHubPort(),
                suiteHubConfigPath: Path.Combine(dir, "khong-co.json"));
            await vm.RefreshCommand.ExecuteAsync(null);
            var (a, b, c) = (vm.Sheets[0], vm.Sheets[1], vm.Sheets[2]);
            Assert.Equal((2, 0, 6, 1, 5), (a.FromRow, a.ToRow, a.TotalLinks, a.HaveCount, a.TodoCount));   // mặc định: dòng dữ liệu đầu → hết
            Assert.Equal((1, 0, 3), (c.FromRow, c.ToRow, c.TotalLinks));             // không tiêu đề → từ dòng 1

            // Đếm theo dải (Excel 4..6 = SP 3..5; SP 1 ở dòng 2 nằm ngoài dải).
            a.FromRow = 4;
            a.ToRow = 6;
            Assert.Equal((3, 0, 3, 0), (a.TotalLinks, a.HaveCount, a.TodoCount, a.SkippedCount));
            c.ToRow = 2;
            Assert.Equal(2, c.TotalLinks);

            // Kẹp ngay khi nhập: Từ < dòng dữ liệu đầu → dòng dữ liệu đầu; Đến âm → 0.
            b.FromRow = 1;
            Assert.Equal(2, b.FromRow);
            b.ToRow = -7;
            Assert.Equal(0, b.ToRow);
            // Đến < Từ (≠ 0) → báo lỗi, giữ dải cũ (không tự thành "tới hết").
            a.ToRow = 3;
            Assert.Equal((4, 6), (a.FromRow, a.ToRow));
            Assert.Contains("nhỏ hơn Từ dòng", vm.Status);
            a.FromRow = 9;
            Assert.Equal((4, 6), (a.FromRow, a.ToRow));
            Assert.Equal(3, a.TotalLinks);

            // Nhớ theo (file, sheet): lần mở sau (kho + VM mới) hiện lại đúng dải; dải mặc định không ghi.
            var reopened = new HarvestSettingsStore(settings.FilePath);
            Assert.Equal((4, 6), (reopened.Current.SheetRanges[a.Key].FromRow, reopened.Current.SheetRanges[a.Key].ToRow));
            Assert.False(reopened.Current.SheetRanges.ContainsKey(b.Key));
            var vm2 = new Modules.Harvest.HarvestViewModel(new Modules.Scrape.ScrapeViewModel(), reopened, hub: new FakeHubPort(),
                suiteHubConfigPath: Path.Combine(dir, "khong-co.json"));
            await vm2.RefreshCommand.ExecuteAsync(null);
            Assert.Equal((4, 6, 3), (vm2.Sheets[0].FromRow, vm2.Sheets[0].ToRow, vm2.Sheets[0].TotalLinks));
            Assert.Equal((1, 2), (vm2.Sheets[2].FromRow, vm2.Sheets[2].ToRow));

            // Lượt chạy nhận đúng dải từng sheet.
            Assert.Equal(new List<(string, int, int)> { ("Shop A", 4, 6), ("Shop B", 2, 0), ("Khong tieu de", 1, 2) },
                vm.CheckedJobs().Select(j => (j.Sheet, j.FromRow, j.ToRow)).ToList());

            // Áp dải của sheet đang chọn cho các sheet đã tick (mỗi sheet tự kẹp theo dòng dữ liệu đầu).
            c.IsChecked = false;
            vm.SelectedSheet = a;
            Assert.True(vm.ApplyRangeToCheckedCommand.CanExecute(null));
            vm.ApplyRangeToCheckedCommand.Execute(null);
            Assert.Equal((4, 6, 3), (b.FromRow, b.ToRow, b.TotalLinks));
            Assert.Equal((1, 2), (c.FromRow, c.ToRow));                              // sheet không tick không đổi
            Assert.Equal(6, new HarvestSettingsStore(settings.FilePath).Current.SheetRanges[b.Key].ToRow);

            // Reset chỉ gỡ SP TRONG DẢI: SP 1 (Excel dòng 2, ngoài dải 4..6) vẫn là đã lấy.
            await vm.ResetCommand.ExecuteAsync(null);
            Assert.Contains("TRONG DẢI", Assert.Single(dialogs.Asked));
            Assert.Contains("id:77.1", rt.Outbox.DoneKeys());
        }
        finally
        {
            Dialogs.Service = oldDialogs;
            HarvestRuntime.SetCurrentForTest(null);
            WorkspaceAppConfig.Initialize(WorkspaceRole.Full);
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void NoiDay_JobHarvest_ChiChayTrongDai_CungHamThuDaiVoiManDem()
    {
        var job = Src(Path.Combine("src", "Shopee.Workspace", "Modules", "Scrape", "ScrapeViewModel.Harvest.cs"));
        var body = Between(job, "private async Task<HarvestSheetEnd> HarvestSheetBodyAsync", "catch (OperationCanceledException)");
        var restrict = body.IndexOf("range = HarvestRowRange.Restrict(range, job.FromRow, job.ToRow);", StringComparison.Ordinal);
        Assert.True(restrict >= 0, "job phải thu dải trước khi dựng tiến độ");
        Assert.True(restrict < body.IndexOf("startRow = range.DenseStart;", StringComparison.Ordinal));
        Assert.True(restrict < body.IndexOf("HarvestPlanning.Build(range,", StringComparison.Ordinal));
        // Số dòng hiển thị = số dòng Excel: bảng ánh xạ chụp từ đúng lần đọc đầu job, truyền vào engine; log bỏ qua / tổng kết dùng nó.
        Assert.Contains("var showRow = HarvestRowRange.ExcelRowMap(range);", body);
        Assert.Contains("DisplayRow: showRow", body);
        Assert.Contains("⚠ Dòng {showRow(row)} chưa lấy được", body);
        Assert.Contains("skipped.Take(20).Select(showRow)", body);
        var loop = Src(Path.Combine("src", "Shopee.Module.MultiBrave", "Engine", "HarvestRunnerLoop.cs"));
        Assert.Contains("options.Row(BlockStartRow(config, startRow)), options.Row(endRow.Value)", loop);   // "✓ Xong dòng a–b"
        var runner = Src(Path.Combine("src", "Shopee.Module.MultiBrave", "ScrapeRunner.cs"));
        Assert.Contains("SlotAssigned?.Invoke(key, spec.Label, $\"{R(from)}–{R(to)}\");", runner);          // cột "Dòng" bảng Tiến trình
        var vm = Src(Path.Combine("src", "Shopee.Workspace", "Modules", "Harvest", "HarvestViewModel.cs"));
        Assert.Contains("HarvestRowRange.Restrict(full, row.FromRow, row.ToRow)", vm);
        Assert.Contains("await Scrape.RunHarvestAsync(CheckedJobs(path, rows),", vm);
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
            Assert.Contains("Cài đặt Hub", texts);
            Assert.DoesNotContain(texts, s => s.Contains("BigSeller", StringComparison.OrdinalIgnoreCase));

            // Tab Cài đặt: MỘT màn "Cài đặt" riêng của Harvest — Kết nối Hub ở đầu, rồi Hiệu năng / Thu phóng / Kho tk / Extension;
            // không có AI, không BigSeller.
            var settingsTab = shell.Tabs.Single(t => t.Title == "Cài đặt");
            var screens = settingsTab.Groups.SelectMany(g => g.Items).OfType<ViewModels.RibbonScreenItem>().ToList();
            var only = Assert.Single(screens);
            Assert.Equal("Cài đặt", only.Title);
            var settings = Assert.IsType<Modules.Harvest.HarvestSettingsViewModel>(only.ScreenVm);
            Assert.Same(shell.HarvestSettings, settings);
            Assert.Same(settings.Hub, shell.HarvestHub);
            Assert.DoesNotMatch(@"\bAI\b|OpenAI|API key", only.ToolTip ?? "");

            var setView = new Modules.Harvest.HarvestSettingsView { DataContext = settings };
            setView.Measure(new System.Windows.Size(1200, 2400));
            setView.Arrange(new System.Windows.Rect(0, 0, 1200, 2400));
            setView.UpdateLayout();
            var setTexts = VisibleTexts(setView);
            foreach (var must in new[] { "Kết nối Hub", "Lấy cấu hình từ Shopee Suite", "Kiểm tra kết nối", "Hiệu năng", "Lưu hiệu năng",
                         "Thu phóng giao diện", "Kho tài khoản Shopee & Proxy", "Nạp lại từ DB", "Extension Shopee Harvest", "Mở thư mục" })
                Assert.Contains(must, setTexts);
            Assert.DoesNotContain(setTexts, s => System.Text.RegularExpressions.Regex.IsMatch(s ?? "", @"\bAI\b|OpenAI|Anthropic|Gemini|API key|Prompt"));
            Assert.DoesNotContain(setTexts, s => s.Contains("BigSeller", StringComparison.OrdinalIgnoreCase));
            // Kết nối Hub là nhóm ĐẦU của màn (nút "Cài đặt Hub" mở màn này là thấy ngay).
            var hubTop = TopOf(setView, "Kết nối Hub");
            Assert.All(new[] { "Hiệu năng", "Thu phóng giao diện", "Kho tài khoản Shopee & Proxy", "Extension Shopee Harvest" },
                t => Assert.True(TopOf(setView, t) > hubTop, $"{t} phải nằm dưới Kết nối Hub"));
            SaveRender(setView, 1200, 2400, "harvest-settings.png");

            // Nút "Cài đặt Hub" ở màn Lấy dữ liệu → tab Cài đặt, màn Cài đặt của Harvest.
            shell.Harvest!.GoToHubSettingsCommand.Execute(null);
            Assert.Same(settingsTab, shell.SelectedTab);
            Assert.Same(settings, shell.CurrentScreen);
        });
    }

    /// <summary>Toạ độ đỉnh (so với <paramref name="root"/>) của TextBlock đầu tiên có đúng chữ <paramref name="text"/>.</summary>
    private static double TopOf(System.Windows.FrameworkElement root, string text)
    {
        var tb = Descendants<System.Windows.Controls.TextBlock>(root).First(t => t.Text == text);
        return tb.TransformToAncestor(root).Transform(new System.Windows.Point(0, 0)).Y;
    }

    /// <summary>Lưu ảnh màn đã dựng khi có biến môi trường <c>HARVEST_RENDER_DIR</c> (xem tay).</summary>
    private static void SaveRender(System.Windows.FrameworkElement view, double w, double h, string name)
    {
        var outDir = Environment.GetEnvironmentVariable("HARVEST_RENDER_DIR");
        if (string.IsNullOrEmpty(outDir)) return;
        Directory.CreateDirectory(outDir);
        var bg = new System.Windows.Controls.Border { Background = System.Windows.Media.Brushes.White, Width = w, Height = h };
        var host = new System.Windows.Controls.Grid();
        bg.Child = host;
        bg.Measure(new System.Windows.Size(w, h));
        bg.Arrange(new System.Windows.Rect(0, 0, w, h));
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)w, (int)h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(bg);
        bmp.Render(view);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(outDir, name));
        enc.Save(fs);
    }

    /// <summary>Lỗi user báo (cửa sổ 1500 px @ 150% = 1000 DIP, dữ liệu thật: tên file dài, sheet có ghi chú dài): màn bị dựng RỘNG
    /// hơn cửa sổ — mô tả không xuống dòng, dải cấu hình không xuống dòng, phần "Hub: đã nhận…" bên phải bị cắt. Ở 1000 DIP: màn
    /// không đòi rộng hơn cửa sổ, mọi chữ / ô / nút nằm trong bề rộng.</summary>
    [Fact]
    public async Task ManHarvest_1000Dip_DuLieuThat_KhongTranNgang()
    {
        var dir = NewTempDir();
        try
        {
            var longDir = Directory.CreateDirectory(Path.Combine(dir, "Thư mục rất dài của người dùng - danh sách sản phẩm Shopee resell")).FullName;
            var sheets = Enumerable.Range(1, 12).Select(i => ($"Shop tên khá dài số {i}", new[]
            {
                $"https://shopee.vn/a-i.77.{i}", "hết hàng — ghi chú dài của người nhập liệu", "https://shopee.vn/x", $"https://shopee.vn/b-i.77.{i + 100}",
            })).ToArray();
            var file = Workbook(longDir, "Sản phẩm shopee resell - 08.10.2026 bản cập nhật cuối cùng.xlsx", sheets);
            var settings = new HarvestSettingsStore(Path.Combine(dir, "harvest-settings.json"));
            settings.Current.WorkbookPath = file;
            settings.Current.RestMinSeconds = 60;
            settings.Current.RestMaxSeconds = 120;
            WorkspaceAppConfig.Initialize(WorkspaceRole.Harvest);
            var vm = new Modules.Harvest.HarvestViewModel(new Modules.Scrape.ScrapeViewModel(), settings,
                hub: new FakeHubPort(), suiteHubConfigPath: Path.Combine(dir, "khong-co.json"));
            await vm.RefreshCommand.ExecuteAsync(null);
            Assert.All(vm.Sheets, s => Assert.NotEqual("", s.Warning));             // ca có ghi chú dài
            OnSta(() =>
            {
                WorkspaceAppConfig.Initialize(WorkspaceRole.Harvest);
                const double w = 1000, h = 700;
                var view = new Modules.Harvest.HarvestView { DataContext = vm };
                LayoutLikeWindow(view, w, h);
                var wide = Descendants<System.Windows.FrameworkElement>(view)
                    .Where(e => Shown(e, view) && e.ActualWidth > 0 && e is not System.Windows.Controls.ScrollContentPresenter)
                    .Select(e => (e, r: BoundsIn(e, view)))
                    .Where(x => x.r.Right > w + 0.5 && !InsideScroller(x.e, view))
                    .Select(x => $"{x.e.GetType().Name}({(x.e as System.Windows.Controls.TextBlock)?.Text ?? x.e.Name}) phải={x.r.Right:0}")
                    .Take(12).ToList();
                Assert.True(view.DesiredSize.Width <= w + 0.5 && wide.Count == 0,
                    $"màn đòi rộng {view.DesiredSize.Width:0} > {w}: " + string.Join(" | ", wide));

                // "Từ dòng / Đến dòng" là ô nhập LUÔN hiện (TextBox có viền), không phải chữ phải double-click; đủ 6 chữ số.
                var rowBox = view.FindResource("rowBox");
                var boxes = Descendants<System.Windows.Controls.TextBox>(view).Where(t => t.Style == rowBox && Shown(t, view)).ToList();
                var shownRows = Descendants<System.Windows.Controls.DataGridRow>(view).Count(r => Shown(r, view) && r.DataContext is Modules.Harvest.HarvestSheetRowViewModel);
                Assert.True(shownRows > 0);
                Assert.Equal(2 * shownRows, boxes.Count);
                Assert.All(boxes, b => Assert.True(b.BorderThickness.Left > 0 && b.IsEnabled));
                foreach (var b in boxes)   // ô nhập nằm trọn trong ô DataGrid (không bị xén viền)
                {
                    // So với khe nội dung của ô (ContentPresenter, đã trừ Padding) — ô Border cắt phần thò ra khỏi khe.
                    var slot = (System.Windows.FrameworkElement)System.Windows.Media.VisualTreeHelper.GetParent(b);
                    var r = BoundsIn(b, slot);
                    Assert.True(r.Left >= -0.5 && r.Right <= slot.ActualWidth + 0.5, $"ô nhập bị xén: {r.Right:0} > {slot.ActualWidth:0}");
                }
                SaveRender(view, w, h, "harvest-1000dip-dulieuthat.png");
                var fromBox = boxes[0];
                var first = Assert.IsType<Modules.Harvest.HarvestSheetRowViewModel>(fromBox.DataContext);
                fromBox.Text = "123456";
                fromBox.UpdateLayout();
                Assert.True(fromBox.ExtentWidth <= fromBox.ViewportWidth + 0.5, "ô không đủ 6 chữ số");
                // Enter / rời ô: áp vào VM theo luật kẹp; nhập nhỏ hơn dòng dữ liệu đầu → ô hiện ngay giá trị đã kẹp.
                Modules.Harvest.HarvestView.CommitRowBox(fromBox);
                Assert.Equal(123456, first.FromRow);
                fromBox.Text = "1";
                Modules.Harvest.HarvestView.CommitRowBox(fromBox);
                Assert.Equal((2, "2"), (first.FromRow, fromBox.Text));
                fromBox.Text = "";                                                    // xoá trắng → giữ giá trị đang áp
                Modules.Harvest.HarvestView.CommitRowBox(fromBox);
                Assert.Equal("2", fromBox.Text);
                view.UpdateLayout();
                var shown = string.Join(" | ", boxes.Select(b => $"{(b.DataContext as Modules.Harvest.HarvestSheetRowViewModel)?.Name}:{b.Text}"));
                Assert.Same(vm.Sheets[0], first);
                Assert.All(boxes, b => Assert.True(b.Text is "2" or "0", shown));
            });
        }
        finally
        {
            WorkspaceAppConfig.Initialize(WorkspaceRole.Full);
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>Dựng màn như trong cửa sổ thật: lượt đầu DataGrid chưa biết bề rộng khung nhìn nên cột * (Sheet, Ghi chú) = 0; dựng lại
    /// vài lượt (mọi phần tử đánh dấu đo lại) để DataGrid chia bề rộng cột * như khi cửa sổ hiện — không cần bơm Dispatcher.</summary>
    private static void LayoutLikeWindow(System.Windows.FrameworkElement view, double w, double h)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            foreach (var e in Descendants<System.Windows.UIElement>(view).ToList()) e.InvalidateMeasure();
            view.InvalidateMeasure();
            view.Measure(new System.Windows.Size(w, h));
            view.Arrange(new System.Windows.Rect(0, 0, w, h));
            view.UpdateLayout();
        }
    }

    /// <summary>Phần tử đang hiện trên màn dựng ngoài cửa sổ (<c>IsVisible</c> luôn false khi không có cửa sổ): nó và mọi cha tới
    /// <paramref name="root"/> đều <c>Visibility.Visible</c>.</summary>
    private static bool Shown(System.Windows.UIElement e, System.Windows.DependencyObject root)
    {
        for (System.Windows.DependencyObject? p = e; p is not null && !ReferenceEquals(p, root); p = System.Windows.Media.VisualTreeHelper.GetParent(p))
            if (p is System.Windows.UIElement u && u.Visibility != System.Windows.Visibility.Visible) return false;
        return true;
    }

    /// <summary>Phần tử nằm trong vùng cuộn (DataGrid / ô log) — tràn ở đó là cuộn ngang hợp lệ, không phải màn tràn.</summary>
    private static bool InsideScroller(System.Windows.DependencyObject e, System.Windows.DependencyObject root)
    {
        for (var p = System.Windows.Media.VisualTreeHelper.GetParent(e); p is not null && !ReferenceEquals(p, root);
             p = System.Windows.Media.VisualTreeHelper.GetParent(p))
            if (p is System.Windows.Controls.ScrollViewer) return true;
        return false;
    }

    /// <summary>GĐ7.1 (lỗi user báo): dải ô số đè lên hàng nút, chữ trong ô bị xén. Dựng màn ở 2 bề rộng × 100% / 125% (bề rộng DIP
    /// tương ứng cửa sổ ~1100 px và ~1900 px): chữ trong mọi ô số không bị xén (ExtentHeight ≤ ViewportHeight), không ô số nào chồng
    /// lên nút nào. Đặt biến môi trường <c>HARVEST_RENDER_DIR</c> thì lưu ảnh PNG để xem tay. VM dựng + quét file TRƯỚC (async), view
    /// dựng trên luồng STA.</summary>
    [Fact]
    public async Task ManHarvest_BoCuc_OSoKhongXenChu_KhongDeLenNut()
    {
        var dir = NewTempDir();
        try
        {
            var file = Workbook(dir, "links.xlsx", Enumerable.Range(1, 12)
                .Select(i => ($"Shop {i}", new[] { $"https://shopee.vn/a-i.77.{i}", $"https://shopee.vn/b-i.77.{i + 100}" })).ToArray());
            var settings = new HarvestSettingsStore(Path.Combine(dir, "harvest-settings.json"));
            settings.Current.WorkbookPath = file;
            settings.Current.RestMinSeconds = 1200;
            settings.Current.RestMaxSeconds = 2400;
            WorkspaceAppConfig.Initialize(WorkspaceRole.Harvest);
            var vm = new Modules.Harvest.HarvestViewModel(new Modules.Scrape.ScrapeViewModel(), settings,
                hub: new FakeHubPort(), suiteHubConfigPath: Path.Combine(dir, "khong-co.json"));
            await vm.RefreshCommand.ExecuteAsync(null);
            Assert.Equal(12, vm.Sheets.Count);
            OnSta(() =>
            {
                WorkspaceAppConfig.Initialize(WorkspaceRole.Harvest);
                var outDir = Environment.GetEnvironmentVariable("HARVEST_RENDER_DIR");
                foreach (var (widthPx, dpi) in new[] { (1100, 96.0), (1100, 120.0), (1900, 96.0), (1900, 120.0) })
                {
                    var w = widthPx * 96.0 / dpi;   // bề rộng DIP của cửa sổ widthPx pixel ở DPI này
                    var h = 1000 * 96.0 / dpi;
                    var view = new Modules.Harvest.HarvestView { DataContext = vm };
                    view.Measure(new System.Windows.Size(w, h));
                    view.Arrange(new System.Windows.Rect(0, 0, w, h));
                    view.UpdateLayout();
                    var boxes = Descendants<System.Windows.Controls.TextBox>(view).Where(t => t.Style == view.FindResource("numBox")).ToList();
                    Assert.Equal(6, boxes.Count);
                    foreach (var b in boxes)
                    {
                        Assert.True(b.ExtentHeight <= b.ViewportHeight + 0.5,
                            $"{widthPx}px@{dpi}: chữ '{b.Text}' bị xén ({b.ExtentHeight:0.0} > {b.ViewportHeight:0.0})");
                        Assert.True(b.ExtentWidth <= b.ViewportWidth + 0.5, $"{widthPx}px@{dpi}: ô '{b.Text}' không đủ rộng");
                    }
                    var buttons = Descendants<System.Windows.Controls.Button>(view).Where(x => x.IsVisible).ToList();
                    foreach (var b in boxes)
                    {
                        var rb = BoundsIn(b, view);
                        foreach (var btn in buttons)
                            Assert.False(rb.IntersectsWith(BoundsIn(btn, view)) && !rb.Equals(BoundsIn(btn, view)),
                                $"{widthPx}px@{dpi}: ô '{b.Text}' chồng lên nút");
                    }
                    if (!string.IsNullOrEmpty(outDir))
                    {
                        Directory.CreateDirectory(outDir);
                        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
                            (int)Math.Ceiling(w * dpi / 96), (int)Math.Ceiling(h * dpi / 96), dpi, dpi, System.Windows.Media.PixelFormats.Pbgra32);
                        var bg = new System.Windows.Controls.Border
                        {
                            Background = System.Windows.Media.Brushes.White, Child = view, Width = w, Height = h,
                        };
                        bg.Measure(new System.Windows.Size(w, h));
                        bg.Arrange(new System.Windows.Rect(0, 0, w, h));
                        bg.UpdateLayout();
                        bmp.Render(bg);
                        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                        using var fs = File.Create(Path.Combine(outDir, $"harvest-{widthPx}px-{(int)(dpi / 96 * 100)}pct.png"));
                        enc.Save(fs);
                        bg.Child = null;
                    }
                }
            });
        }
        finally
        {
            WorkspaceAppConfig.Initialize(WorkspaceRole.Full);
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static System.Windows.Rect BoundsIn(System.Windows.FrameworkElement e, System.Windows.Media.Visual root) =>
        e.TransformToAncestor(root).TransformBounds(new System.Windows.Rect(0, 0, e.ActualWidth, e.ActualHeight));

    private static IEnumerable<T> Descendants<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (c is T t) yield return t;
            foreach (var d in Descendants<T>(c)) yield return d;
        }
    }

    // ── GĐ7.1: chọn / bỏ chọn tất cả sheet; Hub cho Harvest ──

    private sealed class FakeHubPort : Modules.Harvest.IHarvestHubPort
    {
        public Shopee.Core.Coordination.HubClientConfig Saved { get; private set; } = new();
        public int Reconnects { get; private set; }
        public int Saves { get; private set; }
        public bool FailSave { get; set; }
        public bool ConnectOnReconnect { get; set; } = true;
        public string ConfigPath => @"C:\gia\harvest\hub-client.json";
        public Shopee.Core.Coordination.HubClientConfig Current => Saved.Clone();
        public bool Save(Shopee.Core.Coordination.HubClientConfig config)
        {
            Saves++;
            if (FailSave) return false;
            Saved = config.Clone();
            return true;
        }
        public bool Reconnect()
        {
            Reconnects++;
            Connected = ConnectOnReconnect && Saved.IsConfigured && !string.IsNullOrWhiteSpace(Saved.ApiToken);
            return Connected;
        }
        public bool Connected { get; set; }
        public string MachineId => "may-test";
    }

    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }

        public IEnumerable<string> TokensSent => Requests.Where(r => r.Headers.Contains("X-Api-Token"))
            .Select(r => r.Headers.GetValues("X-Api-Token").Single());
    }

    /// <summary>Hub giả: /health 200; lệnh có token 200 nếu token = <paramref name="good"/>, không thì 401.</summary>
    private static FakeHttp HubGia(string good = "bi-mat-123") => new(r => new HttpResponseMessage(
        r.RequestUri!.AbsolutePath == "/health" || (r.Headers.Contains("X-Api-Token") && r.Headers.GetValues("X-Api-Token").Single() == good)
            ? System.Net.HttpStatusCode.OK : System.Net.HttpStatusCode.Unauthorized));

    private static string KhongCo() => Path.Combine(Path.GetTempPath(), "khong-co-" + Guid.NewGuid());

    [Fact]
    public async Task ChonTatCa_BaTrangThai_TickHetBoHet_NhoQuaLanMoSau()
    {
        var dir = NewTempDir();
        try
        {
            var file = Workbook(dir, "links.xlsx", ("A", ["https://shopee.vn/a-i.1.1"]), ("B", ["https://shopee.vn/a-i.1.2"]),
                ("C", ["https://shopee.vn/a-i.1.3"]));
            var settings = new HarvestSettingsStore(Path.Combine(dir, "harvest-settings.json"));
            settings.Current.WorkbookPath = file;
            var vm = new Modules.Harvest.HarvestViewModel(new Modules.Scrape.ScrapeViewModel(), settings, hub: new FakeHubPort(),
                suiteHubConfigPath: Path.Combine(dir, "khong-co.json"));
            await vm.RefreshCommand.ExecuteAsync(null);
            var changed = new List<string?>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            Assert.True(vm.AllSheetsChecked);                                        // mặc định tick hết
            vm.Sheets[1].IsChecked = false;
            Assert.Null(vm.AllSheetsChecked);                                        // một phần
            Assert.Contains(nameof(vm.AllSheetsChecked), changed);
            vm.AllSheetsChecked = null;                                              // bấm khi đang một phần → tick hết
            Assert.All(vm.Sheets, s => Assert.True(s.IsChecked));
            vm.AllSheetsChecked = false;                                             // bỏ hết
            Assert.All(vm.Sheets, s => Assert.False(s.IsChecked));
            Assert.False(vm.AllSheetsChecked);
            Assert.False(vm.RunCommand.CanExecute(null));
            Assert.Equal(3, new HarvestSettingsStore(settings.FilePath).Current.UncheckedSheetKeys.Count);   // nhớ qua lần mở sau
            vm.AllSheetsChecked = true;
            Assert.Empty(new HarvestSettingsStore(settings.FilePath).Current.UncheckedSheetKeys);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task HubHarvest_LuuNapLai_NoiLaiNgay_TuKiemTra_TokenKhongHienLai()
    {
        var port = new FakeHubPort();
        var http = HubGia();
        var hub = new Modules.Harvest.HarvestHubViewModel(port, suiteConfigPath: KhongCo(), checkHandler: http);
        Assert.False(hub.IsConnected);
        Assert.StartsWith("Chưa nối Hub", hub.Status);
        Assert.Contains(@"harvest\hub-client.json", hub.ConfigNote);                 // nói rõ file RIÊNG của Harvest
        var saved = new List<Modules.Harvest.HarvestHubSaveResult>();
        hub.Saved += saved.Add;

        hub.BaseUrl = "https://hub.test/ ";
        hub.Token = "bi-mat-123";
        await hub.SaveCommand.ExecuteAsync(null);
        Assert.Equal(("https://hub.test", "bi-mat-123", true), (port.Saved.BaseUrl, port.Saved.ApiToken, port.Saved.Enabled));
        Assert.Equal(1, port.Reconnects);                                            // nối lại ngay, không mở lại app
        Assert.True(hub.IsConnected);
        Assert.True(hub.StatusOk);                                                   // N1: tự kiểm tra sau Lưu, token đúng → xanh
        Assert.Equal("Kết nối Hub tốt (token đúng).", hub.CheckResult);
        Assert.True(Assert.Single(saved).Ok);
        Assert.Equal("", hub.Token);                                                 // token đã lưu không hiện lại
        Assert.DoesNotContain("bi-mat-123", hub.Status + hub.TokenNote + hub.CheckResult);

        // Mở lại màn: URL từ cấu hình đã lưu, token ẩn; Lưu lại CÙNG URL không gõ token → giữ token cũ.
        var again = new Modules.Harvest.HarvestHubViewModel(port, suiteConfigPath: KhongCo(), checkHandler: http);
        Assert.Equal(("https://hub.test", ""), (again.BaseUrl, again.Token));
        Assert.Contains("để trống nếu giữ nguyên", again.TokenNote);
        await again.SaveCommand.ExecuteAsync(null);
        Assert.Equal(("https://hub.test", "bi-mat-123"), (port.Saved.BaseUrl, port.Saved.ApiToken));
        Assert.Equal("", again.UrlWarning);
        Assert.Contains("https://hub.test", again.PermissionNote);                   // khác Hub mặc định → nhắc cấp quyền ext
    }

    /// <summary>V1: đổi sang URL khác URL đã lưu → KHÔNG tự gửi token cũ (Lưu đòi nhập lại; Kiểm tra chỉ gọi /health).</summary>
    [Fact]
    public async Task HubHarvest_DoiUrl_KhongGuiTokenCu_BatNhapLai()
    {
        var port = new FakeHubPort();
        port.Save(new Shopee.Core.Coordination.HubClientConfig { Enabled = true, BaseUrl = "https://hub.test", ApiToken = "bi-mat-123" });
        var http = HubGia();
        var hub = new Modules.Harvest.HarvestHubViewModel(port, suiteConfigPath: KhongCo(), checkHandler: http);
        hub.BaseUrl = "https://hub-la.test";
        Assert.Contains("nhập token cho Hub này", hub.TokenNote);
        await hub.CheckCommand.ExecuteAsync(null);
        Assert.Empty(http.TokensSent);                                               // không gửi token cũ sang Hub khác
        Assert.Contains("chưa có token", hub.CheckResult);
        await hub.SaveCommand.ExecuteAsync(null);
        Assert.Equal("https://hub.test", port.Saved.BaseUrl);                        // không lưu
        Assert.Contains("Chưa có token cho URL này", hub.Status);
        Assert.False(hub.StatusOk);

        hub.Token = "token-moi";
        await hub.SaveCommand.ExecuteAsync(null);
        Assert.Equal(("https://hub-la.test", "token-moi"), (port.Saved.BaseUrl, port.Saved.ApiToken));
    }

    /// <summary>V1: luật URL chung với extension — chỉ https, hoặc http tới localhost / 127.0.0.1. http ra ngoài máy: cảnh báo rõ,
    /// không lưu, Kiểm tra KHÔNG gửi token. Hub mặc định / localhost: không cần nhắc cấp quyền extension.</summary>
    [Fact]
    public async Task HubHarvest_UrlHttpRaNgoai_CanhBao_KhongGuiToken_KhongLuu()
    {
        var port = new FakeHubPort();
        var http = HubGia("bi-mat-123");
        var hub = new Modules.Harvest.HarvestHubViewModel(port, suiteConfigPath: KhongCo(), checkHandler: http);
        hub.BaseUrl = "http://192.168.1.5:8088";
        hub.Token = "bi-mat-123";
        Assert.Contains("http ra ngoài máy", hub.UrlWarning);
        await hub.CheckCommand.ExecuteAsync(null);
        Assert.Empty(http.TokensSent);
        Assert.Contains("KHÔNG gửi token", hub.CheckResult);
        await hub.SaveCommand.ExecuteAsync(null);
        Assert.Equal(0, port.Saves);
        Assert.Contains("http ra ngoài máy", hub.Status);

        hub.BaseUrl = "http://localhost:8088";
        Assert.Equal(("", ""), (hub.UrlWarning, hub.PermissionNote));
        hub.BaseUrl = Shopee.Core.Coordination.HubDefaults.BaseUrl + "/duong/dan?x=1";
        Assert.Equal(("", ""), (hub.UrlWarning, hub.PermissionNote));
        await hub.SaveCommand.ExecuteAsync(null);
        Assert.Equal(Shopee.Core.Coordination.HubDefaults.BaseUrl, port.Saved.BaseUrl);   // chuẩn hoá về gốc như extension

        Assert.True(HarvestHubUrl.TryNormalize("https://Hub.Test:8443/api/", out var o, out _, out _));
        Assert.Equal("https://hub.test:8443", o);
        Assert.False(HarvestHubUrl.TryNormalize("http://10.0.0.2", out _, out _, out var outside));
        Assert.True(outside);
        Assert.False(HarvestHubUrl.TryNormalize("ftp://hub.test", out _, out _, out outside));
        Assert.False(outside);
        Assert.True(HarvestHubUrl.TryNormalize("http://127.0.0.1:8088", out _, out _, out _));
    }

    /// <summary>V4 + N1: lưu hỏng → báo lỗi, không nối lại, không "Đã nối"; token sai → báo rõ, không xanh, màn chính không báo "đã nối".</summary>
    [Fact]
    public async Task HubHarvest_LuuHong_TokenSai_BaoRo_KhongXanh()
    {
        var port = new FakeHubPort { FailSave = true };
        var hub = new Modules.Harvest.HarvestHubViewModel(port, suiteConfigPath: KhongCo(), checkHandler: HubGia("token-dung"));
        var results = new List<Modules.Harvest.HarvestHubSaveResult>();
        hub.Saved += results.Add;
        hub.BaseUrl = "https://hub.test";
        hub.Token = "token-sai";
        await hub.SaveCommand.ExecuteAsync(null);
        Assert.Contains("Không lưu được cấu hình Hub", hub.Status);
        Assert.DoesNotContain("Đã", hub.Status);
        Assert.Equal(0, port.Reconnects);
        Assert.False(hub.StatusOk);
        Assert.False(Assert.Single(results).Ok);

        port.FailSave = false;
        await hub.SaveCommand.ExecuteAsync(null);
        Assert.Equal(1, port.Reconnects);
        Assert.False(hub.StatusOk);                                                  // token sai → không xanh
        Assert.Contains("token sai", hub.Status);
        Assert.DoesNotContain("token-sai", hub.Status + hub.CheckResult);

        var vm = new Modules.Harvest.HarvestViewModel(new Modules.Scrape.ScrapeViewModel(),
            new HarvestSettingsStore(Path.Combine(Path.GetTempPath(), "hs-" + Guid.NewGuid().ToString("N") + ".json")),
            hub: port, suiteHubConfigPath: KhongCo());
        Assert.True(vm.IsHubConnected);                                              // client có (chưa biết kết quả kiểm tra)
        vm.OnHubSaved(results[^1]);
        Assert.False(vm.IsHubConnected);
        Assert.Contains("Hub chưa dùng được", vm.HubStatus);
        Assert.Contains("token sai", vm.HubStatus);
    }

    [Fact]
    public async Task HubHarvest_LayTuShopeeSuite_ChiKhiBam_BaCauBao_LuuMoiDung()
    {
        var dir = NewTempDir();
        try
        {
            var suite = Path.Combine(dir, "hub-client.json");
            var port = new FakeHubPort();
            var hub = new Modules.Harvest.HarvestHubViewModel(port, suiteConfigPath: suite, checkHandler: HubGia("tok-suite"));
            hub.ImportFromSuiteCommand.Execute(null);                                // N3: thiếu file
            Assert.Contains("Không thấy file cấu hình Hub của Shopee Suite", hub.Status);
            File.WriteAllText(suite, "{ hong");
            hub.ImportFromSuiteCommand.Execute(null);                                // N3: file hỏng
            Assert.Contains("bị hỏng", hub.Status);
            File.WriteAllText(suite, "{\"Enabled\":true,\"BaseUrl\":\"https://api.suite.test/\",\"ApiToken\":\"\"}");
            hub.ImportFromSuiteCommand.Execute(null);                                // N3: thiếu token
            Assert.Contains("không có token", hub.Status);
            Assert.False(port.Saved.IsConfigured);

            File.WriteAllText(suite, "{\"Enabled\":true,\"BaseUrl\":\"https://api.suite.test/\",\"ApiToken\":\"tok-suite\"}");
            var vm = new Modules.Harvest.HarvestViewModel(new Modules.Scrape.ScrapeViewModel(), new HarvestSettingsStore(Path.Combine(dir, "s.json")),
                hub: port, suiteHubConfigPath: suite);
            Assert.False(vm.IsHubConnected);
            Assert.Contains("Lấy cấu hình từ Shopee Suite", vm.HubStatus);           // chỉ GỢI Ý, không tự đọc file của Suite
            Assert.Equal(0, port.Reconnects);
            Assert.False(port.Saved.IsConfigured);

            hub.ImportFromSuiteCommand.Execute(null);
            Assert.Equal("https://api.suite.test", hub.BaseUrl);
            Assert.Equal("", hub.Token);                                             // token Suite giữ ẩn
            Assert.False(port.Saved.IsConfigured);                                   // chưa Lưu thì chưa ghi gì
            await hub.SaveCommand.ExecuteAsync(null);
            Assert.Equal(("https://api.suite.test", "tok-suite"), (port.Saved.BaseUrl, port.Saved.ApiToken));
            vm.OnHubSaved(new Modules.Harvest.HarvestHubSaveResult(true, "ok"));
            Assert.True(vm.IsHubConnected);
            Assert.Equal("Đã nối Hub: https://api.suite.test", vm.HubStatus);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>N2: lưu Hub xong mà app rảnh + đã có file → quét lại (hỏi Hub lại) để "Đã có" / "Còn phải lấy" tính theo Hub.</summary>
    [Fact]
    public async Task HubHarvest_LuuXong_QuetLaiFile()
    {
        var dir = NewTempDir();
        try
        {
            var file = Workbook(dir, "links.xlsx", ("A", ["https://shopee.vn/a-i.1.1", "https://shopee.vn/a-i.1.2"]));
            var rt = new HarvestRuntime(Path.Combine(dir, "outbox.db"), () => null);
            var asked = 0;
            rt.KnownQuery = (ids, _, _) =>
            {
                Interlocked.Increment(ref asked);
                return Task.FromResult<IReadOnlyDictionary<long, DateTimeOffset>>(ids.Where(i => i == 2).ToDictionary(i => i, _ => DateTimeOffset.UtcNow));
            };
            HarvestRuntime.SetCurrentForTest(rt);
            var settings = new HarvestSettingsStore(Path.Combine(dir, "s.json"));
            settings.Current.WorkbookPath = file;
            var vm = new Modules.Harvest.HarvestViewModel(new Modules.Scrape.ScrapeViewModel(), settings, hub: new FakeHubPort { Connected = true },
                suiteHubConfigPath: KhongCo());
            await vm.RefreshCommand.ExecuteAsync(null);
            var before = Volatile.Read(ref asked);
            vm.OnHubSaved(new Modules.Harvest.HarvestHubSaveResult(true, "ok"));
            for (var i = 0; i < 100 && (Volatile.Read(ref asked) == before || vm.IsScanning); i++) await Task.Delay(50);
            Assert.True(Volatile.Read(ref asked) > before, "lưu Hub xong phải quét lại file");
            Assert.Equal((1, 1), (vm.Sheets[0].HaveCount, vm.Sheets[0].TodoCount));
        }
        finally
        {
            HarvestRuntime.SetCurrentForTest(null);
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void HubHarvest_ChuaCauHinh_BaoChuaNoi()
    {
        var vm = new Modules.Harvest.HarvestViewModel(new Modules.Scrape.ScrapeViewModel(),
            new HarvestSettingsStore(Path.Combine(Path.GetTempPath(), "hs-" + Guid.NewGuid().ToString("N") + ".json")),
            hub: new FakeHubPort(), suiteHubConfigPath: KhongCo());
        Assert.False(vm.IsHubConnected);
        Assert.StartsWith("Chưa nối Hub", vm.HubStatus);
        Assert.Contains("Cài đặt → Kết nối Hub", vm.HubStatus);
        var opened = 0;
        vm.OpenHubSettings = () => opened++;
        vm.GoToHubSettingsCommand.Execute(null);
        Assert.Equal(1, opened);
    }

    [Fact]
    public async Task HubHarvest_KiemTraKetNoi_HealthRoiLenhCoToken()
    {
        var ok = HubGia("dung");
        Assert.Equal((true, "Kết nối Hub tốt (token đúng)."), await Modules.Harvest.HarvestHubCheck.CheckAsync("https://hub.test", "dung", ok));
        Assert.Equal("/api/harvested-products/stats", ok.Requests[1].RequestUri!.AbsolutePath);
        var (sai, msg) = await Modules.Harvest.HarvestHubCheck.CheckAsync("https://hub.test", "sai-xyz", ok);
        Assert.False(sai);
        Assert.Contains("token sai", msg);
        Assert.DoesNotContain("sai-xyz", msg);
        var down = new FakeHttp(_ => throw new HttpRequestException("No such host"));
        Assert.False((await Modules.Harvest.HarvestHubCheck.CheckAsync("https://hub.test", "x", down)).Ok);
        Assert.False((await Modules.Harvest.HarvestHubCheck.CheckAsync("abc", "x", ok)).Ok);
    }

    /// <summary>V2: Harvest dùng kho cấu hình Hub RIÊNG (harvest\hub-client.json), gán cho CoordinationRuntime trước init; app chỉ-client.</summary>
    [Fact]
    public void NoiDay_HarvestChiDungClientHub_KhoCauHinhRieng()
    {
        var app = Src(Path.Combine("src", "Shopee.Workspace", "App.xaml.cs"));
        var harvestStart = Between(app, "if (WorkspaceAppConfig.IsHarvest)", "// Kiểm tra Brave");
        Assert.Contains("CoordinationRuntime.ClientOnly = true;", harvestStart);
        Assert.Contains("CoordinationRuntime.ClientStore = Modules.Harvest.HarvestHubPort.Store;", harvestStart);
        Assert.True(app.IndexOf("CoordinationRuntime.ClientStore = ", StringComparison.Ordinal)
                    < app.IndexOf("CoordinationRuntime.InitForCurrentMode();", StringComparison.Ordinal));
        var rt = Src(Path.Combine("src", "Shopee.Core", "Coordination", "CoordinationRuntime.cs"));
        Assert.Contains("InitClientOnlyFromConfig();", Between(rt, "if (ClientOnly)", "var mode ="));
        Assert.Equal(Path.Combine(Shopee.Core.Infrastructure.SuitePaths.ModuleDir("harvest"), "hub-client.json"),
            Modules.Harvest.HarvestHubPort.Store.FilePath);
        Assert.NotEqual(Shopee.Core.Infrastructure.SuitePaths.RootFile("hub-client.json"), Modules.Harvest.HarvestHubPort.Store.FilePath);
        Assert.Equal(Modules.Harvest.HarvestHubPort.Store.FilePath, Modules.Harvest.HarvestHubPort.Instance.ConfigPath);
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
            Assert.DoesNotContain("Kết nối Hub", RibbonTexts(shell));                // mục Hub mới chỉ có ở Harvest
            Assert.Null(shell.HarvestHub);
            Assert.Null(shell.HarvestSettings);
            // Màn Cài đặt của app Scrape vẫn là màn chung, CÒN nhóm AI.
            var setScreen = Assert.Single(shell.Tabs.Single(t => t.Title == "Cài đặt").Groups.SelectMany(g => g.Items)
                .OfType<ViewModels.RibbonScreenItem>());
            var local = Assert.IsType<Modules.Settings.LocalSettingsViewModel>(setScreen.ScreenVm);
            var localView = new Modules.Settings.LocalSettingsView { DataContext = local };
            localView.Measure(new System.Windows.Size(1000, 2000));
            localView.Arrange(new System.Windows.Rect(0, 0, 1000, 2000));
            localView.UpdateLayout();
            var localTexts = VisibleTexts(localView);
            Assert.Contains("AI", localTexts);
            Assert.Contains("OpenAI model / API key", localTexts);
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
