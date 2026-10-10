using Shopee.Core.Infrastructure;

namespace Shopee.Core.Accounts;

/// <summary>
/// Kho tài khoản Shopee dùng chung cho toàn Workspace. Nguồn: DB chung ShopeeBackend (bảng Accounts)
/// + overlay riêng của app này (%Root%\shared\accounts-overlay.json).
/// API công khai giữ nguyên so với bản gốc (Accounts, Find, Add, Remove, ReplaceAll, Save, Changed).
/// Thêm: Reload(), ReadOnlyReason.
/// </summary>
public sealed class AccountStore
{
    private static readonly Lazy<AccountStore> _shared = new(() => new AccountStore());
    public static AccountStore Shared => _shared.Value;

    private readonly ShopeeBackendDbRepo _repo;
    private readonly object _lock = new();
    private readonly List<ShopeeAccount> _accounts = [];
    private List<DbAccountRow> _baseline = [];
    private bool _loadFailed;

    /// <summary>Phát khi danh sách thay đổi (thêm/xóa/lưu/nạp lại) để các ViewModel khác làm mới.</summary>
    public event Action? Changed;

    /// <summary>
    /// Lý do khoá ghi (null = ghi được). Khi không null: Add/Remove/ReplaceAll/Save trả false,
    /// nhưng overlay (LastUsedTick, Disabled, CaptchaUrl…) vẫn ghi được.
    /// </summary>
    public string? ReadOnlyReason => _repo.ReadOnlyReason;

    private AccountStore() : this(null, null, null) { }

    /// <summary>Constructor dành cho test (inject đường dẫn DB, overlay, hàm kiểm tiến trình).</summary>
    public AccountStore(string? dbPath, string? overlayPath, Func<bool>? isShopeeBackendRunning)
    {
        _repo = new ShopeeBackendDbRepo(dbPath, overlayPath, isShopeeBackendRunning);
        Load();
    }

    public IReadOnlyList<ShopeeAccount> Accounts
    {
        get { lock (_lock) return _accounts.ToList(); }
    }

    public ShopeeAccount? Find(string id)
    {
        lock (_lock) return _accounts.FirstOrDefault(a => a.Id == id);
    }

    public bool Add(ShopeeAccount account)
    {
        account.EnsureProfilePath();
        lock (_lock)
        {
            _accounts.Add(account);
            if (SaveLocked()) return true;
            _accounts.Remove(account);
            return false;
        }
    }

    public bool Remove(string id)
    {
        lock (_lock)
        {
            var removed = _accounts.Where(a => a.Id == id).ToList();
            if (removed.Count == 0) return true;
            _accounts.RemoveAll(a => a.Id == id);
            if (SaveLocked()) return true;
            _accounts.AddRange(removed);
            return false;
        }
    }

    /// <summary>Thay toàn bộ danh sách (dùng khi import/sắp xếp lại) rồi lưu.</summary>
    public bool ReplaceAll(IEnumerable<ShopeeAccount> accounts)
    {
        var incoming = accounts.ToList();
        foreach (var a in incoming) a.EnsureProfilePath();
        lock (_lock)
        {
            var previous = _accounts.ToList();
            _accounts.Clear();
            _accounts.AddRange(incoming);
            if (SaveLocked()) return true;
            _accounts.Clear();
            _accounts.AddRange(previous);
            return false;
        }
    }

    /// <summary>Nạp lại từ DB (dùng khi user bấm "Nạp lại từ DB" hoặc app kích hoạt lại).</summary>
    public void Reload()
    {
        lock (_lock) LoadLocked();
        Changed?.Invoke();
    }

    /// <summary>Nạp lần đầu (gọi từ constructor).</summary>
    public void Load()
    {
        lock (_lock) LoadLocked();
    }

    public bool Save()
    {
        lock (_lock) return SaveLocked();
    }

    /// <summary>Ghi captcha / vòng xoay / Disabled. Không ghi username, mật khẩu, cookie vào DB.</summary>
    public bool SaveOverlayOnly()
    {
        lock (_lock)
        {
            if (_loadFailed) return false;
            return _repo.SaveOverlayOnly(_repo.BuildOverlay(_accounts));
        }
    }

    private void LoadLocked()
    {
        _accounts.Clear();
        var (list, rows, error) = _repo.LoadSnapshot();
        _loadFailed = error is not null;
        _baseline = rows;
        if (_loadFailed) return;
        foreach (var a in list) { a.EnsureProfilePath(); _accounts.Add(a); }
    }

    private bool SaveLocked()
    {
        try
        {
            if (_loadFailed) return false;
            var overlay = _repo.BuildOverlay(_accounts);
            var (dbOk, overlayOk, _) = _repo.SaveAccounts(_accounts, overlay, _baseline, _loadFailed);
            if (dbOk) _baseline = _repo.ReadAccountRows();
            // Thành công khi DB không cần ghi (hoặc ghi xong) và overlay ghi được.
            // Khi ShopeeBackend đang mở mà Diff có lệnh DB, dbOk = false → caller khôi phục object.
            var ok = dbOk && overlayOk;
            if (ok) Changed?.Invoke();
            return ok;
        }
        catch
        {
            return false;
        }
    }
}
