using Shopee.Core.Infrastructure;

namespace Shopee.Core.Coordination;

/// <summary>
/// Cấu hình để máy này KẾT NỐI tới Hub (server đồng bộ/điều phối). Cục bộ-theo-máy, KHÔNG đồng bộ
/// (mỗi máy có thể trỏ URL/token khác nhau). Rỗng/tắt = chạy single-machine như cũ.
/// </summary>
public sealed class HubClientConfig
{
    public bool Enabled { get; set; }
    /// <summary>URL gốc của Hub, ví dụ https://api.schedra.net (qua Cloudflare Tunnel).</summary>
    public string BaseUrl { get; set; } = HubDefaults.BaseUrl;
    /// <summary>API token khớp với token Hub đặt (gửi ở header X-Api-Token).</summary>
    public string ApiToken { get; set; } = HubDefaults.ApiToken;

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(BaseUrl);

    public HubClientConfig Clone() => new() { Enabled = Enabled, BaseUrl = BaseUrl, ApiToken = ApiToken };
}

/// <summary>Kho cấu hình client→Hub (local-only). <see cref="Shared"/> = <c>&lt;SuitePaths.Root&gt;\hub-client.json</c>
/// (<c>%AppData%\ShopeeBackend\Workspace\hub-client.json</c>, hoặc theo <c>data-dir.txt</c>) — app Scrape / Update dùng. Kho
/// khác (vd app Harvest: <c>harvest\hub-client.json</c>) dựng bằng <see cref="HubClientConfigStore(string)"/>.</summary>
public sealed class HubClientConfigStore
{
    private static readonly Lazy<HubClientConfigStore> _shared = new(() => new HubClientConfigStore(SuitePaths.RootFile("hub-client.json")));
    public static HubClientConfigStore Shared => _shared.Value;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly object _lock = new();
    private HubClientConfig _config = new();

    public event Action? Changed;

    /// <summary>Đường dẫn file cấu hình của kho này.</summary>
    public string FilePath { get; }

    public HubClientConfigStore(string filePath)
    {
        FilePath = filePath;
        Load();
    }

    public HubClientConfig Current { get { lock (_lock) return _config.Clone(); } }

    public void Load()
    {
        lock (_lock)
        {
            if (!File.Exists(FilePath)) return;   // chưa có file → GIỮ bản đang có (khác file hỏng → về mặc định)
            _config = JsonAtomicFile.TryLoad<HubClientConfig>(FilePath) ?? new HubClientConfig();
        }
    }

    /// <summary>Ghi cấu hình. true = đã xuống đĩa; false = ghi hỏng (không ném — caller cần báo người dùng thì xem kết quả). Bản
    /// trong RAM vẫn đổi như cũ.</summary>
    public bool Save(HubClientConfig config)
    {
        var ok = false;
        try
        {
            string json;
            lock (_lock) { _config = config.Clone(); json = JsonSerializer.Serialize(_config, JsonOpts); }
            ok = JsonAtomicFile.SaveText(FilePath, json);   // serialize trong lock, ghi đĩa ngoài lock (giữ như cũ)
        }
        catch { }
        Changed?.Invoke();
        return ok;
    }
}
