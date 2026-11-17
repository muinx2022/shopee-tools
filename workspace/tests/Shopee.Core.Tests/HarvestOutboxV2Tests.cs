using Microsoft.Data.Sqlite;
using Shopee.Core.Harvest;
using static Shopee.Core.Tests.HarvestTestData;

namespace Shopee.Core.Tests;

/// <summary>
/// Outbox schema v2 (phản biện vòng 12): khoá pending theo SP — SP khác lưu vào đúng số dòng không đè SP đang chờ; tập
/// <c>done</c> chỉ ghi khi pending giữ một bản của chính SP; migration v1 → v2 giữ dữ liệu / id / version; tập <c>failed</c>.
/// Schema v4 (GĐ6): <c>done</c> / <c>failed</c> TOÀN CỤC theo khoá SP (không theo tk BigSeller × sheet); migration v3 → v4 gộp.
/// </summary>
public sealed class HarvestOutboxV2Tests : IDisposable
{
    private readonly string _dir = NewTempDir();
    private string DbPath => Path.Combine(_dir, "outbox.db");

    public void Dispose() => TryDelete(_dir);

    private static HarvestedItem Sp(long itemId, int row, DateTimeOffset? at = null, bool recordDone = true, string sheet = "Shop1") =>
        Item("", row, at, json: SyntheticJson(itemId)) with
        {
            SourceLink = $"https://shopee.vn/sp-i.77.{itemId}", ShopeeShopId = 77, RecordDone = recordDone, Sheet = sheet,
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
        Assert.Equal(new HashSet<string> { "id:77.1001", "id:77.1002" }, box.DoneKeys());
        box.Complete(due.Select(e => (e, true)).ToList());   // worker: Hub nhận cả hai
        Assert.Equal(new HarvestOutboxStats(0, 0, 2, 2), box.GetStats());
    }

    [Fact]
    public async Task BanCuHonCuaCungSP_BiBoQua_VanTinhXong_VaGoKhoiTapLoi()
    {
        var box = new HarvestOutbox(DbPath);
        await box.RecordPermanentFailureAsync(["id:77.1001"], "notProduct", "lượt-1", CancellationToken.None);
        Assert.Single(box.FailedKeys(minRuns: 1));
        await box.SaveAsync(Sp(1001, 5, T0.AddMinutes(10)), CancellationToken.None);
        Assert.Empty(box.FailedKeys(minRuns: 1));                                    // lưu được → không còn là lỗi
        await box.SaveAsync(Sp(1001, 9, T0, sheet: "Shop2"), CancellationToken.None);   // bản cũ hơn ở sheet khác
        Assert.Contains("id:77.1001", box.DoneKeys());
        Assert.Equal(2, box.TakeDue(50, long.MaxValue).Count);                      // mỗi (account, sheet) một dòng nguồn
    }

    /// <summary>GĐ6: cùng một SP lưu ở sheet A (hoặc file khác) là "đã có" ở mọi sheet — tập đã xong không còn theo target.</summary>
    [Fact]
    public async Task TapDaXong_ToanCuc_KhongTheoSheet_RecordDoneFalseThiKhongGhi()
    {
        var box = new HarvestOutbox(DbPath);
        await box.SaveAsync(Sp(1001, 2, sheet: "Shop A"), CancellationToken.None);
        await box.SaveAsync(Sp(1002, 3, recordDone: false), CancellationToken.None);
        Assert.Equal(new HashSet<string> { "id:77.1001" }, box.DoneKeys());
    }

    /// <summary>Phản biện GĐ6: SP lưu outbox xong vào <c>done</c>, nhưng Hub TỪ CHỐI mục đó (pending <c>failed</c>) → Hub không có dữ
    /// liệu → KHÔNG được coi là đã có (lượt sau lấy lại). Mục chờ đẩy bình thường vẫn là đã có (không lấy trùng); "Đẩy lại mục lỗi"
    /// đưa về pending → lại là đã có; Hub nhận (xoá khỏi hàng đợi) → đã có.</summary>
    [Fact]
    public async Task HubTuChoi_KhongTinhDaCo_ChoDayBinhThuongVanTinh()
    {
        var box = new HarvestOutbox(DbPath);
        await box.SaveAsync(Sp(1001, 2), CancellationToken.None);
        await box.SaveAsync(Sp(1002, 3), CancellationToken.None);
        Assert.Equal(new HashSet<string> { "id:77.1001", "id:77.1002" }, box.DoneKeys());   // chưa đẩy kịp: vẫn là đã có

        var due = box.TakeDue(50, long.MaxValue);
        box.MarkFailed([(due.Single(e => e.Item.ItemId == 1001), "raw-error")]);            // Hub từ chối SP 1001
        box.Complete([(due.Single(e => e.Item.ItemId == 1002), true)]);                     // Hub nhận SP 1002
        Assert.Equal(new HashSet<string> { "id:77.1002" }, box.DoneKeys());

        // Cùng SP còn một bản chờ đẩy bình thường (sheet khác) → Hub sắp có → vẫn là đã có.
        await box.SaveAsync(Sp(1001, 9, sheet: "Shop2"), CancellationToken.None);
        Assert.Contains("id:77.1001", box.DoneKeys());
        box.Complete([(box.TakeDue(50, long.MaxValue).Single(), true)]);
        Assert.DoesNotContain("id:77.1001", box.DoneKeys());                                 // chỉ còn bản bị từ chối

        Assert.Equal(1, box.RequeueFailed());                                                // "Đẩy lại mục lỗi"
        Assert.Contains("id:77.1001", box.DoneKeys());
    }

    [Fact]
    public async Task TapDaXong_LocTheoMocLuu()
    {
        var box = new HarvestOutbox(DbPath);
        var truoc = DateTimeOffset.UtcNow.AddSeconds(-1);
        await box.SaveAsync(Sp(1001, 2), CancellationToken.None);
        Assert.Single(box.DoneKeys(truoc));
        Assert.Empty(box.DoneKeys(DateTimeOffset.UtcNow.AddMinutes(1)));   // lưu trước mốc → không còn tính là đã có
    }

    [Fact]
    public async Task TapLoi_ToanCuc_DaXongThiKhongGhi_XoaTheoKhoa()
    {
        var box = new HarvestOutbox(DbPath);
        await box.SaveAsync(Sp(1001, 2), CancellationToken.None);
        await box.RecordPermanentFailureAsync(["id:77.1001", "url:x/y", "url:z/w"], "notProduct", "lượt-1", CancellationToken.None);
        Assert.Equal(new HashSet<string> { "url:x/y", "url:z/w" }, box.FailedKeys(minRuns: 1));   // khoá đã xong không vào tập lỗi
        Assert.Equal(1, box.ClearFailed(["url:x/y", "url:khong-co"]));
        Assert.Equal(new HashSet<string> { "url:z/w" }, box.FailedKeys(minRuns: 1));
        Assert.Single(box.DoneKeys());                                                       // xoá tập lỗi không đụng tập xong
        Assert.Equal(1, box.ClearDone(["id:77.1001"]));
        Assert.Empty(box.DoneKeys());
    }

    /// <summary>Phản biện vòng 13: lỗi một lượt lẻ (vd tài khoản trục trặc lúc đó) KHÔNG được bỏ qua hẳn — chỉ khi thấy ở ≥ 2 LƯỢT
    /// khác nhau; cùng một lượt ghi nhiều lần chỉ tính một.</summary>
    [Fact]
    public async Task TapLoi_CanHaiLuotKhacNhau_CungLuotChiTinhMot()
    {
        var box = new HarvestOutbox(DbPath);
        await box.RecordPermanentFailureAsync(["url:a/1"], "notProduct", "lượt-1", CancellationToken.None);
        await box.RecordPermanentFailureAsync(["url:a/1"], "notProduct", "lượt-1", CancellationToken.None);
        Assert.Empty(box.FailedKeys());                                      // 1 lượt → vẫn lên lịch
        Assert.Single(box.FailedKeys(minRuns: 1));
        await box.RecordPermanentFailureAsync(["url:a/1"], "notProduct", "lượt-2", CancellationToken.None);
        Assert.Equal(new HashSet<string> { "url:a/1" }, box.FailedKeys());   // 2 lượt → bỏ qua
    }

    /// <summary>DB thật đã lên v2 (có bảng failed thiếu cột) → v3 thêm cột an toàn, khoá cũ coi là đã thấy 1 lượt → v4 gộp.</summary>
    [Fact]
    public async Task MigrationV2_ThemCotHits_GiuKhoaCu()
    {
        var box0 = new HarvestOutbox(DbPath);
        await box0.EnsureReadyAsync(CancellationToken.None);
        Exec("""
            DROP TABLE failed;
            DROP TABLE done;
            CREATE TABLE done (target TEXT NOT NULL, item_key TEXT NOT NULL, saved_at INTEGER NOT NULL,
                PRIMARY KEY (target, item_key)) WITHOUT ROWID;
            CREATE TABLE failed (target TEXT NOT NULL, item_key TEXT NOT NULL, reason TEXT NOT NULL DEFAULT '', at INTEGER NOT NULL,
                PRIMARY KEY (target, item_key)) WITHOUT ROWID;
            INSERT INTO failed (target, item_key, reason, at) VALUES ('acc-1|acc:acc-1:shared:s1', 'url:cu/1', 'notProduct', 1);
            PRAGMA user_version = 2;
            """);
        var box = new HarvestOutbox(DbPath);
        await box.EnsureReadyAsync(CancellationToken.None);
        Assert.Empty(box.FailedKeys());
        Assert.Single(box.FailedKeys(minRuns: 1));
        await box.RecordPermanentFailureAsync(["url:cu/1"], "notProduct", "lượt-moi", CancellationToken.None);
        Assert.Single(box.FailedKeys());
        Assert.Equal(4L, UserVersion());
    }

    // ── Migration v3 → v4: done / failed theo target → toàn cục ──

    private const string V3Schema = """
        CREATE TABLE pending (
            id INTEGER PRIMARY KEY AUTOINCREMENT, account_id TEXT NOT NULL, sheet TEXT NOT NULL, row_no INTEGER NOT NULL,
            item_id INTEGER NOT NULL, raw_hash TEXT NOT NULL, harvested_at INTEGER NOT NULL, json TEXT NOT NULL,
            item_dto TEXT NOT NULL, source TEXT NOT NULL DEFAULT '', status TEXT NOT NULL DEFAULT 'pending',
            attempts INTEGER NOT NULL DEFAULT 0, last_error TEXT, next_try_at INTEGER NOT NULL DEFAULT 0,
            created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL, version INTEGER NOT NULL DEFAULT 1,
            UNIQUE (account_id, sheet, item_id));
        CREATE INDEX ix_pending_due ON pending (status, next_try_at, id);
        CREATE INDEX ix_pending_row ON pending (account_id, sheet, row_no);
        CREATE TABLE counters (key TEXT PRIMARY KEY, value INTEGER NOT NULL);
        CREATE TABLE done (target TEXT NOT NULL, item_key TEXT NOT NULL, saved_at INTEGER NOT NULL,
            PRIMARY KEY (target, item_key)) WITHOUT ROWID;
        CREATE TABLE failed (target TEXT NOT NULL, item_key TEXT NOT NULL, reason TEXT NOT NULL DEFAULT '', at INTEGER NOT NULL,
            hits INTEGER NOT NULL DEFAULT 1, last_run_id TEXT NOT NULL DEFAULT '', PRIMARY KEY (target, item_key)) WITHOUT ROWID;
        CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE probe (at INTEGER NOT NULL);
        PRAGMA user_version = 3;
        """;

    /// <summary>DB thật của user đang ở v3 với tập done theo target cũ (tk BigSeller × khoá tiến độ): lên v4 phải GỘP các dòng thành
    /// toàn cục — cùng khoá ở 2 target còn một (mốc lưu mới nhất); tập lỗi gộp lấy hits lớn nhất (2 sheet cùng một lượt không thành
    /// "2 lượt"), lý do của bản mới nhất; khoá lỗi đã có trong done bị bỏ. Hàng đợi đẩy Hub + đếm + link mẫu giữ nguyên.</summary>
    [Fact]
    public async Task MigrationV3_GopDoneVaFailedThanhToanCuc_GiuHangDoi()
    {
        Exec(V3Schema + $"""
            INSERT INTO pending (id, account_id, sheet, row_no, item_id, raw_hash, harvested_at, json, item_dto, source,
                                 status, attempts, last_error, next_try_at, created_at, updated_at, version) VALUES
              {Row(7, 2, 500, 100, "pending", 3)};
            INSERT INTO counters (key, value) VALUES ('pushed', 9), ('applied', 8);
            INSERT INTO meta (key, value) VALUES ('sample_link', 'https://shopee.vn/product/77/500');
            INSERT INTO done (target, item_key, saved_at) VALUES
              ('acc-1|acc:acc-1:shared:s1', 'id:77.500', 100),
              ('acc-2|acc:acc-2:shared:s9', 'id:77.500', 300),
              ('acc-1|acc:acc-1:shared:s1', 'id:77.501', 50);
            INSERT INTO failed (target, item_key, reason, at, hits, last_run_id) VALUES
              ('acc-1|a', 'url:x/1', 'cu', 10, 1, 'r1'),
              ('acc-2|b', 'url:x/1', 'moi', 20, 1, 'r1'),
              ('acc-1|a', 'url:x/2', 'hai-luot', 10, 2, 'r2'),
              ('acc-2|b', 'url:x/2', 'mot-luot', 30, 1, 'r3'),
              ('acc-2|b', 'id:77.501', 'da-xong-roi', 40, 2, 'r4');
            """);

        var box = new HarvestOutbox(DbPath);
        await box.EnsureReadyAsync(CancellationToken.None);

        Assert.Equal(4L, UserVersion());
        Assert.Equal(new HashSet<string> { "id:77.500", "id:77.501" }, box.DoneKeys());
        Assert.Equal(new HashSet<string> { "id:77.500" }, box.DoneKeys(DateTimeOffset.FromUnixTimeMilliseconds(200)));   // mốc mới nhất
        Assert.Equal(new HashSet<string> { "url:x/1", "url:x/2" }, box.FailedKeys(minRuns: 1));
        Assert.Equal(new HashSet<string> { "url:x/2" }, box.FailedKeys());                         // hits = max, không cộng
        Assert.Equal("moi", Scalar("SELECT reason FROM failed WHERE item_key = 'url:x/1';"));      // lý do bản mới nhất
        Assert.Equal(0L, Scalar("SELECT count(*) FROM pragma_table_info('done') WHERE name = 'target';"));
        Assert.Equal(500L, Scalar("SELECT item_id FROM done WHERE item_key = 'id:77.500';"));   // itemId suy từ khoá id: cũ
        Assert.Equal(0L, Scalar("SELECT count(*) FROM pragma_table_info('failed') WHERE name = 'target';"));
        Assert.Equal(new HarvestOutboxStats(1, 0, 9, 8), box.GetStats());
        var e = Assert.Single(box.TakeDue(50, long.MaxValue));
        Assert.Equal((7L, 3L), (e.Id, e.Version));
        Assert.Equal("https://shopee.vn/product/77/500", box.LastSavedLink());

        // Sau migration ghi / đọc theo khoá toàn cục.
        await box.RecordPermanentFailureAsync(["url:x/1"], "notProduct", "r-moi", CancellationToken.None);
        Assert.Contains("url:x/1", box.FailedKeys());
        await box.SaveAsync(Sp(502, 4), CancellationToken.None);
        Assert.Contains("id:77.502", box.DoneKeys());
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
        await box.SaveAsync(Sp(1002, 3, recordDone: false) with { Url = "https://shopee.vn/product/77/1002" }, CancellationToken.None);
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

    private object? Scalar(string sql)
    {
        using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ToString());
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    private long UserVersion() => (long)Scalar("PRAGMA user_version;")!;

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
        Assert.Equal(new HashSet<string> { "id:77.500" }, box.DoneKeys());           // v1 → v4: tập done gộp thành toàn cục
        box.Complete([(e, true)]);                                                   // worker xoá đúng id + version
        Assert.Equal(0, box.GetStats().Pending);
        Assert.Equal(1, box.RequeueFailed());

        // Sau migration: khoá mới — SP khác cùng số dòng không đè.
        await box.SaveAsync(Item("", 3, json: SyntheticJson(700)), CancellationToken.None);
        Assert.Equal(2, box.GetStats().Pending);
        Assert.Equal(4L, UserVersion());
    }
}
