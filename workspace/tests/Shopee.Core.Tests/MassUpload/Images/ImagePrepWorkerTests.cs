using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Shopee.Core.Coordination;
using Shopee.Core.MassUpload.Images;
using static Shopee.Core.Tests.MassUpload.Images.ImageTestData;

namespace Shopee.Core.Tests.MassUpload.Images;

/// <summary>GĐ2b: <see cref="ImagePrepWorker"/> với CDN + Hub + URL công khai GIẢ (<see cref="FakeMediaWorld"/>): luồng thành
/// công, <c>hosted</c> bền qua lần tắt app, lỗi của CHÍNH ảnh (404 nguồn, 413/415, kiểm lệch) vs lỗi HỆ THỐNG (cầu dao chung,
/// không đốt hàng đợi), cấu hình hỏng → dừng worker, dọn <c>deleting</c>, huỷ giữa chừng.</summary>
public sealed class ImagePrepWorkerTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 0, 0, 0, TimeSpan.Zero);

    private readonly string _dir = HarvestTestData.NewTempDir();
    private readonly ManualClock _clock = new(T0);
    private readonly FakeMediaWorld _world = new();
    private readonly HubClient _hub;
    private readonly HttpClient _fetch;
    private readonly UploadStore _store;
    private readonly List<ImagePrepProgress> _progress = [];
    private readonly List<string> _halts = [];
    private readonly List<string> _log = [];

    private static readonly ImagePrepWorkerOptions Fast = new()
    {
        Interval = TimeSpan.FromMilliseconds(20),
        MaxParallel = 1,
        BaseBackoff = TimeSpan.FromMinutes(1),
        MaxBackoff = TimeSpan.FromMinutes(10),
        MaxAttempts = 3,
    };

    public ImagePrepWorkerTests()
    {
        _hub = _world.HubClient();
        _fetch = _world.FetchClient();
        _store = NewStore();
    }

    public void Dispose()
    {
        _hub.Dispose();
        _fetch.Dispose();
        HarvestTestData.TryDelete(_dir);
    }

    private UploadStore NewStore() => new(Path.Combine(_dir, "upload.db"), () => _clock.Now);

    private ImagePrepWorker Worker(ImagePrepWorkerOptions? opt = null, Func<HubClient?>? hub = null, UploadStore? store = null)
    {
        var w = new ImagePrepWorker(store ?? _store, hub ?? (() => _hub), _fetch, opt ?? Fast, s => { lock (_log) _log.Add(s); },
            () => _clock.Now);
        w.Progress += p => { lock (_progress) _progress.Add(p); };
        w.Halted += r => { lock (_halts) _halts.Add(r); };
        return w;
    }

    private static string Md5(byte[] b) => Convert.ToHexString(MD5.HashData(b)).ToLowerInvariant();

    private int PutCount => _world.HubRequests.Count(r => r.Method == HttpMethod.Put);

    private (string State, int Attempts) Row(string key) =>
        (_store.Inspect("shop", key)!.Value.State, _store.Inspect("shop", key)!.Value.Attempts);

    // ── Thành công ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ThanhCong_EndToEnd_AnhSp_VaBangSize_HeaderDung()
    {
        _world.Cdn["vn-jpg"] = Jpeg;
        _world.Cdn["vn-png"] = Png;
        var sizePath = Path.Combine(_dir, "size.jpeg");
        await File.WriteAllBytesAsync(sizePath, Jpeg);
        var sizeKey = SizeChartImage.FromFile(sizePath).Key;
        await _store.EnqueueAsync("shop", 1, ["vn-jpg", "vn-png"]);
        await _store.EnqueueSizeChartAsync("shop", SizeChartImage.FromFile(sizePath));
        var w = Worker(new ImagePrepWorkerOptions { MaxParallel = 2, MaxAttempts = 3 });

        Assert.Equal(2, await w.RunOnceAsync(CancellationToken.None));   // bảng size đi trước
        Assert.Equal("verified", _store.Inspect("shop", sizeKey)!.Value.State);
        Assert.Equal(1, await w.RunOnceAsync(CancellationToken.None));
        Assert.Equal(0, await w.RunOnceAsync(CancellationToken.None));

        var keys = new[] { "vn-jpg", "vn-png", sizeKey };
        var resolve = await _store.GetResolverAsync("shop", keys);
        foreach (var key in keys)
        {
            var row = _store.Inspect("shop", key)!.Value;
            var hosted = _world.Stored[row.HubPath![(HubRoutes.PublicMedia.Length + 1)..]];
            var src = key == "vn-png" ? Png : Jpeg;
            Assert.Equal(Md5(hosted), row.Md5);                         // MD5 ghi sổ = MD5 ảnh trên Hub
            Assert.Equal(ImageMd5Changer.Change(src, "shop", key).Bytes, hosted);
            Assert.NotEqual(Md5(src), row.Md5);
            Assert.Equal(T0.AddDays(7).ToUnixTimeMilliseconds(), row.ExpiresAtMs);
            Assert.Equal(FakeMediaWorld.PublicBase + row.HubPath, resolve(key).Url);
        }
        Assert.Equal(new ImagePrepStats(0, 3, 0, 0), await _store.GetStatsAsync("shop"));
        Assert.All(_progress, p => Assert.Equal(ImagePrepOutcome.Verified, p.Outcome));
        Assert.Empty(_world.Deleted);

        // CDN: đúng URL, UA trình duyệt + Accept ảnh; GET kiểm: UA, KHÔNG token; PUT lên Hub CÓ token.
        var cdn = _world.FetchRequests.Where(r => r.RequestUri!.Host == "down-vn.img.susercontent.com").ToList();
        Assert.Contains(cdn, r => r.RequestUri!.ToString() == "https://down-vn.img.susercontent.com/file/vn-jpg");
        Assert.All(cdn, r =>
        {
            Assert.Equal(ImagePrepWorker.BrowserUserAgent, r.Headers.NonValidated["User-Agent"].ToString());
            Assert.Equal(ImagePrepWorker.ImageAccept, r.Headers.NonValidated["Accept"].ToString());
        });
        var pub = _world.FetchRequests.Where(r => r.RequestUri!.Host == "media.example.com").ToList();
        Assert.Equal(3, pub.Count);
        Assert.All(pub, r => Assert.Equal(ImagePrepWorker.BrowserUserAgent, r.Headers.NonValidated["User-Agent"].ToString()));
        Assert.All(_world.FetchRequests, r => Assert.False(r.Headers.Contains("X-Api-Token")));
        Assert.All(_world.HubRequests, r => Assert.True(r.Headers.Contains("X-Api-Token")));
    }

    [Fact]
    public async Task Nudge_DanhThucVongNen()
    {
        _world.Cdn["a"] = Jpeg;
        var w = Worker(new ImagePrepWorkerOptions { Interval = TimeSpan.FromHours(1), MaxAttempts = 3 });
        w.Start();
        await Task.Delay(50);                                           // vòng đã chạy lượt rỗng, đang ngủ 1 giờ
        await _store.EnqueueAsync("shop", 1, ["a"]);

        w.Nudge();
        for (var i = 0; i < 100 && (await _store.GetStatsAsync("shop")).Verified == 0; i++) await Task.Delay(30);
        await w.StopAsync();

        Assert.Equal("verified", _store.Inspect("shop", "a")!.Value.State);
    }

    // ── Lỗi của CHÍNH ảnh ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task KiemLechMd5_XoaAnhVuaUp_TangSoLanThu_LanSauDat()
    {
        _world.Cdn["vn-1"] = Jpeg;
        await _store.EnqueueAsync("shop", 1, ["vn-1"]);
        _world.PublicTamper = (n, b) => n == 1 ? [.. b, 0x00] : b;      // lần GET đầu: bytes bị đổi
        var w = Worker();

        await w.RunOnceAsync(CancellationToken.None);

        var row = _store.Inspect("shop", "vn-1")!.Value;
        Assert.Equal(("pending", 1, null), (row.State, row.Attempts, row.HubPath));
        Assert.Contains("MD5", row.LastError);
        Assert.Equal(["x0001.jpg"], _world.Deleted);
        Assert.Contains(_log, l => l.Contains("đã xoá /api/media/x0001.jpg"));
        Assert.Equal(ImagePrepOutcome.Retry, _progress.Single().Outcome);
        Assert.True(w.BreakerUntil < _clock.Now);                       // lỗi của ảnh: không ngắt cầu dao

        Assert.Equal(0, await w.RunOnceAsync(CancellationToken.None));   // chưa tới mốc lùi của ảnh (1 phút)
        _clock.Advance(TimeSpan.FromMinutes(1));
        await w.RunOnceAsync(CancellationToken.None);
        Assert.Equal(("verified", "/api/media/x0002.jpg"), (_store.Inspect("shop", "vn-1")!.Value.State, _store.Inspect("shop", "vn-1")!.Value.HubPath));
    }

    [Fact]
    public async Task KiemLechLapLai_DuTran_Failed()
    {
        _world.Cdn["a"] = Jpeg;
        await _store.EnqueueAsync("shop", 1, ["a"]);
        _world.PublicTamper = (_, b) => [.. b, 0x00];
        var w = Worker();

        for (var i = 0; i < 3; i++)
        {
            await w.RunOnceAsync(CancellationToken.None);
            _clock.Advance(TimeSpan.FromMinutes(10));
        }

        var row = _store.Inspect("shop", "a")!.Value;
        Assert.Equal(("failed", 3), (row.State, row.Attempts));
        Assert.Contains("thử 3 lần", row.LastError);
        Assert.Equal(3, _world.Deleted.Count);
    }

    [Fact]
    public async Task KiemSaiContentType_XoaVaThuLai()
    {
        _world.Cdn["vn-1"] = Png;
        await _store.EnqueueAsync("shop", 1, ["vn-1"]);
        _world.UploadOverride = _ =>
        {
            _world.Stored["sai.jpg"] = ImageMd5Changer.Change(Png, "shop", "vn-1").Bytes;   // tên .jpg → GET trả image/jpeg
            return FakeMediaWorld.Json(HttpStatusCode.OK,
                "{\"path\":\"/api/media/sai.jpg\",\"url\":\"https://media.example.com/api/media/sai.jpg\",\"expiresAt\":\"2026-11-24T00:00:00Z\"}");
        };

        await Worker().RunOnceAsync(CancellationToken.None);

        var row = _store.Inspect("shop", "vn-1")!.Value;
        Assert.Equal(("pending", 1), (row.State, row.Attempts));
        Assert.Contains("Content-Type", row.LastError);
        Assert.Equal(["sai.jpg"], _world.Deleted);
    }

    [Fact]
    public async Task NguonKhongCo404_FailedNgay_KhongUp()
    {
        await _store.EnqueueAsync("shop", 1, ["mat-roi"]);

        await Worker().RunOnceAsync(CancellationToken.None);

        var row = _store.Inspect("shop", "mat-roi")!.Value;
        Assert.Equal("failed", row.State);
        Assert.Contains("404", row.LastError);
        Assert.Equal(0, PutCount);
        Assert.Equal(ImagePrepOutcome.Failed, _progress.Single().Outcome);
    }

    [Fact]
    public async Task NguonWebP_FailedNgay_CoLyDo()
    {
        _world.Cdn["vn-webp"] = [.. "RIFF"u8, 0x10, 0, 0, 0, .. "WEBPVP8 "u8, 0, 0, 0, 0];
        await _store.EnqueueAsync("shop", 1, ["vn-webp"]);

        await Worker().RunOnceAsync(CancellationToken.None);

        var row = _store.Inspect("shop", "vn-webp")!.Value;
        Assert.Equal("failed", row.State);
        Assert.Contains("WebP", row.LastError);
        Assert.Equal(0, PutCount);
    }

    [Fact]
    public async Task BangSize_FileMat_HoacDoiNoiDung_Failed_CoLyDoRo()
    {
        var path = Path.Combine(_dir, "size.jpeg");
        await File.WriteAllBytesAsync(path, Jpeg);
        var key = SizeChartImage.FromFile(path).Key;
        await _store.EnqueueSizeChartAsync("shop", SizeChartImage.FromFile(path));
        await File.WriteAllBytesAsync(path, Png);                         // đổi nội dung sau khi lấy khoá

        await Worker().RunOnceAsync(CancellationToken.None);
        Assert.Equal("failed", Row(key).State);
        Assert.Contains("đã đổi nội dung", _store.Inspect("shop", key)!.Value.LastError);

        File.Delete(path);
        await _store.RetryFailedAsync("shop");
        await Worker().RunOnceAsync(CancellationToken.None);
        Assert.Equal("failed", Row(key).State);
        Assert.Contains("Chưa có ảnh bảng size", _store.Inspect("shop", key)!.Value.LastError);
        Assert.Equal(0, PutCount);
    }

    [Theory]
    [InlineData(413, "{\"error\":\"too-large\"}")]
    [InlineData(415, "{\"error\":\"unsupported-type\"}")]
    public async Task Hub413Va415_FailedNgay_KhongLui_WorkerKhongDung(int code, string body)
    {
        foreach (var k in new[] { "a", "b" }) _world.Cdn[k] = Jpeg;
        await _store.EnqueueAsync("shop", 1, ["a", "b"]);
        _world.UploadOverride = n => n == 1 ? FakeMediaWorld.Json((HttpStatusCode)code, body) : null;
        var w = Worker();

        Assert.Equal(1, await w.RunOnceAsync(CancellationToken.None));

        var row = _store.Inspect("shop", "a")!.Value;
        Assert.Equal("failed", row.State);
        Assert.Equal(0L, row.NextAttemptAtMs);
        Assert.Contains(code.ToString(System.Globalization.CultureInfo.InvariantCulture), row.LastError);
        Assert.Null(w.HaltReason);
        Assert.Empty(_halts);
        Assert.True(w.BreakerUntil < _clock.Now);
        Assert.Equal(ImagePrepOutcome.Failed, _progress.Single().Outcome);

        Assert.Equal(1, await w.RunOnceAsync(CancellationToken.None)); // worker vẫn chạy ảnh kế
        Assert.Equal("verified", Row("b").State);
    }

    // ── Lỗi HỆ THỐNG: cầu dao chung, không đốt hàng đợi ────────────────────────────────────────────────────────────

    public static TheoryData<int, string> SystemFaults => new()
    {
        { 502, "<html>Bad gateway</html>" },                            // Cloudflare: 502 không mã
        { 507, "{\"error\":\"media-quota-exceeded\"}" },
        { 507, "{\"error\":\"disk-low\"}" },
        { 503, "<html>down</html>" },
        { 500, "" },
        { 429, "" },
    };

    [Theory]
    [MemberData(nameof(SystemFaults))]
    public async Task LoiHeThong_NgatCauDaoChung_AnhGiuNguyen(int code, string body)
    {
        foreach (var k in new[] { "a", "b" }) _world.Cdn[k] = Jpeg;
        await _store.EnqueueAsync("shop", 1, ["a", "b"]);
        _world.UploadOverride = _ => FakeMediaWorld.Json((HttpStatusCode)code, body);
        var w = Worker();

        Assert.Equal(1, await w.RunOnceAsync(CancellationToken.None));

        var row = _store.Inspect("shop", "a")!.Value;
        Assert.Equal(("pending", 0, 0L, null), (row.State, row.Attempts, row.NextAttemptAtMs, row.LastError));
        Assert.Equal(T0.AddMinutes(1), w.BreakerUntil);
        Assert.Contains(code.ToString(System.Globalization.CultureInfo.InvariantCulture), w.LastError);
        Assert.Null(w.HaltReason);
        Assert.Equal(ImagePrepOutcome.Retry, _progress.Single().Outcome);

        Assert.Equal(0, await w.RunOnceAsync(CancellationToken.None));   // cầu dao ngắt: không lấy ảnh nào
        Assert.Equal(1, PutCount);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await w.RunOnceAsync(CancellationToken.None);
        Assert.Equal(2, PutCount);
        Assert.Equal(T0.AddMinutes(1 + 2), w.BreakerUntil);              // lùi dần chung ×2
        Assert.Equal(("pending", 0), Row("b"));                          // ảnh lỗi trước xuống cuối → lượt sau lấy ảnh khác
    }

    [Fact]
    public async Task P1_Hub502NhieuGio_KhongAnhNaoFailed_SongLaiThiCa5Verified()
    {
        var keys = Enumerable.Range(1, 5).Select(i => $"a{i}").ToArray();
        foreach (var k in keys) _world.Cdn[k] = Jpeg;
        await _store.EnqueueAsync("shop", 1, keys);
        _world.UploadOverride = _ => FakeMediaWorld.Json(HttpStatusCode.BadGateway, "<html>Bad gateway</html>");
        var w = Worker();

        var until = T0.AddHours(6);
        while (_clock.Now < until)
        {
            await w.RunOnceAsync(CancellationToken.None);
            _clock.Now = w.BreakerUntil > _clock.Now ? w.BreakerUntil : _clock.Now.AddMinutes(1);
        }

        Assert.All(keys, k => Assert.Equal(("pending", 0), Row(k)));
        Assert.Equal(new ImagePrepStats(5, 0, 0, 0), await _store.GetStatsAsync("shop"));
        Assert.InRange(PutCount, 30, 60);                                // 6 giờ, trần 10' → ~36 lượt, không dồn dập

        _world.UploadOverride = null;                                    // Hub sống lại
        _clock.Now = w.BreakerUntil;
        for (var i = 0; i < 10 && (await _store.GetStatsAsync("shop")).Verified < 5; i++)
            await w.RunOnceAsync(CancellationToken.None);

        Assert.Equal(new ImagePrepStats(0, 5, 0, 0), await _store.GetStatsAsync("shop"));
        Assert.All(keys, k => Assert.Equal(0, Row(k).Attempts));
    }

    [Fact]
    public async Task MatMangCdn_LoiHeThong_KhongTangSoLanThu()
    {
        await _store.EnqueueAsync("shop", 1, ["a"]);
        using var broken = new HttpClient(new ThrowingHandler());
        var w = new ImagePrepWorker(_store, () => _hub, broken, Fast, clock: () => _clock.Now);

        await w.RunOnceAsync(CancellationToken.None);

        Assert.Equal(("pending", 0), Row("a"));
        Assert.True(w.BreakerUntil > _clock.Now);
    }

    [Fact]
    public async Task ChuaKetNoiHub_KhongDungHangDoi()
    {
        _world.Cdn["a"] = Jpeg;
        await _store.EnqueueAsync("shop", 1, ["a"]);
        var w = Worker(hub: () => null);

        Assert.Equal(0, await w.RunOnceAsync(CancellationToken.None));

        Assert.Equal(("pending", 0), Row("a"));
        Assert.Empty(_world.FetchRequests);
        Assert.Equal("Chưa kết nối Hub", w.LastError);
    }

    // ── Cấu hình hỏng ───────────────────────────────────────────────────────────────────────────────────────────────

    public static TheoryData<int, string> ConfigBroken => new()
    {
        { 200, "{}" },                                                  // client tự gán 502 url-missing
        { 200, "{\"path\":\"/api/media/a.jpg\",\"url\":\"http://localhost/api/media/a.jpg\",\"expiresAt\":\"2026-11-24T00:00:00Z\"}" },
        { 503, "{\"error\":\"public-base-url-missing\"}" },
        { 401, "" },
        { 404, "" },                                                    // Hub cũ chưa có route
    };

    [Theory]
    [MemberData(nameof(ConfigBroken))]
    public async Task CauHinhHong_DungWorker_KhongDoiTrangThaiHangLoat(int code, string body)
    {
        foreach (var k in new[] { "a", "b", "c" }) _world.Cdn[k] = Jpeg;
        await _store.EnqueueAsync("shop", 1, ["a", "b", "c"]);
        _world.UploadOverride = _ => FakeMediaWorld.Json((HttpStatusCode)code, body);
        var w = Worker(new ImagePrepWorkerOptions { MaxParallel = 2, MaxAttempts = 3, Interval = TimeSpan.FromMilliseconds(20) });

        w.Start();
        var loop = w.Loop!;
        Assert.Same(loop, await Task.WhenAny(loop, Task.Delay(5000)));    // vòng nền tự thoát

        Assert.NotNull(w.HaltReason);
        Assert.Equal([w.HaltReason], _halts);
        foreach (var k in new[] { "a", "b", "c" })
        {
            var row = _store.Inspect("shop", k)!.Value;
            Assert.Equal(("pending", 0, null, 0L), (row.State, row.Attempts, row.LastError, row.NextAttemptAtMs));
        }
        Assert.Empty(_progress);
        var puts = PutCount;
        Assert.InRange(puts, 1, 2);
        Assert.Equal(0, await w.RunOnceAsync(CancellationToken.None));
        Assert.Equal(puts, PutCount);

        _world.UploadOverride = null;
        w.Start();
        Assert.Null(w.HaltReason);
        for (var i = 0; i < 100 && (await _store.GetStatsAsync("shop")).Verified < 3; i++) await Task.Delay(30);
        await w.StopAsync();
        Assert.Equal(new ImagePrepStats(0, 3, 0, 0), await _store.GetStatsAsync("shop"));
    }

    // ── hosted: bền qua lần tắt app ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Hosted_GhiBenTruocKhiKiem_AppTatGiuaChung_MoLaiChiKiem_KhongUpLai_KhongMoCoi()
    {
        _world.Cdn["a"] = Jpeg;
        await _store.EnqueueAsync("shop", 1, ["a"]);
        _world.PublicHang = true;
        using var cts = new CancellationTokenSource();
        var run = Worker().RunOnceAsync(cts.Token);
        await _world.PublicEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var row = _store.Inspect("shop", "a")!.Value;
        Assert.Equal(("hosted", "/api/media/x0001.jpg", 0), (row.State, row.HubPath, row.Attempts));
        Assert.Equal(Md5(_world.Stored["x0001.jpg"]), row.Md5);
        cts.Cancel();                                                   // "tắt app"
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(("hosted", 0), Row("a"));

        _world.PublicHang = false;
        var reopened = NewStore();
        Assert.Equal(1, await Worker(store: reopened).RunOnceAsync(CancellationToken.None));

        Assert.Equal("verified", reopened.Inspect("shop", "a")!.Value.State);
        Assert.Equal(1, PutCount);
        Assert.Empty(_world.Deleted);
        Assert.Single(_world.Stored);
    }

    [Fact]
    public async Task Hosted_KiemLech_XoaTrenHub_VePending_LanSauUpLai()
    {
        _world.Cdn["a"] = Jpeg;
        await _store.EnqueueAsync("shop", 1, ["a"]);
        var bytes = ImageMd5Changer.Change(Jpeg, "shop", "a");
        _world.Stored["cu.jpg"] = [.. bytes.Bytes, 0x00];
        _store.MarkHosted(_store.TakeDue(1)[0], bytes.Md5, bytes.ContentType, "/api/media/cu.jpg",
            FakeMediaWorld.PublicBase + "/api/media/cu.jpg", T0.AddDays(7));
        var w = Worker();

        await w.RunOnceAsync(CancellationToken.None);

        var row = _store.Inspect("shop", "a")!.Value;
        Assert.Equal(("pending", 1, null, null), (row.State, row.Attempts, row.HubPath, row.Url));
        Assert.Equal(["cu.jpg"], _world.Deleted);
        Assert.Equal(0, PutCount);

        _clock.Advance(TimeSpan.FromMinutes(1));
        await w.RunOnceAsync(CancellationToken.None);
        Assert.Equal("verified", Row("a").State);
        Assert.Equal(1, PutCount);
    }

    [Fact]
    public async Task Hosted_Kiem404HetHan_VePending_KhongTang_KhongXoa_KhongNgatCauDao()
    {
        _world.Cdn["a"] = Jpeg;
        await _store.EnqueueAsync("shop", 1, ["a"]);
        var bytes = ImageMd5Changer.Change(Jpeg, "shop", "a");
        _store.MarkHosted(_store.TakeDue(1)[0], bytes.Md5, bytes.ContentType, "/api/media/mat.jpg",
            FakeMediaWorld.PublicBase + "/api/media/mat.jpg", T0.AddDays(7));
        var w = Worker();

        await w.RunOnceAsync(CancellationToken.None);

        var row = _store.Inspect("shop", "a")!.Value;
        Assert.Equal(("pending", 0, null), (row.State, row.Attempts, row.HubPath));
        Assert.Contains("404", row.LastError);
        Assert.Empty(_world.Deleted);
        Assert.True(w.BreakerUntil < _clock.Now);
        await w.RunOnceAsync(CancellationToken.None);                   // lượt kế up lại ngay
        Assert.Equal("verified", Row("a").State);
    }

    [Fact]
    public async Task VuaUpMaKiem404_LoiHeThong_NgatCauDao()
    {
        _world.Cdn["a"] = Jpeg;
        await _store.EnqueueAsync("shop", 1, ["a"]);
        _world.PublicOverride = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        var w = Worker();

        await w.RunOnceAsync(CancellationToken.None);

        Assert.Equal(("pending", 0), Row("a"));
        Assert.True(w.BreakerUntil > _clock.Now);
    }

    [Theory]
    [InlineData(502)]
    [InlineData(403)]
    public async Task Hosted_KiemKhongToiDuoc_GiuHosted_KhongTang_LanSauChiKiem(int code)
    {
        _world.Cdn["a"] = Jpeg;
        await _store.EnqueueAsync("shop", 1, ["a"]);
        _world.PublicOverride = _ => new HttpResponseMessage((HttpStatusCode)code);
        var w = Worker();

        for (var i = 0; i < 5; i++)
        {
            await w.RunOnceAsync(CancellationToken.None);
            _clock.Now = w.BreakerUntil;
        }
        var row = _store.Inspect("shop", "a")!.Value;
        Assert.Equal(("hosted", 0, "/api/media/x0001.jpg"), (row.State, row.Attempts, row.HubPath));
        Assert.Empty(_world.Deleted);

        _world.PublicOverride = null;
        await w.RunOnceAsync(CancellationToken.None);
        Assert.Equal("verified", Row("a").State);
        Assert.Equal(1, PutCount);                                       // không up lại
    }

    [Fact]
    public async Task MarkHostedLan2_DongDaHosted_KhongGhiDe_AnhUpThuaBiXoa()
    {
        _world.Cdn["a"] = Jpeg;
        await _store.EnqueueAsync("shop", 1, ["a"]);
        var pending = _store.TakeDue(1)[0];
        // Trong lúc worker đang up, lượt khác đã host ảnh này (vd 2 worker / app chạy 2 bản).
        _world.OnUpload = () =>
        {
            _store.MarkHosted(pending, "md5-khac", "image/jpeg", "/api/media/khac.jpg",
                FakeMediaWorld.PublicBase + "/api/media/khac.jpg", T0.AddDays(7));
            return Task.CompletedTask;
        };

        await Worker().RunOnceAsync(CancellationToken.None);

        Assert.Equal("/api/media/khac.jpg", _store.Inspect("shop", "a")!.Value.HubPath);
        Assert.Equal(["x0001.jpg"], _world.Deleted);
        Assert.Contains(_log, l => l.Contains("up thừa"));
    }

    [Fact]
    public async Task Release_TrongLucDangUp_XoaAnhVuaUp()
    {
        _world.Cdn["a"] = Jpeg;
        await _store.EnqueueAsync("shop", 1, ["a"]);
        _world.OnUpload = () => _store.ReleaseItemAsync("shop", 1, _hub);

        await Worker().RunOnceAsync(CancellationToken.None);

        Assert.Null(_store.Inspect("shop", "a"));
        Assert.Equal(["x0001.jpg"], _world.Deleted);
    }

    [Fact]
    public async Task HuyGiuaChung_GiuPending_KhongTangSoLanThu()
    {
        _world.Cdn["a"] = Jpeg;
        _world.CdnHang = true;
        await _store.EnqueueAsync("shop", 1, ["a"]);
        var w = Worker();
        using var cts = new CancellationTokenSource();

        var run = w.RunOnceAsync(cts.Token);
        await _world.CdnEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        var row = _store.Inspect("shop", "a")!.Value;
        Assert.Equal(("pending", 0, null), (row.State, row.Attempts, row.LastError));
        Assert.Equal(0, PutCount);
        Assert.Empty(_progress);
    }

    // ── Dọn deleting ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task P2_DeletingKet_WorkerDonSauNPhut_HetThamChieuThiXoaDong()
    {
        _world.Cdn["a"] = Jpeg;
        await _store.EnqueueAsync("shop", 1, ["a"]);
        var w = Worker();
        await w.RunOnceAsync(CancellationToken.None);
        _world.DeleteOverride = _ => new HttpResponseMessage(HttpStatusCode.BadGateway);
        await _store.ReleaseItemAsync("shop", 1, _hub);                  // xoá hụt → deleting
        Assert.Equal(new ImagePrepStats(0, 0, 0, 1), await _store.GetStatsAsync("shop"));
        _world.DeleteOverride = null;

        await w.RunOnceAsync(CancellationToken.None);                   // chưa đủ 10'
        Assert.Equal("deleting", Row("a").State);
        _clock.Advance(TimeSpan.FromMinutes(10));
        await w.RunOnceAsync(CancellationToken.None);

        Assert.Null(_store.Inspect("shop", "a"));
        Assert.Equal(["x0001.jpg", "x0001.jpg"], _world.Deleted);
    }

    [Fact]
    public async Task P4_HuyGiuaLucReleaseXoa_WorkerDonSau()
    {
        _world.Cdn["a"] = Jpeg;
        await _store.EnqueueAsync("shop", 1, ["a"]);
        var w = Worker();
        await w.RunOnceAsync(CancellationToken.None);
        _world.DeleteHang = true;
        using var cts = new CancellationTokenSource();
        var release = _store.ReleaseItemAsync("shop", 1, _hub, cts.Token);
        await _world.DeleteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => release);
        _world.DeleteHang = false;

        _clock.Advance(TimeSpan.FromMinutes(10));
        await w.RunOnceAsync(CancellationToken.None);

        Assert.Null(_store.Inspect("shop", "a"));
        Assert.Contains("x0001.jpg", _world.Deleted);
    }

    [Fact]
    public async Task Forget_WorkerXoaTrenHubNgayLuotKe_ConThamChieuThiLamLai()
    {
        foreach (var k in new[] { "a", "b" }) _world.Cdn[k] = Jpeg;
        await _store.EnqueueAsync("shop", 1, ["a", "b"]);
        var w = Worker(new ImagePrepWorkerOptions { MaxParallel = 2, MaxAttempts = 3 });
        await w.RunOnceAsync(CancellationToken.None);
        Assert.Equal(new ImagePrepStats(0, 2, 0, 0), await _store.GetStatsAsync("shop"));

        await _store.ForgetItemAsync("shop", 1);
        await _store.EnqueueAsync("shop", 2, ["b"]);                     // b được SP mới dùng lại trước khi worker dọn
        Assert.Empty(_world.Deleted);
        await w.RunOnceAsync(CancellationToken.None);

        Assert.Null(_store.Inspect("shop", "a"));
        Assert.Equal(["x0001.jpg", "x0002.jpg"], _world.Deleted.Order(StringComparer.Ordinal));
        // b đã mất trên Hub → về pending, cùng lượt được làm lại cho SP 2 (ảnh mới).
        Assert.Equal(("verified", "/api/media/x0003.jpg"), (Row("b").State, _store.Inspect("shop", "b")!.Value.HubPath));
    }

    // ── Vòng 4: timeout / mạng / CDN 4xx-5xx / 404 ngay sau up / bảng size đọc đường dẫn gốc ─────────────────────

    [Fact]
    public void FetchClient_KhongDiProxyHeThong()
    {
        using var handler = ImagePrepWorker.CreateFetchHandler();
        Assert.False(handler.UseProxy);
        using var client = ImagePrepWorker.CreateFetchClient();
        Assert.Equal(TimeSpan.FromSeconds(60), client.Timeout);
    }

    [Fact]
    public async Task TimeoutTaiCdn_LoiHeThong_KhongTangSoLanThu()
    {
        _world.Cdn["a"] = Jpeg;
        _world.CdnHang = true;
        await _store.EnqueueAsync("shop", 1, ["a"]);
        using var slow = new HttpClient(_world.Fetch) { Timeout = TimeSpan.FromMilliseconds(100) };
        var w = new ImagePrepWorker(_store, () => _hub, slow, Fast, clock: () => _clock.Now);

        await w.RunOnceAsync(CancellationToken.None);

        Assert.Equal(("pending", 0), Row("a"));
        Assert.True(w.BreakerUntil > _clock.Now);
        Assert.Equal("quá thời gian chờ", w.LastError);
        Assert.Equal(0, PutCount);
    }

    public static TheoryData<string> NetworkFaults => new() { "timeout", "mạng" };

    private static Exception NetworkFault(string kind) => kind == "timeout"
        ? new TaskCanceledException("timeout", new TimeoutException())
        : new HttpRequestException("mất mạng");

    [Theory]
    [MemberData(nameof(NetworkFaults))]
    public async Task PutTimeoutHoacMatMang_LoiHeThong_KhongTangSoLanThu(string kind)
    {
        _world.Cdn["a"] = Jpeg;
        await _store.EnqueueAsync("shop", 1, ["a"]);
        _world.UploadThrow = () => NetworkFault(kind);
        var w = Worker();

        await w.RunOnceAsync(CancellationToken.None);

        Assert.Equal(("pending", 0), Row("a"));
        Assert.True(w.BreakerUntil > _clock.Now);
        Assert.Null(w.HaltReason);
    }

    [Theory]
    [MemberData(nameof(NetworkFaults))]
    public async Task GetKiemTimeoutHoacMatMang_GiuHosted_KhongTangSoLanThu(string kind)
    {
        _world.Cdn["a"] = Jpeg;
        await _store.EnqueueAsync("shop", 1, ["a"]);
        _world.PublicThrow = () => NetworkFault(kind);
        var w = Worker();

        await w.RunOnceAsync(CancellationToken.None);

        Assert.Equal(("hosted", 0), Row("a"));
        Assert.True(w.BreakerUntil > _clock.Now);
        Assert.Empty(_world.Deleted);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(429)]
    public async Task Cdn5xxHoac429_LoiHeThong_KhongTangSoLanThu(int code)
    {
        await _store.EnqueueAsync("shop", 1, ["a"]);
        _world.CdnStatus = _ => (HttpStatusCode)code;
        var w = Worker();

        await w.RunOnceAsync(CancellationToken.None);

        Assert.Equal(("pending", 0), Row("a"));
        Assert.True(w.BreakerUntil > _clock.Now);
    }

    [Fact]
    public async Task Cdn403VinhVien_LoiCuaAnh_AnhKhacVanVerified_KhongNgatCauDao_DuTranThiFailed_RetryCuuDuoc()
    {
        _world.Cdn["b"] = Jpeg;
        _world.Cdn["c"] = Jpeg;
        await _store.EnqueueAsync("shop", 1, ["a", "b", "c"]);
        _world.CdnStatus = id => id == "a" ? HttpStatusCode.Forbidden : null;
        var w = Worker();

        for (var i = 0; i < 12 && (await _store.GetStatsAsync("shop")).Pending > 0; i++)
        {
            await w.RunOnceAsync(CancellationToken.None);
            Assert.True(w.BreakerUntil < _clock.Now);                   // ảnh 403 không ngắt cầu dao chung
            _clock.Advance(TimeSpan.FromMinutes(5));
        }

        Assert.Equal("verified", Row("b").State);
        Assert.Equal("verified", Row("c").State);
        var row = _store.Inspect("shop", "a")!.Value;
        Assert.Equal(("failed", 3), (row.State, row.Attempts));
        Assert.Contains("CDN trả 403 lặp lại", row.LastError);

        Assert.Equal(1, await _store.RetryFailedAsync("shop"));
        _world.Cdn["a"] = Jpeg;
        _world.CdnStatus = null;
        await w.RunOnceAsync(CancellationToken.None);
        Assert.Equal("verified", Row("a").State);
    }

    [Fact]
    public async Task Cdn400_TangSoLanThuCuaAnh_LuiTheoAnh()
    {
        await _store.EnqueueAsync("shop", 1, ["a"]);
        _world.CdnStatus = _ => HttpStatusCode.BadRequest;
        var w = Worker();

        await w.RunOnceAsync(CancellationToken.None);

        var row = _store.Inspect("shop", "a")!.Value;
        Assert.Equal(("pending", 1), (row.State, row.Attempts));
        Assert.Equal(T0.AddMinutes(1).ToUnixTimeMilliseconds(), row.NextAttemptAtMs);
        Assert.True(w.BreakerUntil < _clock.Now);
    }

    [Fact]
    public async Task VuaUpMaKiem404_XoaBanVuaUp_VePending_Ba404LienTiepThiDungWorker()
    {
        foreach (var k in new[] { "a", "b", "c", "d" }) _world.Cdn[k] = Jpeg;
        await _store.EnqueueAsync("shop", 1, ["a", "b", "c", "d"]);
        _world.PublicOverride = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        var w = Worker();

        await w.RunOnceAsync(CancellationToken.None);
        Assert.Equal(["x0001.jpg"], _world.Deleted);                     // bản vừa up bị xoá trước khi về pending
        Assert.Contains(_log, l => l.Contains("x0001.jpg") && l.Contains("404"));
        Assert.Null(w.HaltReason);
        _clock.Now = w.BreakerUntil;
        await w.RunOnceAsync(CancellationToken.None);
        Assert.Null(w.HaltReason);
        _clock.Now = w.BreakerUntil;
        await w.RunOnceAsync(CancellationToken.None);

        Assert.Equal(ImagePrepWorker.Fresh404HaltReason, w.HaltReason);
        Assert.Equal([ImagePrepWorker.Fresh404HaltReason], _halts);
        Assert.Equal(3, _world.Deleted.Count);
        Assert.All(new[] { "a", "b", "c", "d" }, k => Assert.Equal(("pending", 0), Row(k)));
        Assert.Equal(0, await w.RunOnceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Fresh404_DemLienTiep_MotLanKiemDatThiDatLai()
    {
        foreach (var k in new[] { "a", "b", "c", "d", "e" }) _world.Cdn[k] = Jpeg;
        await _store.EnqueueAsync("shop", 1, ["a", "b", "c", "d", "e"]);
        var w = Worker();
        async Task Pass(bool notFound)
        {
            _world.PublicOverride = notFound ? _ => new HttpResponseMessage(HttpStatusCode.NotFound) : null;
            await w.RunOnceAsync(CancellationToken.None);
            if (w.BreakerUntil > _clock.Now) _clock.Now = w.BreakerUntil;
        }

        await Pass(true);
        await Pass(true);
        await Pass(false);                                               // một lần kiểm đạt → đếm lại từ 0
        await Pass(true);
        await Pass(true);

        Assert.Null(w.HaltReason);
        Assert.Equal(1, (await _store.GetStatsAsync("shop")).Verified);
    }

    [Fact]
    public async Task BangSize_DocTheoDuongDanGoc_KhongDocPhanChuThuongTrongKhoa()
    {
        var path = Path.Combine(_dir, "Size.JPEG");
        await File.WriteAllBytesAsync(path, Jpeg);
        var img = SizeChartImage.FromFile(path);
        Assert.True(SizeChartImage.TryParse(img.Key, out var sha, out _));
        // Khoá trỏ (phần chữ thường) tới chỗ KHÔNG có file; đường dẫn đọc thật nằm ở source_path.
        var key = $"sizechart:{sha}:" + Path.Combine(_dir, "khong-co", "size.jpeg").ToLowerInvariant();
        await _store.EnqueueSizeChartAsync("shop", key, img.FilePath);

        await Worker().RunOnceAsync(CancellationToken.None);

        Assert.Equal("verified", Row(key).State);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("mất mạng");
    }
}
