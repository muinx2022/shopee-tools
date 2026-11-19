using System.Net;
using System.Net.Http;
using System.Text.Json;
using ClosedXML.Excel;
using Shopee.Core.Coordination;
using Shopee.Core.MassUpload;
using Shopee.Core.MassUpload.Export;
using Shopee.Core.MassUpload.Images;
using Shopee.Core.MassUpload.Seller;
using Shopee.Core.Tests.MassUpload.Images;
using static Shopee.Core.Tests.MassUpload.MassUploadTestData;

namespace Shopee.Core.Tests.MassUpload.Export;

/// <summary>GĐ8: lượt "Xuất xlsx" (<see cref="MassUploadExporter"/>) với Hub / CDN / URL công khai GIẢ (<see cref="FakeMediaWorld"/>),
/// worker + sổ ảnh thật: chia SP theo mẫu đúng ngành, SP chưa cào không xuất, huỷ giữa chừng không để lại file dở mà ảnh vẫn ghi theo
/// lượt để xoá, worker dừng thì lượt dừng, cấu hình sai thì chưa làm gì.</summary>
public sealed class MassUploadExporterTests : IAsyncDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 0, 0, 0, TimeSpan.Zero);

    // Ngành cấp 1 của fixture: 100009 (2 SP), 100011, 100637.
    private const string Vong2Tang = "pdp-2tang-8model.json";            // 100009
    private const string VongMotaAnh = "pdp-motaanh-brand-voucher.json";  // 100009
    private const string AoKhongBienThe = "pdp-khong-bien-the.json";     // 100011
    private const string Na4Model = "pdp-1tang-4model-1anh.json";         // 100637

    private readonly string _dir = NewTempDir();
    private readonly ManualClock _clock = new(T0);
    private readonly FakeMediaWorld _world = new();
    private readonly HubClient _hub;
    private readonly HttpClient _fetchHttp;
    private readonly UploadStore _store;
    private readonly ImagePrepWorker _worker;
    private readonly Dictionary<long, HarvestedItemData> _hubData = [];
    private readonly List<ExportSourceRow> _rows = [];
    private readonly List<ExportProgress> _progress = [];
    private int _fetchCalls;
    private Func<Exception?>? _fetchThrow;
    private Func<string, IReadOnlyList<string>, CancellationToken, Task<HubImageLinks>>? _hubLinks;
    private readonly List<(string Shop, IReadOnlyList<string> Ids)> _hubLinkCalls = [];

    public MassUploadExporterTests()
    {
        _world.Clock = () => _clock.Now;
        _hub = _world.HubClient();
        _fetchHttp = _world.FetchClient();
        _store = new UploadStore(Path.Combine(_dir, "upload.db"), () => _clock.Now);
        _worker = new ImagePrepWorker(_store, () => _hub, _fetchHttp,
            new ImagePrepWorkerOptions { Interval = TimeSpan.FromMilliseconds(20), MaxParallel = 2, MaxAttempts = 2 }, clock: () => _clock.Now);
        File.WriteAllBytes(SizePath, ImageTestData.Jpeg);
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.StopAsync();
        _hub.Dispose();
        _fetchHttp.Dispose();
        HarvestTestData.TryDelete(_dir);
    }

    private string SizePath => Path.Combine(_dir, "size.jpg");
    private string OutDir => Path.Combine(_dir, "ra");

    /// <summary>Thêm một dòng nguồn (dòng Excel = 2 + thứ tự) từ fixture; <paramref name="harvested"/> = Hub có dữ liệu. Mọi ảnh của
    /// SP có trên CDN giả.</summary>
    private long AddSource(string fixture, bool harvested = true, string? title = null, string[]? images = null)
    {
        var json = HarvestTestData.Fixture(fixture);
        if (title is not null || images is not null)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
            var data = node["data"] ?? node;
            if (title is not null) data["item"]!["title"] = title;
            if (images is not null) data["product_images"]!["images"] = new System.Text.Json.Nodes.JsonArray(
                images.Select(i => (System.Text.Json.Nodes.JsonNode?)System.Text.Json.Nodes.JsonValue.Create(i)).ToArray());
            json = node.ToJsonString();
        }
        var listing = ShopeeListingReader.Read(json);
        var row = new ExportSourceRow(2 + _rows.Count, $"https://shopee.vn/sp-i.{listing.ShopId}.{listing.ItemId}", listing.ItemId);
        _rows.Add(row);
        if (harvested)
            _hubData[listing.ItemId] = new HarvestedItemData(listing.ItemId, listing.ShopId, T0, JsonDocument.Parse(json).RootElement.Clone());
        foreach (var img in listing.Images.Concat(listing.Tiers.SelectMany(t => t.Images)).Where(i => !string.IsNullOrEmpty(i)))
            _world.Cdn[img!] = ImageTestData.Jpeg;
        return listing.ItemId;
    }

    private MassUploadExportOptions _options = new()
    {
        ImagePollInterval = TimeSpan.FromMilliseconds(20),
        HubLinksRetryDelays = [TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20)],
    };

    private MassUploadExporter Exporter() => new(_store, _worker,
        (ids, _) =>
        {
            Interlocked.Increment(ref _fetchCalls);
            if (_fetchThrow?.Invoke() is { } fx) throw fx;
            return Task.FromResult<IReadOnlyList<HarvestedItemData>>(ids.Where(_hubData.ContainsKey).Select(i => _hubData[i]).ToList());
        },
        (_, _, _, _, _) => Task.FromResult(new ExportSource(_rows.ToList(), [new ExportSourceLink(99, "https://shopee.vn/shop-abc")], 1, _rows.Count + 2)),
        () => _clock.Now, _options, hubLinks: _hubLinks is null ? null : (shop, ids, ct) =>
        {
            lock (_hubLinkCalls) _hubLinkCalls.Add((shop, ids));
            return _hubLinks(shop, ids, ct);
        });

    private const string HubBase = "https://api.example.com/api/v/";

    /// <summary>Hub giả có sẵn ảnh gốc của mọi id, trừ <paramref name="missing"/>.</summary>
    private void HubHas(params string[] missing) => _hubLinks = (_, ids, _) => Task.FromResult(new HubImageLinks(
        ids.Where(i => !missing.Contains(i)).ToDictionary(i => i, i => HubBase + "tok-" + i + ".jpg"),
        ids.Where(missing.Contains).ToList()));

    private int CdnCalls => _world.FetchRequests.Count(r => r.RequestUri!.Host == "down-vn.img.susercontent.com");

    private MassUploadExportRequest Request(params string[] templates) => new("lot-1", "seller-1", "Shop Một", @"C:\nguon.xlsx", "Sheet1", 2, 0,
        templates.Select(TemplatePath).ToList(), SizePath, new SellerDefaults(), OutDir);

    private Task<MassUploadExportResult> Run(MassUploadExportRequest req, CancellationToken ct = default, Action<ExportProgress>? onProgress = null) =>
        Exporter().RunAsync(req, new SyncProgress(p => { lock (_progress) _progress.Add(p); onProgress?.Invoke(p); }), ct);

    /// <summary>Các SKU cha (cột <c>ps_sku_parent_short</c>) có trong file xuất.</summary>
    private static HashSet<string> Skus(string xlsx)
    {
        var schema = TemplateSchema.Load(xlsx);
        Assert.True(schema.TryGetColumn(MassUploadMapper.ColParentSku, out var col));
        return ReadSheet(xlsx, TemplateSchema.MainSheetName).Where(r => r.Key >= TemplateSchema.DataStartRow)
            .Select(r => r.Value.TryGetValue(col.Letter, out var c) ? c.Value : "").ToHashSet();
    }

    private sealed class SyncProgress(Action<ExportProgress> on) : IProgress<ExportProgress>
    {
        public void Report(ExportProgress value) => on(value);
    }

    // ── Chia ngành theo mẫu ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChiaNganh_SpDungNganhVaoMau_NganhKhongCoMau_BaoThieu_KhongXuat_KhongDuaAnhVaoHang()
    {
        var a = AddSource(Vong2Tang);
        var b = AddSource(VongMotaAnh);
        var ao = AddSource(AoKhongBienThe);
        var aoListing = ShopeeListingReader.Read(HarvestTestData.Fixture(AoKhongBienThe));

        var r = await Run(Request(Advanced));

        var file = Assert.Single(r.Files);
        Assert.Equal(Path.Combine(OutDir, "Shop-Một_100009_20261117-000000_1.xlsx"), file);
        Assert.Equal([a.ToString(), b.ToString()], Skus(file).Order());
        Assert.Equal(2, r.ExportedItems);
        var miss = Assert.Single(r.MissingTemplates);
        Assert.Equal(100011, miss.TopCategoryId);
        Assert.Equal([ao], miss.Items.Select(i => i.ItemId));
        Assert.All(aoListing.Images, img => Assert.Null(_store.Inspect("seller-1", img)));   // ảnh SP thiếu mẫu không lên Hub
        Assert.Single(r.NoItemId);
        Assert.Equal(1, r.DuplicateRows);
        Assert.Empty(r.Notices);
        Assert.Equal(["Shop-Một_100009_20261117-000000_1.xlsx"], Directory.GetFileSystemEntries(OutDir).Select(Path.GetFileName));
        var batch = Assert.Single(await _store.ListBatchesAsync());
        Assert.Equal((ExportBatchState.Done, 2, 1), (batch.State, batch.Products, batch.Files));
    }

    [Fact]
    public async Task ChiaNganh_CoBasic_NganhKhongCoMauNangCao_VaoBasic_KemCanhBao()
    {
        var a = AddSource(Vong2Tang);
        var na = AddSource(Na4Model);

        var r = await Run(Request(Advanced, Basic));

        Assert.Equal(2, r.Files.Count);
        var advancedFile = r.Files.Single(f => f.Contains("_100009_", StringComparison.Ordinal));
        var basicFile = r.Files.Single(f => f.Contains("_basic_", StringComparison.Ordinal));
        Assert.Equal([a.ToString()], Skus(advancedFile));
        Assert.Equal([na.ToString()], Skus(basicFile));
        Assert.Equal(TemplateKind.Basic, TemplateSchema.Load(basicFile).Kind);
        Assert.Empty(r.MissingTemplates);
        Assert.Contains(r.Notices, n => n.Contains("basic", StringComparison.Ordinal));
    }

    // ── SP chưa cào ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SpChuaCao_LietKeDongVaLink_KhongXuat_KhongDuaAnhVaoHang()
    {
        var a = AddSource(Vong2Tang);
        AddSource(VongMotaAnh, harvested: false);
        var chuaCao = _rows[1];
        var listing = ShopeeListingReader.Read(HarvestTestData.Fixture(VongMotaAnh));

        var r = await Run(Request(Advanced));

        Assert.Equal([new ExportSourceLink(chuaCao.ExcelRow, chuaCao.Link)], r.NotHarvested);
        Assert.Equal([a.ToString()], Skus(Assert.Single(r.Files)));
        Assert.Equal(1, r.ExportedItems);
        Assert.All(listing.Images, img => Assert.Null(_store.Inspect("seller-1", img)));
    }

    [Fact]
    public async Task KhongSpNaoXuatDuoc_KhongTaoLuot_KhongCoFile()
    {
        AddSource(AoKhongBienThe);

        var r = await Run(Request(Advanced));

        Assert.Empty(r.Files);
        Assert.False(r.BatchCreated);
        Assert.Empty(await _store.ListBatchesAsync());
        Assert.False(Directory.Exists(OutDir) && Directory.EnumerateFileSystemEntries(OutDir).Any());
    }

    // ── Huỷ giữa chừng ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HuyLucGhiFile_KhongDeLaiFileDo_AnhVanGhiTheoLuot_XoaDuoc()
    {
        AddSource(Vong2Tang);
        AddSource(Na4Model);
        using var cts = new CancellationTokenSource();

        // Huỷ ngay sau khi nhóm mẫu đầu đã ghi xong (file đầu đã nằm trong thư mục tạm).
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(Request(Advanced, Basic), cts.Token,
            p => { if (p.Step == ExportStep.Write && p.Done > 0) cts.Cancel(); }));

        Assert.True(Directory.Exists(OutDir));
        Assert.Empty(Directory.EnumerateFileSystemEntries(OutDir));
        var batch = Assert.Single(await _store.ListBatchesAsync());
        Assert.Equal(ExportBatchState.Cancelled, batch.State);
        Assert.True(batch.Images > 1);
        Assert.NotEmpty(_world.Stored);

        var clear = await _store.ClearBatchAsync(batch.BatchId, _hub);

        Assert.True(clear.Complete);
        Assert.Equal(1, clear.SizeChartKept);
        Assert.Single(_world.Stored);   // chỉ còn ảnh bảng size dùng chung
    }

    [Fact]
    public async Task HuyLucChoAnh_KhongCoFile_LuotHuy_AnhDaGhiTheoLuot()
    {
        AddSource(Vong2Tang);
        _world.CdnHang = true;
        using var cts = new CancellationTokenSource();
        var run = Run(Request(Advanced), cts.Token);
        await _world.CdnEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.False(Directory.Exists(OutDir) && Directory.EnumerateFileSystemEntries(OutDir).Any());
        var batch = Assert.Single(await _store.ListBatchesAsync());
        Assert.Equal(ExportBatchState.Cancelled, batch.State);
        Assert.True(batch.Images > 1);
    }

    // ── Lỗi ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WorkerDungViHubTuChoiToken_LuotDung_KhongCoFile()
    {
        AddSource(Vong2Tang);
        _world.UploadOverride = _ => FakeMediaWorld.Json(HttpStatusCode.Unauthorized, "{}");

        var ex = await Assert.ThrowsAsync<MassUploadExportException>(() => Run(Request(Advanced)));

        Assert.Contains("Bước đệm ảnh dừng", ex.Message);
        Assert.False(Directory.Exists(OutDir) && Directory.EnumerateFileSystemEntries(OutDir).Any());
        Assert.Equal(ExportBatchState.Failed, Assert.Single(await _store.ListBatchesAsync()).State);
    }

    [Fact]
    public async Task CauHinhSai_ChuaLamGi()
    {
        AddSource(Vong2Tang);
        var copy = Path.Combine(_dir, "ban-sao-100009.xlsx");
        File.Copy(TemplatePath(Advanced), copy);

        var noTemplate = Request() with { TemplatePaths = [] };
        var twoSame = Request(Advanced) with { TemplatePaths = [TemplatePath(Advanced), copy] };
        var noSize = Request(Advanced) with { SizeChartImagePath = Path.Combine(_dir, "khong-co.jpg") };
        var badDefaults = Request(Advanced) with { Defaults = new SellerDefaults { WeightGrams = 0 } };
        var notTemplate = Request(Advanced) with { TemplatePaths = [SizePath] };

        foreach (var req in new[] { noTemplate, twoSame, noSize, badDefaults, notTemplate })
            await Assert.ThrowsAsync<MassUploadExportException>(() => Run(req));

        Assert.Contains("cùng ngành 100009", (await Assert.ThrowsAsync<MassUploadExportException>(() => Run(twoSame))).Message);
        Assert.Equal(0, _fetchCalls);
        Assert.Empty(await _store.ListBatchesAsync());
        Assert.False(Directory.Exists(OutDir));
    }

    [Fact]
    public async Task FileDichDaCo_KhongGhiDe_KhongDeLaiFileMoi()
    {
        AddSource(Vong2Tang);
        Directory.CreateDirectory(OutDir);
        var existing = Path.Combine(OutDir, "Shop-Một_100009_20261117-000000_1.xlsx");
        File.WriteAllText(existing, "cũ");

        await Assert.ThrowsAsync<MassUploadExportException>(() => Run(Request(Advanced)));

        Assert.Equal("cũ", File.ReadAllText(existing));
        Assert.Equal([existing], Directory.GetFileSystemEntries(OutDir));
    }

    [Fact]
    public async Task MauKhacShop_BaoLoi_KhongXuat()
    {
        AddSource(Vong2Tang);
        var other = Path.Combine(_dir, "mau-shop-khac.xlsx");
        File.Copy(TemplatePath(Basic), other);
        PatchShopId(other, "999888777");
        Assert.Equal("999888777", TemplateSchema.Load(other).ShopId);
        Assert.NotEqual("999888777", TemplateSchema.Load(TemplatePath(Advanced)).ShopId);

        var ex = await Assert.ThrowsAsync<MassUploadExportException>(() =>
            Run(Request(Advanced) with { TemplatePaths = [TemplatePath(Advanced), other] }));

        Assert.Contains("2 shop khác nhau", ex.Message);
        Assert.Equal(0, _fetchCalls);
        Assert.Empty(await _store.ListBatchesAsync());
        Assert.False(Directory.Exists(OutDir));
    }

    [Fact]
    public async Task CanhBaoCat_CuaMapper_VaoKetQua()
    {
        var longTitle = "Vòng tay bạc nữ đính đá " + string.Join(" ", Enumerable.Repeat("phong cách Hàn Quốc sang trọng", 8));
        Assert.True(longTitle.Length > 120);
        var a = AddSource(Vong2Tang, title: longTitle);
        AddSource(VongMotaAnh);

        var r = await Run(Request(Advanced));

        Assert.Equal(2, r.ExportedItems);
        var cut = Assert.Single(r.Warnings, w => w.ItemId == a && w.Text.StartsWith("tên dài", StringComparison.Ordinal));
        Assert.Equal(_rows[0].ExcelRow, cut.ExcelRow);
    }

    /// <summary>Đổi ô D2 (shop_id) của bản sao file mẫu thành chuỗi inline.</summary>
    private static void PatchShopId(string xlsx, string shopId)
    {
        using var zip = System.IO.Compression.ZipFile.Open(xlsx, System.IO.Compression.ZipArchiveMode.Update);
        var part = SheetPart(zip, TemplateSchema.MainSheetName);
        string xml;
        using (var rd = new StreamReader(zip.GetEntry(part)!.Open())) xml = rd.ReadToEnd();
        var at = xml.IndexOf("<c r=\"D2\"", StringComparison.Ordinal);
        var end = xml.IndexOf("</c>", at, StringComparison.Ordinal) + "</c>".Length;
        xml = xml[..at] + $"<c r=\"D2\" t=\"inlineStr\"><is><t>{shopId}</t></is></c>" + xml[end..];
        zip.GetEntry(part)!.Delete();
        using var w = new StreamWriter(zip.CreateEntry(part).Open(), new System.Text.UTF8Encoding(false));
        w.Write(xml);
    }

    // ── Vòng 3: hạn ảnh, hết giờ, chờ ảnh có trần, Hub hết chỗ, mẫu đổi shop, dọn thư mục tạm ─────────────────────

    [Fact]
    public async Task LuotSau55Ngay_AnhSapHetHan_DuaLaiLenHub_HanLuotConDu6Ngay()
    {
        AddSource(Vong2Tang);
        var first = await Run(Request(Advanced));
        Assert.Equal(T0.AddDays(7), first.ImagesExpireAt);
        var uploadsAfterFirst = _world.Uploads;
        var images = (await _store.ListBatchesAsync()).Single().Images;
        Assert.Equal(images, uploadsAfterFirst);   // mọi ảnh (cả bảng size) lên Hub đúng một lần

        _clock.Advance(TimeSpan.FromDays(5.5));
        var second = await Run(Request(Advanced) with { BatchId = "lot-2" });

        Assert.Equal(2 * images, _world.Uploads);   // ảnh còn 1,5 ngày → đưa lại hết, kể cả bảng size
        Assert.NotNull(second.ImagesExpireAt);
        Assert.True(second.ImagesExpireAt >= _clock.Now.AddDays(6), $"hạn lượt {second.ImagesExpireAt}");
        var batch2 = (await _store.ListBatchesAsync()).Single(b => b.BatchId == "lot-2");
        Assert.Equal(second.ImagesExpireAt, batch2.ExpiresAt);
    }

    [Fact]
    public async Task HubHetGio_BaoLoiHubKhongTraLoi_KhongPhaiHuy()
    {
        AddSource(Vong2Tang);
        _fetchThrow = () => new TaskCanceledException("HttpClient.Timeout");

        var ex = await Assert.ThrowsAsync<MassUploadExportException>(() => Run(Request(Advanced)));

        Assert.Equal(MassUploadExporter.TimeoutMessage, ex.Message);
    }

    [Fact]
    public async Task AnhKhongXongTrongTranCho_XuatVoiAnhDaSan_GhiRoAnhBoTheoSp()
    {
        var a = AddSource(Vong2Tang);
        var listing = ShopeeListingReader.Read(HarvestTestData.Fixture(Vong2Tang));
        var stuck = listing.Images[1];
        _world.CdnStatus = id => id == stuck ? System.Net.HttpStatusCode.Forbidden : null;   // ảnh lùi 30 s mỗi lần thử
        _options = new MassUploadExportOptions
        {
            ImagePollInterval = TimeSpan.FromMilliseconds(20),
            ImageWaitIdleLimit = TimeSpan.FromMilliseconds(500),
        };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // Không có trần chờ thì ảnh kẹt chờ mãi (đồng hồ giả không chạy) → huỷ sau 20 s để test ĐỎ chứ không treo.
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var r = await Run(Request(Advanced), guard.Token);

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"chờ {sw.Elapsed}");
        Assert.Equal(1, r.ExportedItems);
        Assert.Equal(1, r.ImagesDroppedPending);
        Assert.Contains(r.Warnings, w => w.ItemId == a && w.Text == "1 ảnh chưa xong, đã bỏ");
        Assert.Contains(r.Notices, n => n.StartsWith("1 ảnh chưa xong", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HubHetCho_DungLuot_BaoXoaAnhLuotCu()
    {
        AddSource(Vong2Tang);
        _world.UploadOverride = _ => FakeMediaWorld.Json(System.Net.HttpStatusCode.InsufficientStorage, """{"error":"media-quota-exceeded"}""");

        var ex = await Assert.ThrowsAsync<MassUploadExportException>(() => Run(Request(Advanced)));

        Assert.StartsWith(MassUploadExporter.HubFullMessage, ex.Message);
        Assert.Equal(ExportBatchState.Failed, Assert.Single(await _store.ListBatchesAsync()).State);
    }

    [Fact]
    public async Task MauDoiShopSoVoiLanTruoc_HoiLai_XacNhanMoiXuat()
    {
        AddSource(Vong2Tang);
        await Run(Request(Advanced));
        var other = Path.Combine(_dir, "mau-100009-shop-khac.xlsx");
        File.Copy(TemplatePath(Advanced), other);
        PatchShopId(other, "999888777");
        var req = Request(Advanced) with { BatchId = "lot-2", TemplatePaths = [other] };

        var ex = await Assert.ThrowsAsync<TemplateShopChangedException>(() => Run(req));

        Assert.Equal(("999888777", TemplateSchema.Load(TemplatePath(Advanced)).ShopId), (ex.TemplateShopId, ex.PreviousShopId));
        Assert.Single(await _store.ListBatchesAsync());   // chưa tạo lượt
        _clock.Advance(TimeSpan.FromMinutes(1));   // tên file theo giờ — khỏi trùng file của lượt 1
        var ok = await Run(req with { AllowTemplateShopChange = true });
        Assert.Single(ok.Files);
        Assert.Equal("999888777", (await _store.ListBatchesAsync()).Single(b => b.BatchId == "lot-2").TemplateShopId);
    }

    [Fact]
    public void DonThuMucTamConSot_CuaLuotHuyLoi()
    {
        Directory.CreateDirectory(OutDir);
        var left = MassUploadExporter.StagingDirectory(OutDir, "lot-x");
        Directory.CreateDirectory(left);
        File.WriteAllText(Path.Combine(left, "do.xlsx"), "x");
        var keep = MassUploadExporter.StagingDirectory(OutDir, "lot-done");
        Directory.CreateDirectory(keep);
        ExportBatchInfo B(string id, string state) => new(id, "seller-1", "", T0, state, 0, 0, OutDir, null, null, 0);

        var msgs = MassUploadExporter.CleanupStaleStaging([B("lot-x", ExportBatchState.Cancelled), B("lot-done", ExportBatchState.Done)]);

        Assert.False(Directory.Exists(left));
        Assert.True(Directory.Exists(keep));   // lượt xong không đụng
        Assert.Contains("lot-x", Assert.Single(msgs));
    }

    // ── GĐ9: ảnh gốc trên Hub ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnhCoTrenHub_GhiLinkHub_KhongGoiCdn_LuotKhongHanChiHanBangSize()
    {
        var a = AddSource(Vong2Tang);
        var listing = ShopeeListingReader.Read(HarvestTestData.Fixture(Vong2Tang));
        HubHas();

        var r = await Run(Request(Advanced));

        Assert.Equal(0, CdnCalls);
        Assert.Equal(1, r.ExportedItems);
        Assert.True(r.HubImages > 0);
        Assert.Equal(0, r.CdnImages);
        Assert.Empty(r.CdnItems!);
        Assert.Null(r.ImagesExpireAt);
        Assert.Equal(T0.AddDays(7), r.SizeChartExpiresAt);
        var call = Assert.Single(_hubLinkCalls);
        Assert.Equal("seller-1", call.Shop);
        Assert.Contains(listing.Images[0], call.Ids);
        var schema = TemplateSchema.Load(r.Files[0]);
        Assert.True(schema.TryGetColumn(MassUploadMapper.ColCoverImage, out var cover));
        var cells = ReadSheet(r.Files[0], TemplateSchema.MainSheetName)[TemplateSchema.DataStartRow];
        Assert.Equal(HubBase + "tok-" + listing.Images[0] + ".jpg", cells[cover.Letter].Value);
        var batch = Assert.Single(await _store.ListBatchesAsync());
        Assert.Null(batch.ExpiresAt);
        Assert.Equal((r.HubImages, 0, T0.AddDays(7)), (batch.HubImages, batch.CdnImages, batch.SizeChartExpiresAt!.Value));
        Assert.Equal(1, batch.Images);   // chỉ ảnh bảng size ghi theo lượt
        Assert.Equal(a.ToString(), Skus(r.Files[0]).Single());
    }

    [Fact]
    public async Task AnhThieuTrenHub_TaiCdnDungAnhDo_BaoCaoSpTaiCdn_HanLuotTheoAnhCdn()
    {
        AddSource(Vong2Tang);
        var listing = ShopeeListingReader.Read(HarvestTestData.Fixture(Vong2Tang));
        var thieu = listing.Images[1];
        HubHas(thieu);

        var r = await Run(Request(Advanced));

        Assert.Equal(1, CdnCalls);
        Assert.Contains(_world.FetchRequests, q => q.RequestUri!.AbsolutePath.EndsWith("/" + thieu, StringComparison.Ordinal));
        Assert.Equal(1, r.CdnImages);
        Assert.Equal("1 ảnh tải từ CDN (chưa có trên Hub)", Assert.Single(r.CdnItems!).Text);
        Assert.Equal(T0.AddDays(7), r.ImagesExpireAt);
        Assert.Equal(1, r.ExportedItems);
    }

    [Fact]
    public async Task CheDoBoSp_AnhSpThieu_GiaoMapper_AnhSauLenThay_SpVanXuatKemCanhBao()
    {
        var a = AddSource(Vong2Tang);
        var b = AddSource(VongMotaAnh);
        var thieu = ShopeeListingReader.Read(HarvestTestData.Fixture(Vong2Tang)).Images[1];
        HubHas(thieu);

        var r = await Run(Request(Advanced) with { MissingHubImages = MissingHubImagePolicy.SkipProduct });

        Assert.Equal(0, CdnCalls);
        Assert.Equal([a.ToString(), b.ToString()], Skus(Assert.Single(r.Files)).Order());
        Assert.Empty(r.ImageSkipped);
        Assert.Contains(r.Warnings, w => w.ItemId == a && w.Text.Contains(thieu) && w.Text.Contains("chưa có trên Hub"));
        Assert.Equal(0, r.CdnImages);
    }

    [Fact]
    public async Task CheDoBoSp_AnhPhanLoaiThieu_BoCotAnhPhanLoai_SpVanXuat()
    {
        var a = AddSource(Vong2Tang);
        var listing = ShopeeListingReader.Read(HarvestTestData.Fixture(Vong2Tang));
        HubHas(listing.Tiers[0].Images.First(i => !string.IsNullOrEmpty(i))!);

        var r = await Run(Request(Advanced) with { MissingHubImages = MissingHubImagePolicy.SkipProduct });

        Assert.Equal([a.ToString()], Skus(Assert.Single(r.Files)));
        Assert.Contains(r.Warnings, w => w.ItemId == a && w.Text.Contains("bỏ cột ảnh phân loại"));
        Assert.Equal(0, CdnCalls);
    }

    [Fact]
    public async Task CheDoBoSp_Sp12AnhThieuAnhThu10_VanXuat()
    {
        var images = Enumerable.Range(1, 12).Select(i => $"anh12-{i:00}-goc").ToArray();
        var a = AddSource(Na4Model, images: images);
        HubHas(images[9]);

        var r = await Run(Request(Basic) with { MissingHubImages = MissingHubImagePolicy.SkipProduct });

        Assert.Equal([a.ToString()], Skus(Assert.Single(r.Files)));
        Assert.Empty(r.ImageSkipped);
        Assert.Equal(0, CdnCalls);
    }

    [Fact]
    public async Task CheDoCdn_DemAnhCdnHubChiTheoAnhCoTrongFile()
    {
        var images = Enumerable.Range(1, 12).Select(i => $"anh12-{i:00}-goc").ToArray();
        AddSource(Na4Model, images: images);
        HubHas(images[..6]);   // 6 ảnh đầu thiếu trên Hub → CDN; 6 ảnh sau có link

        var r = await Run(Request(Basic));

        var cells = ReadSheet(r.Files[0], TemplateSchema.MainSheetName).Where(x => x.Key >= TemplateSchema.DataStartRow)
            .SelectMany(x => x.Value.Values).Select(c => c.Value).ToList();
        // URL kho tạm trong file = ảnh SP đi CDN + 1 ảnh bảng size.
        var cdnInFile = cells.Where(v => v.StartsWith(FakeMediaWorld.PublicBase, StringComparison.Ordinal)).Distinct().Count();
        var hubInFile = cells.Where(v => v.StartsWith(HubBase, StringComparison.Ordinal)).Distinct().Count();
        // 12 ảnh SP + ảnh phân loại được hỏi, file chỉ dùng 9 ảnh SP (+ ảnh phân loại): đếm đúng tập trong file, không đếm dư.
        Assert.Equal(hubInFile, r.HubImages);
        Assert.Equal(cdnInFile - 1, r.CdnImages);   // trừ URL ảnh bảng size (kho tạm)
        Assert.True(r.HubImages + r.CdnImages < 12 + 4);
        Assert.Equal($"{r.CdnImages} ảnh tải từ CDN (chưa có trên Hub)", Assert.Single(r.CdnItems!).Text);
    }

    [Fact]
    public async Task LuotCoCaHubLanCdn_BangSizeHetHanSomHon_HanLuotTheoBangSize()
    {
        AddSource(VongMotaAnh);
        HubHas();
        await Run(Request(Advanced));   // ảnh bảng size lên kho tạm lúc T0 → hết hạn T0+7
        _clock.Advance(TimeSpan.FromDays(1));
        _rows.Clear();
        var listing = ShopeeListingReader.Read(HarvestTestData.Fixture(Vong2Tang));
        AddSource(Vong2Tang);
        HubHas(listing.Images[1]);      // 1 ảnh đi CDN lúc T0+1 → hết hạn T0+8

        var r = await Run(Request(Advanced) with { BatchId = "lot-2" });

        Assert.True(r.HubImages > 0);
        Assert.Equal(1, r.CdnImages);
        Assert.Equal(T0.AddDays(7), r.SizeChartExpiresAt);
        Assert.Equal(T0.AddDays(7), r.ImagesExpireAt);   // sớm hơn hạn ảnh CDN (T0+8)
    }

    [Fact]
    public async Task HubLoiMotLo_CheDoCdn_ChiLoLoiDiCdn_LoKhacGiuLink_ThuLai3Lan()
    {
        AddSource(Vong2Tang);
        _options = new MassUploadExportOptions
        {
            ImagePollInterval = TimeSpan.FromMilliseconds(20),
            HubLinksRetryDelays = [TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20)],
            HubLinksBatchSize = 3,
        };
        string? badFirst = null;
        var calls = new Dictionary<string, int>();
        _hubLinks = (_, ids, _) =>
        {
            badFirst ??= ids[0];
            lock (calls) calls[ids[0]] = calls.GetValueOrDefault(ids[0]) + 1;
            if (ids[0] == badFirst) throw new HttpRequestException("503 Service Unavailable");
            return Task.FromResult(new HubImageLinks(ids.ToDictionary(i => i, i => HubBase + "tok-" + i + ".jpg"), []));
        };

        var r = await Run(Request(Advanced));

        Assert.Equal(3, calls[badFirst!]);                       // 1 lần + 2 lần thử lại
        Assert.All(calls.Where(c => c.Key != badFirst), c => Assert.Equal(1, c.Value));
        Assert.Equal(1, r.ExportedItems);
        Assert.True(r.HubImages > 0);
        Assert.InRange(r.CdnImages, 1, 3);
        Assert.InRange(CdnCalls, 1, 3);
        Assert.Contains(r.Notices, n => n.StartsWith("Hub lỗi khi lấy link 3 ảnh gốc", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HubLoiTamThoi_ThuLaiDuoc_KhongDiCdn()
    {
        AddSource(Vong2Tang);
        var n = 0;
        _hubLinks = (_, ids, _) => ++n <= 2
            ? throw new HttpRequestException("502")
            : Task.FromResult(new HubImageLinks(ids.ToDictionary(i => i, i => HubBase + "tok-" + i + ".jpg"), []));

        var r = await Run(Request(Advanced));

        Assert.Equal(3, n);
        Assert.Equal(0, CdnCalls);
        Assert.Equal(0, r.CdnImages);
        Assert.DoesNotContain(r.Notices, x => x.Contains("Hub lỗi", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HubLoi_CheDoBoSp_DungLuot_KhongTaiCdn_KhongDeLaiFile()
    {
        AddSource(Vong2Tang);
        _hubLinks = (_, _, _) => throw new HttpRequestException("503 Service Unavailable");

        var ex = await Assert.ThrowsAsync<MassUploadExportException>(() =>
            Run(Request(Advanced) with { MissingHubImages = MissingHubImagePolicy.SkipProduct }));

        Assert.StartsWith("Hub lỗi khi lấy link ảnh", ex.Message);
        Assert.Contains("không tải CDN vì đang chọn Bỏ SP", ex.Message);
        Assert.Equal(0, CdnCalls);
        Assert.False(Directory.Exists(OutDir) && Directory.EnumerateFileSystemEntries(OutDir).Any());
        Assert.Equal(ExportBatchState.Failed, Assert.Single(await _store.ListBatchesAsync()).State);
    }

    [Fact]
    public async Task HaiShopCungSpKhacKhoaAnh_RaMd5KhacNhau()
    {
        AddSource(Vong2Tang);
        var img = ShopeeListingReader.Read(HarvestTestData.Fixture(Vong2Tang)).Images[0];
        HubHas(img);   // ảnh bìa thiếu trên Hub → đi CDN, đổi MD5 theo shopKey
        var shopKeys = new List<string>();
        var inner = _hubLinks!;
        _hubLinks = (shop, ids, ct) => { lock (shopKeys) shopKeys.Add(shop); return inner(shop, ids, ct); };

        await Run(Request(Advanced) with { ShopKey = "khoa-anh-a" });
        _clock.Advance(TimeSpan.FromMinutes(1));
        await Run(Request(Advanced) with { ShopKey = "khoa-anh-b", BatchId = "lot-2" });

        Assert.Equal(["khoa-anh-a", "khoa-anh-b"], shopKeys);
        var md5A = _store.Inspect("khoa-anh-a", img)!.Value.Md5;
        var md5B = _store.Inspect("khoa-anh-b", img)!.Value.Md5;
        Assert.NotNull(md5A);
        Assert.NotEqual(md5A, md5B);
    }

    // ── Đọc nguồn ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DocNguon_DaiDongExcelThat_KhuTrung_LinkKhongCoMa()
    {
        var path = Path.Combine(_dir, "nguon.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Shop A");
            ws.Cell(1, 1).Value = "Tên";
            ws.Cell(1, 2).Value = "Link";
            ws.Cell(2, 2).Value = "https://shopee.vn/a-i.1.100";
            ws.Cell(3, 2).Value = "https://shopee.vn/b-i.1.200";
            // dòng 4 trống
            ws.Cell(5, 2).Value = "https://shopee.vn/shop-xyz";
            ws.Cell(6, 2).Value = "https://shopee.vn/product/1/100?x=1";
            ws.Cell(7, 2).Value = "https://shopee.vn/c-i.1.300";
            ws.Cell(8, 2).Value = "https://shopee.vn/d-i.1.400";
            wb.SaveAs(path);
        }

        var s = await ExportSourceReader.ReadAsync(path, "Shop A", 3, 7, CancellationToken.None);

        Assert.Equal([(3, 200L), (6, 100L), (7, 300L)], s.Items.Select(i => (i.ExcelRow, i.ItemId)));
        Assert.Equal([5], s.NoItemId.Select(n => n.ExcelRow));
        Assert.Equal(0, s.DuplicateRows);
        var all = await ExportSourceReader.ReadAsync(path, "Shop A", 0, 0, CancellationToken.None);
        Assert.Equal([100L, 200, 300, 400], all.Items.Select(i => i.ItemId));
        Assert.Equal(1, all.DuplicateRows);   // dòng 6 trùng SP dòng 2
        await Assert.ThrowsAsync<InvalidOperationException>(() => ExportSourceReader.ReadAsync(path, "Shop A", 7, 3, CancellationToken.None));
    }
}
