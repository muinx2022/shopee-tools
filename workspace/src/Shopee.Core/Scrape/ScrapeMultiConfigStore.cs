using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Shopee.Core.Infrastructure;

namespace Shopee.Core.Scrape;

public sealed class ScrapeMultiConfig
{
    public int? StartRow { get; set; }
    public int? EndRow { get; set; }
    public int MaxProcess { get; set; } = 8;
    public int FrameSize { get; set; } = 10;
    public int RestMinSeconds { get; set; } = 120;
    public int RestMaxSeconds { get; set; } = 240;
    public List<string> SelectedAccountIds { get; set; } = [];
}

/// <summary>
/// Lưu trữ cấu hình tham số chạy cho màn hình Scrape Multi:
/// Từ dòng, Đến dòng, Số Process, Cỡ khung, Thời gian nghỉ, Danh sách tài khoản được chọn.
/// File lưu tại: %AppData%\ShopeeBackend\Workspace\shared\scrape-multi-config.json
/// </summary>
public sealed class ScrapeMultiConfigStore
{
    private static readonly Lazy<ScrapeMultiConfigStore> _shared = new(() => new ScrapeMultiConfigStore());
    public static ScrapeMultiConfigStore Shared => _shared.Value;

    private static readonly string FilePath = Path.Combine(SuitePaths.ModuleDir("shared"), "scrape-multi-config.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly object _lock = new();
    private ScrapeMultiConfig _current = new();

    public ScrapeMultiConfig Current
    {
        get
        {
            lock (_lock) return _current;
        }
    }

    private ScrapeMultiConfigStore() => Load();

    public ScrapeMultiConfig Load()
    {
        lock (_lock)
        {
            var loaded = JsonAtomicFile.TryLoad<ScrapeMultiConfig>(FilePath, JsonOpts);
            _current = loaded ?? new ScrapeMultiConfig();
            return _current;
        }
    }

    public void Save(ScrapeMultiConfig config)
    {
        lock (_lock)
        {
            _current = config;
            JsonAtomicFile.Save(FilePath, _current, JsonOpts);
        }
    }
}
