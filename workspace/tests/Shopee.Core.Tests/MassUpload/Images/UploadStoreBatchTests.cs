using System.Net;
using Microsoft.Data.Sqlite;
using Shopee.Core.Coordination;
using Shopee.Core.MassUpload.Images;

namespace Shopee.Core.Tests.MassUpload.Images;

/// <summary>GĐ8: ảnh theo lượt xuất trong <see cref="UploadStore"/> — migration v1 → v2 giữ dữ liệu, ảnh ghi theo lượt, "Xoá ảnh
/// tạm của lượt" KHÔNG xoá ảnh lượt khác (chưa xoá) đang dùng, 404 = đã xoá, xoá hụt thì giữ để bấm lại, không đụng ảnh bảng
/// size.</summary>
public sealed class UploadStoreBatchTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 0, 0, 0, TimeSpan.Zero);

    private readonly string _dir = HarvestTestData.NewTempDir();
    private readonly ManualClock _clock = new(T0);
    private readonly FakeMediaWorld _world = new();
    private readonly HubClient _hub;
    private readonly UploadStore _store;

    public UploadStoreBatchTests()
    {
        _hub = _world.HubClient();
        _store = new UploadStore(DbPath, () => _clock.Now);
    }

    public void Dispose()
    {
        _hub.Dispose();
        HarvestTestData.TryDelete(_dir);
    }

    private string DbPath => Path.Combine(_dir, "upload.db");

    /// <summary>Đặt ảnh thành verified như worker đã làm xong; tên trên Hub = <c>&lt;key&gt;.jpg</c>, có trong kho giả.</summary>
    private void Verify(string shop, string key)
    {
        var e = _store.MarkHosted(new ImagePrepEntry(shop, key, ImagePrepKind.Product, 0), "md5", "image/jpeg",
            $"{HubRoutes.PublicMedia}/{key}.jpg", $"https://media.example.com/api/media/{key}.jpg", T0.AddDays(7))!;
        Assert.True(_store.MarkVerified(e));
        _world.Stored[key + ".jpg"] = [1];
    }

    private SqliteConnection Raw()
    {
        var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ToString());
        con.Open();
        return con;
    }

    private long Scalar(string sql)
    {
        using var con = Raw();
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [Fact]
    public async Task XoaLuotA_AnhLuotBDangDung_KhongXoa_XoaBXongMoiXoa()
    {
        await _store.CreateBatchAsync("A", "shop", "Shop 1", null);
        await _store.CreateBatchAsync("B", "shop", "Shop 1", null);
        await _store.EnqueueForBatchAsync("A", "shop", 1, ["chung", "rieng-a"]);
        await _store.EnqueueForBatchAsync("B", "shop", 2, ["chung", "rieng-b"]);
        Verify("shop", "chung");
        Verify("shop", "rieng-a");
        Verify("shop", "rieng-b");

        var a = await _store.ClearBatchAsync("A", _hub);

        Assert.Equal(new BatchClearResult(1, 1, 0, 0), a);
        Assert.Equal(["rieng-a.jpg"], _world.Deleted);
        Assert.Null(_store.Inspect("shop", "rieng-a"));
        Assert.Equal("verified", _store.Inspect("shop", "chung")!.Value.State);
        Assert.Equal("https://media.example.com/api/media/chung.jpg", (await _store.GetResolverAsync("shop", ["chung"]))("chung").Url);
        Assert.Equal(2, _store.RefCount("shop", "chung"));   // ảnh giữ nguyên, kể cả tham chiếu

        var b = await _store.ClearBatchAsync("B", _hub);

        Assert.Equal(new BatchClearResult(2, 0, 0, 0), b);
        Assert.Equal(["rieng-a.jpg", "chung.jpg", "rieng-b.jpg"], _world.Deleted);
        Assert.Null(_store.Inspect("shop", "chung"));
        Assert.All(await _store.ListBatchesAsync(), x => Assert.NotNull(x.ClearedAt));
    }

    [Theory]
    [InlineData(ExportBatchState.Cancelled)]
    [InlineData(ExportBatchState.Failed)]
    public async Task LuotHuyHoacLoi_KhongGiuAnh_XoaLuotXongThiAnhBiXoa(string otherState)
    {
        await _store.CreateBatchAsync("A", "shop", "", null);
        await _store.CreateBatchAsync("B", "shop", "", null);
        await _store.EnqueueForBatchAsync("A", "shop", 1, ["chung"]);
        await _store.EnqueueForBatchAsync("B", "shop", 1, ["chung", "rieng-b"]);
        Verify("shop", "chung");
        Verify("shop", "rieng-b");
        await _store.FinishBatchAsync("A", ExportBatchState.Done, 1, 1, null);
        await _store.FinishBatchAsync("B", otherState, 0, 0, "x");

        var a = await _store.ClearBatchAsync("A", _hub);

        Assert.Equal(new BatchClearResult(1, 0, 0, 0), a);
        Assert.Equal(["chung.jpg"], _world.Deleted);
        Assert.Null(_store.Inspect("shop", "chung"));

        var b = await _store.ClearBatchAsync("B", _hub);   // lượt huỷ / lỗi vẫn xoá được ảnh của chính nó

        Assert.Equal(new BatchClearResult(1, 0, 0, 0), b);
        Assert.Equal(["chung.jpg", "rieng-b.jpg"], _world.Deleted);
    }

    [Fact]
    public async Task LuotXongChuaXoa_VanGiuAnh()
    {
        await _store.CreateBatchAsync("A", "shop", "", null);
        await _store.CreateBatchAsync("B", "shop", "", null);
        await _store.EnqueueForBatchAsync("A", "shop", 1, ["chung"]);
        await _store.EnqueueForBatchAsync("B", "shop", 2, ["chung"]);
        Verify("shop", "chung");
        await _store.FinishBatchAsync("A", ExportBatchState.Done, 1, 1, null);
        await _store.FinishBatchAsync("B", ExportBatchState.Done, 1, 1, null);

        Assert.Equal(new BatchClearResult(0, 1, 0, 0), await _store.ClearBatchAsync("A", _hub));
        Assert.Empty(_world.Deleted);
        Assert.Equal("verified", _store.Inspect("shop", "chung")!.Value.State);
    }

    [Fact]
    public async Task XoaSachKhoAnhGoc_DanhDauLuotCoLinkHub()
    {
        await _store.CreateBatchAsync("hub", "shop", "", null);
        await _store.CreateBatchAsync("cdn", "shop", "", null);
        await _store.FinishBatchAsync("hub", ExportBatchState.Done, 1, 1, null, hubImages: 5, cdnImages: 1);
        await _store.FinishBatchAsync("cdn", ExportBatchState.Done, 1, 1, null, cdnImages: 4);

        Assert.Equal(1, await _store.CountBatchesUsingHubSourcesAsync());
        Assert.Equal(1, await _store.MarkHubSourcesDeletedAsync(T0.AddHours(3)));

        var list = await _store.ListBatchesAsync();
        Assert.Equal(T0.AddHours(3), list.Single(b => b.BatchId == "hub").HubSourcesDeletedAt);
        Assert.Null(list.Single(b => b.BatchId == "cdn").HubSourcesDeletedAt);
        Assert.Equal(0, await _store.CountBatchesUsingHubSourcesAsync());
    }

    [Fact]
    public async Task LuotKhacShop_CungKhoa_KhongTinhLaDungChung()
    {
        await _store.CreateBatchAsync("A", "shop-1", "", null);
        await _store.CreateBatchAsync("B", "shop-2", "", null);
        await _store.EnqueueForBatchAsync("A", "shop-1", 1, ["x"]);
        await _store.EnqueueForBatchAsync("B", "shop-2", 1, ["x"]);
        Verify("shop-1", "x");
        _world.Stored.Clear();
        Verify("shop-2", "x");

        var a = await _store.ClearBatchAsync("A", _hub);

        Assert.Equal(1, a.Deleted);
        Assert.Null(_store.Inspect("shop-1", "x"));
        Assert.Equal("verified", _store.Inspect("shop-2", "x")!.Value.State);
    }

    [Fact]
    public async Task Xoa_Hub404_TinhLaDaXoa_PendingFailedKhongGoiHub_BangSizeKhongBaoGioXoa()
    {
        var sizePath = Path.Combine(_dir, "size.jpeg");
        await File.WriteAllBytesAsync(sizePath, ImageTestData.Jpeg);
        var size = SizeChartImage.FromFile(sizePath);
        await _store.CreateBatchAsync("A", "shop", "", null);
        await _store.EnqueueSizeChartForBatchAsync("A", "shop", size);
        await _store.EnqueueForBatchAsync("A", "shop", 1, ["mat-tren-hub", "cho", "loi"]);
        Verify("shop", "mat-tren-hub");
        _world.Stored.Remove("mat-tren-hub.jpg");
        _world.DeleteOverride = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        _store.MarkFailed(new ImagePrepEntry("shop", "loi", ImagePrepKind.Product, 0), "404");
        var sizeRow = _store.Inspect("shop", size.Key)!.Value;

        var r = await _store.ClearBatchAsync("A", _hub);

        Assert.Equal(new BatchClearResult(3, 0, 1, 0), r);
        Assert.Equal(["mat-tren-hub.jpg"], _world.Deleted);   // pending / failed chưa có gì trên Hub → không gọi
        Assert.Null(_store.Inspect("shop", "cho"));
        Assert.Null(_store.Inspect("shop", "loi"));
        Assert.Equal(sizeRow, _store.Inspect("shop", size.Key)!.Value);
        Assert.Equal(4, (await _store.ListBatchesAsync()).Single().Images);
    }

    [Fact]
    public async Task XoaHut_GiuDeleting_LuotChuaXoaXong_BamLaiThiXong()
    {
        await _store.CreateBatchAsync("A", "shop", "", null);
        await _store.CreateBatchAsync("B", "shop", "", null);
        await _store.EnqueueForBatchAsync("A", "shop", 1, ["x"]);
        await _store.EnqueueForBatchAsync("B", "shop", 1, ["x"]);
        Verify("shop", "x");
        await _store.ClearBatchAsync("B", _hub);   // giữ vì A còn dùng
        _world.DeleteOverride = _ => new HttpResponseMessage(HttpStatusCode.BadGateway);

        var first = await _store.ClearBatchAsync("A", _hub);

        Assert.Equal(new BatchClearResult(0, 0, 0, 1), first);
        Assert.False(first.Complete);
        Assert.Equal("deleting", _store.Inspect("shop", "x")!.Value.State);
        Assert.Null((await _store.ListBatchesAsync()).Single(b => b.BatchId == "A").ClearedAt);

        _world.DeleteOverride = null;
        var second = await _store.ClearBatchAsync("A", _hub);

        Assert.Equal(new BatchClearResult(1, 0, 0, 0), second);
        Assert.Null(_store.Inspect("shop", "x"));
        Assert.NotNull((await _store.ListBatchesAsync()).Single(b => b.BatchId == "A").ClearedAt);
    }

    [Fact]
    public async Task EnqueueForBatch_LuotChuaTao_DaXoa_KhacShop_ThiNem_KhongGhiGi()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.EnqueueForBatchAsync("khong-co", "shop", 1, ["a"]));
        await _store.CreateBatchAsync("A", "shop", "", null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.EnqueueForBatchAsync("A", "shop-khac", 1, ["a"]));
        await _store.ClearBatchAsync("A", _hub);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.EnqueueForBatchAsync("A", "shop", 1, ["a"]));
        Assert.Null(_store.Inspect("shop", "a"));
        Assert.Equal(0, Scalar("SELECT count(*) FROM image_ref;"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.ClearBatchAsync("khong-co", _hub));
    }

    [Fact]
    public async Task ListBatches_MoiNhatTruoc_FinishVaMarkInterrupted()
    {
        await _store.CreateBatchAsync("A", "shop", "Shop A", @"D:\ra");
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _store.CreateBatchAsync("B", "shop", "Shop B", null);
        await _store.EnqueueForBatchAsync("A", "shop", 1, ["a", "b"]);
        await _store.FinishBatchAsync("A", ExportBatchState.Done, 3, 2, null);

        Assert.Equal(1, await _store.MarkInterruptedBatchesAsync());

        var list = await _store.ListBatchesAsync();
        Assert.Equal(["B", "A"], list.Select(b => b.BatchId));
        Assert.Equal(("Shop A", ExportBatchState.Done, 3, 2, @"D:\ra", 2), (list[1].ShopLabel, list[1].State, list[1].Products,
            list[1].Files, list[1].OutputDir, list[1].Images));
        Assert.Equal(T0, list[1].CreatedAt);
        Assert.Equal(ExportBatchState.Failed, list[0].State);
        Assert.NotNull(list[0].Note);
    }

    [Fact]
    public async Task Migration_DbV1_LenV2_GiuDuLieu()
    {
        using (var con = Raw())
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = $"""
                CREATE TABLE image_prep (shop_key TEXT NOT NULL, source_key TEXT NOT NULL, kind TEXT NOT NULL DEFAULT 'product',
                    state TEXT NOT NULL DEFAULT 'pending', md5 TEXT, content_type TEXT, source_path TEXT, hub_path TEXT, url TEXT,
                    expires_at INTEGER, attempts INTEGER NOT NULL DEFAULT 0, next_attempt_at INTEGER NOT NULL DEFAULT 0, last_error TEXT,
                    updated_at INTEGER NOT NULL, PRIMARY KEY (shop_key, source_key));
                CREATE TABLE image_ref (shop_key TEXT NOT NULL, source_key TEXT NOT NULL, item_id INTEGER NOT NULL,
                    PRIMARY KEY (shop_key, source_key, item_id));
                INSERT INTO image_prep (shop_key, source_key, state, hub_path, url, expires_at, updated_at)
                VALUES ('shop', 'cu', 'verified', '/api/media/cu.jpg', 'https://media.example.com/api/media/cu.jpg', {T0.AddDays(7).ToUnixTimeMilliseconds()}, 1);
                INSERT INTO image_ref VALUES ('shop', 'cu', 9);
                PRAGMA user_version = 1;
                """;
            cmd.ExecuteNonQuery();
        }

        Assert.Equal("https://media.example.com/api/media/cu.jpg", (await _store.GetResolverAsync("shop", ["cu"]))("cu").Url);
        Assert.Equal((long)UploadStore.SchemaVersion, Scalar("PRAGMA user_version;"));
        Assert.Equal(1, _store.RefCount("shop", "cu"));
        await _store.CreateBatchAsync("A", "shop", "", null);
        await _store.EnqueueForBatchAsync("A", "shop", 9, ["cu"]);
        Assert.Equal(1, (await _store.ListBatchesAsync()).Single().Images);

        // Mở lại lần nữa: không migrate lại, không mất gì.
        var reopened = new UploadStore(DbPath, () => _clock.Now);
        Assert.Single(await reopened.ListBatchesAsync());
        Assert.Equal((long)UploadStore.SchemaVersion, Scalar("PRAGMA user_version;"));
        Assert.Equal(5, UploadStore.SchemaVersion);
    }
}
