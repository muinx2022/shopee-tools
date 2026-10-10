using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Shopee.Core.Accounts;
using Shopee.Core.Infrastructure;

namespace Shopee.Workspace.Tests;

// ═══════════════════════════════════════════════════════════════════════════════
//  BuildLoginLine
// ═══════════════════════════════════════════════════════════════════════════════

public class BuildLoginLineTests
{
    // 1. Cookie rỗng → "user|pass"
    [Fact]
    public void EmptyCookie_ReturnsUserPass()
    {
        var line = ShopeeBackendDbFuncs.BuildLoginLine("alice", "p@ss", "");
        Assert.Equal("alice|p@ss", line);
    }

    // 2. Cookie bắt đầu ".shopee.vn=" → giữ nguyên
    [Fact]
    public void DomainCookie_ReturnsAsIs()
    {
        var line = ShopeeBackendDbFuncs.BuildLoginLine("alice", "p@ss", ".shopee.vn=SPC_F=abc123");
        Assert.Equal("alice|p@ss|.shopee.vn=SPC_F=abc123", line);
    }

    // 3. Cookie bắt đầu "SPC_F=" → bọc domain
    [Fact]
    public void SpcFCookie_WrapsWithDomain()
    {
        var line = ShopeeBackendDbFuncs.BuildLoginLine("alice", "p@ss", "SPC_F=xyz789");
        Assert.Equal("alice|p@ss|.shopee.vn=SPC_F=xyz789", line);
    }

    // 4. Cookie là value thô (không prefix) → bọc domain + SPC_F
    [Fact]
    public void RawValueCookie_WrapsWithDomainAndSpcF()
    {
        var line = ShopeeBackendDbFuncs.BuildLoginLine("alice", "p@ss", "rawvalue");
        Assert.Equal("alice|p@ss|.shopee.vn=SPC_F=rawvalue", line);
    }

    // 5. Trim user/pass/cookie
    [Fact]
    public void Trim_StripsWhitespace()
    {
        var line = ShopeeBackendDbFuncs.BuildLoginLine(" alice ", " p@ss ", "  SPC_F=trim  ");
        Assert.Equal("alice|p@ss|.shopee.vn=SPC_F=trim", line);
    }

    // 6. Cookie có parse được bằng ShopeeAuth.ParseLoginLine, SPC_F khớp
    [Fact]
    public void ValidCookie_ParseableWithCorrectSpcF()
    {
        var line = ShopeeBackendDbFuncs.BuildLoginLine("user1", "pass1", "SPC_F=token99");
        var parsed = ShopeeAuth.ParseLoginLine(line, ShopeeLoginLineOptions.Strict);
        Assert.True(parsed.Ok, $"ParseLoginLine failed: {parsed.Error}");
        Assert.Equal("token99", parsed.SpcF);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
//  SplitLoginLine
// ═══════════════════════════════════════════════════════════════════════════════

public class SplitLoginLineTests
{
    // 1. Cookie không đổi → trả lại originalCookie nguyên văn (dạng "SPC_F=x")
    [Fact]
    public void Unchanged_SpcFFormat_ReturnsOriginal()
    {
        var original = "SPC_F=mytoken";
        var line = ShopeeBackendDbFuncs.BuildLoginLine("alice", "pass", original);
        var (user, pass, cookie) = ShopeeBackendDbFuncs.SplitLoginLine(line, original);
        Assert.Equal("alice", user);
        Assert.Equal("pass", pass);
        Assert.Equal(original, cookie);   // nguyên văn, không chuẩn hoá
    }

    // 2. Cookie không đổi → trả lại originalCookie nguyên văn (chỉ giá trị, không prefix)
    [Fact]
    public void Unchanged_RawValue_ReturnsOriginal()
    {
        var original = "rawvalue123";
        var line = ShopeeBackendDbFuncs.BuildLoginLine("alice", "pass", original);
        var (_, _, cookie) = ShopeeBackendDbFuncs.SplitLoginLine(line, original);
        Assert.Equal(original, cookie);   // nguyên văn
    }

    // 3. Cookie mới → chuẩn hoá về "SPC_F=<value>"
    [Fact]
    public void NewCookie_NormalizesToSpcF()
    {
        var original = "SPC_F=oldtoken";
        // line chứa newtoken — khác original
        var line = "alice|pass|.shopee.vn=SPC_F=newtoken";
        var (_, _, cookie) = ShopeeBackendDbFuncs.SplitLoginLine(line, original);
        Assert.Equal("SPC_F=newtoken", cookie);
    }

    // 4. Không có phần cookie → ""
    [Fact]
    public void NoCookie_ReturnsEmpty()
    {
        var (_, _, cookie) = ShopeeBackendDbFuncs.SplitLoginLine("alice|pass", "SPC_F=old");
        Assert.Equal("", cookie);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
//  Diff
// ═══════════════════════════════════════════════════════════════════════════════

public class DiffTests
{
    private static DbAccountRow Row(string id, string user, string pass, string cookie) =>
        new() { Id = id, Username = user, Password = pass, Cookie = cookie, ProfileName = "prof", Status = "Ready" };

    private static ShopeeAccount Acc(string id, string user, string pass, string cookie) =>
        new()
        {
            Id = id,
            ShopeeAccountLogin = ShopeeBackendDbFuncs.BuildLoginLine(user, pass, cookie),
            Label = user, RequireProxy = true, OpenWithShopeeAccount = true,
            ProfileRelativePath = $"profiles/{id}",
        };

    // 1. Thêm tài khoản mới → Insert
    [Fact]
    public void NewAccount_GeneratesInsert()
    {
        var cmds = ShopeeBackendDbFuncs.Diff([], [Acc("id1", "u1", "p1", "")]);
        Assert.Single(cmds);
        Assert.Equal(DbCommandKind.Insert, cmds[0].Kind);
        Assert.Equal("id1", cmds[0].Row.Id);
        Assert.Equal("Ready", cmds[0].Row.Status);
    }

    // 2. Sửa password → Update
    [Fact]
    public void ChangedPassword_GeneratesUpdate()
    {
        var before = new[] { Row("id1", "u1", "oldpass", "") };
        var after = new[] { Acc("id1", "u1", "newpass", "") };
        var cmds = ShopeeBackendDbFuncs.Diff(before, after);
        Assert.Single(cmds);
        Assert.Equal(DbCommandKind.Update, cmds[0].Kind);
        // ProfileName và Status giữ nguyên từ dòng cũ
        Assert.Equal("prof", cmds[0].Row.ProfileName);
        Assert.Equal("Ready", cmds[0].Row.Status);
    }

    // 3. Xoá tài khoản → Delete
    [Fact]
    public void RemovedAccount_GeneratesDelete()
    {
        var before = new[] { Row("id1", "u1", "p1", "") };
        var cmds = ShopeeBackendDbFuncs.Diff(before, []);
        Assert.Single(cmds);
        Assert.Equal(DbCommandKind.Delete, cmds[0].Kind);
        Assert.Equal("id1", cmds[0].Row.Id);
    }

    [Fact]
    public void Merge_KeepsRowAddedAfterLoad_AndDoesNotRewritePipePassword()
    {
        var baseline = new[] { Row("id1", "u1", "pa|ss", "SPC_F=x") };
        var current = new[]
        {
            Row("id1", "u1", "pa|ss", "SPC_F=x"),
            Row("idx", "extra", "p", ""),
        };
        var acc = Acc("id1", "u1", "pa|ss", "SPC_F=x");
        var cmds = ShopeeBackendDbFuncs.Merge(baseline, current, [acc]);
        Assert.Empty(cmds);
    }

    [Fact]
    public void EmptyUsernameRow_IsNotDeleted()
    {
        var before = new[] { Row("id1", "u1", "p1", ""), Row("id3", "", "", "") };
        var cmds = ShopeeBackendDbFuncs.Diff(before, []);
        Assert.Single(cmds);
        Assert.Equal("id1", cmds[0].Row.Id);
    }

    // 4. Chỉ đổi trường overlay (vd Label) → 0 lệnh
    [Fact]
    public void OverlayOnlyChange_GeneratesNoDiff()
    {
        var before = new[] { Row("id1", "u1", "p1", "") };
        var afterAcc = Acc("id1", "u1", "p1", "");
        afterAcc.Label = "new-label";
        var cmds = ShopeeBackendDbFuncs.Diff(before, [afterAcc]);
        Assert.Empty(cmds);
    }

    // 5. Không bao giờ sinh Delete toàn bảng — chỉ Delete theo Id
    [Fact]
    public void AllDeleted_GeneratesIndividualDeletes()
    {
        var before = new[] { Row("id1", "u1", "p1", ""), Row("id2", "u2", "p2", "") };
        var cmds = ShopeeBackendDbFuncs.Diff(before, []);
        Assert.Equal(2, cmds.Count);
        Assert.All(cmds, c => Assert.Equal(DbCommandKind.Delete, c.Kind));
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
//  DiffProxies
// ═══════════════════════════════════════════════════════════════════════════════

public class DiffProxiesTests
{
    private static DbProxyRow Proxy(string id, string name, string address) =>
        new() { Id = id, Name = name, Address = address };

    // 1. Dòng "none" không bao giờ bị xoá/sửa
    [Fact]
    public void NoneRow_NeverTouched()
    {
        // none có Address khác rỗng để đảm bảo bộ lọc Address không che lỗi
        var before = new[] { Proxy("none", "Không proxy", "addr-of-none") };
        var cmds = ShopeeBackendDbFuncs.DiffProxies(before, ["key1"]);
        Assert.DoesNotContain(cmds, c => c.Row.Id == "none");
    }

    // 2. Dòng còn trong danh sách → giữ nguyên Name (không Update)
    [Fact]
    public void ExistingKey_Unchanged_GeneratesNothing()
    {
        var before = new[] { Proxy("g1", "Kiot …abcd", "keyabcd") };
        var cmds = ShopeeBackendDbFuncs.DiffProxies(before, ["keyabcd"]);
        Assert.Empty(cmds);
    }

    // 3. Key mới → Insert với Name="Kiot …<4 ký tự cuối>"
    [Fact]
    public void NewKey_GeneratesInsert()
    {
        var cmds = ShopeeBackendDbFuncs.DiffProxies([], ["newkey1234"]);
        Assert.Single(cmds);
        Assert.Equal(DbCommandKind.Insert, cmds[0].Kind);
        Assert.Equal("Kiot \u20261234", cmds[0].Row.Name);
        Assert.Equal("newkey1234", cmds[0].Row.Address);
    }

    // 4. Key mất → Delete
    [Fact]
    public void MissingKey_GeneratesDelete()
    {
        var before = new[] { Proxy("g1", "Kiot …abcd", "keyabcd") };
        var cmds = ShopeeBackendDbFuncs.DiffProxies(before, []);
        Assert.Single(cmds);
        Assert.Equal(DbCommandKind.Delete, cmds[0].Kind);
        Assert.Equal("g1", cmds[0].Row.Id);
    }

    // 5. Dòng Address rỗng không tính vào diff
    [Fact]
    public void EmptyAddressRow_IgnoredInDiff()
    {
        var before = new[]
        {
            Proxy("none", "Không proxy", ""),
            Proxy("g1", "KQ empty", ""),
        };
        var cmds = ShopeeBackendDbFuncs.DiffProxies(before, ["realkey"]);
        // chỉ Insert realkey, không Delete g1 (g1 không có Address nên không tham gia diff)
        Assert.Single(cmds);
        Assert.Equal(DbCommandKind.Insert, cmds[0].Kind);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
//  Tích hợp trên SQLite tạm
// ═══════════════════════════════════════════════════════════════════════════════

public class IntegrationDbTests : IDisposable
{
    // Schema đúng của ShopeeBackend (DataManager.cs)
    private const string CreateAccountsTable =
        "CREATE TABLE IF NOT EXISTS Accounts (Id TEXT PRIMARY KEY, Username TEXT, Password TEXT, Cookie TEXT, ProfileName TEXT, Status TEXT)";
    private const string CreateProxiesTable =
        "CREATE TABLE IF NOT EXISTS Proxies (Id TEXT PRIMARY KEY, Name TEXT, Address TEXT)";
    private const string CreateCategoriesTable =
        "CREATE TABLE IF NOT EXISTS Categories (Id TEXT PRIMARY KEY, Name TEXT)";

    private readonly string _tempDir;

    public IntegrationDbTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ShopeeWsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    /// <summary>Tạo DB mới tại path, tuỳ chọn có dữ liệu mẫu.</summary>
    private static void CreateTestDb(string dbPath, bool withData = false)
    {
        using var conn = OpenDb(dbPath);
        Exec(conn, CreateAccountsTable);
        Exec(conn, CreateProxiesTable);
        Exec(conn, CreateCategoriesTable);
        Exec(conn, "INSERT INTO Categories VALUES ('cat1', 'TestCategory')");

        if (withData)
        {
            Exec(conn, "INSERT INTO Accounts VALUES ('id1','user1','pass1','SPC_F=tok1','profile1','Ready')");
            Exec(conn, "INSERT INTO Accounts VALUES ('id2','user2','pass2','','profile2','Active')");
            Exec(conn, "INSERT INTO Accounts VALUES ('id3','','','','','')"); // Username rỗng
            Exec(conn, "INSERT INTO Proxies VALUES ('none','Không proxy','')");
            Exec(conn, "INSERT INTO Proxies VALUES ('g2','Kiot …pty','')");   // Address rỗng
            Exec(conn, "INSERT INTO Proxies VALUES ('g3','Kiot …1234','key1234')");
            Exec(conn, "INSERT INTO Proxies VALUES ('g4','Kiot …5678','key5678')");
        }
    }

    private static SqliteConnection OpenDb(string path)
    {
        var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        return conn;
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static long CountRows(string dbPath, string table)
    {
        SqliteConnection.ClearAllPools();
        using var conn = OpenDb(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)cmd.ExecuteScalar()!;
    }

    private static string HashDb(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        var bytes = File.ReadAllBytes(dbPath);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private string NewDb(string name) => Path.Combine(_tempDir, name + ".db");
    private string NewOverlay(string name) => Path.Combine(_tempDir, name + "-overlay.json");

    // ─── Test 1: Nạp với dữ liệu hỗn hợp ────────────────────────────────────

    [Fact]
    public void Load_WithMixedData_FiltersEmptyUsername()
    {
        var db = NewDb("t1"); var ov = NewOverlay("t1");
        CreateTestDb(db, withData: true);
        SqliteConnection.ClearAllPools();

        var store = new AccountStore(db, ov, () => false);
        Assert.Equal(2, store.Accounts.Count);  // id3 (Username rỗng) bị bỏ
    }

    [Fact]
    public void Load_ProxyKeys_FiltersNoneAndEmptyAddress()
    {
        var db = NewDb("t1p"); var ov = NewOverlay("t1p");
        CreateTestDb(db, withData: true);
        SqliteConnection.ClearAllPools();

        var repo = new ShopeeBackendDbRepo(db, ov, () => false);
        var (keys, error) = repo.LoadProxyKeys();
        Assert.Null(error);
        Assert.Equal(2, keys.Count);
        Assert.Contains("key1234", keys);
        Assert.Contains("key5678", keys);
    }

    // ─── Test 2: Thêm / Sửa / Xoá qua AccountStore ────────────────────────────

    [Fact]
    public void AddChangePwdDelete_DbHasExact3Changes_MetaUnchanged()
    {
        var db = NewDb("t2"); var ov = NewOverlay("t2");
        CreateTestDb(db, withData: true);
        SqliteConnection.ClearAllPools();

        var store = new AccountStore(db, ov, () => false);

        // Thêm 1 tk mới
        var newAcc = new ShopeeAccount
        {
            Id = "id99",
            ShopeeAccountLogin = "newuser|newpass",
            Label = "new", RequireProxy = true, OpenWithShopeeAccount = true,
            ProfileRelativePath = "profiles/id99",
        };
        Assert.True(store.Add(newAcc));

        // Sửa password id1
        var accounts = store.Accounts.ToList();
        var acc1 = accounts.First(a => a.Id == "id1");
        acc1.ShopeeAccountLogin = "user1|newpassword|.shopee.vn=SPC_F=tok1";
        Assert.True(store.ReplaceAll(accounts));

        // Xoá id2
        Assert.True(store.Remove("id2"));

        // Kiểm DB trực tiếp
        SqliteConnection.ClearAllPools();
        var repo = new ShopeeBackendDbRepo(db, ov, () => false);
        var rows = repo.ReadAccountRows();

        Assert.Equal(2, rows.Count(r => r.Username.Length > 0)); // id1 + id99
        Assert.Contains(rows, r => r.Id == "id3"); // Username rỗng không bị xoá
        Assert.DoesNotContain(rows, r => r.Id == "id2");

        // id1: ProfileName/Status giữ nguyên
        var row1 = rows.First(r => r.Id == "id1");
        Assert.Equal("profile1", row1.ProfileName);
        Assert.Equal("Ready", row1.Status);

        // Bảng Categories không đổi
        SqliteConnection.ClearAllPools();
        Assert.Equal(1, CountRows(db, "Categories"));
    }

    // ─── Test 3: Khoá khi ShopeeBackend chạy ─────────────────────────────────

    [Fact]
    public void Add_WhenLocked_ReturnsFalse_DbUnchanged()
    {
        var db = NewDb("t3"); var ov = NewOverlay("t3");
        CreateTestDb(db, withData: false);
        using (var conn = OpenDb(db))
            Exec(conn, "INSERT INTO Accounts VALUES ('id1','user1','pass1','','','Ready')");
        SqliteConnection.ClearAllPools();

        var hashBefore = HashDb(db);

        // Giả lập ShopeeBackend đang chạy
        var store = new AccountStore(db, ov, () => true);
        Assert.NotNull(store.ReadOnlyReason);

        var newAcc = new ShopeeAccount
        {
            Id = "id99", ShopeeAccountLogin = "newuser|newpass",
            Label = "new", RequireProxy = true, OpenWithShopeeAccount = true,
            ProfileRelativePath = "profiles/id99",
        };
        var added = store.Add(newAcc);
        Assert.False(added);

        // DB không đổi
        SqliteConnection.ClearAllPools();
        Assert.Equal(hashBefore, HashDb(db));
    }

    [Fact]
    public void Save_WhenLocked_WritesOverlay_DbUnchanged()
    {
        var db = NewDb("t4"); var ov = NewOverlay("t4");
        CreateTestDb(db, withData: false);
        using (var conn = OpenDb(db))
            Exec(conn, "INSERT INTO Accounts VALUES ('id1','user1','pass1','','','Ready')");
        SqliteConnection.ClearAllPools();

        // Load không khoá để lấy account
        var store = new AccountStore(db, ov, () => false);
        var acc = store.Accounts[0];

        // Dùng repo trực tiếp với khoá để kiểm SaveAccounts
        var repo = new ShopeeBackendDbRepo(db, ov, () => true);
        var overlay = new Dictionary<string, AccountOverlayEntry>
        {
            ["id1"] = new AccountOverlayEntry { Label = "test", Disabled = true }
        };
        var hashBefore = HashDb(db);
        var (dbOk, overlayOk, reason) = repo.SaveAccounts([acc], overlay);

        Assert.True(dbOk);         // chỉ overlay → thành công dù ShopeeBackend đang mở
        Assert.Null(reason);
        Assert.True(overlayOk);
        Assert.True(File.Exists(ov));
        SqliteConnection.ClearAllPools();
        Assert.Equal(hashBefore, HashDb(db));
    }

    // ─── Test 4: DB không tồn tại ─────────────────────────────────────────────

    [Fact]
    public void LoadAccounts_DbNotExist_ReturnsZeroAndError_NoFileCreated()
    {
        var notExist = Path.Combine(_tempDir, "nonexistent.db");
        var ov = NewOverlay("t5");
        var repo = new ShopeeBackendDbRepo(notExist, ov, () => false);
        var (accounts, error) = repo.LoadAccounts();

        Assert.Empty(accounts);
        Assert.NotNull(error);
        Assert.False(File.Exists(notExist));  // KHÔNG tạo file DB
    }

    // ─── Test 5: SuitePaths.Root ──────────────────────────────────────────────

    [Fact]
    public void SuitePathsRoot_EndsWithShopeeBackendWorkspace()
    {
        var root = SuitePaths.Root;
        Assert.True(
            root.EndsWith(Path.Combine("ShopeeBackend", "Workspace"), StringComparison.OrdinalIgnoreCase),
            $"Thực tế: {root}");
    }
}
