using Shopee.Core.BigSeller;
using Shopee.Core.Browser;
using Shopee.Core.Coordination;
using Shopee.Core.Harvest;
using Shopee.Core.Scrape;
using static Shopee.Core.Tests.HarvestTestData;

namespace Shopee.Core.Tests;

/// <summary>
/// GĐ5: phần Core mà exe Harvest nối vào — sink bọc gọi Nudge sau khi lưu, poller Hub fold ledger theo op của process
/// (Harvest không đụng scrape-progress.json), quét Brave mồ côi của <c>harvest-profiles</c> chỉ nhìn ShopeeHarvest.
/// </summary>
public sealed class HarvestAppWiringTests
{
    // ── NudgingHarvestSink ──────────────────────────────────────────────────────────────────────────────────────

    private sealed class FakeSink : IHarvestSink
    {
        public Exception? SaveError { get; set; }
        public int Saved { get; private set; }
        public int Ready { get; private set; }
        public List<string> Order { get; } = [];

        public Task EnsureReadyAsync(CancellationToken cancellationToken)
        {
            Ready++;
            return Task.CompletedTask;
        }

        public async Task SaveAsync(HarvestedItem item, CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (SaveError is not null) throw SaveError;
            Saved++;
            Order.Add("save");
        }
    }

    private static HarvestedItem AnyItem() =>
        new("acc", "shop", "Sheet", 3, "https://shopee.vn/product/1/2", "https://shopee.vn/x-i.1.2", 2, 1,
            SyntheticJson(2), "hook", T0);

    [Fact]
    public async Task NudgingSink_LuuXong_GoiOnSavedMotLan_SauKhiLuu()
    {
        var inner = new FakeSink();
        var nudged = 0;
        var sink = new NudgingHarvestSink(inner, () => { nudged++; inner.Order.Add("nudge"); });

        await sink.SaveAsync(AnyItem(), CancellationToken.None);

        Assert.Equal(1, inner.Saved);
        Assert.Equal(1, nudged);
        Assert.Equal(["save", "nudge"], inner.Order);
    }

    [Fact]
    public async Task NudgingSink_SinkThatNem_KhongGoiOnSaved_VaNemNguyenLoi()
    {
        var boom = new IOException("đĩa đầy");
        var inner = new FakeSink { SaveError = boom };
        var nudged = 0;
        var sink = new NudgingHarvestSink(inner, () => nudged++);

        var ex = await Assert.ThrowsAsync<IOException>(() => sink.SaveAsync(AnyItem(), CancellationToken.None));

        Assert.Same(boom, ex);
        Assert.Equal(0, nudged);
    }

    [Fact]
    public async Task NudgingSink_OnSavedNem_BiNuot_VaVanTinhLaDaLuu()
    {
        var inner = new FakeSink();
        var sink = new NudgingHarvestSink(inner, () => throw new InvalidOperationException("UI chết"));

        await sink.SaveAsync(AnyItem(), CancellationToken.None);   // không ném → engine tính dòng xong

        Assert.Equal(1, inner.Saved);
    }

    [Fact]
    public async Task NudgingSink_EnsureReady_ChuyenThang_KhongGoiOnSaved()
    {
        var inner = new FakeSink();
        var nudged = 0;
        var sink = new NudgingHarvestSink(inner, () => nudged++);

        await sink.EnsureReadyAsync(CancellationToken.None);

        Assert.Equal(1, inner.Ready);
        Assert.Equal(0, nudged);
    }

    // ── Fold ledger theo op của process ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FoldTarget_MacDinhScrape_GiuNguyenHanhViCu()
    {
        Assert.Equal(CoordOp.Scrape, HttpCoordinationHub.ProgressFoldOp);
        var (op, store) = HttpCoordinationHub.FoldTarget(CoordOp.Scrape);
        Assert.Equal(AssignmentOps.Scrape, op);
        Assert.Same(ScrapeProgressStore.Shared, store);
    }

    [Fact]
    public void FoldTarget_Harvest_ChiLedgerHarvest_VaoKhoHarvest()
    {
        var (op, store) = HttpCoordinationHub.FoldTarget(CoordOp.Harvest);
        Assert.Equal(AssignmentOps.Harvest, op);
        Assert.Same(ScrapeProgressStore.Harvest, store);
        Assert.NotSame(ScrapeProgressStore.Shared, store);
    }

    // ── Quét Brave mồ côi theo root ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SweepPeers_HarvestProfiles_ChiShopeeHarvest()
    {
        Assert.Equal(["ShopeeHarvest"], BraveFleet.SweepPeerProcessNames(@"C:\x\persistent-data\harvest-profiles"));
    }

    [Fact]
    public void SweepPeers_CacRootCu_GiuNguyen()
    {
        Assert.Equal(["ShopeeScrape", "ShopeeWorkspace", "ShopeeSuite"],
            BraveFleet.SweepPeerProcessNames(@"C:\x\persistent-data\profiles"));
        Assert.Equal(["ShopeeUpdate", "ShopeeWorkspace", "ShopeeSuite"],
            BraveFleet.SweepPeerProcessNames(@"C:\x\persistent-data\bigseller-profiles"));
        Assert.Equal(["ShopeeWorkspace", "ShopeeScrape", "ShopeeUpdate", "ShopeeSuite", "ShopeeHarvest"],
            BraveFleet.SweepPeerProcessNames(@"C:\x\persistent-data"));
    }

    /// <summary>Root mặc định (app Full, không SetManagedSubRoot) BAO harvest-profiles → Harvest đang chạy phải làm Full
    /// thôi quét (không thì StartupSweep/RunMaintenance giết Brave Harvest đang làm việc).</summary>
    [Fact]
    public void SweepPeers_RootMacDinh_DemCaShopeeHarvest()
    {
        Assert.Contains("ShopeeHarvest", BraveFleet.SweepPeerProcessNames(SuitePathsPersistent()));
        // Root riêng của Scrape / Update KHÔNG chứa harvest-profiles → không cần đếm Harvest (giữ nguyên nhánh cũ).
        Assert.DoesNotContain("ShopeeHarvest", BraveFleet.SweepPeerProcessNames(Path.Combine(SuitePathsPersistent(), "profiles")));
        Assert.DoesNotContain("ShopeeHarvest", BraveFleet.SweepPeerProcessNames(Path.Combine(SuitePathsPersistent(), "bigseller-profiles")));
    }

    private static string SuitePathsPersistent() =>
        Path.GetFullPath(Shopee.Core.Infrastructure.SuitePaths.ModuleDir("persistent-data")).TrimEnd('\\');

    [Fact]
    public void SoleAppPeers_DemCaShopeeHarvest_VaGiuTenCu()
    {
        Assert.Equal(["ShopeeWorkspace", "ShopeeScrape", "ShopeeUpdate", "ShopeeSuite", "ShopeeHarvest"],
            BraveFleet.SoleAppPeerNames);
    }

    // ── Bộ đọc workbook riêng của Harvest ──

    private static string NewXlsx(string dir, string name, params (string A, string F, string G)[] rows)
    {
        var path = Path.Combine(dir, name);
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.AddWorksheet("Data");
        wb.AddWorksheet("Khac").Cell(2, 1).Value = "x";
        ws.Cell(1, 1).Value = "Link"; ws.Cell(1, 6).Value = "Tên"; ws.Cell(1, 7).Value = "Tên mới";
        for (var i = 0; i < rows.Length; i++)
        {
            ws.Cell(i + 2, 1).Value = rows[i].A;
            ws.Cell(i + 2, 6).Value = rows[i].F;
            if (rows[i].G.Length > 0) ws.Cell(i + 2, 7).Value = rows[i].G;
        }
        wb.SaveAs(path);
        return path;
    }

    [Fact]
    public async Task BoDocHarvest_FileDangBiGiuGhi_VanDoc_KhongGiuHandleLau()
    {
        var dir = NewTempDir();
        try
        {
            var path = NewXlsx(dir, "nguon.xlsx", ("https://x/1", "SP1", ""));
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))   // như Excel đang mở để sửa
                Assert.Equal(1, await HarvestWorkbookReader.TotalDataRowsAsync(path, "Data", CancellationToken.None));
            Assert.True(HarvestWorkbookReader.LastHandleHeld < TimeSpan.FromMilliseconds(500),
                $"giữ handle {HarvestWorkbookReader.LastHandleHeld.TotalMilliseconds} ms");
        }
        finally { TryDelete(dir); }
    }

    /// <summary>Đọc link của Harvest (RAM) phải giống hệt <see cref="ScrapeWorkbook"/> đọc file: dòng trống giữa, thiếu tên (F),
    /// thiếu link, link www., cột không header, khoảng start/end, ExcelRowsToDenseRange, TotalDataRows.</summary>
    [Fact]
    public async Task BoDocHarvest_DocLink_GiongHetScrapeWorkbook()
    {
        var dir = NewTempDir();
        try
        {
            var path = Path.Combine(dir, "nguon.xlsx");
            using (var wb = new ClosedXML.Excel.XLWorkbook())
            {
                var ws = wb.AddWorksheet("Shop A");
                ws.Cell(1, 1).Value = "Link"; ws.Cell(1, 6).Value = "Tên"; ws.Cell(1, 9).Value = "SKU";
                var r = 2;
                for (var i = 0; i < 300; i++, r++)
                {
                    if (i % 37 == 5) { r++; }                                          // dòng trống giữa
                    ws.Cell(r, 1).Value = i % 23 == 3 ? "" : i % 11 == 0 ? $"www.shopee.vn/p/{i}" : $" https://shopee.vn/p/{i} ";
                    if (i % 17 != 4) ws.Cell(r, 6).Value = "SP " + i;
                    ws.Cell(r, 8).Value = "x" + i;                                     // cột không header
                    ws.Cell(r, 9).Value = "SKU" + i;
                }
                wb.SaveAs(path);
            }
            static string Dump(ScrapeLinkResult x) => string.Join("\n", x.Items.Select(i =>
                $"{i.RowNumber}|{i.Link}|{string.Join(";", i.Values.Select(kv => kv.Key + "=" + kv.Value))}"))
                + $"#{x.TotalDataRows}/{x.SkippedMissingProductName}/{x.SkippedMissingLink}";
            foreach (var (s, e) in new[] { (1, 300), (1, 10), (50, 120), (290, 400) })
                Assert.Equal(Dump(ScrapeWorkbook.FetchLinks(path, "Shop A", s, e)),
                    Dump(await HarvestWorkbookReader.FetchLinksAsync(path, "Shop A", s, e, CancellationToken.None)));
            foreach (var (s, e) in new[] { (2, 0), (2, 50), (40, 0), (100, 120), (999, 0) })
            {
                var dense = ScrapeWorkbook.ExcelRowsToDenseRange(path, "Shop A", s, e);
                var range = await HarvestWorkbookReader.ReadRangeAsync(path, "Shop A", s, e, CancellationToken.None);
                Assert.Equal(dense, (range.DenseStart, range.DenseEnd));
                if (dense.denseEnd >= dense.denseStart)
                    Assert.Equal(Dump(ScrapeWorkbook.FetchLinks(path, "Shop A", dense.denseStart, dense.denseEnd)), Dump(range.Links!));
                else Assert.Null(range.Links);
            }
            Assert.Equal(ScrapeWorkbook.TotalDataRows(path, "Shop A"),
                await HarvestWorkbookReader.TotalDataRowsAsync(path, "Shop A", CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => HarvestWorkbookReader.FetchLinksAsync(path, "Khong Co", 1, 2, CancellationToken.None));
        }
        finally { TryDelete(dir); }
    }

    [Fact]
    public async Task BoDocHarvest_KhoaUpdate_ChoNhaRoiDoc_QuaHanThiBaoLoiCoTienTo_HuyDuoc()
    {
        var dir = NewTempDir();
        var (oldWait, oldPoll) = (HarvestWorkbookReader.LockWait, HarvestWorkbookReader.LockPoll);
        try
        {
            var path = NewXlsx(dir, "nguon.xlsx", ("https://x/1", "SP1", ""));
            HarvestWorkbookReader.LockPoll = TimeSpan.FromMilliseconds(100);
            HarvestWorkbookReader.LockWait = TimeSpan.FromSeconds(5);
            var held = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            Assert.True(HarvestWorkbookReader.IsUpdateLocked(path));
            Assert.Throws<HarvestWorkbookLockedException>(() => HarvestWorkbookReader.ReadAllShared(path));   // bản không chờ
            var release = Task.Run(async () => { await Task.Delay(600); held.Dispose(); });
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Assert.Equal(1, await HarvestWorkbookReader.TotalDataRowsAsync(path, "Data", CancellationToken.None));   // chờ nhả rồi đọc
            Assert.True(sw.ElapsedMilliseconds >= 500, $"{sw.ElapsedMilliseconds} ms");
            await release;
            Assert.False(HarvestWorkbookReader.IsUpdateLocked(path));               // file .lock còn nhưng không ai giữ

            HarvestWorkbookReader.LockWait = TimeSpan.FromMilliseconds(300);
            using (new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                sw.Restart();
                var ex = await Assert.ThrowsAsync<HarvestWorkbookLockedException>(
                    () => HarvestWorkbookReader.FetchLinksAsync(path, "Data", 1, 1, CancellationToken.None));
                Assert.True(sw.ElapsedMilliseconds < 3000, $"chờ {sw.ElapsedMilliseconds} ms — phải dừng theo LockWait");
                Assert.True(HarvestWorkbookLockedException.Is(ex.Message));

                // Huỷ được giữa lúc chờ (không Thread.Sleep).
                HarvestWorkbookReader.LockWait = TimeSpan.FromMinutes(5);
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
                sw.Restart();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => HarvestWorkbookReader.ReadRangeAsync(path, "Data", 2, 0, cts.Token));
                Assert.True(sw.ElapsedMilliseconds < 3000, $"huỷ mất {sw.ElapsedMilliseconds} ms");
            }
        }
        finally
        {
            (HarvestWorkbookReader.LockWait, HarvestWorkbookReader.LockPoll) = (oldWait, oldPoll);
            TryDelete(dir);
        }
    }

    /// <summary>Đọc link xong là handle đã đóng: ngay sau đó ghi đè file (như <c>wb.Save()</c> của Update) không lỗi.</summary>
    [Fact]
    public async Task BoDocHarvest_DocLinkXong_FileGhiDuocNgay()
    {
        var dir = NewTempDir();
        try
        {
            var path = NewXlsx(dir, "nguon.xlsx", ("https://x/1", "SP1", ""), ("https://x/2", "SP2", ""));
            Assert.Equal(2, (await HarvestWorkbookReader.FetchLinksAsync(path, "Data", 1, 2, CancellationToken.None)).Items.Count);
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) fs.WriteByte((byte)'P');
        }
        finally { TryDelete(dir); }
    }

    // ── Khoá SP + dựng lại tiến độ từ tập đã xong ──

    [Theory]
    [InlineData("https://shopee.vn/Ao-thun-nam-i.123.456", "id:123.456")]
    [InlineData("http://shopee.vn/Ao-thun-nam-i.123.456?sp_atk=x&xptdk=y#frag", "id:123.456")]
    [InlineData("  www.shopee.vn/ao-i.123.456  ", "id:123.456")]
    [InlineData("shopee.vn/ao-I.123.456", "id:123.456")]
    [InlineData("HTTPS://SHOPEE.VN/AO-I.123.456", "id:123.456")]
    [InlineData("https://shopee.vn/product/123/456", "id:123.456")]
    [InlineData("https://shopee.vn/product/123/456/?a=1", "id:123.456")]
    [InlineData("https://shopee.vn/Wi.2.4G-may-i.123.456", "id:123.456")]                     // "Wi.2" không phải id
    [InlineData("https://shopee.vn/combo-i.1.2-kem-i.123.456", "id:123.456")]                 // cặp id CUỐI
    [InlineData("https://shopee.vn/Bo-phat-Wi.2.4G", "url:shopee.vn/Bo-phat-Wi.2.4G")]          // "Wi.2.4G" không phải id
    [InlineData("https://S.Shopee.VN/AbCd/?x=1#y", "url:s.shopee.vn/AbCd?x=1")]                // query GIỮ (bỏ fragment)
    [InlineData("https://s.shopee.vn/AbCd?sp_atk=9&utm_source=zalo&xptdk=1", "url:s.shopee.vn/AbCd")] // chỉ tham số theo dõi → bỏ
    [InlineData("https://shopee.vn/x?b=2&a=1&utm_x=3", "url:shopee.vn/x?a=1&b=2")]              // tham số sắp theo tên
    [InlineData("https://s.shopee.vn/an_redir?origin_link=https%3A%2F%2Fshopee.vn%2Fao-i.11.22%3Fsp_atk%3Dx&affiliate_id=1", "id:11.22")]
    [InlineData("https://s.shopee.vn/an_redir?origin_link=https%253A%252F%252Fshopee.vn%252Fproduct%252F11%252F22", "id:11.22")]   // decode lồng
    [InlineData("https://shopee.vn/universal-link?redir=https%3A%2F%2Fshopee.vn%2Fproduct%2F11%2F22%3Fx%3D1", "id:11.22")]
    [InlineData("https://shopee.vn/universal-link/product/11/22?deep_and_deferred=1", "id:11.22")]
    [InlineData("https://shopee.vn/product?shopid=11&itemid=22", "id:11.22")]
    [InlineData("https://shopee.vn/product?itemId=22&shopId=11&sp_atk=x", "id:11.22")]
    [InlineData("https://shopee.vn/universal-link?redir=https%3A%2F%2Fshopee.vn%2Fsearch%3Fkeyword%3Dao", "url:shopee.vn/universal-link?redir=https://shopee.vn/search?keyword=ao")]
    [InlineData("http://s.shopee.vn/AbCd", "url:s.shopee.vn/AbCd")]
    [InlineData("không phải link", "txt:không phải link")]
    public void KhoaSP_TuLink(string link, string key) => Assert.Equal(key, HarvestItemKey.FromLink(link));

    /// <summary>Phản biện vòng 12: khoá url: bỏ hết query thì nhiều SP khác nhau (link chuyển hướng / rút gọn khác tham số)
    /// ra CÙNG khoá → SP thứ hai bị coi là đã xong. Nay khác SP → khác khoá.</summary>
    [Fact]
    public void KhoaSP_LinkChuyenHuongKhacNhau_KhacKhoa()
    {
        string[] links =
        [
            "https://s.shopee.vn/an_redir?origin_link=https%3A%2F%2Fshopee.vn%2Fao-i.1.2",
            "https://s.shopee.vn/an_redir?origin_link=https%3A%2F%2Fshopee.vn%2Fquan-i.1.3",
            "https://shopee.vn/universal-link?redir=https%3A%2F%2Fshopee.vn%2Fproduct%2F1%2F4",
            "https://shopee.vn/universal-link?redir=https%3A%2F%2Fshopee.vn%2Fproduct%2F1%2F5",
            "https://shopee.vn/product?shopid=1&itemid=6",
            "https://shopee.vn/product?shopid=1&itemid=7",
            "https://s.shopee.vn/an_redir?origin_link=https%3A%2F%2Fshopee.vn%2Fm%2Fsale-a",
            "https://s.shopee.vn/an_redir?origin_link=https%3A%2F%2Fshopee.vn%2Fm%2Fsale-b",
        ];
        var keys = links.Select(HarvestItemKey.FromLink).ToList();
        Assert.Equal(links.Length, keys.Distinct().Count());
    }

    /// <summary>Phản biện vòng 13: chỉ tách id trên giá trị tham số TRÔNG NHƯ URL hoặc cặp shopid/itemid ngay ở query —
    /// không quét cả chuỗi query.</summary>
    [Theory]
    [InlineData("https://shopee.vn/search?keyword=ao-i.5.6", "url:shopee.vn/search?keyword=ao-i.5.6")]
    [InlineData("https://shopee.vn/shop?shopid=7&redir=x%26itemid%3D5", "url:shopee.vn/shop?redir=x&itemid=5&shopid=7")]
    [InlineData("https://shopee.vn/search?keyword=%2Fproduct%2F5%2F6", "url:shopee.vn/search?keyword=/product/5/6")]
    [InlineData("https://shopee.vn/search?keyword=shopee.vn%2Fao-i.5.6", "url:shopee.vn/search?keyword=shopee.vn/ao-i.5.6")]   // không có http(s):// → không phải link lồng
    public void KhoaSP_QueryGia_KhongThanhId(string link, string key)
    {
        Assert.Equal(key, HarvestItemKey.FromLink(link));
        Assert.DoesNotContain("id:", HarvestItemKey.FromLink(link));
    }

    /// <summary>JSON trả SP KHÁC link (link chuyển hướng / sai): không ghi khoá id: suy từ link (sai SP), mà ghi khoá url: của link
    /// + khoá id thật từ JSON.</summary>
    [Fact]
    public void KhoaLuuXong_LinkKhacJson_KhongGhiIdSai()
    {
        Assert.Equal(["url:shopee.vn/ao-i.1.2", "id:1.3"], HarvestItemKey.ForSaved("https://shopee.vn/ao-i.1.2", 3, 1));
        Assert.Equal(["url:shopee.vn/ao-i.1.2", "id:9.2"], HarvestItemKey.ForSaved("https://shopee.vn/ao-i.1.2", 2, 9));   // shop lệch
        Assert.Equal(["id:1.2"], HarvestItemKey.ForSaved("https://shopee.vn/ao-i.1.2", 2, 1));
        Assert.Equal(["id:1.2"], HarvestItemKey.ForSaved("https://shopee.vn/ao-i.1.2", 2, null));                         // JSON không có shop
        Assert.Equal(["url:shopee.vn/ao-i.1.2"], HarvestItemKey.ForSaved("https://shopee.vn/ao-i.1.2", 3, null));
    }

    [Fact]
    public void KhoaSP_Rong_Null_VaKhoaLuuXong()
    {
        Assert.Null(HarvestItemKey.FromLink("  "));
        Assert.Null(HarvestItemKey.FromLink(null));
        Assert.Equal(["id:1.2"], HarvestItemKey.ForSaved("https://shopee.vn/x-i.1.2", 2, 1));
        Assert.Equal(["url:shp.ee/q", "id:9.8"], HarvestItemKey.ForSaved("https://shp.ee/q", 8, 9));
        Assert.Equal(["url:shp.ee/q"], HarvestItemKey.ForSaved("https://shp.ee/q", 8, null));
    }

    private static ScrapeLinkItem L(int row, string link) => new(row, link, new Dictionary<string, string>());

    private static string Show(HarvestDoneRebuildResult r) =>
        string.Join(",", r.Completed.Select(c => $"{c.From}-{c.To}")) + " | " + string.Join(",", r.Remaining.Select(x => $"{x.from}-{x.to}"));

    private static HashSet<string> Done(params long[] items) => items.Select(i => HarvestItemKey.FromIds(1, i)).ToHashSet();

    private static string U(long item) => $"https://shopee.vn/sp-i.1.{item}";

    [Fact]
    public void DungLai_TapRong_ConCaDai_TapDu_XongCaDai()
    {
        var items = new[] { L(1, U(10)), L(2, U(11)), L(3, U(12)) };
        Assert.Equal(" | 1-3", Show(HarvestDoneRebuild.Build(1, 3, items, Done())));
        var all = HarvestDoneRebuild.Build(1, 3, items, Done(10, 11, 12));
        Assert.Equal("1-3 | ", Show(all));
        Assert.Equal((3, 0), (all.DoneRows, all.TodoRows));
    }

    [Fact]
    public void DungLai_ChenDauDoiThuTuXoaDong_DongCoSPDaLayVanXong()
    {
        var done = Done(10, 11, 12);                                                          // lượt trước: 10, 11, 12
        var chen = new[] { L(1, U(99)), L(2, U(10)), L(3, U(11)), L(4, U(12)) };            // chèn SP mới ở đầu
        Assert.Equal("2-4 | 1-1", Show(HarvestDoneRebuild.Build(1, 4, chen, done)));
        var dao = new[] { L(1, U(12)), L(2, U(98)), L(3, U(10)) };                           // đổi thứ tự + xoá 11 + thêm 98
        Assert.Equal("1-1,3-3 | 2-2", Show(HarvestDoneRebuild.Build(1, 3, dao, done)));
        var them = new[] { L(1, U(10)), L(2, U(11)), L(3, U(12)), L(4, U(13)), L(5, U(14)) };   // thêm cuối
        Assert.Equal("1-3 | 4-5", Show(HarvestDoneRebuild.Build(1, 5, them, done)));
    }

    [Fact]
    public void DungLai_GopLaiDoiNguon_LinkKhacDangCungSP_VanXong()
    {
        var done = new HashSet<string> { HarvestItemKey.FromLink("https://shopee.vn/ao-i.1.10?sp_atk=a")! };
        var nguonMoi = new[] { L(1, "https://shopee.vn/product/1/10"), L(2, "www.shopee.vn/quan-i.1.20") };
        Assert.Equal("1-1 | 2-2", Show(HarvestDoneRebuild.Build(1, 2, nguonMoi, done)));
    }

    [Fact]
    public void DungLai_KhoaTrung_MotLanXongLaTatCaXong_KhongTachDuocThiSoUrl()
    {
        var items = new[] { L(1, U(10)), L(2, "https://shp.ee/abc"), L(3, U(10)), L(4, "https://shp.ee/abc/?sp_atk=1"), L(5, "https://shp.ee/khac") };
        var done = new HashSet<string> { HarvestItemKey.FromIds(1, 10), HarvestItemKey.FromLink("https://SHP.EE/abc")! };
        Assert.Equal("1-4 | 5-5", Show(HarvestDoneRebuild.Build(1, 5, items, done)));
    }

    [Fact]
    public void DungLai_LoiVinhVien_KhongLenLich_VaoSoBoQua_DaXongThangLoi()
    {
        var items = new[] { L(1, U(10)), L(2, U(11)), L(3, U(12)), L(4, U(13)) };
        var failed = new HashSet<string> { HarvestItemKey.FromIds(1, 11), HarvestItemKey.FromIds(1, 13) };
        var r = HarvestDoneRebuild.Build(1, 4, items, Done(13), failed);   // 13 vừa lỗi vừa đã xong → xong
        Assert.Equal("2-2,4-4 | 1-1,3-3", Show(r));
        Assert.Equal([2], r.Skipped);
        Assert.Equal((1, 2), (r.DoneRows, r.TodoRows));
    }

    /// <summary>Kho tiến độ: API thêm cho Harvest ghi đè cả vùng phủ + sổ bỏ qua bằng MỘT lần ghi / MỘT lần Changed.</summary>
    [Fact]
    public void KhoTienDo_ReplaceProgress_MotLanGhi_MotLanChanged()
    {
        var store = ScrapeProgressStore.Harvest;   // Core.Tests: thư mục dữ liệu riêng (data-dir.txt)
        var acc = "acc-rp-" + Guid.NewGuid().ToString("N");
        var changed = 0;
        void OnChanged() => changed++;
        store.Changed += OnChanged;
        try
        {
            store.MarkCompleted(acc, "shared:s1", 1, 50);
            store.MarkSkipped(acc, "shared:s1", 40);
            changed = 0;
            var ranges = Enumerable.Range(0, 2000).Select(i => new RowRange { From = i * 2 + 1, To = i * 2 + 1 }).ToList();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            store.ReplaceProgress(acc, "shared:s1", "n", 5000, ranges, [4000, 4002]);
            sw.Stop();
            Assert.Equal(1, changed);
            var p = store.Find(acc, "shared:s1")!;
            Assert.Equal(2001, p.Completed.Count);                    // 2000 dòng lẻ; 4000 nhập vào 3999, 4002 riêng
            Assert.Equal([4000, 4002], p.SkippedRows);
            Assert.Equal((4002, 5000, LedgerStatus.Running), (p.LastRowReached, p.TotalRowsAtLastRun, p.Status));
            Assert.True(sw.ElapsedMilliseconds < 2000, $"{sw.ElapsedMilliseconds} ms");
            Assert.Equal([(4, 4)], store.RemainingSegments(acc, "shared:s1", 4, 4));   // dòng chẵn chưa xong
        }
        finally
        {
            store.Changed -= OnChanged;
            store.Clear(acc, "shared:s1");
        }
    }

    [Fact]
    public void DungLai_DongThieuTenHoacLink_TinhLaXong_NgoaiDaiBiBo()
    {
        // Dòng 2, 4 không có trong danh sách hợp lệ (thiếu tên / link) → như engine: không có gì để lấy → xong.
        var items = new[] { L(1, U(10)), L(3, U(11)), L(5, U(12)), L(9, U(13)) };
        Assert.Equal("2-2,4-4 | 1-1,3-3,5-5", Show(HarvestDoneRebuild.Build(1, 5, items, Done())));
        // Dải StartRow/EndRow (2..4): dòng 1 và 5 ngoài dải không được tính.
        var dai = HarvestDoneRebuild.Build(2, 4, items, Done());
        Assert.Equal("2-2,4-4 | 3-3", Show(dai));
        Assert.Equal((2, 1), (dai.DoneRows, dai.TodoRows));
        Assert.Equal(" | ", Show(HarvestDoneRebuild.Build(5, 4, items, Done())));
    }

    // ── Nạp lại cấu hình workbook chung (chỉ app Harvest gọi) ──

    [Fact]
    public void SharedWorkbook_ReloadIfExternalChange_NapBanMoi_DocLoiThiGiuBanCu()
    {
        SharedWorkbookStore.Disable();                                  // process này ghi → không tính là đổi ngoài
        Assert.False(SharedWorkbookStore.ReloadIfExternalChange());

        var path = SharedWorkbookStore.ConfigPath;
        var other = new SharedWorkbookState { Enabled = false, SourceName = "do-app-scrape.xlsx", MergedRowCount = 7 };
        Shopee.Core.Infrastructure.JsonAtomicFile.Save(path, other);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        var fired = 0;
        void OnChanged() => fired++;
        SharedWorkbookStore.Changed += OnChanged;
        try
        {
            Assert.True(SharedWorkbookStore.ReloadIfExternalChange());
            Assert.Equal("do-app-scrape.xlsx", SharedWorkbookStore.Snapshot().SourceName);
            Assert.Equal(1, fired);

            var good = File.ReadAllText(path);
            File.WriteAllText(path, "{ hỏng");
            var tLoi = DateTime.UtcNow.AddMinutes(10);
            File.SetLastWriteTimeUtc(path, tLoi);
            Assert.False(SharedWorkbookStore.ReloadIfExternalChange());   // đọc lỗi → giữ bản cũ
            Assert.Equal("do-app-scrape.xlsx", SharedWorkbookStore.Snapshot().SourceName);
            File.WriteAllText(path, good);
            File.SetLastWriteTimeUtc(path, tLoi);                     // CÙNG mốc với lần đọc lỗi
            Assert.True(SharedWorkbookStore.ReloadIfExternalChange());    // mốc chưa bị "tiêu" → nhịp sau nạp được
        }
        finally { SharedWorkbookStore.Changed -= OnChanged; SharedWorkbookStore.Disable(); }
    }
}



