using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shopee.Core.Coordination;
using Shopee.Core.MassUpload.Export;
using Shopee.Core.MassUpload.Images;
using Shopee.Upload.Forms;
using Shopee.Upload.Services;

namespace Shopee.Upload;

/// <summary>Một file mẫu của shop đang chọn, kèm mô tả đọc từ file (loại / ngành / shop) hoặc lỗi.</summary>
public sealed partial class TemplateRow(string path) : ObservableObject
{
    public string Path { get; } = path;

    public string FileName => System.IO.Path.GetFileName(Path);

    [ObservableProperty] private string _info = "đang đọc…";
}

/// <summary>Một lượt xuất trong danh sách "Ảnh tạm theo lượt".</summary>
public sealed class BatchRow(ExportBatchInfo info)
{
    public ExportBatchInfo Info { get; } = info;

    public string BatchId => Info.BatchId;

    public string When => Info.CreatedAt.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);

    public string Shop => string.IsNullOrWhiteSpace(Info.ShopLabel) ? Info.ShopKey : Info.ShopLabel;

    public int Images => Info.Images;

    public string State => Info.State switch
    {
        ExportBatchState.Done => $"xong · {Info.Products} SP · {Info.Files} file",
        ExportBatchState.Cancelled => "đã huỷ",
        ExportBatchState.Failed => "lỗi",
        ExportBatchState.Running => "đang chạy",
        _ => Info.State,
    };

    public bool Cleared => Info.ClearedAt is not null;

    public string ClearedText => Cleared ? "rồi" : "chưa";

    /// <summary>Hạn THẬT của lượt (ảnh sớm hết hạn nhất trên Hub mà file trỏ tới) — <see cref="ExportBatchInfo.ExpiresAt"/>.</summary>
    public string Expiry => ExpiryText(Info, DateTimeOffset.Now);

    internal static string ExpiryText(ExportBatchInfo info, DateTimeOffset now)
    {
        if (info.HubSourcesDeletedAt is { } gone)
            return $"ảnh gốc trên Hub đã xoá lúc {Short(gone)} — file của lượt này sẽ hỏng ảnh nếu chưa đẩy";
        if (info.ClearedAt is not null) return "";
        if (info.ExpiresAt is null && info.HubImages > 0)
        {
            // GĐ9: mọi ảnh SP là link kho ảnh gốc Hub (không hạn); chỉ ảnh bảng size nằm kho tạm có hạn.
            var size = info.SizeChartExpiresAt is not { } se ? ""
                : se <= now ? " · ảnh bảng size trên Hub đã hết hạn — xuất lại"
                : $" · ảnh bảng size: đẩy lên Shopee trước {Short(se)}";
            return "không hạn (ảnh SP nằm trên Hub)" + size;
        }
        if (info.ExpiresAt is not { } at) return info.State == ExportBatchState.Done ? "chưa rõ hạn ảnh" : "";
        return at <= now
            ? "ảnh trên Hub đã hết hạn — đừng đẩy file này nữa, xuất lại"
            : $"đẩy lên Shopee trước {Short(at)}";
    }

    internal static string Short(DateTimeOffset at) => at.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>
/// GĐ8 — panel "Xuất xlsx" của shop đang chọn: nguồn (file Excel + sheet + dải dòng), file mẫu, ảnh bảng size, thư mục ra, nút Xuất
/// (tiến trình + Huỷ), kết quả; và danh sách lượt với nút "Xoá ảnh tạm của lượt". Lượt xuất chạy theo giá trị ĐANG TRÊN FORM (chưa cần
/// lưu), như nút mở Brave. App không truy cập Shopee.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Hạn ảnh trên kho ảnh Hub (ngày).</summary>
    internal const int HubImageDays = 7;

    internal const string ClearWarning =
        "Chỉ xoá SAU KHI đã đẩy file của lượt này lên Shopee và Shopee báo thành công (Shopee đã lưu ảnh về kho của họ). Xoá trước thì " +
        "Shopee không tải được ảnh. Phải đẩy file lên Shopee trước hạn của lượt (hiện dưới bảng) — quá hạn Hub tự xoá ảnh.";

    private IExportService _export = DisabledExportService.Instance;
    private IExportPrompts? _exportPrompts;
    private CancellationTokenSource? _exportCts;
    private Task? _exportTask;

    /// <summary>Lượt xuất đang nhận báo tiến độ; null = không lượt nào (báo tới muộn bị bỏ).</summary>
    private object? _exportRun;

    public ObservableCollection<string> SheetNames { get; } = [];

    public ObservableCollection<TemplateRow> Templates { get; } = [];

    public ObservableCollection<BatchRow> Batches { get; } = [];

    [ObservableProperty] private TemplateRow? _selectedTemplate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearBatchCommand))]
    private BatchRow? _selectedBatch;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand), nameof(CancelExportCommand), nameof(ClearBatchCommand),
        nameof(DeleteAllSourceImagesCommand))]
    private bool _isExporting;



    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand), nameof(ClearBatchCommand))]
    private bool _isClearing;

    [ObservableProperty] private string _exportStatus = "Chọn shop, file Excel nguồn, sheet và file mẫu rồi bấm Xuất xlsx.";
    [ObservableProperty] private string _exportStepText = "";
    [ObservableProperty] private double _exportPercent;
    [ObservableProperty] private string _exportResultText = "";
    [ObservableProperty] private string _sheetStatus = "";

    /// <summary>Thư mục của lượt xuất xong gần nhất (nút "Mở thư mục").</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenExportFolderCommand))]
    private string? _lastExportFolder;

    public string ClearWarningText => ClearWarning;

    private void InitExport(IExportService? export, IExportPrompts? prompts)
    {
        _export = export ?? DisabledExportService.Instance;
        _exportPrompts = prompts;
        _ = RefreshBatchesAsync();
        _ = RefreshHubStorageAsync();
    }

    private IExportPrompts Prompts => _exportPrompts ??= new WpfExportPrompts();

    // ─────────────── shop đang chọn: sheet + mẫu ───────────────

    private AccountForm? _watched;

    partial void OnSelectedChanged(AccountForm? value)
    {
        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnSelectedPropertyChanged;
            _watched.TemplatePaths.CollectionChanged -= OnSelectedTemplatesChanged;
        }
        _watched = value;
        if (value is not null)
        {
            value.PropertyChanged += OnSelectedPropertyChanged;
            value.TemplatePaths.CollectionChanged += OnSelectedTemplatesChanged;
        }
        RebuildTemplates();
        _ = LoadSheetsAsync();
        ExportCommand.NotifyCanExecuteChanged();
        PickWorkbookCommand.NotifyCanExecuteChanged();
        AddTemplatesCommand.NotifyCanExecuteChanged();
    }

    private void OnSelectedPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountForm.WorkbookPath)) _ = LoadSheetsAsync();
    }

    private void OnSelectedTemplatesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        RebuildTemplates();

    private void RebuildTemplates()
    {
        Templates.Clear();
        foreach (var path in Selected?.TemplatePaths ?? [])
        {
            var row = new TemplateRow(path);
            Templates.Add(row);
            _ = DescribeAsync(row);
        }
    }

    private async Task DescribeAsync(TemplateRow row)
    {
        string info;
        try { info = await _export.DescribeTemplateAsync(row.Path); }
        catch (Exception ex) { info = "lỗi: " + ex.Message; }
        _ui(() => row.Info = info);
    }

    private int _sheetLoad;

    /// <summary>Đọc danh sách sheet của file Excel nguồn (shop đang chọn). Đọc lỗi → báo dưới ô, danh sách rỗng (vẫn gõ tay được).</summary>
    internal async Task LoadSheetsAsync()
    {
        var mine = ++_sheetLoad;
        var path = Selected?.WorkbookPath?.Trim() ?? "";
        SheetNames.Clear();
        if (path.Length == 0) { SheetStatus = ""; return; }
        if (!File.Exists(path)) { SheetStatus = "Không thấy file."; return; }
        SheetStatus = "Đang đọc sheet…";
        IReadOnlyList<string> names;
        try { names = await _export.ListSheetsAsync(path); }
        catch (Exception ex)
        {
            if (mine == _sheetLoad) SheetStatus = $"Không đọc được sheet: {ex.Message}";
            return;
        }
        if (mine != _sheetLoad) return;
        foreach (var n in names) SheetNames.Add(n);
        SheetStatus = names.Count == 0 ? "File không có sheet nào." : $"{names.Count} sheet.";
    }

    [RelayCommand(CanExecute = nameof(HasSelected))]
    private void PickWorkbook()
    {
        if (Selected is not { } f) return;
        if (Prompts.PickWorkbook(f.WorkbookPath) is { } path) f.WorkbookPath = path;
    }

    [RelayCommand(CanExecute = nameof(HasSelected))]
    private void AddTemplates()
    {
        if (Selected is not { } f) return;
        foreach (var path in Prompts.PickTemplates())
            if (!f.TemplatePaths.Contains(path, StringComparer.OrdinalIgnoreCase)) f.TemplatePaths.Add(path);
    }

    [RelayCommand]
    private void RemoveTemplate(TemplateRow? row)
    {
        row ??= SelectedTemplate;
        if (Selected is not { } f || row is null) return;
        var i = f.TemplatePaths.ToList().FindIndex(p => string.Equals(p, row.Path, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) f.TemplatePaths.RemoveAt(i);
    }

    [RelayCommand]
    private void PickSizeChart()
    {
        if (Prompts.PickImage(Settings.SizeChartImagePath) is { } path) Settings.SizeChartImagePath = path;
    }

    [RelayCommand]
    private void PickExportFolder()
    {
        if (Prompts.PickFolder(Settings.ExportDirectory) is { } path) Settings.ExportDirectory = path;
    }

    // ─────────────── xuất ───────────────

    private bool CanExport() => Selected is not null && !IsExporting && !IsClearing && !IsDeletingSources;

    /// <summary>Xuất xlsx cho shop đang chọn theo giá trị ĐANG TRÊN FORM. Ô nhập sai của shop → không chạy, báo lý do.</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportAsync()
    {
        if (Selected is not { } f || IsExporting || IsClearing) return Task.CompletedTask;
        _exportTask = RunExportAsync(f);
        return _exportTask;
    }

    private async Task RunExportAsync(AccountForm f)
    {
        var parseErrors = new List<string>();
        var account = SellerFormMapper.ToAccount(f, parseErrors);
        var settings = SellerFormMapper.ToSettings(Settings, _loaded.Settings, []);
        Log($"── {f.DisplayName} ({f.Id}): xuất xlsx ──");
        foreach (var e in account.Defaults.Validate()) parseErrors.Add($"{f.DisplayName}: {e}");
        if (parseErrors.Count > 0)
        {
            foreach (var e in parseErrors) Log("Lỗi: " + e);
            ExportStatus = $"Không xuất — {parseErrors[0]}";
            return;
        }
        if (_export.HubProblem is { } hub)
        {
            Log(hub);
            ExportStatus = hub;
            return;
        }

        var batchId = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
        // shopKey = khoá ảnh của shop (GĐ9, ngẫu nhiên 128-bit, không đổi) — không dùng Id seller-N (chỉ duy nhất trên một máy).
        var request = new MassUploadExportRequest(batchId, account.ImageKey ?? account.Id, account.Label, account.Source.WorkbookPath ?? "",
            account.Source.Sheet, account.Source.FromRow, account.Source.ToRow, account.TemplatePaths ?? [],
            settings.SizeChartImagePath ?? "", account.Defaults, settings.ExportDirectory ?? "",
            MissingHubImages: Settings.SkipMissingHubImages ? MissingHubImagePolicy.SkipProduct : MissingHubImagePolicy.DownloadFromCdn);

        IsExporting = true;
        ExportResultText = "";
        ExportPercent = 0;
        ExportStatus = $"Đang xuất cho {f.DisplayName}…";
        _exportCts = new CancellationTokenSource();
        // Báo tiến độ tới muộn (đã xếp hàng từ luồng nền trước khi lượt kết thúc) không được đè trạng thái kết quả / "Đã huỷ".
        var run = new object();
        _exportRun = run;
        var progress = new Progress<ExportProgress>(p => { if (ReferenceEquals(_exportRun, run)) OnExportProgress(p); });
        try
        {
            MassUploadExportResult result;
            try { result = await _export.ExportAsync(request, progress, _exportCts.Token); }
            catch (TemplateShopChangedException changed)
            {
                // Mẫu có shop_id khác lần trước: chưa làm gì — hỏi, đồng ý thì chạy lại (cùng mã lượt, lượt chưa được tạo).
                if (!Prompts.Confirm("Mẫu của shop khác?", $"{changed.Message}\n\nCó thể đã chọn nhầm file mẫu tải từ shop khác. " +
                                     "Vẫn xuất bằng mẫu này?"))
                {
                    _exportRun = null;
                    ExportStatus = $"Không xuất — {changed.Message} Chọn lại file mẫu của đúng shop.";
                    Log($"Không xuất ({batchId}): {changed.Message}");
                    return;
                }
                Log($"Xác nhận dùng mẫu shop_id {changed.TemplateShopId} (lần trước {changed.PreviousShopId}).");
                result = await _export.ExportAsync(request with { AllowTemplateShopChange = true }, progress, _exportCts.Token);
            }
            _exportRun = null;
            ExportPercent = 100;
            ShowResult(f, result);
            if (result.Files.Count > 0)
            {
                LastExportFolder = request.OutputDirectory;
                Prompts.OpenFolder(request.OutputDirectory);
            }
        }
        catch (OperationCanceledException) when (_exportCts.IsCancellationRequested)
        {
            // CHỈ khi user bấm Huỷ. Hết giờ chờ HTTP (TaskCanceledException) rơi xuống nhánh lỗi bên dưới.
            _exportRun = null;
            ExportStatus = "Đã huỷ — không để lại file nào. Ảnh đã đưa lên Hub ghi theo lượt, xoá ở mục \"Ảnh tạm theo lượt\".";
            ExportStepText = "";
            Log($"Đã huỷ lượt {batchId}.");
        }
        catch (MassUploadExportException ex)
        {
            _exportRun = null;
            ExportStatus = "Không xuất: " + ex.Message;
            Log($"Không xuất ({batchId}): {ex.Message}");
        }
        catch (Exception ex)
        {
            _exportRun = null;
            ExportStatus = "Lỗi: " + ex.Message;
            Log($"Lỗi khi xuất ({batchId}): {ex}");
        }
        finally
        {
            _exportCts.Dispose();
            _exportCts = null;
            IsExporting = false;
            await RefreshBatchesAsync();
        }
    }

    private void OnExportProgress(ExportProgress p)
    {
        // Phần trăm theo bước: chuẩn bị 0–5, nguồn 5–10, Hub 10–20, ảnh 20–85, ghi 85–100.
        var (lo, hi) = p.Step switch
        {
            ExportStep.Prepare => (0d, 5d),
            ExportStep.ReadSource => (5d, 10d),
            ExportStep.FetchHub => (10d, 20d),
            ExportStep.Images => (20d, 85d),
            ExportStep.Write => (85d, 100d),
            _ => (100d, 100d),
        };
        ExportPercent = p.Total > 0 ? lo + (hi - lo) * Math.Clamp((double)p.Done / p.Total, 0, 1) : lo;
        ExportStepText = StepName(p.Step) + (p.Total > 0 ? $" {p.Done}/{p.Total}" : "");
        ExportStatus = p.Message;
    }

    internal static string StepName(ExportStep s) => s switch
    {
        ExportStep.Prepare => "Chuẩn bị",
        ExportStep.ReadSource => "Đọc nguồn",
        ExportStep.FetchHub => "Lấy Hub",
        ExportStep.Images => "Ảnh",
        ExportStep.Write => "Ghi file",
        _ => "Xong",
    };

    private void ShowResult(AccountForm f, MassUploadExportResult r)
    {
        var summary = $"{f.DisplayName}: xuất {r.ExportedItems} SP vào {r.Files.Count} file · chưa cào {r.NotHarvested.Count} · " +
                      $"thiếu mẫu {r.MissingTemplates.Sum(m => m.Items.Count)} · lỗi ảnh {r.ImageSkipped.Count} SP ({r.FailedImages} ảnh) · " +
                      $"bỏ khác {r.Skipped.Count} · link không có mã SP {r.NoItemId.Count}" +
                      (r.DuplicateRows > 0 ? $" · dòng trùng SP {r.DuplicateRows}" : "") +
                      (r.ImagesDroppedPending > 0 ? $" · ảnh chưa xong đã bỏ {r.ImagesDroppedPending}" : "") +
                      $" · cảnh báo cắt {r.Warnings.Count} · thiếu thuộc tính {r.NeedsFix.Count}" +
                      $" · ảnh: {r.HubImages} từ Hub, {r.CdnImages} tải CDN." +
                      (r.Files.Count > 0 ? " " + DeadlineText(r) : "");
        ExportStatus = summary;
        ExportStepText = "Xong";
        var lines = DescribeResult(r);
        ExportResultText = string.Join(Environment.NewLine, lines);
        Log(summary);
        foreach (var line in lines) Log("  " + line);
    }

    /// <summary>Câu hạn của lượt: có ảnh đi kho tạm → "đẩy lên Shopee trước …"; mọi ảnh SP là link Hub → "không hạn" + hạn riêng của
    /// ảnh bảng size; không biết → rỗng.</summary>
    internal static string DeadlineText(MassUploadExportResult r)
    {
        if (r.ImagesExpireAt is { } exp) return $"Đẩy lên Shopee trước {BatchRow.Short(exp)}.";
        if (r.HubImages > 0)
            return "Ảnh SP nằm trên Hub (không hạn)" +
                   (r.SizeChartExpiresAt is { } se ? $"; ảnh bảng size: đẩy lên Shopee trước {BatchRow.Short(se)}." : ".");
        return "";
    }

    /// <summary>Báo cáo xuất, mỗi dòng một ý (hàm thuần — test được).</summary>
    internal static List<string> DescribeResult(MassUploadExportResult r)
    {
        var lines = new List<string>();
        foreach (var file in r.Files) lines.Add("File: " + file);
        if (r.Files.Count > 0 && DeadlineText(r) is { Length: > 0 } deadline) lines.Add("Hạn ảnh: " + deadline);
        if (r.HubImages > 0 || r.CdnImages > 0) lines.Add($"Ảnh SP: {r.HubImages} lấy link Hub, {r.CdnImages} tải từ CDN.");
        foreach (var n in r.CdnItems ?? []) lines.Add($"Tải CDN — dòng {n.ExcelRow} (SP {n.ItemId}): {n.Text}");
        foreach (var n in r.Notices) lines.Add("Lưu ý: " + n);
        foreach (var m in r.MissingTemplates)
            lines.Add($"Thiếu mẫu ngành {m.TopCategoryId} ({m.Items.Count} SP): dòng " +
                      string.Join(", ", m.Items.Select(i => i.ExcelRow.ToString(CultureInfo.InvariantCulture))));
        foreach (var n in r.NotHarvested) lines.Add($"Chưa cào — dòng {n.ExcelRow}: {n.Link}");
        foreach (var n in r.NoItemId) lines.Add($"Không có mã SP — dòng {n.ExcelRow}: {n.Link}");
        foreach (var n in r.ImageSkipped) lines.Add($"Lỗi ảnh — dòng {n.ExcelRow} (SP {n.ItemId}): {n.Text}");
        foreach (var n in r.Skipped) lines.Add($"Bỏ — dòng {n.ExcelRow} (SP {n.ItemId}): {n.Text}");
        foreach (var n in r.NeedsFix) lines.Add($"Thiếu thuộc tính — dòng {n.ExcelRow} (SP {n.ItemId}): {n.Text}");
        foreach (var n in r.ItemNotes ?? []) lines.Add($"Phân loại — dòng {n.ExcelRow} (SP {n.ItemId}): {n.Text}");
        foreach (var g in r.Warnings.GroupBy(w => (w.ExcelRow, w.ItemId)))
            lines.Add($"Cắt — dòng {g.Key.ExcelRow} (SP {g.Key.ItemId}): {string.Join("; ", g.Select(w => w.Text))}");
        return lines;
    }

    private bool CanCancelExport() => IsExporting;

    [RelayCommand(CanExecute = nameof(CanCancelExport))]
    private void CancelExport()
    {
        // Ghi trạng thái TRƯỚC khi huỷ: lượt có thể kết thúc ngay trong Cancel() (continuation chạy liền) và ghi "Đã huỷ".
        ExportStatus = "Đang huỷ…";
        try { _exportCts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    private bool CanOpenExportFolder() => LastExportFolder is not null;

    [RelayCommand(CanExecute = nameof(CanOpenExportFolder))]
    private void OpenExportFolder()
    {
        if (LastExportFolder is { } dir) Prompts.OpenFolder(dir);
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryFailedImagesCommand))]
    private bool _isRetryingImages;

    private bool CanRetryFailedImages() => Selected is not null && !IsRetryingImages;

    /// <summary>"Làm lại ảnh lỗi": mọi ảnh <c>failed</c> của shop đang chọn về chờ làm lại (lần xuất sau tải lại).</summary>
    [RelayCommand(CanExecute = nameof(CanRetryFailedImages))]
    private async Task RetryFailedImagesAsync()
    {
        if (Selected is not { } f || IsRetryingImages) return;
        IsRetryingImages = true;
        try
        {
            var n = await _export.RetryFailedImagesAsync(string.IsNullOrWhiteSpace(f.ImageKey) ? f.Id : f.ImageKey);
            ExportStatus = n == 0
                ? $"{f.DisplayName}: không có ảnh lỗi nào."
                : $"{f.DisplayName}: đưa {n} ảnh lỗi về chờ làm lại — bấm Xuất xlsx để thử lại.";
            Log(ExportStatus);
        }
        catch (Exception ex)
        {
            ExportStatus = "Không làm lại được ảnh lỗi: " + ex.Message;
            Log(ExportStatus);
        }
        finally { IsRetryingImages = false; }
    }

    // ─────────────── ảnh tạm theo lượt ───────────────

    [RelayCommand]
    internal async Task RefreshBatchesAsync()
    {
        IReadOnlyList<ExportBatchInfo> list;
        try { list = await _export.ListBatchesAsync(); }
        catch (Exception ex)
        {
            Log($"Không đọc được danh sách lượt (upload.db): {ex.Message}");
            return;
        }
        var keep = SelectedBatch?.BatchId;
        Batches.Clear();
        foreach (var b in list) Batches.Add(new BatchRow(b));
        SelectedBatch = Batches.FirstOrDefault(b => b.BatchId == keep);
    }

    private bool CanClearBatch() => SelectedBatch is not null && !IsExporting && !IsClearing;

    /// <summary>"Xoá ảnh tạm của lượt": hỏi lại kèm cảnh báo (phải đẩy file lên Shopee trước, trong 7 ngày) rồi xoá trên Hub các ảnh
    /// của lượt mà không lượt nào khác (chưa xoá) còn dùng. Đang xuất thì khoá nút.</summary>
    [RelayCommand(CanExecute = nameof(CanClearBatch))]
    private async Task ClearBatchAsync()
    {
        if (SelectedBatch is not { } b || IsExporting || IsClearing) return;
        var head = $"Xoá ảnh tạm của lượt {b.When} — {b.Shop} ({b.Images} ảnh)?" +
                   (b.Expiry.Length > 0 ? $" Hạn của lượt: {b.Expiry}." : "");
        if (!Prompts.Confirm("Xoá ảnh tạm của lượt", head + Environment.NewLine + Environment.NewLine + ClearWarning)) return;
        IsClearing = true;
        try
        {
            var r = await _export.ClearBatchAsync(b.BatchId);
            var text = $"Lượt {b.When} — {b.Shop}: đã xoá {r.Deleted} ảnh" +
                       (r.SharedKept > 0 ? $", giữ {r.SharedKept} ảnh lượt khác còn dùng" : "") +
                       (r.SizeChartKept > 0 ? ", giữ ảnh bảng size dùng chung (Hub tự xoá khi hết hạn)" : "") +
                       (r.Failed > 0 ? $", HỤT {r.Failed} ảnh (Hub không trả lời) — bấm xoá lại sau" : "") + ".";
            ExportStatus = text;
            Log(text);
        }
        catch (Exception ex)
        {
            ExportStatus = "Không xoá được ảnh của lượt: " + ex.Message;
            Log($"Không xoá được ảnh của lượt {b.BatchId}: {ex.Message}");
        }
        finally
        {
            IsClearing = false;
            await RefreshBatchesAsync();
        }
    }

    // ─────────────── dung lượng Hub (GĐ9) ───────────────

    /// <summary>Câu xác nhận phải gõ để xoá sạch kho ảnh gốc.</summary>
    internal const string DeleteAllTypedWord = "XOA";

    private SourceImageStatsResponse? _hubStats;

    [ObservableProperty] private string _hubDiskText = "—";
    [ObservableProperty] private string _hubSourceText = "—";
    [ObservableProperty] private string _hubMediaText = "—";
    [ObservableProperty] private string _hubStorageStatus = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteAllSourceImagesCommand), nameof(ExportCommand))]
    private bool _isDeletingSources;

    /// <summary>Đọc dung lượng Hub (tự gọi một lần lúc mở app; nút Làm mới). Lỗi → báo dưới mục, số giữ "—".</summary>
    [RelayCommand]
    internal async Task RefreshHubStorageAsync()
    {
        try
        {
            var st = await _export.GetHubStorageAsync();
            _hubStats = st;
            HubDiskText = st.DiskTotalBytes < 0 ? "không đo được" : $"còn trống {Bytes(st.DiskFreeBytes)} / {Bytes(st.DiskTotalBytes)}";
            HubSourceText = $"{st.SourceCount:N0} ảnh · {Bytes(st.SourceBytes)} / {Bytes(st.SourceMaxBytes)}";
            HubMediaText = $"{st.MediaCount:N0} file · {Bytes(st.MediaBytes)} / {Bytes(st.MediaMaxBytes)}";
            HubStorageStatus = $"Cập nhật lúc {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            _hubStats = null;
            HubDiskText = HubSourceText = HubMediaText = "—";
            HubStorageStatus = "Không đọc được dung lượng Hub: " + ex.Message;
        }
    }

    private bool CanDeleteAllSourceImages() => !IsExporting && !IsDeletingSources;

    /// <summary>"Xoá ảnh gốc Harvest trên Hub": đọc lại số liệu, hỏi xác nhận (gõ <see cref="DeleteAllTypedWord"/>), cảnh báo file đã
    /// xuất chưa đẩy sẽ hỏng ảnh; huỷ → không gọi Hub. Xoá xong đọc lại dung lượng và báo "Hub còn N ảnh gốc".</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteAllSourceImages))]
    private async Task DeleteAllSourceImagesAsync()
    {
        if (IsExporting || IsDeletingSources) return;
        // Đặt cờ NGAY (trước khi đọc số liệu / hỏi): nút Xuất bị khoá suốt lúc này, không có lượt nào chen vào.
        IsDeletingSources = true;
        try
        {
            await RefreshHubStorageAsync();
            if (_hubStats is not { } before)
            {
                HubStorageStatus = "Chưa đọc được số ảnh gốc trên Hub — bấm Làm mới rồi thử lại.";
                return;
            }
            int affected;
            try { affected = await _export.CountBatchesUsingHubSourcesAsync(); }
            catch (Exception ex) { affected = -1; Log("Không đếm được lượt dùng ảnh trên Hub: " + ex.Message); }
            var message = $"Xoá TOÀN BỘ {before.SourceCount:N0} ảnh gốc Harvest ({Bytes(before.SourceBytes)}) trên Hub?" +
                          Environment.NewLine + Environment.NewLine +
                          "Cảnh báo: file đã xuất mà CHƯA đẩy lên Shopee sẽ hỏng ảnh (link ảnh trỏ vào kho này)" +
                          (affected > 0 ? $" — {affected} lượt đã xuất trên máy này đang dùng ảnh trên Hub" : "") +
                          ". Sau khi xoá, SP phải cào lại ảnh, nếu không lúc xuất sẽ tải từ CDN. Không hoàn tác được.";
            if (!Prompts.ConfirmTyped("Xoá ảnh gốc Harvest trên Hub", message, DeleteAllTypedWord))
            {
                HubStorageStatus = "Đã huỷ — không xoá gì.";
                return;
            }
            if (IsExporting)
            {
                HubStorageStatus = "Đang có lượt xuất — không xoá. Chờ lượt xong rồi thử lại.";
                return;
            }
            var r = await _export.DeleteAllSourceImagesAsync();
            Log($"Xoá ảnh gốc Harvest trên Hub: đã xoá {r.Deleted:N0} ảnh, giải phóng {Bytes(r.FreedBytes)}, còn {r.Remaining:N0}.");
            if (r.Deleted > 0 || r.Remaining == 0)
            {
                try
                {
                    var marked = await _export.MarkHubSourcesDeletedAsync(DateTimeOffset.Now);
                    if (marked > 0) Log($"Đã đánh dấu {marked} lượt: ảnh gốc trên Hub đã xoá.");
                }
                catch (Exception ex) { Log("Không đánh dấu được các lượt dùng ảnh trên Hub: " + ex.Message); }
                await RefreshBatchesAsync();
            }
            await RefreshHubStorageAsync();
            var left = _hubStats?.SourceCount ?? r.Remaining;
            HubStorageStatus = r.Remaining == 0 && left == 0
                ? $"Đã xoá {r.Deleted:N0} ảnh ({Bytes(r.FreedBytes)}). Hub còn 0 ảnh gốc."
                : $"Đã xoá {r.Deleted:N0} ảnh nhưng Hub còn {Math.Max(r.Remaining, left):N0} ảnh gốc chưa xoá được — bấm xoá lại.";
            Log(HubStorageStatus);
        }
        catch (Exception ex)
        {
            HubStorageStatus = "Không xoá được ảnh gốc trên Hub: " + ex.Message;
            Log(HubStorageStatus);
        }
        finally { IsDeletingSources = false; }
    }

    /// <summary>Số byte đọc được: GB / MB / KB.</summary>
    internal static string Bytes(long b) => b switch
    {
        < 0 => "?",
        >= 1L << 30 => (b / (double)(1L << 30)).ToString("0.##", CultureInfo.InvariantCulture) + " GB",
        >= 1L << 20 => (b / (double)(1L << 20)).ToString("0.#", CultureInfo.InvariantCulture) + " MB",
        _ => (b / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB",
    };

    /// <summary>Tắt app: huỷ lượt xuất đang chạy, đợi nó dọn file tạm, rồi dừng bước đệm ảnh.</summary>
    private async Task ShutdownExportAsync()
    {
        try { _exportCts?.Cancel(); } catch (ObjectDisposedException) { }
        if (_exportTask is { } t)
        {
            try { await t; } catch { }
        }
        try { await _export.DisposeAsync(); } catch { }
    }
}
