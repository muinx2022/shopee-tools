using System.Net;
using Microsoft.Data.Sqlite;
using Shopee.Core.Coordination;
using Shopee.Core.MassUpload;
using Shopee.Core.MassUpload.Images;

namespace Shopee.Core.Tests.MassUpload.Images;

/// <summary>GĐ2b: <see cref="UploadStore"/> — hàng đợi <c>image_prep</c>, đếm tham chiếu <c>image_ref</c>, resolver 3 trạng thái
/// (chỉ hạ ảnh CẦN dùng sắp hết hạn), Release chỉ xoá ảnh của CHÍNH SP (KHÔNG BAO GIỜ xoá bảng size), Forget, RetryFailed,
/// schema theo user_version.</summary>
public sealed class UploadStoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 0, 0, 0, TimeSpan.Zero);

    private readonly string _dir = HarvestTestData.NewTempDir();
    private readonly ManualClock _clock = new(T0);
    private readonly FakeMediaWorld _world = new();
    private readonly HubClient _hub;
    private readonly UploadStore _store;

    public UploadStoreTests()
    {
        _hub = _world.HubClient();
        _store = NewStore();
    }

    public void Dispose()
    {
        _hub.Dispose();
        HarvestTestData.TryDelete(_dir);
    }

    private string DbPath => Path.Combine(_dir, "upload.db");

    private UploadStore NewStore() => new(DbPath, () => _clock.Now);

    private static ImagePrepEntry Entry(string shop, string key, ImagePrepKind kind = ImagePrepKind.Product) => new(shop, key, kind, 0);

    /// <summary>Đặt ảnh thành hosted (như worker vừa up xong, chưa kiểm), hết hạn sau <paramref name="ttl"/>.</summary>
    private ImagePrepEntry Host(string shop, string key, TimeSpan ttl, ImagePrepKind kind = ImagePrepKind.Product) =>
        _store.MarkHosted(Entry(shop, key, kind), "md5-" + key, "image/jpeg", $"{HubRoutes.PublicMedia}/{key}.jpg",
            $"https://media.example.com/api/media/{key}.jpg", _clock.Now + ttl) ?? throw new InvalidOperationException("không pending");

    /// <summary>Đặt ảnh thành verified (như worker đã làm xong), hết hạn sau <paramref name="ttl"/>.</summary>
    private void Verify(string shop, string key, TimeSpan ttl, ImagePrepKind kind = ImagePrepKind.Product) =>
        Assert.True(_store.MarkVerified(Host(shop, key, ttl, kind)));

    private long UserVersion()
    {
        using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ToString());
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return (long)cmd.ExecuteScalar()!;
    }

    [Fact]
    public async Task Enqueue_Idempotent_DemThamChieu()
    {
        await _store.EnqueueAsync("shop", 1, ["a", "b", "a", "", " "]);
        await _store.EnqueueAsync("shop", 1, ["a", "b"]);
        await _store.EnqueueAsync("shop", 2, ["b", "c"]);

        Assert.Equal(new ImagePrepStats(3, 0, 0, 0), await _store.GetStatsAsync("shop"));
        Assert.Equal(1, _store.RefCount("shop", "a"));
        Assert.Equal(2, _store.RefCount("shop", "b"));
        Assert.Equal(1, _store.RefCount("shop", "c"));
        Assert.Equal(new ImagePrepStats(0, 0, 0, 0), await _store.GetStatsAsync("shop-khac"));
    }

    [Fact]
    public async Task Enqueue_AnhDaCo_GiuTrangThai()
    {
        await _store.EnqueueAsync("shop", 1, ["a", "b"]);
        Verify("shop", "a", TimeSpan.FromDays(7));
        _store.MarkFailed(Entry("shop", "b"), "404");

        await _store.EnqueueAsync("shop", 2, ["a", "b"]);

        Assert.Equal("verified", _store.Inspect("shop", "a")!.Value.State);
        Assert.Equal("failed", _store.Inspect("shop", "b")!.Value.State);
        Assert.Equal(2, _store.RefCount("shop", "a"));
    }

    [Fact]
    public async Task EnqueueSizeChart_KhongThamChieu_FailedThiDatLaiPending()
    {
        const string key = "sizechart:abc";
        await _store.EnqueueSizeChartAsync("shop", key, @"C:\anh\size.jpeg");
        var row = _store.Inspect("shop", key)!.Value;
        Assert.Equal(("pending", "sizechart"), (row.State, row.Kind));
        Assert.Equal(0, _store.RefCount("shop", key));

        _store.MarkFailed(Entry("shop", key, ImagePrepKind.SizeChart), "không thấy file");
        await _store.EnqueueSizeChartAsync("shop", key, @"C:\anh\size.jpeg");
        row = _store.Inspect("shop", key)!.Value;
        Assert.Equal(("pending", 0, null), (row.State, row.Attempts, row.LastError));

        Verify("shop", key, TimeSpan.FromDays(7), ImagePrepKind.SizeChart);
        await _store.EnqueueSizeChartAsync("shop", key, @"C:\anh\size.jpeg");
        Assert.Equal("verified", _store.Inspect("shop", key)!.Value.State);
    }

    [Fact]
    public async Task Resolver_BaTrangThai_ChiHaAnhCanDungSapHetHan()
    {
        await _store.EnqueueAsync("shop", 1, ["xa", "gan", "gan-khac", "cho", "loi", "host"]);
        Verify("shop", "xa", TimeSpan.FromDays(5));
        Verify("shop", "gan", TimeSpan.FromHours(20));      // còn < 1 ngày, CÓ trong danh sách cần
        Verify("shop", "gan-khac", TimeSpan.FromHours(20)); // còn < 1 ngày, KHÔNG trong danh sách cần
        _store.MarkFailed(Entry("shop", "loi"), "ảnh gốc không còn trên CDN (404)");
        Host("shop", "host", TimeSpan.FromDays(7));

        var resolve = await _store.GetResolverAsync("shop", ["xa", "gan", "cho", "loi", "host"]);

        Assert.Equal(ImageResolution.Ready("https://media.example.com/api/media/xa.jpg"), resolve("xa"));
        Assert.Equal(ImageReadiness.Pending, resolve("gan").State);
        Assert.Equal(ImageReadiness.Pending, resolve("gan-khac").State);    // sắp hết hạn → không trả URL
        Assert.Equal(ImageReadiness.Pending, resolve("cho").State);
        Assert.Equal(ImageReadiness.Pending, resolve("host").State);        // hosted chưa kiểm
        Assert.Equal(ImageReadiness.Pending, resolve("khong-co").State);
        Assert.Equal(ImageResolution.Failed("ảnh gốc không còn trên CDN (404)"), resolve("loi"));

        var gan = _store.Inspect("shop", "gan")!.Value;
        Assert.Equal(("pending", null, null, 0L), (gan.State, gan.Url, gan.HubPath, gan.NextAttemptAtMs));
        Assert.Equal("verified", _store.Inspect("shop", "gan-khac")!.Value.State);   // ảnh không cần: không đụng
        Assert.Equal(new ImagePrepStats(3, 2, 1, 0), await _store.GetStatsAsync("shop"));

        Assert.Equal(ImageReadiness.Pending, (await _store.GetResolverAsync("shop", ["xa"], TimeSpan.FromDays(6)))("xa").State);
        Assert.Equal(ImageReadiness.Pending, (await _store.GetResolverAsync("shop-khac", ["xa"]))("xa").State);
    }

    [Fact]
    public async Task Release_ChiXoaAnhCuaChinhSp_GiuAnhDungChung_KhongBaoGioXoaBangSize()
    {
        await _store.EnqueueAsync("shop", 1, ["chung", "rieng1", "cho1"]);
        await _store.EnqueueAsync("shop", 2, ["chung", "rieng2"]);
        await _store.EnqueueSizeChartAsync("shop", "size", @"C:\anh\size.jpeg");
        Verify("shop", "chung", TimeSpan.FromDays(7));
        Verify("shop", "rieng1", TimeSpan.FromDays(7));
        Verify("shop", "rieng2", TimeSpan.FromDays(7));
        Verify("shop", "size", TimeSpan.FromDays(7), ImagePrepKind.SizeChart);

        Assert.Equal(1, await _store.ReleaseItemAsync("shop", 1, _hub));

        Assert.Equal(["rieng1.jpg"], _world.Deleted);
        Assert.Null(_store.Inspect("shop", "rieng1"));
        Assert.Null(_store.Inspect("shop", "cho1"));                     // pending hết tham chiếu: xoá dòng, không gọi Hub
        Assert.Equal("verified", _store.Inspect("shop", "chung")!.Value.State);
        Assert.Equal("verified", _store.Inspect("shop", "rieng2")!.Value.State);
        Assert.Equal(1, _store.RefCount("shop", "chung"));

        Assert.Equal(2, await _store.ReleaseItemAsync("shop", 2, _hub));
        Assert.Equal(["chung.jpg", "rieng1.jpg", "rieng2.jpg"], _world.Deleted.Order(StringComparer.Ordinal));
        Assert.Equal("verified", _store.Inspect("shop", "size")!.Value.State);
        Assert.Equal(new ImagePrepStats(0, 1, 0, 0), await _store.GetStatsAsync("shop"));

        Assert.Equal(0, await _store.ReleaseItemAsync("shop", 2, _hub));
        Assert.DoesNotContain("size.jpg", _world.Deleted);
    }

    [Fact]
    public async Task Release_KhongQuetDeletingCuaSpKhac()
    {
        await _store.EnqueueAsync("shop", 1, ["k1"]);
        await _store.EnqueueAsync("shop", 2, ["k2"]);
        Verify("shop", "k1", TimeSpan.FromDays(7));
        Verify("shop", "k2", TimeSpan.FromDays(7));
        await _store.ForgetItemAsync("shop", 2);                         // k2 → deleting, chờ worker

        Assert.Equal(1, await _store.ReleaseItemAsync("shop", 1, _hub));

        Assert.Equal(["k1.jpg"], _world.Deleted);                        // không đụng k2 của SP khác
        Assert.Equal("deleting", _store.Inspect("shop", "k2")!.Value.State);
        Assert.Equal(new ImagePrepStats(0, 0, 0, 1), await _store.GetStatsAsync("shop"));
    }

    [Fact]
    public async Task Release_XoaHubHut_GiuDeleting_KhongThamChieuThiChoWorker_CoThamChieuThiVeHosted()
    {
        await _store.EnqueueAsync("shop", 1, ["k", "j"]);
        await _store.EnqueueAsync("shop", 2, ["j"]);
        Verify("shop", "k", TimeSpan.FromDays(7));
        Verify("shop", "j", TimeSpan.FromDays(7));
        _world.DeleteOverride = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        Assert.Equal(0, await _store.ReleaseItemAsync("shop", 1, _hub));
        Assert.Equal("deleting", _store.Inspect("shop", "k")!.Value.State);
        Assert.Equal(ImageReadiness.Pending, (await _store.GetResolverAsync("shop", ["k"]))("k").State);
        Assert.Equal(new ImagePrepStats(0, 1, 0, 1), await _store.GetStatsAsync("shop"));

        // Dòng deleting chỉ đến hạn dọn sau N phút.
        Assert.Empty(_store.TakeStaleDeleting(TimeSpan.FromMinutes(10), 10));
        _clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(["k"], _store.TakeStaleDeleting(TimeSpan.FromMinutes(10), 10).Select(d => d.SourceKey));

        // Có SP mới dùng lại trong lúc xoá dở + lần xoá sau vẫn hụt → về hosted (ảnh còn sống, worker kiểm lại).
        await _store.EnqueueAsync("shop", 3, ["k"]);
        Assert.False(await _store.FinishDeleteAsync(_store.TakeStaleDeleting(TimeSpan.FromMinutes(10), 10)[0], _hub, default));
        var row = _store.Inspect("shop", "k")!.Value;
        Assert.Equal(("hosted", "/api/media/k.jpg"), (row.State, row.HubPath));
    }

    [Fact]
    public async Task Release_HuyGiuaLucXoa_DongNamODeleting_KhongKet()
    {
        await _store.EnqueueAsync("shop", 1, ["k"]);
        Verify("shop", "k", TimeSpan.FromDays(7));
        _world.DeleteHang = true;
        using var cts = new CancellationTokenSource();

        var release = _store.ReleaseItemAsync("shop", 1, _hub, cts.Token);
        await _world.DeleteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => release);

        Assert.Equal("deleting", _store.Inspect("shop", "k")!.Value.State);
        _world.DeleteHang = false;
        _clock.Advance(TimeSpan.FromMinutes(10));
        var stale = Assert.Single(_store.TakeStaleDeleting(TimeSpan.FromMinutes(10), 10));
        Assert.True(await _store.FinishDeleteAsync(stale, _hub, default));
        Assert.Null(_store.Inspect("shop", "k"));
    }

    [Fact]
    public async Task Forget_KhongGoiHub_AnhHetThamChieuChoWorkerDonNgay()
    {
        await _store.EnqueueAsync("shop", 1, ["v", "h", "p", "f", "chung"]);
        await _store.EnqueueAsync("shop", 2, ["chung"]);
        Verify("shop", "v", TimeSpan.FromDays(7));
        Host("shop", "h", TimeSpan.FromDays(7));
        _store.MarkFailed(Entry("shop", "f"), "404");
        Verify("shop", "chung", TimeSpan.FromDays(7));

        await _store.ForgetItemAsync("shop", 1);

        Assert.Empty(_world.HubRequests);
        Assert.Equal("deleting", _store.Inspect("shop", "v")!.Value.State);
        Assert.Equal("deleting", _store.Inspect("shop", "h")!.Value.State);
        Assert.Null(_store.Inspect("shop", "p"));
        Assert.Null(_store.Inspect("shop", "f"));
        Assert.Equal("verified", _store.Inspect("shop", "chung")!.Value.State);
        Assert.Equal(["h", "v"], _store.TakeStaleDeleting(TimeSpan.FromMinutes(10), 10).Select(d => d.SourceKey).Order());
    }

    [Fact]
    public async Task RetryFailed_DuaVePending_XoaSoLanThu()
    {
        await _store.EnqueueAsync("shop", 1, ["a", "b"]);
        await _store.EnqueueAsync("shop-khac", 1, ["a"]);
        _store.MarkFailed(Entry("shop", "a"), "404");
        _store.MarkFailed(Entry("shop-khac", "a"), "404");
        Verify("shop", "b", TimeSpan.FromDays(7));

        Assert.Equal(1, await _store.RetryFailedAsync("shop"));

        var a = _store.Inspect("shop", "a")!.Value;
        Assert.Equal(("pending", 0, null, 0L), (a.State, a.Attempts, a.LastError, a.NextAttemptAtMs));
        Assert.Equal("verified", _store.Inspect("shop", "b")!.Value.State);
        Assert.Equal("failed", _store.Inspect("shop-khac", "a")!.Value.State);
    }

    [Fact]
    public async Task Hosted_ThongKeTinhVaoCho_TakeDueMangChoHost_MoLaiDbConNguyen()
    {
        await _store.EnqueueAsync("shop", 1, ["h", "v"]);
        Host("shop", "h", TimeSpan.FromDays(7));
        Verify("shop", "v", TimeSpan.FromDays(7));

        Assert.Equal(new ImagePrepStats(1, 1, 0, 0), await _store.GetStatsAsync("shop"));
        var due = Assert.Single(_store.TakeDue(10));
        Assert.Equal(("h", "hosted", "md5-h", "image/jpeg", "/api/media/h.jpg", "https://media.example.com/api/media/h.jpg"),
            (due.SourceKey, due.State, due.Md5, due.ContentType, due.HubPath, due.Url));
        Assert.Equal("/api/media/h.jpg", Assert.Single(NewStore().TakeDue(10)).HubPath);
    }

    [Fact]
    public async Task MarkHosted_DongDaHosted_KhongGhiDe()
    {
        await _store.EnqueueAsync("shop", 1, ["h"]);
        var pending = Assert.Single(_store.TakeDue(10));
        Host("shop", "h", TimeSpan.FromDays(7));

        var second = _store.MarkHosted(pending, "md5-moi", "image/png", "/api/media/moi.png",
            "https://media.example.com/api/media/moi.png", T0.AddDays(7));

        Assert.Null(second);
        var row = _store.Inspect("shop", "h")!.Value;
        Assert.Equal(("hosted", "/api/media/h.jpg", "md5-h"), (row.State, row.HubPath, row.Md5));
    }

    [Fact]
    public async Task Schema_UserVersion_MoLaiGiuDuLieu_VersionMoiHonThiNem()
    {
        await _store.EnqueueAsync("shop", 1, ["a"]);
        Verify("shop", "a", TimeSpan.FromDays(7));
        Assert.Equal(UploadStore.SchemaVersion, UserVersion());

        var reopened = NewStore();
        Assert.Equal("https://media.example.com/api/media/a.jpg", (await reopened.GetResolverAsync("shop", ["a"]))("a").Url);
        Assert.Equal(1, reopened.RefCount("shop", "a"));

        using (var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ToString()))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = $"PRAGMA user_version = {UploadStore.SchemaVersion + 1};";
            cmd.ExecuteNonQuery();
        }
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => NewStore().GetStatsAsync("shop"));
        Assert.Contains("schema", ex.Message);
    }

    [Fact]
    public async Task TakeDue_BangSizeTruoc_ChiPendingDenHan_AnhVuaTouchXuongCuoi()
    {
        await _store.EnqueueAsync("shop", 1, ["a", "b", "c"]);
        await _store.EnqueueSizeChartAsync("shop", "size", @"C:\anh\size.jpeg");
        _store.Defer(Entry("shop", "a"), TimeSpan.FromMinutes(5), "lệch", 8);
        _clock.Advance(TimeSpan.FromSeconds(1));
        _store.Touch(Entry("shop", "b"));

        Assert.Equal(["size", "c", "b"], _store.TakeDue(10).Select(e => e.SourceKey));
        _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(["size", "c", "b", "a"], _store.TakeDue(10).Select(e => e.SourceKey));
    }

    [Fact]
    public void SizeChart_KhoaChuThuongDeSo_GiuDuongDanGocDeDoc_DoiNoiDungRaKhoaMoi_KhongCoFileThiNem()
    {
        var path = Path.Combine(_dir, "Size.JPEG");
        File.WriteAllBytes(path, [1, 2, 3]);
        var img = SizeChartImage.FromFile(path);
        Assert.Equal(Path.GetFullPath(path), img.FilePath);                 // giữ nguyên hoa/thường để đọc file
        Assert.Equal(img.Key, SizeChartImage.FromFile(path.ToUpperInvariant()).Key);   // khoá không phân biệt hoa/thường
        Assert.True(SizeChartImage.TryParse(img.Key, out var sha, out var p));
        Assert.Equal(64, sha.Length);
        Assert.Equal(Path.GetFullPath(path).ToLowerInvariant(), p);

        File.WriteAllBytes(path, [1, 2, 4]);
        Assert.NotEqual(img.Key, SizeChartImage.FromFile(path).Key);

        var ex = Assert.Throws<FileNotFoundException>(() => SizeChartImage.FromFile(Path.Combine(_dir, "khong-co.jpeg")));
        Assert.Contains("Chưa có ảnh bảng size", ex.Message);
        Assert.False(SizeChartImage.TryParse(@"D:\images\1.jpeg", out _, out _));
    }

    [Fact]
    public async Task EnqueueSizeChart_GhiDuongDanGoc_TakeDueMangTheo()
    {
        var path = Path.Combine(_dir, "Size.JPEG");
        File.WriteAllBytes(path, [1, 2, 3]);
        var img = SizeChartImage.FromFile(path);

        await _store.EnqueueSizeChartAsync("shop", img);

        var due = Assert.Single(_store.TakeDue(10));
        Assert.Equal((img.Key, img.FilePath, ImagePrepKind.SizeChart), (due.SourceKey, due.SourcePath, due.Kind));
    }

    [Fact]
    public async Task FinishDelete_LogCaKhiXoaThanhCong()
    {
        var log = new List<string>();
        var store = new UploadStore(DbPath, () => _clock.Now, log: s => log.Add(s));
        await store.EnqueueAsync("shop", 1, ["k"]);
        Assert.True(store.MarkVerified(store.MarkHosted(Entry("shop", "k"), "m", "image/jpeg", "/api/media/k.jpg",
            "https://media.example.com/api/media/k.jpg", T0.AddDays(7))!));

        Assert.Equal(1, await store.ReleaseItemAsync("shop", 1, _hub));

        Assert.Contains(log, l => l.Contains("đã xoá ảnh k") && l.Contains("/api/media/k.jpg"));
    }
}
