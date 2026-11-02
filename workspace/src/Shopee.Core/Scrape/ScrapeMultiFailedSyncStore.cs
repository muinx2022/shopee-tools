using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Shopee.Core.Infrastructure;

namespace Shopee.Core.Scrape;

/// <summary>
/// Đại diện cho 1 sản phẩm bị lỗi khi Fan-out đồng bộ sang tài khoản BigSeller phụ.
/// Giữ nguyên Url và JsonData Shopee API đã bóc tách để có thể sync bù trực tiếp mà không cần cào lại Shopee.
/// </summary>
public sealed class FailedSyncItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AccountId { get; set; } = "";
    public string AccountName { get; set; } = "";
    public int Row { get; set; }
    public string Url { get; set; } = "";
    public string JsonData { get; set; } = "";
    public string Error { get; set; } = "";
    public DateTimeOffset FailedAt { get; set; } = DateTimeOffset.Now;
}

/// <summary>
/// Kho lưu trữ các sản phẩm Fan-out sync bị lỗi, lưu tại:
/// %AppData%\ShopeeBackend\Workspace\shared\scrape-multi-failed-sync.json
/// </summary>
public sealed class ScrapeMultiFailedSyncStore
{
    private static readonly Lazy<ScrapeMultiFailedSyncStore> _shared = new(() => new ScrapeMultiFailedSyncStore());
    public static ScrapeMultiFailedSyncStore Shared => _shared.Value;

    private static readonly string FilePath = Path.Combine(SuitePaths.ModuleDir("shared"), "scrape-multi-failed-sync.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly object _lock = new();
    private readonly List<FailedSyncItem> _items = [];

    public event Action? Changed;

    public int Count
    {
        get
        {
            lock (_lock) return _items.Count;
        }
    }

    public int CountFor(string accountId)
    {
        lock (_lock) return _items.Count(x => x.AccountId == accountId);
    }

    private ScrapeMultiFailedSyncStore() => Load();

    public void Load()
    {
        lock (_lock)
        {
            _items.Clear();
            var list = JsonAtomicFile.TryLoad<List<FailedSyncItem>>(FilePath, JsonOpts);
            if (list is not null) _items.AddRange(list);
        }
        Changed?.Invoke();
    }

    private void SaveLocked()
    {
        JsonAtomicFile.Save(FilePath, _items, JsonOpts);
    }

    public void AddOrUpdate(FailedSyncItem item)
    {
        lock (_lock)
        {
            var existing = _items.FirstOrDefault(x => x.AccountId == item.AccountId && x.Row == item.Row);
            if (existing is not null)
            {
                existing.AccountName = item.AccountName;
                existing.Url = item.Url;
                existing.JsonData = item.JsonData;
                existing.Error = item.Error;
                existing.FailedAt = item.FailedAt;
            }
            else
            {
                _items.Add(item);
            }
            SaveLocked();
        }
        Changed?.Invoke();
    }

    public bool Remove(string accountId, int row)
    {
        bool removed;
        lock (_lock)
        {
            removed = _items.RemoveAll(x => x.AccountId == accountId && x.Row == row) > 0;
            if (removed) SaveLocked();
        }
        if (removed) Changed?.Invoke();
        return removed;
    }

    public bool Remove(string id)
    {
        bool removed;
        lock (_lock)
        {
            removed = _items.RemoveAll(x => x.Id == id) > 0;
            if (removed) SaveLocked();
        }
        if (removed) Changed?.Invoke();
        return removed;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
            SaveLocked();
        }
        Changed?.Invoke();
    }

    public IReadOnlyList<FailedSyncItem> GetAll()
    {
        lock (_lock)
        {
            return _items.Select(x => new FailedSyncItem
            {
                Id = x.Id,
                AccountId = x.AccountId,
                AccountName = x.AccountName,
                Row = x.Row,
                Url = x.Url,
                JsonData = x.JsonData,
                Error = x.Error,
                FailedAt = x.FailedAt
            }).ToList();
        }
    }
}
