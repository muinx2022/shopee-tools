using Microsoft.Data.Sqlite;
using Shopee.Core.Harvest;
using static Shopee.Core.Tests.HarvestTestData;

namespace Shopee.Core.Tests;

/// <summary>
/// Outbox schema v2 (phản biện vòng 12): khoá pending theo SP — SP khác lưu vào đúng số dòng không đè SP đang chờ; tập
/// <c>done</c> chỉ ghi khi pending giữ một bản của chính SP; migration v1 → v2 giữ dữ liệu / id / version; tập <c>failed</c>.
/// </summary>
public sealed class HarvestOutboxV2Tests : IDisposable
{
    private readonly string _dir = NewTempDir();
    private string DbPath => Path.Combine(_dir, "outbox.db");

    public void Dispose() => TryDelete(_dir);

    private const string Target = "acc-1|acc:acc-1:shared:s1";

    private static HarvestedItem Sp(long itemId, int row, DateTimeOffset? at = null, string? target = Target) =>
        Item("", row, at, json: SyntheticJson(itemId)) with
        {
            SourceLink = $"https://shopee.vn/sp-i.77.{itemId}", ShopeeShopId = 77, DoneTarget = target,
        };

    /// <summary>Ca phản biện: workbook dời dòng khi Hub đang sập — A lưu ở dòng 5, rồi B cũng ở dòng 5. v1 để B đè A trong
    /// pending (A đã vào done → không bao giờ lên Hub). v2: cả hai còn, cả hai được gửi.</summary>
    [Fact]
    public async Task HaiSPCungDong_CaHaiConTrongPending_CaHaiLenHub()
    {
        var box = new HarvestOutbox(DbPath);
        await box.SaveAsync(Sp(1001, 5, T0), CancellationToken.None);
        await box.SaveAsync(Sp(1002, 5, T0.AddMinutes(1)), CancellationToken.None);

        var due = box.TakeDue(50, long.MaxValue);
        Assert.Equal([1001L, 1002L], due.Select(e => e.Item.ItemId).OrderBy(x => x));
        Assert.Equal(new HashSet<string> { "id:77.1001", "id:77.1002" }, box.DoneKeys(Target));
        box.Complete(due.Select(e => (e, true)).ToList());   // worker: Hub nhận cả hai
        Assert.Equal(new HarvestOutboxStats(0, 0, 2, 2), box.GetStats());
    }

    [Fact]
    public async Task BanCuHonCuaCungSP_BiBoQua_VanTinhXong_VaGoKhoiTapLoi()
    {
        var box = new HarvestOutbox(DbPath);
        await box.RecordPermanentFailureAsync(Target, ["id:77.1001"], "notProduct", "lượt-1", CancellationToken.None);
        Assert.Single(box.FailedKeys(Target, minRuns: 1));
        await box.SaveAsync(Sp(1001, 5, T0.AddMinutes(10)), CancellationToken.None);
        Assert.Empty(box.FailedKeys(Target, minRuns: 1));                         // lưu được → không còn là lỗi
        await box.SaveAsync(Sp(1001, 9, T0, target: "khac"), CancellationToken.None);   // bản cũ hơn: pending vẫn giữ bản mới của SP
        Assert.Contains("id:77.1001", box.DoneKeys("khac"));
        var e = Assert.Single(box.TakeDue(50, long.MaxValue));
        Assert.Equal(5, e.Item.RowNo);
    }

    [Fact]
    public async Task TapLoi_GhiTheoTarget_DaXongThiKhongGhi_XoaDungTarget()
    {
        var box = new HarvestOutbox(DbPath);
        await box.SaveAsync(Sp(1001, 2), CancellationToken.None);
        await box.RecordPermanentFailureAsync(Target, ["id:77.1001", "url:x/y"], "notProduct", "lượt-1", CancellationToken.None);
        await box.RecordPermanentFailureAsync("t2", ["url:x/y"], "notProduct", "lượt-1", CancellationToken.None);
        Assert.Equal(new HashSet<string> { "url:x/y" }, box.FailedKeys(Target, minRuns: 1));   // khoá đã xong không vào tập lỗi
        Assert.Equal(1, box.ClearFailed(Target));
        Assert.Empty(box.FailedKeys(Target, minRuns: 1));
        Assert.Single(box.FailedKeys("t2", minRuns: 1));
        Assert.Single(box.DoneKeys(Target));                                       // xoá tập lỗi không đụng tập xong
    }

    /// <summary>Phản biện vòng 13: lỗi một lượt lẻ (vd tài khoản trục trặc lúc đó) KHÔNG được bỏ qua hẳn — chỉ khi thấy ở ≥ 2 LƯỢT
    /// khác nhau; cùng một lượt ghi nhiều lần chỉ tính một.</summary>
    [Fact]
    public async Task TapLoi_CanHaiLuotKhacNhau_CungLuotChiTinhMot()
    {
        var box = new HarvestOutbox(DbPath);
        await box.RecordPermanentFailureAsync(Target, ["url:a/1"], "notProduct", "lượt-1", CancellationToken.None);
        await box.RecordPermanentFailureAsync(Target, ["url:a/1"], "notProduct", "lượt-1", CancellationToken.None);
        Assert.Empty(box.FailedKeys(Target));                                      // 1 lượt → vẫn lên lịch
        Assert.Single(box.FailedKeys(Target, minRuns: 1));
        await box.RecordPermanentFailureAsync(Target, ["url:a/1"], "notProduct", "lượt-2", CancellationToken.None);
        Assert.Equal(new HashSet<string> { "url:a/1" }, box.FailedKeys(Target));   // 2 lượt → bỏ qua
    }

    /// <summary>DB thật đã lên v2 (có bảng failed thiếu cột) → v3 thêm cột an toàn, khoá cũ coi là đã thấy 1 lượt.</summary>
    [Fact]
    public async Task MigrationV2_ThemCotHits_GiuKhoaCu()
    {
        var box0 = new HarvestOutbox(DbPath);
        await box0.EnsureReadyAsync(CancellationToken.None);
        Exec("""
            DROP TABLE failed;
            CREATE TABLE failed (target TEXT NOT NULL, item_key TEXT NOT NULL, reason TEXT NOT NULL DEFAULT '', at INTEGER NOT NULL,
                PRIMARY KEY (target, item_key)) WITHOUT ROWID;
            INSERT INTO failed (target, item_key, reason, at) VALUES ('acc-1|acc:acc-1:shared:s1', 'url:cu/1', 'notProduct', 1);
            PRAGMA user_version = 2;
            """);
        var box = new HarvestOutbox(DbPath);
        await box.EnsureReadyAsync(CancellationToken.None);
        Assert.Empty(box.FailedKeys(Target));
        Assert.Single(box.FailedKeys(Target, minRuns: 1));
        await box.RecordPermanentFailureAsync(Target, ["url:cu/1"], "notProduct", "lượt-moi", CancellationToken.None);
        Assert.Single(box.FailedKeys(Target));
    }

    /// <summary>Migration v1 gộp trùng: bản đang chờ đẩy (pending) thắng bản Hub đã từ chối (failed) dù failed mới hơn.</summary>
    [Fact]
    public async Task MigrationV1_GopTrung_UuTienBanPending()
    {
        Exec(V1Schema + $"""
            INSERT INTO pending (id, account_id, sheet, row_no, item_id, raw_hash, harvested_at, json, item_dto, source,
                                 status, attempts, last_error, next_try_at, created_at, updated_at, version) VALUES
              {Row(1, 2, 500, 100, "pending", 1)},
              {Row(2, 7, 500, 900, "failed", 3, attempts: 2)};
            """);
        var box = new HarvestOutbox(DbPath);
        await box.EnsureReadyAsync(CancellationToken.None);
        Assert.Equal(new HarvestOutboxStats(1, 0, 0, 0), box.GetStats());
        Assert.Equal(1L, Assert.Single(box.TakeDue(50, long.MaxValue)).Id);
    }

    [Fact]
    public async Task LinkMau_LaLinkDaLamSachCuaSPLuuGanNhat()
    {
        var box = new HarvestOutbox(DbPath);
        Assert.Null(box.LastSavedLink());
        await box.SaveAsync(Sp(1001, 2), CancellationToken.None);
        await box.SaveAsync(Sp(1002, 3, target: null) with { Url = "https://shopee.vn/product/77/1002" }, CancellationToken.None);
        Assert.Equal("https://shopee.vn/product/77/1002", box.LastSavedLink());
    }

    // ── Migration v1 → v2 ──

    private const string V1Schema = """
        CREATE TABLE pending (
            id INTEGER PRIMARY KEY AUTOINCREMENT, account_id TEXT NOT NULL, sheet TEXT NOT NULL, row_no INTEGER NOT NULL,
            item_id INTEGER NOT NULL, raw_hash TEXT NOT NULL, harvested_at INTEGER NOT NULL, json TEXT NOT NULL,
            item_dto TEXT NOT NULL, source TEXT NOT NULL DEFAULT '', status TEXT NOT NULL DEFAULT 'pending',
            attempts INTEGER NOT NULL DEFAULT 0, last_error TEXT, next_try_at INTEGER NOT NULL DEFAULT 0,
            created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL, version INTEGER NOT NULL DEFAULT 1,
            UNIQUE (account_id, sheet, row_no));
        CREATE INDEX ix_pending_due ON pending (status, next_try_at, id);
        CREATE TABLE counters (key TEXT PRIMARY KEY, value INTEGER NOT NULL);
        CREATE TABLE done (target TEXT NOT NULL, item_key TEXT NOT NULL, saved_at INTEGER NOT NULL,
            PRIMARY KEY (target, item_key)) WITHOUT ROWID;
        CREATE TABLE probe (at INTEGER NOT NULL);
        PRAGMA user_version = 1;
        """;

    private void Exec(string sql)
    {
        using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ToString());
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string Row(long id, int row, long item, long at, string status, long version, int attempts = 0) =>
        $"({id}, 'acc-1', 'Shop1', {row}, {item}, 'h{id}', {at}, '{SyntheticJson(item).Replace("'", "''")}', "
        + $"'{{\"itemId\":{item},\"shopId\":77,\"rowNo\":{row},\"sheet\":\"Shop1\",\"accountId\":\"acc-1\",\"rawHash\":\"h{id}\",\"harvestedAt\":\"2026-11-16T01:00:00+00:00\"}}', "
        + $"'hook', '{status}', {attempts}, NULL, 0, 1, 1, {version})";

    [Fact]
    public async Task MigrationV1_GopTrungTheoSP_GiuIdVersionTrangThai_DemGiuNguyen()
    {
        Exec(V1Schema + $"""
            INSERT INTO pending (id, account_id, sheet, row_no, item_id, raw_hash, harvested_at, json, item_dto, source,
                                 status, attempts, last_error, next_try_at, created_at, updated_at, version) VALUES
              {Row(1, 2, 500, 100, "pending", 1)},
              {Row(2, 7, 500, 300, "pending", 4)},
              {Row(3, 3, 600, 100, "failed", 2, attempts: 3)};
            INSERT INTO counters (key, value) VALUES ('pushed', 9), ('applied', 8);
            INSERT INTO done (target, item_key, saved_at) VALUES ('t', 'id:77.500', 1);
            """);

        var box = new HarvestOutbox(DbPath);
        await box.EnsureReadyAsync(CancellationToken.None);

        Assert.Equal(new HarvestOutboxStats(1, 1, 9, 8), box.GetStats());           // trùng SP 500 → giữ 1, đếm giữ nguyên
        var e = Assert.Single(box.TakeDue(50, long.MaxValue));
        Assert.Equal((2L, 4L, 500L, 7), (e.Id, e.Version, e.Item.ItemId, e.Item.RowNo!.Value));   // bản mới nhất, id/version giữ
        Assert.Equal(("failed", 3), (box.Inspect("acc-1", "Shop1", 3)!.Value.Status, box.Inspect("acc-1", "Shop1", 3)!.Value.Attempts));
        Assert.Single(box.DoneKeys("t"));
        box.Complete([(e, true)]);                                                   // worker xoá đúng id + version
        Assert.Equal(0, box.GetStats().Pending);
        Assert.Equal(1, box.RequeueFailed());

        // Sau migration: khoá mới — SP khác cùng số dòng không đè.
        await box.SaveAsync(Item("", 3, json: SyntheticJson(700)), CancellationToken.None);
        Assert.Equal(2, box.GetStats().Pending);
        using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ToString());
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        Assert.Equal(3L, (long)cmd.ExecuteScalar()!);
    }
}
