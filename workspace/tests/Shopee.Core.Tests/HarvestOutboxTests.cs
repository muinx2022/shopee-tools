using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Shopee.Core.Harvest;
using static Shopee.Core.Tests.HarvestTestData;

namespace Shopee.Core.Tests;

/// <summary>
/// GĐ4: <see cref="HarvestOutbox"/> trên file SQLite TẠM — idempotent theo (account, sheet, SP) (schema v2), bản cũ không đè
/// bản mới, SP khác cùng số dòng không đè nhau,
/// ghi song song nhiều cửa sổ + worker xoá cùng lúc, BUSY tự thử lại trong ngân sách, EnsureReady lỗi khi không ghi được.
/// </summary>
public sealed class HarvestOutboxTests : IDisposable
{
    private readonly string _dir = NewTempDir();
    private string DbPath => Path.Combine(_dir, "outbox.db");

    public void Dispose() => TryDelete(_dir);

    private const string F1 = "pdp-2tang-8model.json";
    private const string F2 = "pdp-video-1tang-11model-2capdm.json";

    [Fact]
    public async Task Save_LuuDtoDayDu_JsonSach_HashKhop()
    {
        var box = new HarvestOutbox(DbPath);
        await box.EnsureReadyAsync(CancellationToken.None);
        var item = Item(F1, 5);

        await box.SaveAsync(item, CancellationToken.None);

        var e = Assert.Single(box.TakeDue(50, long.MaxValue));
        var dto = e.Item;
        Assert.Equal(item.ItemId, dto.ItemId);
        Assert.Equal(1490438686, dto.ShopId);
        Assert.Equal(item.Url, dto.Url);
        Assert.Equal(item.SourceLink, dto.SourceUrl);
        Assert.Equal(("acc-1", "Shop1", 5), (dto.AccountId, dto.Sheet, dto.RowNo));
        Assert.Equal(item.HarvestedAt, dto.HarvestedAt);
        Assert.Equal((2, 8, 5, 2), (dto.TierCount, dto.ModelCount, dto.ImageUrls!.Length, dto.VideoUrls!.Length));
        Assert.Equal(HarvestPayloadCleaner.Clean(item.Json), dto.RawJson);
        Assert.Equal(HarvestPayloadCleaner.Hash(dto.RawJson!), dto.RawHash);
        Assert.DoesNotContain("delivery_address", dto.RawJson);
        Assert.Equal(new HarvestOutboxStats(1, 0, 0, 0), box.GetStats());
    }

    [Fact]
    public async Task Save_CungSPNhieuLan_Idempotent_MotDongMoiSheet()
    {
        var box = new HarvestOutbox(DbPath);
        var item = Item(F1, 5);

        for (var i = 0; i < 3; i++) await box.SaveAsync(item, CancellationToken.None);
        await box.SaveAsync(Item(F1, 6), CancellationToken.None);              // cùng SP, dòng khác → cùng mục, row_no = 6
        await box.SaveAsync(Item(F1, 5, sheet: "Shop2"), CancellationToken.None);   // sheet khác → mục riêng

        Assert.Equal(2, box.GetStats().Pending);
        var e = box.TakeDue(50, long.MaxValue).Single(x => x.Item.Sheet == "Shop1");
        Assert.Equal(item.ItemId, e.Item.ItemId);
        Assert.Equal(6, e.Item.RowNo);
    }

    [Fact]
    public async Task Save_BanCuKhongDeBanMoi_BanMoiHoacBangThiThay()
    {
        var box = new HarvestOutbox(DbPath);
        await box.SaveAsync(Item(F1, 5, at: T0.AddMinutes(10)), CancellationToken.None);

        await box.SaveAsync(Item(F1, 7, at: T0), CancellationToken.None);       // cũ hơn, cùng SP → bỏ qua (giữ dòng 5)
        var e = Assert.Single(box.TakeDue(50, long.MaxValue));
        Assert.Equal((5, 1L), (e.Item.RowNo!.Value, e.Version));

        await box.SaveAsync(Item(F1, 8, at: T0.AddMinutes(10)), CancellationToken.None);   // bằng → thay
        e = Assert.Single(box.TakeDue(50, long.MaxValue));
        Assert.Equal((8, 2L), (e.Item.RowNo!.Value, e.Version));

        await box.SaveAsync(Item(F1, 9, at: T0.AddMinutes(11)), CancellationToken.None);   // mới hơn → thay
        e = Assert.Single(box.TakeDue(50, long.MaxValue));
        Assert.Equal((9, 3L), (e.Item.RowNo!.Value, e.Version));
    }

    /// <summary>Bản mới lưu đè TRONG LÚC lô cũ đang bay: Hub báo ok cho bản cũ thì KHÔNG được xoá bản mới.</summary>
    [Fact]
    public async Task Complete_BanDaBiDeTrongLucBay_KhongXoaBanMoi()
    {
        var box = new HarvestOutbox(DbPath);
        await box.SaveAsync(Item(F1, 5, at: T0), CancellationToken.None);
        var inFlight = Assert.Single(box.TakeDue(50, long.MaxValue));

        await box.SaveAsync(Item(F1, 6, at: T0.AddMinutes(1)), CancellationToken.None);   // bản mới hơn của CÙNG SP
        box.Complete([(inFlight, true)]);
        box.MarkFailed([(inFlight, "x")]);

        var con = Assert.Single(box.TakeDue(50, long.MaxValue));
        Assert.Equal(ItemIdOf(Fixture(F1)), con.Item.ItemId);
        Assert.Equal(6, con.Item.RowNo);
        Assert.Equal(new HarvestOutboxStats(1, 0, 1, 1), box.GetStats());
    }

    [Fact]
    public async Task Failed_KhongLayRaGui_Requeue_VeHangCho()
    {
        var box = new HarvestOutbox(DbPath);
        await box.SaveAsync(Item(F1, 5), CancellationToken.None);
        var e = Assert.Single(box.TakeDue(50, long.MaxValue));

        box.MarkFailed([(e, "hub: db 22P05")]);
        Assert.Empty(box.TakeDue(50, long.MaxValue));
        Assert.Equal(new HarvestOutboxStats(0, 1, 0, 0), box.GetStats());
        Assert.Equal("hub: db 22P05", box.Inspect("acc-1", "Shop1", 5)!.Value.LastError);

        Assert.Equal(1, box.RequeueFailed());
        Assert.Single(box.TakeDue(50, long.MaxValue));
        Assert.Equal(new HarvestOutboxStats(1, 0, 0, 0), box.GetStats());
    }

    [Fact]
    public async Task Save_SauKhiFailed_BanMoiVePending()
    {
        var box = new HarvestOutbox(DbPath);
        await box.SaveAsync(Item(F1, 5, at: T0), CancellationToken.None);
        box.MarkFailed([(box.TakeDue(50, long.MaxValue)[0], "x")]);

        await box.SaveAsync(Item(F1, 5, at: T0.AddSeconds(1)), CancellationToken.None);

        var st = box.Inspect("acc-1", "Shop1", 5)!.Value;
        Assert.Equal(("pending", 0, (string?)null), (st.Status, st.Attempts, st.LastError));
    }

    [Fact]
    public async Task Defer_ChuaDenHanThiKhongLay_CuTruoc()
    {
        var box = new HarvestOutbox(DbPath);
        await box.SaveAsync(Item(F1, 1), CancellationToken.None);
        await box.SaveAsync(Item(F2, 2), CancellationToken.None);
        var all = box.TakeDue(50, 1000);
        Assert.Equal([1, 2], all.Select(e => e.Item.RowNo!.Value));

        box.Defer([all[0]], 5000, "mất mạng");

        Assert.Equal([2], box.TakeDue(50, 4999).Select(e => e.Item.RowNo!.Value));
        Assert.Equal([1, 2], box.TakeDue(50, 5000).Select(e => e.Item.RowNo!.Value));
        Assert.Equal(1, box.Inspect("acc-1", "Shop1", 1)!.Value.Attempts);
    }

    /// <summary>8 "cửa sổ Brave" ghi song song 25 dòng mỗi cửa sổ (kèm ghi lại trùng), cùng lúc một "worker" lấy/xoá liên
    /// tục: không ném, không mất dòng, không trùng.</summary>
    [Fact]
    public async Task SongSong_NhieuCuaSoGhi_WorkerXoa_KhongMatKhongTrung()
    {
        var box = new HarvestOutbox(DbPath);
        await box.EnsureReadyAsync(CancellationToken.None);
        var json = Fixture(F1);
        const int windows = 8, rows = 25;
        using var stop = new CancellationTokenSource();
        var deleted = 0;
        var pushedKeys = new System.Collections.Concurrent.ConcurrentBag<(string?, int?)>();

        var worker = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                var batch = box.TakeDue(7, long.MaxValue);
                box.Complete(batch.Select(e => (e, true)).ToList());
                foreach (var e in batch) pushedKeys.Add((e.Item.Sheet, e.Item.RowNo));
                Interlocked.Add(ref deleted, batch.Count);
                await Task.Yield();
            }
        });
        var writers = Enumerable.Range(0, windows).Select(w => Task.Run(async () =>
        {
            for (var r = 1; r <= rows; r++)
            {
                var spJson = SyntheticJson(10_000 + r);   // mỗi dòng một SP (khoá v2 theo SP)
                await box.SaveAsync(Item(F1, r, sheet: "S" + w, json: spJson), CancellationToken.None);
                if (r % 5 == 0) await box.SaveAsync(Item(F1, r, sheet: "S" + w, json: spJson), CancellationToken.None);
            }
        })).ToArray();

        await Task.WhenAll(writers);
        stop.Cancel();
        await worker;
        var rest = box.TakeDue(1000, long.MaxValue);
        var stats = box.GetStats();

        Assert.Equal(stats.PushedTotal, deleted);
        // Mỗi (sheet,row) có thể bị xoá rồi lưu lại (ghi lặp sau khi worker đã xoá) → tổng ≥ số dòng; còn lại không trùng khoá.
        var seen = pushedKeys.Concat(rest.Select(e => (e.Item.Sheet, e.Item.RowNo))).ToHashSet();
        for (var w = 0; w < windows; w++)
            for (var r = 1; r <= rows; r++)
                Assert.Contains(("S" + w, (int?)r), seen);   // mỗi dòng đã lưu: hoặc đã "đẩy", hoặc còn chờ — không mất
        Assert.Equal(rest.Count, rest.Select(e => (e.Item.Sheet, e.Item.RowNo)).Distinct().Count());
        Assert.Equal(rest.Count, stats.Pending);
    }

    /// <summary>Process KHÁC giữ khoá ghi: ngắn hơn ngân sách → lưu được sau khi nhả; dài hơn → ném (không treo mãi).</summary>
    [Fact]
    public async Task Busy_TuThuLaiTrongNganSach_QuaThiNem()
    {
        var box = new HarvestOutbox(DbPath, busyBudget: TimeSpan.FromSeconds(3));
        await box.EnsureReadyAsync(CancellationToken.None);

        async Task GiuKhoa(TimeSpan lau)
        {
            using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ToString());
            con.Open();
            using var tx = con.BeginTransaction();   // BEGIN IMMEDIATE: giữ khoá ghi
            using (var cmd = con.CreateCommand()) { cmd.Transaction = tx; cmd.CommandText = "INSERT INTO probe (at) VALUES (1);"; cmd.ExecuteNonQuery(); }
            await Task.Delay(lau);
            tx.Rollback();
        }

        var ngan = GiuKhoa(TimeSpan.FromMilliseconds(1500));
        await Task.Delay(100);
        await box.SaveAsync(Item(F1, 1), CancellationToken.None);
        await ngan;
        Assert.Equal(1, box.GetStats().Pending);

        var dai = GiuKhoa(TimeSpan.FromSeconds(7));
        await Task.Delay(100);
        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAsync<SqliteException>(() => box.SaveAsync(Item(F1, 2), CancellationToken.None));
        Assert.InRange(sw.Elapsed.TotalSeconds, 2.5, 6.5);
        await dai;
    }

    [Fact]
    public async Task EnsureReady_ThuMucKhongGhiDuoc_Nem()
    {
        var file = Path.Combine(_dir, "la-file");
        File.WriteAllText(file, "x");
        var box = new HarvestOutbox(Path.Combine(file, "outbox.db"));   // "thư mục" là một file → không tạo được

        await Assert.ThrowsAnyAsync<Exception>(() => box.EnsureReadyAsync(CancellationToken.None));
        await Assert.ThrowsAnyAsync<Exception>(() => box.SaveAsync(Item(F1, 1), CancellationToken.None));
    }

    [Fact]
    public async Task EnsureReady_DiaKhongDuCho_Nem()
    {
        var box = new HarvestOutbox(DbPath, minFreeBytes: long.MaxValue);
        var ex = await Assert.ThrowsAsync<IOException>(() => box.EnsureReadyAsync(CancellationToken.None));
        Assert.Contains("trống", ex.Message);
    }

    [Fact]
    public async Task Save_JsonHong_Nem_KhongLuu()
    {
        var box = new HarvestOutbox(DbPath);
        var item = Item(F1, 1) with { Json = "{\"error\":null,\"data\":null}" };

        await Assert.ThrowsAsync<HarvestItemRejectedException>(() => box.SaveAsync(item, CancellationToken.None));
        Assert.Equal(0, box.GetStats().Pending);
    }

    /// <summary>Link gốc dài quá MaxUrlLength: không gửi SourceUrl (Hub lấy Url sạch) thay vì để Hub từ chối CẢ SP; log 1 dòng.</summary>
    [Fact]
    public async Task Save_LinkGocQuaDai_BoSourceUrl_GhiLog()
    {
        var log = new List<string>();
        var box = new HarvestOutbox(DbPath, log: log.Add);
        var dai = "https://shopee.vn/x-i.1.2?q=" + new string('a', Coordination.HarvestedProductLimits.MaxUrlLength);
        await box.SaveAsync(Item(F1, 1) with { SourceLink = dai }, CancellationToken.None);
        var vua = "https://shopee.vn/x-i.1.2?q=" + new string('b', Coordination.HarvestedProductLimits.MaxUrlLength - 30);
        await box.SaveAsync(Item(F2, 2) with { SourceLink = vua }, CancellationToken.None);

        var all = box.TakeDue(50, long.MaxValue);
        var e1 = all.Single(e => e.Item.RowNo == 1).Item;
        Assert.Null(e1.SourceUrl);
        Assert.NotNull(e1.Url);
        Assert.Equal(vua, all.Single(e => e.Item.RowNo == 2).Item.SourceUrl);
        Assert.Contains(log, l => l.Contains("link gốc dài"));
        Assert.Single(log);
    }

    [Fact]
    public async Task Schema_UserVersion4_VersionLaThiEnsureReadyNem()
    {
        var box = new HarvestOutbox(DbPath);
        await box.EnsureReadyAsync(CancellationToken.None);
        Assert.Equal(4L, ReadUserVersion());

        using (var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ToString()))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "PRAGMA user_version = 99;";
            cmd.ExecuteNonQuery();
        }
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new HarvestOutbox(DbPath).EnsureReadyAsync(CancellationToken.None));
        Assert.Contains("v99", ex.Message);
        Assert.Equal(99L, ReadUserVersion());   // không bị hạ xuống
    }

    private long ReadUserVersion()
    {
        using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ToString());
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return (long)cmd.ExecuteScalar()!;
    }
}
