using Shopee.Core.Coordination;
using Shopee.Core.Harvest;
using Shopee.Core.Scrape;
using Shopee.Modules.MultiBrave;
using Shopee.Workspace.Infrastructure;

namespace Shopee.Workspace.Tests;

/// <summary>
/// GĐ5 — exe Shopee Harvest: vai trò + đối số, kho tiến độ/cấu hình RIÊNG theo vai trò, profile Brave tách khỏi Scrape,
/// mutex chặn 2 bản, runtime outbox (sink báo UI + Nudge, đóng app có giới hạn thời gian). Các vai trò cũ giữ nguyên.
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
            Assert.Equal("Lấy dữ liệu", RoleText.ScrapeOp);
            Assert.Equal("lấy dữ liệu", RoleText.ScrapeOpLower);

            WorkspaceAppConfig.Initialize(WorkspaceRole.Scrape);
            Assert.Same(ScrapeProgressStore.Shared, RoleStores.ScrapeProgress);
            Assert.Same(ScrapeTargetConfigStore.Shared, RoleStores.ScrapeTargets);
            Assert.Equal("Scrape", RoleText.ScrapeOp);
            Assert.Equal("scrape", RoleText.ScrapeOpLower);
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

    // ── Workbook chung: Harvest chỉ đọc ──

    [Theory]
    [InlineData(WorkspaceRole.Harvest, false)]
    [InlineData(WorkspaceRole.Scrape, true)]
    [InlineData(WorkspaceRole.Full, true)]
    [InlineData(WorkspaceRole.ImportUpdate, true)]
    public void ChiHarvest_KhoaSuaWorkbookChung(WorkspaceRole role, bool canEdit) =>
        Assert.Equal(canEdit, SharedWorkbookPolicy.CanEdit(role));

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

    // ── Tập SP đã lấy xong (outbox) ──

    private static HarvestedItem ItemFor(string target, string sourceLink, long itemId, int row, long? shop = null) =>
        new("acc-1", "shop-1", "Shop1", row, HarvestLinks.CleanProductUrl(sourceLink), sourceLink, itemId, shop,
            "{\"error\":null,\"data\":{\"item\":{\"item_id\":" + itemId + ",\"shop_id\":77,\"title\":\"SP\"},\"product_images\":{\"images\":[\"vn-a\"],\"video\":null}}}",
            "hook", DateTimeOffset.Now, target);

    [Fact]
    public async Task Outbox_LuuXong_GhiKhoaVaoTapCuaTarget_ResetXoaDungTarget()
    {
        var dir = NewTempDir();
        try
        {
            var ob = new HarvestOutbox(Path.Combine(dir, "outbox.db"));
            var a = HarvestDoneTarget.Of("acc-1", "acc:acc-1:shared:S1");
            var b = HarvestDoneTarget.Of("acc-1", "acc:acc-1:shared:S2");
            await ob.SaveAsync(ItemFor(a, "https://shopee.vn/ao-i.10.100", 100, 2), CancellationToken.None);
            await ob.SaveAsync(ItemFor(a, "https://shp.ee/abc", 200, 3, shop: 77), CancellationToken.None);      // link rút gọn: thêm khoá từ JSON
            await ob.SaveAsync(ItemFor(b, "https://shopee.vn/product/10/300", 300, 5), CancellationToken.None);
            await ob.SaveAsync(ItemFor(null!, "https://shopee.vn/product/10/400", 400, 9), CancellationToken.None);   // không theo dõi

            Assert.Equal(new HashSet<string> { "id:10.100", "url:shp.ee/abc", "id:77.200" }, ob.DoneKeys(a));
            Assert.Equal(new HashSet<string> { "id:10.300" }, ob.DoneKeys(b));
            Assert.Equal(a, HarvestDoneTarget.Of("acc-1", "ACC:acc-1:SHARED:s1"));   // khoá tiến độ không phân biệt hoa thường

            Assert.Equal(3, ob.ClearDone(a));
            Assert.Empty(ob.DoneKeys(a));
            Assert.Single(ob.DoneKeys(b));                                           // target khác không bị đụng
            Assert.Equal(4, (await ob.GetStatsAsync()).Pending);                     // dữ liệu SP chờ đẩy Hub giữ nguyên
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task ResetTienDo_Harvest_XoaTapDaXongCuaDungTarget_ScrapeKhongDung()
    {
        var dir = NewTempDir();
        try
        {
            var rt = new HarvestRuntime(Path.Combine(dir, "outbox.db"), () => null);
            var target = HarvestDoneTarget.Of("acc-r", "acc:acc-r:shared:S1");
            var other = HarvestDoneTarget.Of("acc-r", "acc:acc-r:shared:S2");
            await rt.Sink.SaveAsync(ItemFor(target, "https://shopee.vn/product/1/1", 1, 2), CancellationToken.None);
            await rt.Sink.SaveAsync(ItemFor(other, "https://shopee.vn/product/1/2", 2, 2), CancellationToken.None);
            HarvestRuntime.SetCurrentForTest(rt);
            try
            {
                WorkspaceAppConfig.Initialize(WorkspaceRole.Scrape);
                Assert.Equal(0, Modules.Scrape.ScrapeProgressReset.ClearHarvestDone("acc-r", "acc:acc-r:shared:S1"));
                Assert.Single(rt.Outbox.DoneKeys(target));

                Modules.Scrape.ScrapeProgressReset.ClearKey("acc-r", "acc:acc-r:shared:S1");
                Assert.Single(rt.Outbox.DoneKeys(target));                               // app Scrape: ClearKey = Clear cũ

                WorkspaceAppConfig.Initialize(WorkspaceRole.Harvest);
                Modules.Scrape.ScrapeProgressReset.ClearKey("acc-r", "ACC:acc-r:shared:s1");   // khoá tiến độ hoa thường khác
                Assert.Empty(rt.Outbox.DoneKeys(target));
                Assert.Single(rt.Outbox.DoneKeys(other));
                await rt.Sink.SaveAsync(ItemFor(target, "https://shopee.vn/product/1/3", 3, 3), CancellationToken.None);
                Assert.Equal(1, Modules.Scrape.ScrapeProgressReset.ClearHarvestDone("acc-r", "acc:acc-r:shared:S1"));
            }
            finally
            {
                HarvestRuntime.SetCurrentForTest(null);
                WorkspaceAppConfig.Initialize(WorkspaceRole.Full);
            }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ── Resolve của Harvest: không rơi sang file gộp khi file nguồn chưa đọc được ──

    private static Shopee.Core.BigSeller.BigSellerShop Shop(string id, string name) => new() { Id = id, Name = name };

    private static (string Src, string Merged) Workbooks(string dir)
    {
        var src = Path.Combine(dir, "nguon.xlsx");
        var merged = Path.Combine(dir, "gop.xlsx");
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            wb.AddWorksheet("Shop A").Cell(2, 1).Value = "https://shopee.vn/product/1/1";
            wb.AddWorksheet("Shop B");
            wb.SaveAs(src);
        }
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            wb.AddWorksheet(Shopee.Core.BigSeller.SharedWorkbookMerge.SheetName);
            wb.SaveAs(merged);
        }
        return (src, merged);
    }

    [Fact]
    public void HarvestResolve_DocDuoc_KhopResolveChuan()
    {
        var dir = NewTempDir();
        try
        {
            var (src, merged) = Workbooks(dir);
            var acc = new Shopee.Core.BigSeller.BigSellerAccount { Id = "acc" };
            foreach (var enabled in new[] { true, false })
            foreach (var shop in new[] { Shop("1", "Shop A"), Shop("2", "Khong Khop") })
            foreach (var source in new[] { src, "" })
            foreach (var gop in new[] { merged, Path.Combine(dir, "khong-co.xlsx") })
            {
                var harvest = HarvestWorkbookResolve.Resolve(enabled, source, gop, acc, shop, out var error);
                Assert.Null(error);
                Assert.Equal(Shopee.Core.BigSeller.SharedWorkbookCatalog.ResolveForUpdate(enabled, source, gop, acc, shop), harvest);
            }
            // Đọc được mà thật sự không có sheet của shop → file gộp (hành vi chuẩn).
            Assert.Equal(merged, HarvestWorkbookResolve.Resolve(true, src, merged, acc, Shop("2", "Khong Khop"), out _)!.Value.DataPath);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>Ca thật: file nguồn có sheet của shop nhưng đang bị mở ĐỘC QUYỀN → bản dùng chung đọc lỗi thành "không khớp" rồi
    /// trả file gộp (dòng của mọi shop); Harvest phải dừng với lý do rõ, KHÔNG lấy file gộp.</summary>
    [Fact]
    public void HarvestResolve_NguonMoDocQuyen_KhongRoiSangGop_BanChuanThiRoi()
    {
        var dir = NewTempDir();
        try
        {
            var (src, merged) = Workbooks(dir);
            var acc = new Shopee.Core.BigSeller.BigSellerAccount { Id = "acc" };
            var shop = Shop("1", "Shop A");
            using (new FileStream(src, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.Equal(merged, Shopee.Core.BigSeller.SharedWorkbookCatalog.ResolveForUpdate(true, src, merged, acc, shop).DataPath);
                Assert.Null(HarvestWorkbookResolve.Resolve(true, src, merged, acc, shop, out var error));
                Assert.Equal(HarvestWorkbookResolve.SourceUnreadableMessage, error);
            }
            using (new FileStream(src + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))   // app Update đang ghi
            {
                Assert.Null(HarvestWorkbookResolve.Resolve(true, src, merged, acc, shop, out var error));
                Assert.True(HarvestWorkbookLockedException.Is(error));
            }
            File.Delete(src + ".lock");
            var missing = Path.Combine(dir, "da-xoa.xlsx");
            Assert.Null(HarvestWorkbookResolve.Resolve(true, missing, merged, acc, shop, out var miss));   // nguồn đã chọn mà vắng
            Assert.Equal(HarvestWorkbookResolve.SourceMissingMessage(missing), miss);
            Assert.Equal("Shop A", HarvestWorkbookResolve.Resolve(true, src, merged, acc, shop, out _)!.Value.DataSheet);   // nhả → đọc được
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void HarvestResolve_DanhSachSheetRong_LaChuaDocDuoc()
    {
        var dir = NewTempDir();
        var old = HarvestWorkbookResolve.ListSheets;
        try
        {
            var (src, merged) = Workbooks(dir);
            HarvestWorkbookResolve.ListSheets = _ => [];
            Assert.Null(HarvestWorkbookResolve.Resolve(true, src, merged, new Shopee.Core.BigSeller.BigSellerAccount { Id = "a" }, Shop("1", "Shop A"), out var error));
            Assert.Equal(HarvestWorkbookResolve.SourceUnreadableMessage, error);
        }
        finally
        {
            HarvestWorkbookResolve.ListSheets = old;
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Theory]
    [InlineData(WorkspaceRole.Harvest, false)]
    [InlineData(WorkspaceRole.Scrape, true)]
    public void DanhDauDaXong_TatOHarvest(WorkspaceRole role, bool can)
    {
        try
        {
            WorkspaceAppConfig.Initialize(role);
            var vm = (Modules.Workspace.WorkspaceShopViewModel)System.Runtime.CompilerServices.RuntimeHelpers
                .GetUninitializedObject(typeof(Modules.Workspace.WorkspaceShopViewModel));
            Assert.Equal(can, vm.CanMarkScrapeDone);
        }
        finally { WorkspaceAppConfig.Initialize(WorkspaceRole.Full); }
    }

    // ── Cấu hình chạy riêng cho Harvest ──

    [Fact]
    public void RunConfig_Harvest_KhoiTaoDaiDongMacDinh_ChiChepThongSoMay_LuuNgay_KhongDoiCuaTk()
    {
        var dir = NewTempDir();
        try
        {
            var path = Path.Combine(dir, "harvest-run-config.json");
            var store = new HarvestRunConfigStore(path);
            var account = new Shopee.Core.BigSeller.BigSellerAccount
            {
                Id = "acc-1",
                RunConfig = new Shopee.Core.BigSeller.BigSellerRunConfig
                    { StartRow = 500, EndRow = 900, Processes = 3, FrameSize = 7, RestMinSeconds = 30, RestMaxSeconds = 60 },
            };

            var h = RunConfigSource.For(WorkspaceRole.Harvest, account, store);
            Assert.NotSame(account.RunConfig, h);
            Assert.Equal((2, 0), (h.StartRow, h.EndRow));                   // KHÔNG ăn dải dòng của Scrape
            Assert.Equal((3, 7, 30, 60), (h.Processes, h.FrameSize, h.RestMinSeconds, h.RestMaxSeconds));
            Assert.True(File.Exists(path));                                   // lưu ngay khi tạo

            account.RunConfig.Processes = 9;                                  // Scrape đổi sau đó
            Assert.Equal(3, new HarvestRunConfigStore(path).For(account).Processes);   // không chép lại

            h.StartRow = 700;
            RunConfigSource.Save(WorkspaceRole.Harvest, store, saveBigSeller: () => throw new Exception("không được lưu bigseller.json"));
            Assert.Equal(500, account.RunConfig.StartRow);
            Assert.Equal(700, new HarvestRunConfigStore(path).For(account).StartRow);

            var noCfg = new Shopee.Core.BigSeller.BigSellerAccount { Id = "acc-2" };
            Assert.Equal(2, RunConfigSource.For(WorkspaceRole.Harvest, noCfg, store).StartRow);
            Assert.Null(noCfg.RunConfig);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Theory]
    [InlineData(WorkspaceRole.Scrape)]
    [InlineData(WorkspaceRole.Full)]
    [InlineData(WorkspaceRole.ImportUpdate)]
    public void RunConfig_VaiTroKhac_DungCuaTk_VaLuuBigSellerJson(WorkspaceRole role)
    {
        var dir = NewTempDir();
        try
        {
            var store = new HarvestRunConfigStore(Path.Combine(dir, "h.json"));
            var account = new Shopee.Core.BigSeller.BigSellerAccount { Id = "acc", RunConfig = new() };
            Assert.Same(account.RunConfig, RunConfigSource.For(role, account, store));
            var saved = 0;
            RunConfigSource.Save(role, store, saveBigSeller: () => saved++);
            Assert.Equal(1, saved);
            Assert.False(File.Exists(Path.Combine(dir, "h.json")));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
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
    public void NoiDay_RunOneJob_Harvest_DungLaiTuTapDaXong_KhongResetKhongTinTienDoCu()
    {
        var vm = Src(Path.Combine("src", "Shopee.Workspace", "Modules", "Scrape", "ScrapeViewModel.cs"));
        var run = Between(vm, "private async Task RunOneJobAsync", "/// <summary>Tạo + phóng 1 job");
        Assert.DoesNotContain("SharedProgressGuard", vm);
        var harvestBranch = Between(run, "if (harvest)\r\n            {\r\n                // App Harvest: Chạy hay Tiếp tục", "else\r\n            {\r\n                // RESET");
        Assert.Contains("HarvestDoneRebuild.Build(startRow, totalRows, harvestRange!.Links?.Items ?? [],", harvestBranch);
        Assert.Contains("outbox.DoneKeys(doneTarget), outbox.FailedKeys(doneTarget)", harvestBranch);
        Assert.Contains("RoleStores.ScrapeProgress.ReplaceProgress(account.Id, sheet, account.DisplayName, totalRows, rebuilt.Completed, rebuilt.Skipped);", harvestBranch);
        Assert.DoesNotContain("MarkCompleted(", harvestBranch);
        Assert.Contains("progressTouched = true;", harvestBranch);
        Assert.Contains("segments = rebuilt.Remaining;", harvestBranch);
        Assert.Contains("var progressTouched = !harvest;", run);
        Assert.Contains("if (progressTouched)\r\n                try { RoleStores.ScrapeProgress.FinishRun(account.Id, sheet, startRow, 0); }",
            Between(run, "catch (OperationCanceledException)", "catch (Exception ex)"));
        Assert.Contains("FailureSink: HarvestRuntime.Current.Outbox", run);
        Assert.DoesNotContain("ScrapeProgressReset.Reset(", harvestBranch);          // Chạy ở Harvest không xoá tập đã xong
        Assert.DoesNotContain("RemainingSegments(", harvestBranch);
        Assert.Contains("HarvestWorkbookReader.ReadRangeAsync(dataPath, dataSheet, excelStart, excelEnd, ct)", run);
        Assert.Contains("catch (HarvestWorkbookLockedException ex)", run);
        Assert.Contains("if (resume || harvest) RoleStores.ScrapeProgress.BeginResume(", run);
        Assert.Contains("DoneTarget: HarvestDoneTarget.Of(account.Id, sheet)", run);
    }

    [Fact]
    public void NoiDay_ResetTienDo_XoaTapDaXong_ThongKeHoiTruocOHarvest()
    {
        var reset = Src(Path.Combine("src", "Shopee.Workspace", "Modules", "Scrape", "ScrapeProgressReset.cs"));
        Assert.Contains("ClearKey(accountId, sheet, log);", Between(reset, "public static void Reset(string accountId, string sheet, BigSellerShop? shop", "/// <summary>ĐƯỜNG DUY NHẤT"));
        var bySheet = Between(reset, "public static void Reset(string accountId, string sheet, Action<string>? log = null)", "\r\n}");
        Assert.DoesNotContain("RoleStores.ScrapeProgress.Clear(", bySheet);
        Assert.Contains("ClearKey(accountId, sheet ?? \"\", log);", bySheet);
        var stats = Src(Path.Combine("src", "Shopee.Workspace", "Modules", "Scrape", "ScrapeStatsViewModel.cs"));
        var clear = Between(stats, "private async Task ClearProgress(", "ScrapeProgressReset.Reset(");
        Assert.Contains("WorkspaceAppConfig.IsHarvest", clear);
        Assert.Contains("Dialogs.ConfirmAsync(", clear);
    }

    [Fact]
    public void NoiDay_MoiNutResetQuaDuongChung_DangKyJobHarvestDungResolveChat()
    {
        foreach (var f in new[]
        {
            Path.Combine("src", "Shopee.Workspace", "Modules", "Workspace", "WorkspaceAccountViewModel.cs"),
            Path.Combine("src", "Shopee.Workspace", "Modules", "Workspace", "WorkspaceShopViewModel.cs"),
        })
        {
            var text = Src(f);
            Assert.DoesNotContain("RoleStores.ScrapeProgress.Clear(", text);
            Assert.Contains("ScrapeProgressReset.ClearKey(", text);
        }
        var shopVm = Src(Path.Combine("src", "Shopee.Workspace", "Modules", "Workspace", "WorkspaceShopViewModel.cs"));
        Assert.Equal(2, Between(shopVm, "private void ResetScrapeProgress()", "[RelayCommand]").Split("ScrapeProgressReset.ClearKey(").Length - 1);
        Assert.Contains("[RelayCommand(CanExecute = nameof(CanMarkScrapeDone))]", shopVm);
        var acct = Src(Path.Combine("src", "Shopee.Workspace", "Modules", "Workspace", "WorkspaceAccountViewModel.cs"));
        Assert.Contains("ScrapeProgressReset.ClearKey(Account.Id, scrapeKey);", Between(acct, "private async Task ResetAllShopsProgressAsync()", "else if (WorkspaceAppConfig.IsImportUpdateOnly)"));
        var reset = Src(Path.Combine("src", "Shopee.Workspace", "Modules", "Scrape", "ScrapeProgressReset.cs"));
        var clearKey = Between(reset, "public static void ClearKey(", "/// <summary>App Harvest");
        Assert.Contains("RoleStores.ScrapeProgress.Clear(accountId, sheet);", clearKey);
        Assert.Contains("ClearHarvestDone(accountId, sheet, log);", clearKey);

        var vm = Src(Path.Combine("src", "Shopee.Workspace", "Modules", "Scrape", "ScrapeViewModel.cs"));
        var start = Between(vm, "private bool StartJob", "var started = s.Jobs.TryAdd(");
        Assert.Contains("if (WorkspaceAppConfig.IsHarvest)", start);
        Assert.Contains("HarvestWorkbookResolve.ResolveCurrent(target.Account, shop, out var resolveError) is not { } strict", start);
        Assert.Contains("refused = resolveError;", start);
        var one = Between(vm, "private bool StartOneAccount", "/// <summary>Dừng RIÊNG 1 tk");
        Assert.Contains("if (refused is null)", one);
    }

    [Fact]
    public void NoiDay_CaoLaiDongDaBo_XoaTapLoiCuaTarget_ResetXoaCaHai()
    {
        var stats = Src(Path.Combine("src", "Shopee.Workspace", "Modules", "Scrape", "ScrapeStatsViewModel.cs"));
        var reopen = Between(stats, "private Task ReopenSkippedAsync(", "Load();");
        Assert.Contains("ScrapeProgressReset.ClearHarvestFailed(_accountId, sheet);", reopen);
        var reset = Src(Path.Combine("src", "Shopee.Workspace", "Modules", "Scrape", "ScrapeProgressReset.cs"));
        var clear = Between(reset, "internal static int ClearHarvestDone(", "/// <summary>App Harvest, nút");
        Assert.Contains("rt.Outbox.ClearFailed(target);", clear);
        Assert.Contains("return rt.Outbox.ClearDone(target);", clear);
    }

    [Fact]
    public void NoiDay_EngineLuuGhiDoneTarget()
    {
        var proc = Src(Path.Combine("src", "Shopee.Module.MultiBrave", "Engine", "HarvestRowProcessor.cs"));
        Assert.Contains("_options.DoneTarget);", Between(proc, "var harvested = new HarvestedItem(", "var saveError"));
        var loop = Src(Path.Combine("src", "Shopee.Module.MultiBrave", "Engine", "HarvestRunnerLoop.cs"));
        var flushLine = System.Text.RegularExpressions.Regex.Match(loop, @"^[ \t]+await processor\.FlushPendingNotProductAsync\(cancellationToken\)",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        Assert.True(flushLine.Success, "phải GỌI (không comment) FlushPendingNotProductAsync");
        var flush = flushLine.Index;
        Assert.True(flush > 0 && flush < loop.IndexOf("processor.CompleteBlock(totalLinks, sheet);", StringComparison.Ordinal),
            "hết khối phải xả chuỗi notProduct chưa tới ngưỡng TRƯỚC CompleteBlock");
        var vm = Src(Path.Combine("src", "Shopee.Workspace", "Modules", "Scrape", "ScrapeViewModel.cs"));
        Assert.Contains("RunId: Guid.NewGuid().ToString(\"N\")", vm);
    }

    [Fact]
    public void NoiDay_CauHinhChay_ManDichScrape_DiQuaNguonTheoVaiTro()
    {
        var vm = Src(Path.Combine("src", "Shopee.Workspace", "Modules", "Scrape", "ScrapeTargetViewModel.cs"));
        Assert.Contains("RunConfigSource.For(WorkspaceAppConfig.Role, Account)", vm);
        Assert.Contains("RunConfigSource.Save(WorkspaceAppConfig.Role)", vm);
        Assert.DoesNotContain("BigSellerStore.Shared.Save()", vm);   // mọi lần lưu cấu hình chạy đi qua nguồn theo vai trò
        Assert.DoesNotContain("Account.RunConfig ??=", vm);
    }

    [Fact]
    public void NoiDay_HarvestDong_KhongLuuCaBigSellerJson_ChoDebounceGhiXong()
    {
        var app = Src(Path.Combine("src", "Shopee.Workspace", "App.xaml.cs"));
        var hooks = Between(app, "private void RunShutdownHooks()", "private static void HookBindingTrace()");
        var harvest = Between(hooks, "if (WorkspaceAppConfig.IsHarvest)", "else");
        Assert.Contains("Thread.Sleep(HarvestRuntime.PendingSaveWait)", harvest);
        // Chờ phải dài hơn độ trễ debounce THẬT (đọc từ mã nguồn PersistDebounce dùng chung) + biên 100 ms.
        var debounce = Src(Path.Combine("src", "Shopee.Workspace", "Modules", "BigSeller", "PersistDebounce.cs"));
        var m = System.Text.RegularExpressions.Regex.Match(debounce, @"_timer\.Change\((\d+),");
        Assert.True(m.Success);
        Assert.True(HarvestRuntime.PendingSaveWait.TotalMilliseconds >= int.Parse(m.Groups[1].Value) + 100);
        Assert.DoesNotContain("BigSellerStore.Shared.Save()", harvest);
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

    [Fact]
    public void NoiDay_HarvestKhongMigrateRunConfig()
    {
        foreach (var f in new[]
        {
            Path.Combine("src", "Shopee.Workspace", "Modules", "Scrape", "ScrapeTargetViewModel.cs"),
            Path.Combine("src", "Shopee.Workspace", "Modules", "UpdateProduct", "UpdateRunTargetViewModel.cs"),
        })
        {
            var text = Src(f);
            var i = text.IndexOf("RunConfigMigration.EnsureRunConfig(account)", StringComparison.Ordinal);
            Assert.True(i > 0, f);
            Assert.Contains("IsHarvest", text[Math.Max(0, i - 200)..i]);
        }
    }

    [Fact]
    public void NoiDay_NapLaiCauHinhWorkbookChung_TruocKhiResolve()
    {
        const string reload = "if (WorkspaceAppConfig.IsHarvest) SharedWorkbookStore.ReloadIfExternalChange();";
        var vm = Src(Path.Combine("src", "Shopee.Workspace", "Modules", "Scrape", "ScrapeViewModel.cs"));
        Assert.Contains(reload, Between(vm, "private async Task StartCoreAsync", "ValidateTarget("));
        Assert.Contains(reload, Between(vm, "private bool StartOneAccount", "ValidateTarget("));
        var ws = Src(Path.Combine("src", "Shopee.Workspace", "Modules", "Workspace", "WorkspaceViewModel.cs"));
        Assert.Contains(reload, Between(ws, "externalSyncTimer.Tick +=", "externalSyncTimer.Start();"));
        var shell = Src(Path.Combine("src", "Shopee.Workspace", "ViewModels", "ShellViewModel.cs"));
        Assert.Contains("SharedWorkbookStore.ReloadIfExternalChange();",
            Between(shell, "partial void OnSelectedTabChanged", "private RibbonTab?"));
    }

    /// <summary>Nối dây VM: app Harvest không gọi được chọn file chung / gộp / ngừng dùng (BigSeller + Workspace).</summary>
    [Theory]
    [InlineData(WorkspaceRole.Harvest, false)]
    [InlineData(WorkspaceRole.Scrape, true)]
    public void VM_LenhSuaWorkbookChung_BiKhoaOHarvest(WorkspaceRole role, bool canExecute)
    {
        Exception? error = null;
        var results = new List<(string Name, bool Can)>();
        var thread = new Thread(() =>
        {
            try
            {
                WorkspaceAppConfig.Initialize(role);
                lock (AppGate)
                {
                    if (System.Windows.Application.Current is null)
                    {
                        try { new App().InitializeComponent(); }
                        catch (InvalidOperationException) { /* lớp test khác vừa tạo App */ }
                    }
                }
                var shell = new ViewModels.ShellViewModel();
                results.Add(("PickShared", shell.BigSeller.PickSharedWorkbookCommand.CanExecute(null)));
                results.Add(("MergeShared", shell.BigSeller.MergeSharedCommand.CanExecute(null)));
                results.Add(("StopShared", shell.BigSeller.StopSharedCommand.CanExecute(null)));
                results.Add(("WsPickShared", shell.Workspace.PickSharedUpdateWorkbookCommand.CanExecute(null)));
                results.Add(("WsClearShared", shell.Workspace.ClearSharedUpdateWorkbookCommand.CanExecute(null)));
            }
            catch (Exception ex) { error = ex; }
            finally
            {
                WorkspaceAppConfig.Initialize(WorkspaceRole.Full);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
        Assert.Equal(5, results.Count);
        Assert.All(results, r => Assert.True(r.Can == canExecute, $"{r.Name} CanExecute={r.Can}"));
    }

    private static readonly object AppGate = new();

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
