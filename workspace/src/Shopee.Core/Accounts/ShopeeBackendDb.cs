using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Shopee.Core.Infrastructure;

namespace Shopee.Core.Accounts;

// ═══════════════════════════════════════════════════════════════════════════════
//  Dữ liệu phân tầng DB + Overlay
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>Một dòng từ bảng Accounts trong DB ShopeeBackend.</summary>
internal sealed class DbAccountRow
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Cookie { get; set; } = "";
    public string ProfileName { get; set; } = "";
    public string Status { get; set; } = "";
}

/// <summary>Một dòng từ bảng Proxies trong DB ShopeeBackend.</summary>
internal sealed class DbProxyRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
}

/// <summary>Overlay riêng của Shopee.Workspace, lưu theo Id tài khoản.</summary>
internal sealed class AccountOverlayEntry
{
    public string Label { get; set; } = "";
    public string KiotProxyKey { get; set; } = "";
    public string Region { get; set; } = "random";
    public string ProxyType { get; set; } = "http";
    public string ManualProxy { get; set; } = "";
    public bool RequireProxy { get; set; } = true;
    public bool OpenWithShopeeAccount { get; set; } = true;
    public string ProfileRelativePath { get; set; } = "";
    public bool Disabled { get; set; }
    public string? LastError { get; set; }
    public string? CaptchaUrl { get; set; }
    public long LastUsedTick { get; set; }
}

/// <summary>Nội dung file overlay JSON.</summary>
internal sealed class AccountOverlayFile
{
    public Dictionary<string, AccountOverlayEntry> Accounts { get; set; } = new();
}

// ═══════════════════════════════════════════════════════════════════════════════
//  Lệnh ghi DB (diff)
// ═══════════════════════════════════════════════════════════════════════════════

internal enum DbCommandKind { Insert, Update, Delete }

internal sealed class DbAccountCommand
{
    public DbCommandKind Kind { get; init; }
    public DbAccountRow Row { get; init; } = new();
}

internal sealed class DbProxyCommand
{
    public DbCommandKind Kind { get; init; }
    public DbProxyRow Row { get; init; } = new();
}

// ═══════════════════════════════════════════════════════════════════════════════
//  Hàm thuần (unit test được)
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Hàm thuần chuyển đổi giữa ShopeeAccount và dòng DB.
/// Tất cả hàm ở đây không có I/O, test được mà không cần setup DB.
/// </summary>
internal static class ShopeeBackendDbFuncs
{
    // ─── BuildLoginLine ───────────────────────────────────────────────────────
    /// <summary>
    /// Xây dựng chuỗi login line từ username, password và cookie thô từ DB.
    /// - cookie rỗng → "user|pass"
    /// - bắt đầu ".shopee.vn=" → "user|pass|&lt;cookie&gt;"
    /// - bắt đầu "SPC_F=" → "user|pass|.shopee.vn=&lt;cookie&gt;"
    /// - còn lại → "user|pass|.shopee.vn=SPC_F=&lt;cookie&gt;"
    /// </summary>
    public static string BuildLoginLine(string user, string pass, string cookie)
    {
        user = (user ?? "").Trim();
        pass = (pass ?? "").Trim();
        cookie = (cookie ?? "").Trim();

        if (string.IsNullOrEmpty(cookie))
            return $"{user}|{pass}";

        if (cookie.StartsWith(".shopee.vn=", StringComparison.OrdinalIgnoreCase))
            return $"{user}|{pass}|{cookie}";

        if (cookie.StartsWith("SPC_F=", StringComparison.OrdinalIgnoreCase))
            return $"{user}|{pass}|.shopee.vn={cookie}";

        return $"{user}|{pass}|.shopee.vn=SPC_F={cookie}";
    }

    // ─── SplitLoginLine ───────────────────────────────────────────────────────
    /// <summary>
    /// Tách login line ngược lại về (user, pass, cookieChoDb).
    /// - Nếu BuildLoginLine(user, pass, originalCookie) == line → trả lại originalCookie nguyên văn.
    /// - Ngược lại: cookie mới chuẩn hoá về dạng "SPC_F=&lt;value&gt;" hoặc "" nếu không có.
    /// </summary>
    public static (string user, string pass, string cookieChoDb) SplitLoginLine(string line, string originalCookie)
    {
        line = (line ?? "").Trim();

        // Parse để lấy user, pass, phần cookie
        var parts = line.Split('|');
        if (parts.Length < 2) return ("", "", "");

        var user = parts[0].Trim();
        var pass = parts[1].Trim();

        // Ghép lại phần cookie (parts[2..])
        string rawCookie = parts.Length >= 3 ? string.Join("|", parts.Skip(2)).Trim() : "";

        // Kiểm tra xem có khớp với original không → giữ nguyên văn
        if (BuildLoginLine(user, pass, originalCookie) == line)
            return (user, pass, originalCookie);

        // Chuẩn hoá cookie mới → dạng SPC_F=<value>
        if (string.IsNullOrEmpty(rawCookie))
            return (user, pass, "");

        // rawCookie có dạng ".shopee.vn=SPC_F=abc" hoặc ".shopee.vn=abc"
        var eqIdx = rawCookie.IndexOf('=', StringComparison.Ordinal);
        if (eqIdx < 0) return (user, pass, "");

        var afterDomain = rawCookie[(eqIdx + 1)..].Trim();  // "SPC_F=abc" hoặc "abc"
        const string prefix = "SPC_F=";
        if (afterDomain.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return (user, pass, afterDomain);       // đã đúng dạng "SPC_F=abc"

        return (user, pass, $"SPC_F={afterDomain}"); // chuẩn hoá
    }

    // ─── Compose ──────────────────────────────────────────────────────────────
    /// <summary>
    /// Tổng hợp danh sách ShopeeAccount từ dòng DB + overlay.
    /// Bỏ qua dòng Username rỗng; tài khoản chưa có overlay dùng giá trị mặc định.
    /// </summary>
    public static List<ShopeeAccount> Compose(
        IEnumerable<DbAccountRow> dbRows,
        Dictionary<string, AccountOverlayEntry> overlay)
    {
        var result = new List<ShopeeAccount>();
        foreach (var row in dbRows)
        {
            if (string.IsNullOrWhiteSpace(row.Username)) continue;

            overlay.TryGetValue(row.Id, out var ov);
            var loginLine = BuildLoginLine(row.Username, row.Password, row.Cookie);
            result.Add(new ShopeeAccount
            {
                Id = row.Id,
                ShopeeAccountLogin = loginLine,
                Label = ov?.Label is { Length: > 0 } l ? l : row.Username,
                KiotProxyKey = ov?.KiotProxyKey ?? "",
                Region = ov?.Region ?? "random",
                ProxyType = ov?.ProxyType ?? "http",
                ManualProxy = ov?.ManualProxy ?? "",
                RequireProxy = ov?.RequireProxy ?? true,
                OpenWithShopeeAccount = ov?.OpenWithShopeeAccount ?? true,
                ProfileRelativePath = ov?.ProfileRelativePath is { Length: > 0 } p ? p : $"profiles/{row.Id}",
                Disabled = ov?.Disabled ?? false,
                LastError = ov?.LastError,
                CaptchaUrl = ov?.CaptchaUrl,
                LastUsedTick = ov?.LastUsedTick ?? 0,
            });
        }
        return result;
    }

    // ─── Diff ─────────────────────────────────────────────────────────────────
    /// <summary>
    /// So sánh danh sách DB hiện tại với danh sách accounts mới, sinh lệnh Insert/Update/Delete theo Id.
    /// KHÔNG BAO GIỜ sinh lệnh xoá cả bảng.
    /// Chỉ Update khi Username/Password/Cookie thực sự thay đổi.
    /// </summary>
    public static List<DbAccountCommand> Diff(
        IReadOnlyList<DbAccountRow> dbRowsTruoc,
        IReadOnlyList<ShopeeAccount> accountsSau)
    {
        var result = new List<DbAccountCommand>();
        var oldById = dbRowsTruoc.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var newIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var acc in accountsSau)
        {
            var (user, pass, cookieChoDb) = SplitLoginLine(acc.ShopeeAccountLogin,
                oldById.TryGetValue(acc.Id, out var existing) ? existing.Cookie : "");

            newIds.Add(acc.Id);

            if (!oldById.TryGetValue(acc.Id, out var old))
            {
                // INSERT
                result.Add(new DbAccountCommand
                {
                    Kind = DbCommandKind.Insert,
                    Row = new DbAccountRow
                    {
                        Id = acc.Id,
                        Username = user,
                        Password = pass,
                        Cookie = cookieChoDb,
                        ProfileName = "",   // mặc định trống lúc INSERT
                        Status = "Ready",
                    }
                });
            }
            else if (user != old.Username || pass != old.Password || cookieChoDb != old.Cookie)
            {
                // UPDATE — chỉ Username/Password/Cookie; ProfileName/Status giữ nguyên
                result.Add(new DbAccountCommand
                {
                    Kind = DbCommandKind.Update,
                    Row = new DbAccountRow
                    {
                        Id = acc.Id,
                        Username = user,
                        Password = pass,
                        Cookie = cookieChoDb,
                        ProfileName = old.ProfileName,  // giữ nguyên
                        Status = old.Status,            // giữ nguyên
                    }
                });
            }
        }

        // DELETE — dòng cũ không còn trong danh sách mới.
        // Dòng Username rỗng bị Compose bỏ khỏi danh sách làm việc, không phải user xoá → giữ nguyên trong DB.
        foreach (var old in dbRowsTruoc)
        {
            if (string.IsNullOrWhiteSpace(old.Username)) continue;
            if (!newIds.Contains(old.Id))
                result.Add(new DbAccountCommand { Kind = DbCommandKind.Delete, Row = old });
        }

        return result;
    }

    /// <summary>
    /// Ghép 3 bản: baseline (lúc nạp), current (DB lúc ghi), desired (bộ nhớ).
    /// Không xoá dòng ShopeeBackend thêm sau lúc nạp. Không ghi đè user/pass/cookie nếu người dùng chưa sửa
    /// dòng đăng nhập so với baseline hoặc so với DB hiện tại. Dòng username rỗng không xoá.
    /// </summary>
    public static List<DbAccountCommand> Merge(
        IReadOnlyList<DbAccountRow> baseline,
        IReadOnlyList<DbAccountRow> current,
        IReadOnlyList<ShopeeAccount> desired)
    {
        var result = new List<DbAccountCommand>();
        var baseById = baseline.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var curById = current.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var desiredIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var acc in desired)
        {
            desiredIds.Add(acc.Id);
            curById.TryGetValue(acc.Id, out var cur);
            baseById.TryGetValue(acc.Id, out var old);
            var line = (acc.ShopeeAccountLogin ?? "").Trim();

            if (cur is null)
            {
                if (old is not null) continue;
                if (!ShopeeAuth.ParseLoginLine(line, ShopeeLoginLineOptions.AllowMissingCookie).Ok) continue;
                var (user, pass, cookie) = SplitLoginLine(line, "");
                if (string.IsNullOrWhiteSpace(user)) continue;
                result.Add(new DbAccountCommand
                {
                    Kind = DbCommandKind.Insert,
                    Row = new DbAccountRow
                    {
                        Id = acc.Id,
                        Username = user,
                        Password = pass,
                        Cookie = cookie,
                        ProfileName = ProfileFolder(user, acc.Id),
                        Status = "Ready",
                    }
                });
                continue;
            }

            var currentLine = BuildLoginLine(cur.Username, cur.Password, cur.Cookie);
            var baselineLine = old is null ? null : BuildLoginLine(old.Username, old.Password, old.Cookie);
            if (line == currentLine || (baselineLine is not null && line == baselineLine)) continue;
            if (!ShopeeAuth.ParseLoginLine(line, ShopeeLoginLineOptions.AllowMissingCookie).Ok) continue;

            var (u, p, c) = SplitLoginLine(line, cur.Cookie);
            if (u == cur.Username && p == cur.Password && c == cur.Cookie) continue;
            result.Add(new DbAccountCommand
            {
                Kind = DbCommandKind.Update,
                Row = new DbAccountRow
                {
                    Id = acc.Id,
                    Username = u,
                    Password = p,
                    Cookie = c,
                    ProfileName = cur.ProfileName,
                    Status = cur.Status,
                }
            });
        }

        foreach (var row in current)
        {
            if (string.IsNullOrWhiteSpace(row.Username)) continue;
            if (desiredIds.Contains(row.Id)) continue;
            if (!baseById.ContainsKey(row.Id)) continue;
            result.Add(new DbAccountCommand { Kind = DbCommandKind.Delete, Row = row });
        }

        return result;
    }

    private static string ProfileFolder(string user, string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string((user ?? "").Where(c => !invalid.Contains(c)).ToArray()).Trim();
        if (safe.Length == 0) safe = string.IsNullOrEmpty(id) ? "acc" : id[..Math.Min(8, id.Length)];
        return "Profile_" + safe;
    }

    // ─── DiffProxies ──────────────────────────────────────────────────────────
    /// <summary>
    /// So sánh bảng Proxies hiện tại với danh sách keys mới, sinh lệnh Insert/Update/Delete.
    /// KHÔNG BAO GIỜ đụng dòng Id='none'.
    /// Giữ Name của dòng còn; thêm dòng mới với Name="Kiot …{4 ký tự cuối}".
    /// </summary>
    public static List<DbProxyCommand> DiffProxies(
        IReadOnlyList<DbProxyRow> rowsTruoc,
        IReadOnlyList<string> keysSau)
    {
        var result = new List<DbProxyCommand>();
        // Dòng có thể ghi = không phải 'none' và Address không rỗng
        var oldByAddress = new Dictionary<string, DbProxyRow>(StringComparer.Ordinal);
        foreach (var r in rowsTruoc)
        {
            if (string.Equals(r.Id, "none", StringComparison.OrdinalIgnoreCase) || r.Address.Length == 0) continue;
            if (!oldByAddress.ContainsKey(r.Address)) oldByAddress[r.Address] = r;
        }

        var newKeys = keysSau.ToHashSet(StringComparer.Ordinal);

        // Thêm dòng mới
        foreach (var key in keysSau)
        {
            if (!oldByAddress.ContainsKey(key))
            {
                var tail = key.Length >= 4 ? key[^4..] : key;
                result.Add(new DbProxyCommand
                {
                    Kind = DbCommandKind.Insert,
                    Row = new DbProxyRow
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Name = $"Kiot …{tail}",
                        Address = key,
                    }
                });
            }
        }

        // Xoá dòng không còn trong keys mới
        foreach (var old in oldByAddress.Values)
        {
            if (!newKeys.Contains(old.Address))
                result.Add(new DbProxyCommand { Kind = DbCommandKind.Delete, Row = old });
        }

        return result;
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
//  Repository đọc/ghi DB ShopeeBackend
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Repository đọc/ghi DB ShopeeBackend. Inject đường dẫn DB và overlay để test không phụ thuộc máy.
/// </summary>
internal sealed class ShopeeBackendDbRepo
{
    private readonly string _dbPath;
    private readonly string _overlayPath;
    private readonly Func<bool> _isShopeeBackendRunning;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <param name="dbPath">Đường dẫn file shopee_data.db (mặc định = SuitePaths.ShopeeBackendDbPath).</param>
    /// <param name="overlayPath">Đường dẫn file overlay JSON (mặc định = shared/accounts-overlay.json).</param>
    /// <param name="isShopeeBackendRunning">Hàm kiểm "ShopeeBackend đang chạy" — inject để test không phụ thuộc máy.</param>
    public ShopeeBackendDbRepo(string? dbPath = null, string? overlayPath = null, Func<bool>? isShopeeBackendRunning = null)
    {
        _dbPath = dbPath ?? SuitePaths.ShopeeBackendDbPath;
        _overlayPath = overlayPath ?? Path.Combine(SuitePaths.ModuleDir("shared"), "accounts-overlay.json");
        _isShopeeBackendRunning = isShopeeBackendRunning ?? DefaultIsShopeeBackendRunning;
    }

    private static bool DefaultIsShopeeBackendRunning()
    {
        try
        {
            var procs = Process.GetProcessesByName("ShopeeBackend");
            try { return procs.Length > 0; }
            finally { foreach (var p in procs) p.Dispose(); }
        }
        catch { return false; }
    }

    /// <summary>
    /// Lý do khoá ghi (null = ghi được). Kiểm lại mỗi lần gọi.
    /// </summary>
    public string? ReadOnlyReason =>
        _isShopeeBackendRunning()
            ? "ShopeeBackend \u0111ang m\u1edf \u2014 \u0111\u00f3ng n\u00f3 tr\u01b0\u1edbc khi s\u1eeda t\u00e0i kho\u1ea3n/proxy, n\u1ebfu kh\u00f4ng s\u1eeda \u0111\u1ed5i s\u1ebd b\u1ecb ShopeeBackend ghi \u0111\u00e8 khi n\u00f3 l\u01b0u."
            : null;

    // ─── Đọc DB ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Nạp danh sách tài khoản từ DB + overlay.
    /// DB không tồn tại → trả về (0 tk, thông điệp lỗi tiếng Việt).
    /// </summary>
    public (List<ShopeeAccount> accounts, string? error) LoadAccounts()
    {
        var (accounts, _, error) = LoadSnapshot();
        return (accounts, error);
    }

    /// <summary>Nạp tài khoản kèm bản chụp dòng DB (kể cả username rỗng) để lần ghi sau không xoá dữ liệu người khác thêm.</summary>
    public (List<ShopeeAccount> accounts, List<DbAccountRow> rows, string? error) LoadSnapshot()
    {
        if (!File.Exists(_dbPath))
            return ([], [], $"Không tìm thấy DB ShopeeBackend ({_dbPath}). Mở ShopeeBackend ít nhất một lần để tạo DB.");

        try
        {
            var dbRows = ReadAccountRows();
            var overlay = ReadOverlay().Accounts;
            var accounts = ShopeeBackendDbFuncs.Compose(dbRows, overlay);
            return (accounts, dbRows, null);
        }
        catch (Exception ex)
        {
            return ([], [], $"Lỗi đọc DB ShopeeBackend: {ex.Message}");
        }
    }

    /// <summary>Nạp danh sách proxy keys từ DB (Address của các dòng Id != 'none' và Address != '').</summary>
    public (List<string> keys, string? error) LoadProxyKeys()
    {
        if (!File.Exists(_dbPath))
            return ([], $"Không tìm thấy DB ShopeeBackend.");

        try
        {
            var rows = ReadProxyRows();
            var keys = rows
                .Where(r => !string.Equals(r.Id, "none", StringComparison.OrdinalIgnoreCase) && r.Address.Length > 0)
                .Select(r => r.Address)
                .ToList();
            return (keys, null);
        }
        catch (Exception ex)
        {
            return ([], $"Lỗi đọc bảng Proxies: {ex.Message}");
        }
    }

    // ─── Ghi ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ghi danh sách accounts vào DB + overlay.
    /// - Đọc lại dòng hiện tại → Diff → chạy trong 1 transaction.
    /// - Diff rỗng → chỉ ghi overlay, không mở ghi DB, trả thành công kể cả khi ShopeeBackend đang mở.
    /// - Khi khoá và Diff có lệnh DB: không ghi DB, trả false; overlay không nhận Id chưa insert được.
    /// </summary>
    public (bool dbOk, bool overlayOk, string? reason) SaveAccounts(
        IReadOnlyList<ShopeeAccount> accounts,
        Dictionary<string, AccountOverlayEntry> overlay,
        IReadOnlyList<DbAccountRow>? baseline = null,
        bool loadFailed = false)
    {
        if (loadFailed)
            return (false, false, "Chưa nạp được danh sách tài khoản, không ghi.");
        if (!File.Exists(_dbPath))
            return (false, false, $"Không tìm thấy DB ShopeeBackend ({_dbPath}).");

        List<DbAccountCommand> cmds;
        try
        {
            var current = ReadAccountRows();
            cmds = ShopeeBackendDbFuncs.Merge(baseline ?? current, current, accounts);
        }
        catch (Exception ex)
        {
            return (false, false, $"Lỗi ghi DB: {ex.Message}");
        }

        var lockReason = ReadOnlyReason;
        if (lockReason is not null && cmds.Count > 0)
        {
            var blocked = new HashSet<string>(
                cmds.Where(c => c.Kind == DbCommandKind.Insert).Select(c => c.Row.Id),
                StringComparer.Ordinal);
            var kept = overlay.Where(kv => !blocked.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            var overlayKept = TrySaveOverlay(new AccountOverlayFile { Accounts = kept });
            return (false, overlayKept, lockReason);
        }

        if (cmds.Count > 0)
        {
            try
            {
                if (!ExecuteAccountCommands(cmds))
                    return (false, false, "Lỗi ghi bảng Accounts.");
            }
            catch (Exception ex)
            {
                return (false, false, $"Lỗi ghi DB: {ex.Message}");
            }
        }

        var overlayOk = TrySaveOverlay(new AccountOverlayFile { Accounts = new Dictionary<string, AccountOverlayEntry>(overlay) });
        return (true, overlayOk, null);
    }

    /// <summary>
    /// Ghi danh sách proxy keys vào DB.
    /// - Diff rỗng → không mở ghi DB.
    /// - Khi khoá: trả false.
    /// </summary>
    public (bool ok, string? reason) SaveProxies(IReadOnlyList<string> keys)
    {
        var reason = ReadOnlyReason;
        if (reason is not null) return (false, reason);

        if (!File.Exists(_dbPath))
            return (false, $"Không tìm thấy DB ShopeeBackend ({_dbPath}).");

        try
        {
            var currentRows = ReadProxyRows();
            var cmds = ShopeeBackendDbFuncs.DiffProxies(currentRows, keys);
            if (cmds.Count == 0) return (true, null);
            return ExecuteProxyCommands(cmds) ? (true, null) : (false, "Lỗi ghi bảng Proxies.");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi ghi DB Proxies: {ex.Message}");
        }
    }

    // ─── Đọc overlay ──────────────────────────────────────────────────────────

    public AccountOverlayFile ReadOverlay()
    {
        try
        {
            if (!File.Exists(_overlayPath)) return new AccountOverlayFile();
            var json = File.ReadAllText(_overlayPath);
            return JsonSerializer.Deserialize<AccountOverlayFile>(json) ?? new AccountOverlayFile();
        }
        catch { return new AccountOverlayFile(); }
    }

    /// <summary>Tạo overlay từ danh sách accounts, giữ các trường overlay-only.</summary>
    public Dictionary<string, AccountOverlayEntry> BuildOverlay(IReadOnlyList<ShopeeAccount> accounts)
    {
        var result = new Dictionary<string, AccountOverlayEntry>(StringComparer.Ordinal);
        foreach (var acc in accounts)
        {
            result[acc.Id] = new AccountOverlayEntry
            {
                Label = acc.Label,
                KiotProxyKey = acc.KiotProxyKey,
                Region = acc.Region,
                ProxyType = acc.ProxyType,
                ManualProxy = acc.ManualProxy,
                RequireProxy = acc.RequireProxy,
                OpenWithShopeeAccount = acc.OpenWithShopeeAccount,
                ProfileRelativePath = acc.ProfileRelativePath,
                Disabled = acc.Disabled,
                LastError = acc.LastError,
                CaptchaUrl = acc.CaptchaUrl,
                LastUsedTick = acc.LastUsedTick,
            };
        }
        return result;
    }

    // ─── Helpers đọc DB ───────────────────────────────────────────────────────

    internal List<DbAccountRow> ReadAccountRows()
    {
        var result = new List<DbAccountRow>();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Username, Password, Cookie, ProfileName, Status FROM Accounts";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new DbAccountRow
            {
                Id = reader.GetString(0),
                Username = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Password = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Cookie = reader.IsDBNull(3) ? "" : reader.GetString(3),
                ProfileName = reader.IsDBNull(4) ? "" : reader.GetString(4),
                Status = reader.IsDBNull(5) ? "" : reader.GetString(5),
            });
        }
        return result;
    }

    internal List<DbProxyRow> ReadProxyRows()
    {
        var result = new List<DbProxyRow>();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, Address FROM Proxies ORDER BY rowid";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new DbProxyRow
            {
                Id = reader.GetString(0),
                Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Address = reader.IsDBNull(2) ? "" : reader.GetString(2),
            });
        }
        return result;
    }

    private SqliteConnection OpenConnection()
    {
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 5,
        }.ToString();
        var conn = new SqliteConnection(connStr);
        conn.Open();
        return conn;
    }

    private SqliteConnection OpenReadOnlyConnection()
    {
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            DefaultTimeout = 5,
        }.ToString();
        var conn = new SqliteConnection(connStr);
        conn.Open();
        return conn;
    }

    // ─── Helpers ghi DB ───────────────────────────────────────────────────────

    private bool ExecuteAccountCommands(List<DbAccountCommand> cmds)
    {
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            foreach (var c in cmds)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                switch (c.Kind)
                {
                    case DbCommandKind.Insert:
                        cmd.CommandText =
                            "INSERT INTO Accounts (Id, Username, Password, Cookie, ProfileName, Status) " +
                            "VALUES ($id, $usr, $pwd, $cookie, $pname, $status)";
                        cmd.Parameters.AddWithValue("$id", c.Row.Id);
                        cmd.Parameters.AddWithValue("$usr", c.Row.Username);
                        cmd.Parameters.AddWithValue("$pwd", c.Row.Password);
                        cmd.Parameters.AddWithValue("$cookie", c.Row.Cookie);
                        cmd.Parameters.AddWithValue("$pname", c.Row.ProfileName);
                        cmd.Parameters.AddWithValue("$status", "Ready");
                        break;
                    case DbCommandKind.Update:
                        cmd.CommandText =
                            "UPDATE Accounts SET Username=$usr, Password=$pwd, Cookie=$cookie WHERE Id=$id";
                        cmd.Parameters.AddWithValue("$id", c.Row.Id);
                        cmd.Parameters.AddWithValue("$usr", c.Row.Username);
                        cmd.Parameters.AddWithValue("$pwd", c.Row.Password);
                        cmd.Parameters.AddWithValue("$cookie", c.Row.Cookie);
                        break;
                    case DbCommandKind.Delete:
                        cmd.CommandText = "DELETE FROM Accounts WHERE Id=$id";
                        cmd.Parameters.AddWithValue("$id", c.Row.Id);
                        break;
                }
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return true;
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            return false;
        }
    }

    private bool ExecuteProxyCommands(List<DbProxyCommand> cmds)
    {
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            foreach (var c in cmds)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                switch (c.Kind)
                {
                    case DbCommandKind.Insert:
                        cmd.CommandText =
                            "INSERT INTO Proxies (Id, Name, Address) VALUES ($id, $name, $addr)";
                        cmd.Parameters.AddWithValue("$id", c.Row.Id);
                        cmd.Parameters.AddWithValue("$name", c.Row.Name);
                        cmd.Parameters.AddWithValue("$addr", c.Row.Address);
                        break;
                    case DbCommandKind.Delete:
                        cmd.CommandText = "DELETE FROM Proxies WHERE Id=$id";
                        cmd.Parameters.AddWithValue("$id", c.Row.Id);
                        break;
                }
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return true;
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            return false;
        }
    }

    /// <summary>Chỉ ghi overlay (captcha, LastUsedTick…). Không đụng bảng Accounts.</summary>
    public bool SaveOverlayOnly(Dictionary<string, AccountOverlayEntry> overlay) =>
        TrySaveOverlay(new AccountOverlayFile { Accounts = new Dictionary<string, AccountOverlayEntry>(overlay) });

    private bool TrySaveOverlay(AccountOverlayFile data)
    {
        try
        {
            var dir = Path.GetDirectoryName(_overlayPath);
            if (dir is not null) Directory.CreateDirectory(dir);
            return JsonAtomicFile.Save(_overlayPath, data, JsonOpts);
        }
        catch { return false; }
    }
}
