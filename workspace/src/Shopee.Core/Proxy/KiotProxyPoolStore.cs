using Shopee.Core.Accounts;
using Shopee.Core.Infrastructure;

namespace Shopee.Core.Proxy;

/// <summary>
/// Kho KiotProxy dùng chung, nguồn: bảng Proxies của DB ShopeeBackend.
/// Giữ nguyên API công khai (Keys, Count, ProxyForAccount, ReplaceAll, Changed).
/// Logic xoay vòng ProxyForAccount giữ nguyên.
/// </summary>
public sealed class KiotProxyPoolStore
{
    private static readonly Lazy<KiotProxyPoolStore> _shared = new(() => new KiotProxyPoolStore());
    public static KiotProxyPoolStore Shared => _shared.Value;

    private readonly ShopeeBackendDbRepo _repo;
    private readonly object _lock = new();
    private readonly List<string> _keys = [];

    /// <summary>Phát khi kho đổi (lưu/nạp lại) để UI làm mới.</summary>
    public event Action? Changed;

    private KiotProxyPoolStore() : this(null, null, null) { }

    /// <summary>Constructor dành cho test.</summary>
    public KiotProxyPoolStore(string? dbPath, string? overlayPath, Func<bool>? isShopeeBackendRunning)
    {
        _repo = new ShopeeBackendDbRepo(dbPath, overlayPath, isShopeeBackendRunning);
        Load();
    }

    /// <summary>Danh sách entry (Address từ bảng Proxies, Id != 'none', Address != ''), theo thứ tự rowid.</summary>
    public IReadOnlyList<string> Keys
    {
        get { lock (_lock) return _keys.ToList(); }
    }

    public int Count { get { lock (_lock) return _keys.Count; } }

    /// <summary>Thay toàn bộ kho (dùng cho Lưu tay). Bỏ dòng trống, giữ thứ tự.</summary>
    public bool ReplaceAll(IEnumerable<string> entries)
    {
        var incoming = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in entries ?? [])
        {
            var e = (raw ?? "").Trim();
            if (e.Length == 0) continue;
            if (seen.Add(e)) incoming.Add(e);
        }
        lock (_lock)
        {
            var prev = _keys.ToList();
            _keys.Clear();
            _keys.AddRange(incoming);
            if (SaveLocked()) return true;
            _keys.Clear();
            _keys.AddRange(prev);
            return false;
        }
    }

    /// <summary>Nạp lại từ DB (dùng khi user bấm "Nạp lại từ DB").</summary>
    public void Reload()
    {
        lock (_lock) LoadLocked();
        Changed?.Invoke();
    }

    public void Load()
    {
        lock (_lock) LoadLocked();
    }

    private void LoadLocked()
    {
        _keys.Clear();
        var (list, _) = _repo.LoadProxyKeys();
        _keys.AddRange(list);
    }

    /// <summary>
    /// Proxy cho 1 acc theo XOAY VÒNG vị trí acc trong kho tài khoản: idx = vị trí trong
    /// <see cref="AccountStore.Accounts"/>; entry = keys[idx % keys.Count].
    /// null nếu kho proxy RỖNG hoặc không thấy acc.
    /// </summary>
    public (string KiotKey, string Manual)? ProxyForAccount(string accountId)
    {
        if (string.IsNullOrEmpty(accountId)) return null;
        var accounts = AccountStore.Shared.Accounts;
        var idx = -1; var pos = 0;
        for (var i = 0; i < accounts.Count; i++)
        {
            if (accounts[i].Disabled) continue;
            if (string.Equals(accounts[i].Id, accountId, StringComparison.Ordinal)) { idx = pos; break; }
            pos++;
        }
        if (idx < 0) return null;
        string entry;
        lock (_lock)
        {
            if (_keys.Count == 0) return null;
            entry = _keys[idx % _keys.Count];
        }
        return ProxyPool.IsDirectProxy(entry) ? ("", entry) : (entry, "");
    }

    private bool SaveLocked()
    {
        try
        {
            var (ok, _) = _repo.SaveProxies(_keys);
            if (ok) Changed?.Invoke();
            return ok;
        }
        catch { return false; }
    }
}
