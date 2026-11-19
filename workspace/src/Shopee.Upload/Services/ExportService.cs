using Shopee.Core.Coordination;
using Shopee.Core.Harvest;
using Shopee.Core.Infrastructure;
using Shopee.Core.MassUpload.Export;
using Shopee.Core.MassUpload.Images;

namespace Shopee.Upload.Services;

/// <summary>Phần chạy thật của panel "Xuất xlsx" (GĐ8). App thật: <see cref="ExportService"/>; test tiêm bản giả.</summary>
public interface IExportService : IAsyncDisposable
{
    /// <summary>Lý do chưa xuất được vì Hub (chưa cấu hình kết nối…); null = sẵn sàng.</summary>
    string? HubProblem { get; }

    /// <summary>Đường dẫn file Hub mà app đọc (để chỉ user chỗ cấu hình).</summary>
    string HubConfigPath { get; }

    Task<MassUploadExportResult> ExportAsync(MassUploadExportRequest request, IProgress<ExportProgress> progress, CancellationToken ct);

    Task<IReadOnlyList<ExportBatchInfo>> ListBatchesAsync(CancellationToken ct = default);

    Task<BatchClearResult> ClearBatchAsync(string batchId, CancellationToken ct = default);

    /// <summary>Tên sheet của file Excel nguồn (đọc chia sẻ, như Harvest). Ném khi không đọc được.</summary>
    Task<IReadOnlyList<string>> ListSheetsAsync(string workbookPath, CancellationToken ct = default);

    /// <summary>Một dòng mô tả file mẫu (loại, ngành, shop) hoặc lỗi đọc.</summary>
    Task<string> DescribeTemplateAsync(string path, CancellationToken ct = default);

    /// <summary>Nút "Làm lại ảnh lỗi": đưa mọi ảnh <c>failed</c> của shop về chờ làm lại. Trả số ảnh.</summary>
    Task<int> RetryFailedImagesAsync(string shopKey, CancellationToken ct = default);

    /// <summary>(GĐ9) Dung lượng Hub: ổ đĩa, kho ảnh gốc, kho ảnh tạm. Ném khi chưa nối Hub / Hub lỗi.</summary>
    Task<SourceImageStatsResponse> GetHubStorageAsync(CancellationToken ct = default);

    /// <summary>(GĐ9) Xoá SẠCH kho ảnh gốc Harvest trên Hub — gọi viên phải hỏi người dùng trước.</summary>
    Task<SourceImageDeleteAllResponse> DeleteAllSourceImagesAsync(CancellationToken ct = default);

    /// <summary>(GĐ9) Số lượt đã xuất (máy này) có ảnh SP là link Hub, chưa bị đánh "kho gốc đã xoá".</summary>
    Task<int> CountBatchesUsingHubSourcesAsync(CancellationToken ct = default);

    /// <summary>(GĐ9) Đánh các lượt đó: kho ảnh gốc trên Hub đã xoá lúc <paramref name="at"/>. Trả số lượt.</summary>
    Task<int> MarkHubSourcesDeletedAsync(DateTimeOffset at, CancellationToken ct = default);
}

/// <summary>
/// Bản thật (GĐ8): sổ <c>upload.db</c> (<see cref="UploadStore"/>), bước đệm ảnh (<see cref="ImagePrepWorker"/> — tải ảnh gốc từ CDN
/// Shopee bằng IP máy, đổi MD5, đưa lên kho ảnh Hub), Hub qua <see cref="CoordinationRuntime.Client"/>. Cấu hình Hub là file RIÊNG
/// của app Harvest (<c>harvest\hub-client.json</c>) — cùng cách app Harvest lấy <see cref="HubClient"/>; App gán kho đó cho
/// <see cref="CoordinationRuntime.ClientStore"/> (<see cref="UseHarvestHubConfig"/>) trước khi init. Worker chỉ chạy khi có lượt xuất.
/// </summary>
public sealed class ExportService : IExportService
{
    private readonly UploadStore _store;
    private readonly HttpClient _fetch;
    private readonly ImagePrepWorker _worker;
    private readonly MassUploadExporter _exporter;
    private readonly Func<HubClient?> _hub;
    private readonly Action<string>? _log;

    /// <summary>Lỗi lúc khởi tạo kết nối Hub (App ghi lại); null = không có. <see cref="HubProblem"/> hiện kèm.</summary>
    public static string? HubInitError { get; set; }

    /// <param name="log">Log một dòng (luồng nền).</param>
    public ExportService(Action<string>? log = null, string? dbPath = null, Func<HubClient?>? hub = null, HttpClient? fetch = null,
        ImagePrepWorkerOptions? workerOptions = null)
    {
        _log = log;
        _hub = hub ?? (() => CoordinationRuntime.Client);
        _store = new UploadStore(dbPath ?? UploadStore.DefaultDbPath(), log: log);
        _fetch = fetch ?? ImagePrepWorker.CreateFetchClient();
        _worker = new ImagePrepWorker(_store, _hub, _fetch, workerOptions, log);
        _exporter = new MassUploadExporter(_store, _worker, async (ids, ct) =>
            _hub() is { } client
                ? await client.GetHarvestedByItemsAsync(ids, ct).ConfigureAwait(false)
                : throw new InvalidOperationException("Chưa nối Hub."), log: log, hubLinks: HubLinksAsync);
    }

    /// <summary>Đường dẫn file cấu hình Hub của app Harvest: <c>&lt;SuitePaths.Root&gt;\harvest\hub-client.json</c>.</summary>
    public static string HarvestHubConfigPath => Path.Combine(SuitePaths.ModuleDir("harvest"), "hub-client.json");

    /// <summary>Gọi MỘT lần lúc khởi động, trước <see cref="CoordinationRuntime.InitForCurrentMode"/>: chỉ dựng client (không
    /// heartbeat / poll / lease), đọc cấu hình Hub RIÊNG của app Harvest — như app Harvest.</summary>
    public static void UseHarvestHubConfig()
    {
        CoordinationRuntime.ClientOnly = true;
        CoordinationRuntime.ClientStore = new HubClientConfigStore(HarvestHubConfigPath);
    }

    public string? HubProblem => _hub() is null
        ? $"Chưa nối Hub — cấu hình Hub ở app Harvest (file {HarvestHubConfigPath}) rồi mở lại app."
          + (HubInitError is { } e ? $" Lỗi lúc nối: {e}" : "")
        : null;

    public string HubConfigPath => HarvestHubConfigPath;

    public Task<MassUploadExportResult> ExportAsync(MassUploadExportRequest request, IProgress<ExportProgress> progress,
        CancellationToken ct) => _exporter.RunAsync(request, progress, ct);

    /// <summary>Đã đánh các lượt "running" sót lại của lần chạy trước (app tắt giữa lượt) chưa.</summary>
    private bool _interruptedMarked;

    public async Task<IReadOnlyList<ExportBatchInfo>> ListBatchesAsync(CancellationToken ct = default)
    {
        if (!_interruptedMarked)
        {
            // Lần đọc đầu (lúc mở app, chưa có lượt nào của phiên này): app chặn chạy 2 bản nên "running" = lượt cũ bị tắt ngang.
            await _store.MarkInterruptedBatchesAsync(ct).ConfigureAwait(false);
            _interruptedMarked = true;
            // Thư mục tạm .dang-xuat-<lượt> còn sót của lượt huỷ / lỗi (app tắt / dọn hụt) → dọn, không được thì báo.
            var all = await _store.ListBatchesAsync(int.MaxValue, ct).ConfigureAwait(false);
            foreach (var m in MassUploadExporter.CleanupStaleStaging(all)) _log?.Invoke("[xuất] " + m);
        }
        return await _store.ListBatchesAsync(ct: ct).ConfigureAwait(false);
    }

    public Task<BatchClearResult> ClearBatchAsync(string batchId, CancellationToken ct = default) =>
        _hub() is { } client
            ? _store.ClearBatchAsync(batchId, client, ct)
            : throw new InvalidOperationException(HubProblem);

    public Task<IReadOnlyList<string>> ListSheetsAsync(string workbookPath, CancellationToken ct = default) =>
        Task.Run(() => HarvestWorkbookReader.ListSheetNames(workbookPath), ct);

    public Task<string> DescribeTemplateAsync(string path, CancellationToken ct = default) => Task.Run(() =>
    {
        var (templates, errors) = ExportTemplates.Load([path]);
        return templates.Count == 1 ? templates[0].Describe() : "lỗi: " + string.Join(" ", errors);
    }, ct);

    public Task<int> RetryFailedImagesAsync(string shopKey, CancellationToken ct = default) => _store.RetryFailedAsync(shopKey, ct);

    /// <summary>(GĐ9) Link ảnh gốc trên Hub cho lượt xuất (HubClient tự chia lô 200 id). Hub trả lỗi → ném để lượt đi đường cũ
    /// kèm cảnh báo.</summary>
    private async Task<HubImageLinks> HubLinksAsync(string shopKey, IReadOnlyList<string> ids, CancellationToken ct)
    {
        var client = _hub() ?? throw new InvalidOperationException("Chưa nối Hub.");
        var (status, body, error) = await client.GetSourceImageLinksAsync(shopKey, ids, ct).ConfigureAwait(false);
        if (body is null)
            throw new HttpRequestException($"Hub trả {(int)status}{(error is null ? "" : " " + error)} khi lấy link ảnh gốc");
        var urls = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in body.Items) urls[item.Id] = item.Url;
        return new HubImageLinks(urls, body.Missing);
    }

    public Task<SourceImageStatsResponse> GetHubStorageAsync(CancellationToken ct = default) =>
        (_hub() ?? throw new InvalidOperationException(HubProblem)).GetSourceImageStatsAsync(ct);

    public Task<SourceImageDeleteAllResponse> DeleteAllSourceImagesAsync(CancellationToken ct = default) =>
        (_hub() ?? throw new InvalidOperationException(HubProblem)).DeleteAllSourceImagesAsync(ct);

    public Task<int> CountBatchesUsingHubSourcesAsync(CancellationToken ct = default) => _store.CountBatchesUsingHubSourcesAsync(ct);

    public Task<int> MarkHubSourcesDeletedAsync(DateTimeOffset at, CancellationToken ct = default) =>
        _store.MarkHubSourcesDeletedAsync(at, ct);

    public async ValueTask DisposeAsync()
    {
        await _worker.StopAsync().ConfigureAwait(false);
        _fetch.Dispose();
    }
}

/// <summary>Bản rỗng khi app không dựng panel xuất (test cũ của GĐ5): không đọc / ghi gì.</summary>
public sealed class DisabledExportService : IExportService
{
    public static readonly DisabledExportService Instance = new();

    public string? HubProblem => "Chưa bật xuất file.";
    public string HubConfigPath => "";

    public Task<MassUploadExportResult> ExportAsync(MassUploadExportRequest request, IProgress<ExportProgress> progress,
        CancellationToken ct) => throw new InvalidOperationException(HubProblem);

    public Task<IReadOnlyList<ExportBatchInfo>> ListBatchesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ExportBatchInfo>>([]);

    public Task<BatchClearResult> ClearBatchAsync(string batchId, CancellationToken ct = default) =>
        throw new InvalidOperationException(HubProblem);

    public Task<IReadOnlyList<string>> ListSheetsAsync(string workbookPath, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<string> DescribeTemplateAsync(string path, CancellationToken ct = default) => Task.FromResult("");

    public Task<int> RetryFailedImagesAsync(string shopKey, CancellationToken ct = default) => Task.FromResult(0);

    public Task<SourceImageStatsResponse> GetHubStorageAsync(CancellationToken ct = default) =>
        throw new InvalidOperationException(HubProblem);

    public Task<SourceImageDeleteAllResponse> DeleteAllSourceImagesAsync(CancellationToken ct = default) =>
        throw new InvalidOperationException(HubProblem);

    public Task<int> CountBatchesUsingHubSourcesAsync(CancellationToken ct = default) => Task.FromResult(0);

    public Task<int> MarkHubSourcesDeletedAsync(DateTimeOffset at, CancellationToken ct = default) => Task.FromResult(0);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
