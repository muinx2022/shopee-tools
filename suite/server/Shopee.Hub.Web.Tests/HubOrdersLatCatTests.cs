using Microsoft.Data.Sqlite;
using Shopee.Core.Coordination;
using Shopee.Hub;
using Shopee.Hub.Web.Components;

namespace Shopee.Hub.Web.Tests;

/// <summary>
/// Tầng DB của gương lát cắt (KM/Combo/Flash/Mở-list-shop) — plan
/// <c>plans/2026-09-04-guong-mang-lat-cat-dang-chay.md</c> mục 3 bước 4, tiêu chí N4/N6: upsert/đọc cột
/// <c>lat_cat</c>, migration DB cũ, và đếm "Running" của máy không đếm đúp dòng vừa có phiên vừa có lát cắt.
/// </summary>
public sealed class HubOrdersLatCatTests : IDisposable
{
    private const string MachineId = "m1";
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "hub-latcat-test-" + Guid.NewGuid().ToString("N"));

    private static OrdersAccountsPushRequest Push(params OrdersAccountItem[] accounts) =>
        new(MachineId, "PC-01", accounts.ToList());

    private static OrdersAccountItem Item(string login, string sessionState = "", string? latCat = null) =>
        new(login, sessionState, [], false, null) { LatCat = latCat };

    // ===== 1. Upsert có LatCat → đọc lại đúng; không có → null =====
    [Fact]
    public void Upsert_CoLatCat_DocLaiDung()
    {
        using var db = new HubDatabase(_dataDir);
        db.UpsertOrdersAccounts(Push(Item("a@mail.com", latCat: "Flash Sale"), Item("b@mail.com")));

        var accs = db.OrdersAccountsOf(MachineId);

        Assert.Equal("Flash Sale", accs.Single(a => a.Login == "a@mail.com").LatCat);
        Assert.Null(accs.Single(a => a.Login == "b@mail.com").LatCat);
    }

    /// <summary><c>lat_cat</c> KHÔNG theo luật "rỗng giữ giá trị cũ" của 3 ô đăng nhập (<c>GiuLaiNeuRong</c>):
    /// đây là trạng thái SỐNG, lượt đẩy sau khi lát cắt đã xong PHẢI xoá được nhãn — giữ giá trị cũ ở đây là
    /// nhãn "Flash Sale" kẹt vĩnh viễn trên hub dù máy đã rảnh từ lâu.</summary>
    [Fact]
    public void LatCatXong_LuotDayKeTiepKhongMangTheo_ThiXoaNhan()
    {
        using var db = new HubDatabase(_dataDir);
        db.UpsertOrdersAccounts(Push(Item("a@mail.com", latCat: "Flash Sale")));
        Assert.Equal("Flash Sale", db.OrdersAccountsOf(MachineId).Single().LatCat);

        db.UpsertOrdersAccounts(Push(Item("a@mail.com")));   // lượt đẩy kế: lát cắt đã xong, LatCat = null

        Assert.Null(db.OrdersAccountsOf(MachineId).Single().LatCat);
    }

    /// <summary>
    /// N5(b) THẬT — tầng DB (phản biện 04/09/2026, VỪA): bài tương ứng ở <c>DispatchLatCatViewTests</c> chỉ đưa
    /// hai <c>List</c> rời, không đi qua đường Razor thật gọi
    /// <c>DispatchViewLogic.LatCatCuaMay(Db.OrdersAccountsOf(_oMach))</c> — tự khẳng định đúng bất kể lọc theo
    /// máy có hỏng hay không. Bài này đi trọn đường thật: hai máy, chỉ m1 có tài khoản đang giữ lát cắt.
    /// </summary>
    [Fact]
    public void LatCatCuaMay_QuaOrdersAccountsOfThat_MayKhacKhongThayNhanCuaMayKia()
    {
        using var db = new HubDatabase(_dataDir);
        db.UpsertOrdersAccounts(Push(Item("a@mail.com", latCat: "Flash Sale")));
        db.UpsertOrdersAccounts(new OrdersAccountsPushRequest("m2", "PC-02", [Item("c@mail.com")]));

        Assert.NotNull(DispatchViewLogic.LatCatCuaMay(db.OrdersAccountsOf(MachineId)));
        Assert.Null(DispatchViewLogic.LatCatCuaMay(db.OrdersAccountsOf("m2")));
    }

    // ===== 2. DB CŨ (chưa có cột lat_cat) → migration thêm cột, đọc được, upsert mới có LatCat =====
    [Fact]
    public void DbCu_ThieuCotLatCat_MigrationThemVaVanUpsertDuoc()
    {
        Directory.CreateDirectory(_dataDir);
        using (var conn = OpenHubDb())
        using (var c = conn.CreateCommand())
        {
            // Bảng orders_accounts kiểu bản CŨ (trước 04/09/2026): chưa có cột lat_cat.
            c.CommandText = @"
CREATE TABLE orders_accounts(
  machine_id TEXT NOT NULL, login TEXT NOT NULL, session_state TEXT DEFAULT '',
  verify_failed INTEGER DEFAULT 0, last_sync_at TEXT DEFAULT '', updated_at TEXT,
  password TEXT DEFAULT '', verify_email TEXT DEFAULT '', verify_email_password TEXT DEFAULT '',
  PRIMARY KEY(machine_id, login));
INSERT INTO orders_accounts(machine_id,login,session_state,updated_at)
VALUES('m1','old@mail.com','','2026-08-06T00:00:00.0000000+00:00');";
            c.ExecuteNonQuery();
        }

        using var db = new HubDatabase(_dataDir);   // = hub khởi động lại trên DB cũ → MigrateSchema chạy

        var cu = Assert.Single(db.OrdersAccountsOf(MachineId));
        Assert.Equal("old@mail.com", cu.Login);
        Assert.Null(cu.LatCat);

        // Và vẫn upsert được dòng MỚI có LatCat trên chính DB vừa migrate.
        db.UpsertOrdersAccounts(Push(Item("new@mail.com", latCat: "Combo Khuyến Mãi")));
        Assert.Equal("Combo Khuyến Mãi",
            db.OrdersAccountsOf(MachineId).Single(a => a.Login == "new@mail.com").LatCat);
    }

    // ===== 3. OrdersMirrorCounts: LatCat cộng vào Running, không đếm đúp khi có cả hai =====

    /// <summary>Trước bản vá, "Running" chỉ đếm <c>session_state&lt;&gt;''</c> — máy đang giữ Flash Sale có
    /// <c>session_state=''</c> nên biến mất khỏi số đếm đúng lúc người vận hành cần thấy nó.</summary>
    [Fact]
    public void MirrorCounts_ChiCoLatCat_VanDemLaRunning()
    {
        using var db = new HubDatabase(_dataDir);
        db.UpsertOrdersAccounts(Push(Item("a@mail.com", latCat: "Flash Sale")));

        var counts = db.OrdersMirrorCounts()[MachineId];

        Assert.Equal(1, counts.Accounts);
        Assert.Equal(1, counts.Running);
    }

    /// <summary>Một dòng có CẢ session_state khác rỗng LẪN lat_cat khác rỗng vẫn chỉ đếm MỘT — mệnh đề OR trong
    /// một CASE WHEN, không phải hai SUM cộng lại.</summary>
    [Fact]
    public void MirrorCounts_CoCaHaiTruong_ChiDemMotKhongDemDup()
    {
        using var db = new HubDatabase(_dataDir);
        db.UpsertOrdersAccounts(Push(Item("a@mail.com", OrdersSessionStates.Running, "Flash Sale")));

        var counts = db.OrdersMirrorCounts()[MachineId];

        Assert.Equal(1, counts.Accounts);
        Assert.Equal(1, counts.Running);
    }

    [Fact]
    public void MirrorCounts_KhongCoGi_KhongDem()
    {
        using var db = new HubDatabase(_dataDir);
        db.UpsertOrdersAccounts(Push(Item("a@mail.com")));

        var counts = db.OrdersMirrorCounts()[MachineId];

        Assert.Equal(1, counts.Accounts);
        Assert.Equal(0, counts.Running);
    }

    // ===== 4. OrdersRunningAccounts: KPI toàn hệ thống thấy được dòng chỉ có LatCat =====
    [Fact]
    public void RunningAccounts_ChiCoLatCat_VanXuatHienChoKpi()
    {
        using var db = new HubDatabase(_dataDir);
        db.UpsertOrdersAccounts(Push(Item("a@mail.com", latCat: "Flash Sale"), Item("b@mail.com")));

        var running = db.OrdersRunningAccounts();

        var row = Assert.Single(running);
        Assert.Equal("a@mail.com", row.Login);
        Assert.Equal("Flash Sale", row.LatCat);
    }

    private SqliteConnection OpenHubDb()
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_dataDir, "hub.db"),
        }.ToString());
        conn.Open();
        return conn;
    }

    public void Dispose()
    {
        try { Directory.Delete(_dataDir, recursive: true); } catch { /* thư mục temp — kệ */ }
    }
}
