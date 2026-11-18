using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shopee.Core.Coordination;
using Shopee.Core.Harvest;
using Shopee.Core.Infrastructure;
using Shopee.Workspace.Infrastructure;

namespace Shopee.Workspace.Modules.Harvest;

/// <summary>Cổng cấu hình Hub của app Harvest (tách để test không đụng file thật / kết nối thật).</summary>
public interface IHarvestHubPort
{
    /// <summary>Cấu hình client→Hub đang lưu (file RIÊNG của Harvest — <see cref="HarvestHubPort.ConfigPath"/>).</summary>
    HubClientConfig Current { get; }
    /// <summary>Đường dẫn file cấu hình (cho câu báo lỗi).</summary>
    string ConfigPath { get; }
    /// <summary>true = đã ghi xuống đĩa.</summary>
    bool Save(HubClientConfig config);
    /// <summary>Nối lại NGAY theo cấu hình vừa lưu (không mở lại app) + đánh thức worker đẩy Hub. true = đã có client Hub.</summary>
    bool Reconnect();
    bool Connected { get; }
    string MachineId { get; }
}

/// <summary>Cổng thật. Cấu hình Hub của Harvest là file RIÊNG <c>&lt;SuitePaths.Root&gt;\harvest\hub-client.json</c> (user chốt V2):
/// lưu ở Harvest KHÔNG tạo / sửa <c>hub-client.json</c> chung mà app Scrape / Update đọc. App Harvest gán kho này cho
/// <see cref="CoordinationRuntime.ClientStore"/> lúc khởi động (App.xaml.cs) nên init / Reconnect đọc đúng file.</summary>
public sealed class HarvestHubPort : IHarvestHubPort
{
    public static readonly HarvestHubPort Instance = new();

    private static readonly Lazy<HubClientConfigStore> _store =
        new(() => new HubClientConfigStore(Path.Combine(SuitePaths.ModuleDir("harvest"), "hub-client.json")));

    /// <summary>Kho cấu hình Hub riêng của Harvest.</summary>
    public static HubClientConfigStore Store => _store.Value;

    public string ConfigPath => Store.FilePath;
    public HubClientConfig Current => Store.Current;
    public bool Save(HubClientConfig config) => Store.Save(config);

    public bool Reconnect()
    {
        var ok = CoordinationRuntime.Reconnect();
        if (HarvestRuntime.Current is { } rt) _ = rt.OnHubReconnectedAsync();   // N4: bỏ lùi dần "chưa kết nối", đẩy ngay
        return ok;
    }

    public bool Connected => CoordinationRuntime.Client is not null;
    public string MachineId => MachineIdentity.Shared.MachineId;
}

/// <summary>Kết quả lưu + kiểm tra Hub (màn Lấy dữ liệu dùng để báo trạng thái). <see cref="Ok"/> = đã lưu, đã nối, token đúng.</summary>
public sealed record HarvestHubSaveResult(bool Ok, string Message);

/// <summary>Kiểm tra kết nối Hub: <c>/health</c> (tới được Hub chưa) rồi một lệnh CÓ token (<c>stats</c>) để biết token đúng.
/// URL theo luật chung với extension (<see cref="HarvestHubUrl"/>): URL http ra ngoài máy thì CHỈ gọi <c>/health</c>, KHÔNG gửi
/// token. Không ném; KHÔNG đưa token vào câu báo.</summary>
public static class HarvestHubCheck
{
    public static async Task<(bool Ok, string Message)> CheckAsync(string baseUrl, string token, HttpMessageHandler? handler = null,
        CancellationToken ct = default)
    {
        var valid = HarvestHubUrl.TryNormalize(baseUrl, out var origin, out var error, out var httpOutside);
        if (!valid && !httpOutside) return (false, error);
        var target = valid ? origin : new Uri(baseUrl.Trim()).GetLeftPart(UriPartial.Authority);
        using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.BaseAddress = new Uri(target + "/");
        http.Timeout = TimeSpan.FromSeconds(15);
        try
        {
            using var health = await http.GetAsync(HubRoutes.Health.TrimStart('/'), ct).ConfigureAwait(false);
            if (!health.IsSuccessStatusCode) return (false, $"Hub trả {(int)health.StatusCode} ở /health — kiểm tra lại URL.");
            if (httpOutside) return (false, error + " Đã KHÔNG gửi token.");
            if (string.IsNullOrWhiteSpace(token)) return (false, "Tới được Hub nhưng chưa có token cho URL này — nhập token.");
            using var req = new HttpRequestMessage(HttpMethod.Get, HubRoutes.HarvestedProductsStats.TrimStart('/'));
            req.Headers.TryAddWithoutValidation("X-Api-Token", token);
            using var stats = await http.SendAsync(req, ct).ConfigureAwait(false);
            return (int)stats.StatusCode switch
            {
                >= 200 and < 300 => (true, "Kết nối Hub tốt (token đúng)."),
                401 or 403 => (false, "Tới được Hub nhưng token sai."),
                _ => (false, $"Tới được Hub nhưng lệnh có token trả {(int)stats.StatusCode}."),
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return (false, "Hub không trả lời (quá giờ)."); }
        catch (HttpRequestException ex) { return (false, "Không tới được Hub: " + ex.Message); }
    }
}

/// <summary>
/// Mục "Kết nối Hub" trong tab Cài đặt của app Harvest: URL, token (ô mật khẩu — token đã lưu KHÔNG hiện lại; để trống = giữ
/// nguyên, nhưng CHỈ khi URL không đổi), mã máy (chỉ đọc), Lưu (ghi file cấu hình Hub RIÊNG của Harvest, nối lại ngay rồi tự kiểm tra),
/// Kiểm tra kết nối, Lấy cấu hình từ Shopee Suite (chỉ khi người dùng bấm — không đọc ngầm file của app khác). Token không bao giờ
/// ra log / câu báo; URL theo luật chung với extension (<see cref="HarvestHubUrl"/>).
/// </summary>
public sealed partial class HarvestHubViewModel : ObservableObject
{
    private readonly IHarvestHubPort _port;
    private readonly string _suiteConfigPath;
    private readonly HttpMessageHandler? _checkHandler;
    // Cấu hình lấy từ Shopee Suite, chờ người dùng bấm Lưu (token không hiện ra ô). Chỉ dùng khi URL trên ô vẫn là URL đã lấy.
    private (string Url, string Token)? _imported;

    /// <summary>Đã lưu (kèm kết quả kiểm tra) — màn Lấy dữ liệu cập nhật dòng trạng thái Hub + quét lại.</summary>
    public event Action<HarvestHubSaveResult>? Saved;

    public HarvestHubViewModel(IHarvestHubPort? port = null, string? suiteConfigPath = null, HttpMessageHandler? checkHandler = null)
    {
        _port = port ?? HarvestHubPort.Instance;
        _suiteConfigPath = suiteConfigPath ?? DefaultSuiteConfigPath();
        _checkHandler = checkHandler;
        var cur = _port.Current;
        BaseUrl = cur.IsConfigured ? cur.BaseUrl : HubDefaults.BaseUrl;
        Status = _port.Connected ? $"Đã nối Hub: {cur.BaseUrl}"
            : cur.IsConfigured ? $"Chưa nối được Hub ({cur.BaseUrl}) — kiểm tra URL / token rồi Lưu lại." : "Chưa nối Hub — nhập URL + token rồi Lưu.";
        StatusOk = _port.Connected;
        RefreshNotes();
    }

    /// <summary><c>%AppData%\ShopeeSuite\hub-client.json</c> — cấu hình Hub của app Shopee Suite (nếu máy có cài).</summary>
    public static string DefaultSuiteConfigPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShopeeSuite", "hub-client.json");

    public string MachineId => _port.MachineId;

    /// <summary>Câu giới thiệu: nói rõ file cấu hình nằm đâu (RIÊNG của Harvest).</summary>
    public string ConfigNote => $"Lưu vào {_port.ConfigPath} — file riêng của app Harvest, không đổi cấu hình Hub của app Scrape / Update.";

    [ObservableProperty] private string _baseUrl = "";
    /// <summary>Token mới gõ (ô mật khẩu). Rỗng = giữ token đã lưu / token vừa lấy từ Shopee Suite — chỉ khi URL không đổi.</summary>
    [ObservableProperty] private string _token = "";
    [ObservableProperty] private string _status = "";
    /// <summary>Trạng thái tốt (đã nối + kiểm tra đạt) → chữ xanh; còn lại → chữ cảnh báo.</summary>
    [ObservableProperty] private bool _statusOk;
    [ObservableProperty] private string _checkResult = "";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _tokenNote = "";
    /// <summary>URL sai luật (http ra ngoài máy / không phải URL) — rỗng = ổn.</summary>
    [ObservableProperty] private string _urlWarning = "";
    /// <summary>URL khác Hub mặc định → nhắc cấp quyền ở trang Cài đặt của extension.</summary>
    [ObservableProperty] private string _permissionNote = "";

    partial void OnBaseUrlChanged(string value) => RefreshNotes();
    partial void OnTokenChanged(string value) => RefreshNotes();

    private string SavedUrl()
    {
        var cur = _port.Current;
        return cur.IsConfigured && HarvestHubUrl.TryNormalize(cur.BaseUrl, out var o, out _, out _) ? o : "";
    }

    private void RefreshNotes()
    {
        IsConnected = _port.Connected;
        var valid = HarvestHubUrl.TryNormalize(BaseUrl, out var origin, out var error, out _);
        UrlWarning = valid ? "" : error;
        PermissionNote = valid && HarvestHubUrl.NeedsExtensionPermission(origin)
            ? $"URL khác Hub mặc định: mở trang Cài đặt của extension Shopee Harvest (chuột phải biểu tượng → Tùy chọn), nhập {origin} và bấm Lưu để cấp quyền — không thì extension không gửi được dữ liệu lên Hub này."
            : "";
        var saved = _port.Current;
        TokenNote = !string.IsNullOrWhiteSpace(Token) ? ""
            : valid && _imported is { } imp && imp.Url == origin ? "Token đã lấy từ Shopee Suite (ẩn) — bấm Lưu để dùng."
            : valid && origin == SavedUrl() && !string.IsNullOrWhiteSpace(saved.ApiToken) ? "Đã có token (ẩn) — để trống nếu giữ nguyên."
            : !string.IsNullOrWhiteSpace(saved.ApiToken) ? "URL khác URL đã lưu — nhập token cho Hub này (token cũ KHÔNG gửi sang URL mới)."
            : "Chưa có token.";
    }

    /// <summary>Token sẽ dùng cho <paramref name="origin"/>: gõ mới → token mới; token lấy từ Suite CHỈ khi đúng URL đã lấy; token đã
    /// lưu CHỈ khi đúng URL đã lưu (đổi URL thì không tự gửi token cũ sang Hub khác).</summary>
    private string EffectiveToken(string origin)
    {
        if (!string.IsNullOrWhiteSpace(Token)) return Token.Trim();
        if (_imported is { } imp && imp.Url == origin) return imp.Token;
        return origin.Length > 0 && origin == SavedUrl() ? _port.Current.ApiToken ?? "" : "";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!HarvestHubUrl.TryNormalize(BaseUrl, out var url, out var error, out _)) { Fail(error); return; }
        var token = EffectiveToken(url);
        if (string.IsNullOrWhiteSpace(token)) { Fail("Chưa có token cho URL này — nhập token Hub rồi Lưu."); return; }
        if (!_port.Save(new HubClientConfig { Enabled = true, BaseUrl = url, ApiToken = token }))
        {
            Fail($"Không lưu được cấu hình Hub vào {_port.ConfigPath} (ổ đầy / không có quyền ghi?) — chưa đổi kết nối.");
            Saved?.Invoke(new HarvestHubSaveResult(false, "chưa lưu được cấu hình Hub"));
            return;
        }
        BaseUrl = url;
        Token = "";
        _imported = null;
        var connected = _port.Reconnect();
        IsConnected = connected;
        // N1: lưu xong tự kiểm tra — token sai thì báo rõ, không xanh.
        CheckResult = "Đang kiểm tra…";
        var (ok, message) = connected ? await HarvestHubCheck.CheckAsync(url, token, _checkHandler) : (false, "chưa nối được Hub");
        CheckResult = message;
        var good = connected && ok;
        Status = good ? $"Đã lưu và nối Hub: {url}" : $"Đã lưu, nhưng Hub chưa dùng được: {message}";
        StatusOk = good;
        RefreshNotes();
        Saved?.Invoke(new HarvestHubSaveResult(good, message));
    }

    private void Fail(string message)
    {
        Status = message;
        StatusOk = false;
    }

    [RelayCommand]
    private async Task CheckAsync()
    {
        CheckResult = "Đang kiểm tra…";
        var origin = HarvestHubUrl.TryNormalize(BaseUrl, out var o, out _, out _) ? o : "";
        var (_, message) = await HarvestHubCheck.CheckAsync(BaseUrl, EffectiveToken(origin), _checkHandler);
        CheckResult = message;
    }

    /// <summary>Đọc <c>%AppData%\ShopeeSuite\hub-client.json</c> (chỉ khi người dùng bấm) → điền URL, giữ token ẩn chờ Lưu. Ba câu báo
    /// khác nhau: thiếu file / file hỏng / thiếu URL hoặc token.</summary>
    [RelayCommand]
    private void ImportFromSuite()
    {
        if (!File.Exists(_suiteConfigPath)) { Fail($"Không thấy file cấu hình Hub của Shopee Suite ({_suiteConfigPath})."); return; }
        var suite = JsonAtomicFile.TryLoad<HubClientConfig>(_suiteConfigPath);
        if (suite is null) { Fail($"File cấu hình Hub của Shopee Suite bị hỏng (không đọc được JSON): {_suiteConfigPath}."); return; }
        if (string.IsNullOrWhiteSpace(suite.BaseUrl)) { Fail("File cấu hình Hub của Shopee Suite không có URL Hub — nhập tay."); return; }
        if (string.IsNullOrWhiteSpace(suite.ApiToken)) { Fail("File cấu hình Hub của Shopee Suite không có token — nhập token tay."); return; }
        if (!HarvestHubUrl.TryNormalize(suite.BaseUrl, out var url, out var error, out _))
        {
            BaseUrl = suite.BaseUrl.Trim();
            Fail("URL Hub trong Shopee Suite không dùng được: " + error);
            return;
        }
        Token = "";
        _imported = (url, suite.ApiToken.Trim());
        BaseUrl = url;
        RefreshNotes();
        Status = "Đã lấy URL + token từ Shopee Suite — bấm Lưu để nối Hub.";
        StatusOk = false;
    }
}
