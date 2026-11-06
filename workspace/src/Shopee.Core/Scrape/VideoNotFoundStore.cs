using System.Text.Json;
using Shopee.Core.Infrastructure;

namespace Shopee.Core.Scrape;

/// <summary>
/// Lưu danh sách các video đã xác nhận KHÔNG CÓ trên Hub (Hub trả 404).
/// Giúp app không phải quét và tải lại các video này mỗi khi Update chạy.
/// Tự động hết hạn sau 24h để nếu máy cào bổ sung video sau này thì vẫn quét lại được.
/// File lưu tại: %AppData%\ShopeeBackend\Workspace\shared\videos-not-on-hub.json
/// </summary>
public sealed class VideoNotFoundStore
{
    private static readonly Lazy<VideoNotFoundStore> _shared = new(() => new VideoNotFoundStore());
    public static VideoNotFoundStore Shared => _shared.Value;

    private static readonly string FilePath = Path.Combine(SuitePaths.ModuleDir("shared"), "videos-not-on-hub.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    private readonly object _lock = new();
    private readonly Dictionary<string, long> _items = new(StringComparer.OrdinalIgnoreCase);

    public int Count
    {
        get
        {
            lock (_lock)
            {
                PurgeExpiredLocked(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                return _items.Count;
            }
        }
    }

    private VideoNotFoundStore() => Load();

    public bool Contains(string remoteName)
    {
        if (string.IsNullOrWhiteSpace(remoteName)) return false;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (_lock)
        {
            if (_items.TryGetValue(remoteName, out var addedAt))
            {
                if (now - addedAt < Ttl.TotalSeconds) return true;
                _items.Remove(remoteName);
            }
            return false;
        }
    }

    public void Add(string remoteName)
    {
        if (string.IsNullOrWhiteSpace(remoteName)) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (_lock) _items[remoteName] = now;
    }

    public void AddRange(IEnumerable<string> remoteNames)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (_lock)
        {
            foreach (var name in remoteNames)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    _items[name] = now;
            }
        }
    }

    public void Remove(string remoteName)
    {
        if (string.IsNullOrWhiteSpace(remoteName)) return;
        lock (_lock) _items.Remove(remoteName);
    }

    public void Clear()
    {
        lock (_lock) _items.Clear();
        Save();
    }

    public void Load()
    {
        lock (_lock)
        {
            _items.Clear();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // Thử load Dictionary (format mới kèm timestamp)
            var dict = JsonAtomicFile.TryLoad<Dictionary<string, long>>(FilePath, JsonOpts);
            if (dict is not null && dict.Count > 0)
            {
                foreach (var (k, v) in dict)
                {
                    if (now - v < Ttl.TotalSeconds)
                        _items[k] = v;
                }
                return;
            }

            // Fallback sang List<string> (format cũ)
            var list = JsonAtomicFile.TryLoad<List<string>>(FilePath, JsonOpts);
            if (list is not null)
            {
                foreach (var item in list)
                    _items[item] = now;
            }
        }
    }

    public void Save()
    {
        Dictionary<string, long> snapshot;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (_lock)
        {
            PurgeExpiredLocked(now);
            snapshot = new Dictionary<string, long>(_items, StringComparer.OrdinalIgnoreCase);
        }
        JsonAtomicFile.Save(FilePath, snapshot, JsonOpts);
    }

    private void PurgeExpiredLocked(long now)
    {
        var expired = _items.Where(kv => now - kv.Value >= Ttl.TotalSeconds).Select(kv => kv.Key).ToList();
        foreach (var k in expired) _items.Remove(k);
    }
}
