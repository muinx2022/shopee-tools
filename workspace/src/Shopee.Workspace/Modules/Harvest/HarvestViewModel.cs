using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shopee.Core.Harvest;
using Shopee.Core.Scrape;
using Shopee.Workspace.Infrastructure;
using Shopee.Workspace.Modules.Scrape;
using Shopee.Workspace.Services;

namespace Shopee.Workspace.Modules.Harvest;

/// <summary>
/// Màn chính của app Harvest: chọn MỘT file Excel (nhớ qua lần mở sau), mỗi sheet một dòng (tổng link · đã có · còn phải lấy ·
/// bỏ qua/lỗi · tiến độ lượt), tick sheet nào thì chạy sheet đó (mặc định tick hết). Không có tài khoản BigSeller / shop: "shop"
/// gửi Hub là tên sheet. Lượt chạy do <see cref="ScrapeViewModel.RunHarvestAsync"/> làm (kho tk Shopee, khung, lưới tiến trình,
/// log); màn này lo file, sheet, số đếm, cấu hình và 3 số Hub.
/// </summary>
public sealed partial class HarvestViewModel : ObservableObject
{
    public ScrapeViewModel Scrape { get; }
    public ObservableCollection<HarvestSheetRowViewModel> Sheets { get; } = [];

    private readonly HarvestSettingsStore _settings;
    private readonly IFilePickerService? _picker;
    private CancellationTokenSource? _scanCts;

    private readonly IHarvestHubPort _hub;
    private readonly string _suiteHubConfigPath;

    public HarvestViewModel(ScrapeViewModel scrape, HarvestSettingsStore? settings = null, IFilePickerService? picker = null,
        IHarvestHubPort? hub = null, string? suiteHubConfigPath = null)
    {
        Scrape = scrape;
        _settings = settings ?? HarvestSettingsStore.Shared;
        _picker = picker;
        _hub = hub ?? HarvestHubPort.Instance;
        _suiteHubConfigPath = suiteHubConfigPath ?? HarvestHubViewModel.DefaultSuiteConfigPath();
        RefreshHubState();
        var s = _settings.Current;
        _loading = true;
        WorkbookPath = s.WorkbookPath ?? "";
        MaxAgeDays = HarvestPlanning.ClampMaxAgeDays(s.MaxAgeDays);
        Processes = Math.Max(1, s.Processes);
        FrameSize = Math.Max(1, s.FrameSize);
        RowsPerAccount = Math.Max(1, s.RowsPerAccount);
        var rest = ScrapeRestWindow.Resolve(s.RestMinSeconds, s.RestMaxSeconds);
        RestMinSeconds = rest.MinSeconds;
        RestMaxSeconds = rest.MaxSeconds;
        _loading = false;
        Scrape.PropertyChanged += OnScrapeChanged;

        if (HarvestRuntime.Current is { } rt)
        {
            _hubStatsRefresher = new CoalescingRefresher(async () =>
            {
                var st = await rt.GetStatsAsync().ConfigureAwait(false);
                var err = rt.Worker.LastError ?? "";
                UiThread.Post(() =>
                {
                    HubSaved = st.HubReceivedTotal;   // GĐ7: outbox app đẩy + extension tự đẩy
                    HubPending = st.Pending;
                    HubFailed = st.Failed;
                    HubError = err;
                });
            });
            rt.StatsChanged += RefreshHubStats;
            RefreshHubStats();
        }
        if (HasFile) _ = RefreshAsync();
    }

    public string PageTitle => "Shopee Harvest — Lấy dữ liệu";
    public string PageSubtitle =>
        "Chọn file Excel danh sách link (một sheet dùng chung hoặc mỗi shop một sheet). App mở từng link Shopee, lấy toàn bộ dữ liệu SP, "
        + "lưu vào máy rồi tự đẩy lên Hub. SP đã có dữ liệu còn hạn (trên máy này hoặc trên Hub) thì bỏ qua, không mở trang.";

    // ── File + sheet ────────────────────────────────────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile), nameof(WorkbookName))]
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(RefreshCommand), nameof(ResetCommand), nameof(RetrySkippedCommand))]
    private string _workbookPath = "";

    // Đang nạp cấu hình đã lưu trong ctor → không ghi ngược.
    private bool _loading;

    public bool HasFile => !string.IsNullOrWhiteSpace(WorkbookPath);
    public string WorkbookName => HasFile ? Path.GetFileName(WorkbookPath) : "Chưa chọn file Excel";

    [ObservableProperty] private string _status = "";
    /// <summary>Hub không trả lời được lúc quét / chạy → số "đã có" chỉ tính SP trên máy này.</summary>
    [ObservableProperty] private string _hubNote = "";
    [ObservableProperty] private bool _isScanning;

    public bool IsBusy => Scrape.IsBusy;
    public bool IsIdle => !Scrape.IsBusy;

    private void OnScrapeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ScrapeViewModel.IsBusy)) return;
        UiThread.Post(() =>
        {
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsIdle));
            RunCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
            ResetCommand.NotifyCanExecuteChanged();
            RetrySkippedCommand.NotifyCanExecuteChanged();
            PickFileCommand.NotifyCanExecuteChanged();
            RefreshCommand.NotifyCanExecuteChanged();
            ApplyRangeToCheckedCommand.NotifyCanExecuteChanged();
        });
    }

    private bool CanEditFile() => !Scrape.IsBusy;

    [RelayCommand(CanExecute = nameof(CanEditFile))]
    private async Task PickFileAsync()
    {
        var path = _picker is not null
            ? await _picker.OpenFileAsync("Chọn file Excel danh sách link", "Excel|*.xlsx;*.xlsm|Tất cả|*.*")
            : await FilePicker.OpenFileAsync("Chọn file Excel danh sách link", "Excel|*.xlsx;*.xlsm|Tất cả|*.*");
        if (string.IsNullOrWhiteSpace(path)) return;
        WorkbookPath = path;
        _settings.Current.WorkbookPath = path;
        _settings.Save();
        Sheets.Clear();
        await RefreshAsync();
    }

    private bool CanRefresh() => HasFile && !Scrape.IsBusy;

    /// <summary>Đọc lại file: danh sách sheet + đếm từng sheet (tập đã xong cục bộ còn hạn + hỏi Hub). Không mở Brave.</summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        var path = WorkbookPath;
        if (string.IsNullOrWhiteSpace(path)) return;
        _scanCts?.Cancel();
        var cts = _scanCts = new CancellationTokenSource();
        IsScanning = true;
        Status = "Đang đọc file…";
        try
        {
            if (!File.Exists(path)) { Status = $"Không tìm thấy file {path} — chọn lại file."; Sheets.Clear(); return; }
            var scan = await HarvestScan.ScanAsync(path, MaxAgeDays, HarvestRuntime.Current, cts.Token);
            if (cts.IsCancellationRequested) return;
            ApplyScan(scan);
            RefreshHubState();
            Status = $"{scan.Sheets.Count} sheet · {Sheets.Sum(x => x.TotalLinks)} link trong dải đã chọn.";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex) { Status = "Không đọc được file: " + ex.Message; }
        finally
        {
            if (ReferenceEquals(_scanCts, cts)) { IsScanning = false; _scanCts = null; }
            cts.Dispose();
        }
    }

    // Tập đã xong / lỗi / Hub của lần quét gần nhất — đổi dải một sheet thì đếm lại từ đây, không đọc lại file / hỏi lại Hub.
    private HarvestScanResult? _lastScan;

    private void ApplyScan(HarvestScanResult scan)
    {
        _lastScan = scan;
        var old = Sheets.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
        var unchecked_ = new HashSet<string>(_settings.Current.UncheckedSheetKeys, StringComparer.OrdinalIgnoreCase);
        Sheets.Clear();
        foreach (var sh in scan.Sheets)
        {
            var key = HarvestSource.SheetKey(scan.Path, sh.Sheet);
            var row = old.TryGetValue(key, out var r)
                ? r
                : new HarvestSheetRowViewModel(sh.Sheet, key, !unchecked_.Contains(key), OnSheetChecked, OnSheetRangeChanged);
            // Dải: lần đầu lấy từ cấu hình (theo file + sheet); quét lại giữ dải đang hiện, chỉ kẹp lại theo dòng dữ liệu đầu mới.
            var (from, to) = old.ContainsKey(key)
                ? (row.FromRow, row.ToRow)
                : _settings.Current.SheetRanges.TryGetValue(key, out var saved) ? (saved.FromRow, saved.ToRow) : (0, 0);
            row.LoadRange(from, to, sh.Range.FirstDataRow);
            Recount(row, sh.Range);
            Sheets.Add(row);
        }
        HubNote = scan.Hub.Known is null
            ? $"⚠ Không hỏi được Hub ({scan.Hub.Error}) — \"đã có\" chỉ tính SP đã lấy trên máy này."
            : "";
        OnCheckedSetChanged();
    }

    /// <summary>Đếm lại một sheet theo dải đang đặt (từ lần quét gần nhất).</summary>
    private void Recount(HarvestSheetRowViewModel row, HarvestWorkbookRange? full = null)
    {
        full ??= row.FullRange;
        if (full is null || _lastScan is null) return;
        var inRange = HarvestRowRange.Restrict(full, row.FromRow, row.ToRow);
        row.ApplyScan(inRange.Links?.Items ?? [], HarvestPlanning.Build(inRange, _lastScan.Done, _lastScan.Failed, _lastScan.Hub.Known),
            full.Info?.Warning(row.Name), full);
    }

    /// <summary>User sửa Từ dòng / Đến dòng của một sheet: nhập sai → báo (ô đã trả dải cũ); hợp lệ → lưu theo (file, sheet) + đếm lại.</summary>
    private void OnSheetRangeChanged(HarvestSheetRowViewModel row, string? error)
    {
        if (error is not null) { Status = error; return; }
        SaveRange(row);
        if (!_bulkRange) _settings.Save();
        Recount(row);
    }

    private void SaveRange(HarvestSheetRowViewModel row)
    {
        // Dải mặc định → bỏ khỏi cấu hình (file sau này thêm / bớt dòng tiêu đề thì mặc định tự theo).
        if (row.IsDefaultRange) _settings.Current.SheetRanges.Remove(row.Key);
        else _settings.Current.SheetRanges[row.Key] = new HarvestSheetRowRange { FromRow = row.FromRow, ToRow = row.ToRow };
    }

    // Đang áp dải cho nhiều sheet → lưu MỘT lần ở cuối.
    private bool _bulkRange;

    /// <summary>Sheet đang chọn (dòng được bôi) trong bảng — nguồn dải của nút "Áp dải cho các sheet đã tick".</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyRangeToCheckedCommand))]
    private HarvestSheetRowViewModel? _selectedSheet;

    private bool CanApplyRange() => SelectedSheet is not null && !Scrape.IsBusy && Sheets.Any(s => s.IsChecked && !ReferenceEquals(s, SelectedSheet));

    /// <summary>Đặt dải Từ dòng / Đến dòng của sheet đang chọn cho mọi sheet đã tick (mỗi sheet tự kẹp theo dòng dữ liệu đầu của nó).</summary>
    [RelayCommand(CanExecute = nameof(CanApplyRange))]
    private void ApplyRangeToChecked()
    {
        if (SelectedSheet is not { } src) return;
        var targets = Sheets.Where(s => s.IsChecked && !ReferenceEquals(s, src)).ToList();
        _bulkRange = true;
        try { foreach (var t in targets) t.SetRangeFromUser(src.FromRow, src.ToRow); }
        finally { _bulkRange = false; }
        _settings.Save();
        Status = $"Đã đặt dải {HarvestRowRange.Describe(src.FromRow, src.ToRow)} cho {targets.Count} sheet đã tick.";
    }

    // Đang tick / bỏ tick hàng loạt (ô "chọn tất cả") → lưu MỘT lần ở cuối.
    private bool _bulkChecking;

    private void OnSheetChecked(HarvestSheetRowViewModel row)
    {
        var list = _settings.Current.UncheckedSheetKeys;
        list.RemoveAll(k => string.Equals(k, row.Key, StringComparison.OrdinalIgnoreCase));
        if (!row.IsChecked) list.Add(row.Key);
        if (_bulkChecking) return;
        _settings.Save();
        OnCheckedSetChanged();
    }

    private void OnCheckedSetChanged()
    {
        OnPropertyChanged(nameof(AllSheetsChecked));
        RunCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
        RetrySkippedCommand.NotifyCanExecuteChanged();
        ApplyRangeToCheckedCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Ô ở tiêu đề cột "Chạy": true = tick hết, false = không sheet nào, null = một phần. Gán true / false → tick / bỏ hết
    /// (nhớ như tick tay).</summary>
    public bool? AllSheetsChecked
    {
        get
        {
            if (Sheets.Count == 0) return false;
            var n = Sheets.Count(s => s.IsChecked);
            return n == Sheets.Count ? true : n == 0 ? false : null;
        }
        set
        {
            var target = value ?? true;   // bấm khi đang "một phần" → tick hết
            _bulkChecking = true;
            try { foreach (var s in Sheets) s.IsChecked = target; }
            finally { _bulkChecking = false; }
            _settings.Save();
            OnCheckedSetChanged();
        }
    }

    // ── Trạng thái Hub ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Đã nối Hub (có client) chưa.</summary>
    [ObservableProperty] private bool _isHubConnected;
    /// <summary>Dòng trạng thái Hub trên màn chính ("Đã nối Hub: …" / "Chưa nối Hub — …").</summary>
    [ObservableProperty] private string _hubStatus = "";

    /// <summary>Shell cấp: mở Cài đặt → Kết nối Hub.</summary>
    public Action? OpenHubSettings { get; set; }

    [RelayCommand]
    private void GoToHubSettings() => OpenHubSettings?.Invoke();

    /// <summary>Đọc lại trạng thái Hub (sau khi lưu ở Kết nối Hub). Chưa nối mà máy có cấu hình của Shopee Suite → gợi ý lấy (KHÔNG tự
    /// đọc ngầm file của app khác).</summary>
    public void RefreshHubState()
    {
        var cur = _hub.Current;
        // Lần lưu / kiểm tra gần nhất báo Hub chưa dùng được (vd token sai) → KHÔNG báo "đã nối" dù đã có client.
        var failed = _lastHubSave is { Ok: false };
        IsHubConnected = _hub.Connected && !failed;
        HubStatus = IsHubConnected
            ? $"Đã nối Hub: {cur.BaseUrl}"
            : failed && _hub.Connected
                ? $"Hub chưa dùng được: {_lastHubSave!.Message} Vào Cài đặt → Kết nối Hub."
            : File.Exists(_suiteHubConfigPath)
                ? "Chưa nối Hub — có cấu hình Hub của Shopee Suite trên máy này: vào Cài đặt → Kết nối Hub, bấm \"Lấy cấu hình từ Shopee Suite\" rồi Lưu."
                : "Chưa nối Hub — dữ liệu chỉ lưu trên máy, chưa đẩy lên Hub được và không bỏ qua được SP Hub đã có. Vào Cài đặt → Kết nối Hub.";
        if (IsHubConnected && HubNote.Contains("chưa nối Hub", StringComparison.Ordinal)) HubNote = "";
    }

    // Kết quả lưu + kiểm tra Hub gần nhất (null = chưa lưu lần nào trong phiên này).
    private HarvestHubSaveResult? _lastHubSave;

    /// <summary>Vừa lưu ở Cài đặt → Kết nối Hub: cập nhật dòng trạng thái; app rảnh và đã có file thì quét lại để "Đã có" / "Còn phải
    /// lấy" tính lại theo Hub (N2).</summary>
    public void OnHubSaved(HarvestHubSaveResult result)
    {
        _lastHubSave = result;
        RefreshHubState();
        if (!Scrape.IsBusy && HasFile) _ = RefreshAsync();
    }

    // ── Cấu hình chạy ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>"Lấy lại nếu dữ liệu (trên máy / trên Hub) cũ hơn N ngày".</summary>
    [ObservableProperty] private int _maxAgeDays;
    // Ô số nhập ngoài khoảng → ô hiện NGAY giá trị đã kẹp (gõ 0 vào N ngày → 30), không để ô hiện 0 mà lượt chạy dùng 30.
    partial void OnMaxAgeDaysChanged(int value)
    {
        var v = HarvestPlanning.ClampMaxAgeDays(value);
        if (v != value) { MaxAgeDays = v; return; }   // set lại → handler chạy lần nữa với giá trị hợp lệ và lưu
        SaveSetting(s => s.MaxAgeDays = v);
    }

    /// <summary>Số cửa sổ Brave chạy song song.</summary>
    [ObservableProperty] private int _processes;
    partial void OnProcessesChanged(int value)
    {
        if (value < 1) { Processes = 1; return; }
        SaveSetting(s => s.Processes = value);
    }

    [ObservableProperty] private int _frameSize;
    partial void OnFrameSizeChanged(int value)
    {
        if (value < 1) { FrameSize = 1; return; }
        SaveSetting(s => s.FrameSize = value);
    }

    /// <summary>Nhịp chạy: số dòng mỗi lần (mỗi tk Shopee nhận một khối liên tiếp).</summary>
    [ObservableProperty] private int _rowsPerAccount;
    partial void OnRowsPerAccountChanged(int value)
    {
        if (value < 1) { RowsPerAccount = 1; return; }
        SaveSetting(s => s.RowsPerAccount = value);
    }

    [ObservableProperty] private int _restMinSeconds;
    partial void OnRestMinSecondsChanged(int value) => ApplyRest(value, RestMaxSeconds);

    [ObservableProperty] private int _restMaxSeconds;
    partial void OnRestMaxSecondsChanged(int value) => ApplyRest(RestMinSeconds, value);

    /// <summary>Hợp lệ hoá cặp nghỉ giữa link như lúc chạy (<see cref="ScrapeRestWindow.Resolve"/>: 0 → mặc định, max &lt; min → kéo
    /// lên) và cho ô hiện đúng cặp đó.</summary>
    private void ApplyRest(int min, int max)
    {
        if (_loading) return;
        var w = ScrapeRestWindow.Resolve(min, max);
        if (w.MinSeconds != RestMinSeconds) { RestMinSeconds = w.MinSeconds; return; }
        if (w.MaxSeconds != RestMaxSeconds) { RestMaxSeconds = w.MaxSeconds; return; }
        SaveSetting(s => { s.RestMinSeconds = w.MinSeconds; s.RestMaxSeconds = w.MaxSeconds; });
    }

    private void SaveSetting(Action<HarvestSettings> set)
    {
        if (_loading) return;
        set(_settings.Current);
        _settings.Save();
    }

    private HarvestRunSettings CurrentRunSettings() => new(Math.Max(1, Processes), Math.Max(1, FrameSize), Math.Max(1, RowsPerAccount),
        ScrapeRestWindow.Resolve(RestMinSeconds, RestMaxSeconds), HarvestPlanning.ClampMaxAgeDays(MaxAgeDays));

    // ── Chạy / Dừng / Reset / Cào lại dòng đã bỏ ──────────────────────────────────────────────────────────────────

    private bool CanRun() => HasFile && !Scrape.IsBusy && Sheets.Any(s => s.IsChecked);

    /// <summary>Chạy / Tiếp tục (như nhau): mỗi sheet dựng lại tiến độ từ tập SP đã có rồi chạy phần còn lại, lần lượt từng sheet.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        var path = WorkbookPath;
        var rows = Sheets.Where(s => s.IsChecked).ToList();
        if (rows.Count == 0) return;
        foreach (var r in Sheets) { r.RunText = ""; r.IsRunning = false; }
        var byKey = rows.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
        HarvestSheetRowViewModel? RowOf(HarvestSheetJob j) => byKey.TryGetValue(j.ProgressKey, out var r) ? r : null;
        var callbacks = new HarvestRunCallbacks
        {
            Planned = (job, plan, hub) =>
            {
                var progress = RoleStores.ScrapeProgress.Find(HarvestSource.AccountId, job.ProgressKey);
                UiThread.Post(() =>
                {
                    RowOf(job)?.ApplyPlan(plan, progress);
                    if (hub.Known is null) HubNote = $"⚠ Không hỏi được Hub ({hub.Error}) — lượt này chỉ bỏ qua SP đã lấy trên máy này.";
                });
            },
            Changed = job =>
            {
                var progress = RoleStores.ScrapeProgress.Find(HarvestSource.AccountId, job.ProgressKey);
                UiThread.Post(() => RowOf(job)?.ApplyProgress(progress));
            },
            Running = job => UiThread.Post(() =>
            {
                foreach (var r in rows) r.IsRunning = job is not null && string.Equals(r.Key, job.ProgressKey, StringComparison.OrdinalIgnoreCase);
            }),
        };
        Status = $"Đang lấy dữ liệu {rows.Count} sheet…";
        await Scrape.RunHarvestAsync(CheckedJobs(path, rows), CurrentRunSettings(), callbacks);
        Status = Scrape.Status;
        await RefreshAsync();
    }

    /// <summary>Job của các sheet đã tick, mang theo dải Từ dòng / Đến dòng của từng sheet (lượt chạy chỉ lên lịch dòng trong dải).</summary>
    internal IReadOnlyList<HarvestSheetJob> CheckedJobs(string? path = null, IEnumerable<HarvestSheetRowViewModel>? rows = null) =>
        (rows ?? Sheets.Where(s => s.IsChecked)).Select(r => new HarvestSheetJob(path ?? WorkbookPath, r.Name, r.FromRow, r.ToRow)).ToList();

    private bool CanStop() => Scrape.IsBusy;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        if (Scrape.StopCommand.CanExecute(null)) Scrape.StopCommand.Execute(null);
    }

    private bool CanEditChecked() => HasFile && !Scrape.IsBusy && Sheets.Any(s => s.IsChecked);

    /// <summary>Reset (có hỏi): bỏ các SP của sheet đã tick khỏi danh sách "đã lấy" + "lỗi" trên máy này và xoá tiến độ sheet →
    /// lượt sau lấy lại. SP Hub đã có còn hạn vẫn bỏ qua (đổi N ngày nếu muốn lấy lại cả những SP đó). CHỈ các SP nằm TRONG DẢI
    /// Từ dòng / Đến dòng của từng sheet (<see cref="HarvestSheetRowViewModel.Items"/>) — dòng ngoài dải không bị đụng (an toàn hơn:
    /// không gỡ nhầm SP của phần sheet user không định chạy lại). Tiến độ sheet dựng lại mỗi lượt nên xoá cả sheet không mất gì.</summary>
    [RelayCommand(CanExecute = nameof(CanEditChecked))]
    private async Task ResetAsync()
    {
        if (HarvestRuntime.Current is not { } rt) { Status = "Chưa mở được kho lưu dữ liệu (harvest-outbox.db)."; return; }
        var rows = Sheets.Where(s => s.IsChecked).ToList();
        var keys = HarvestPlanning.KeysOf(rows.SelectMany(r => r.Items));
        if (!await Dialogs.ConfirmAsync(
                $"Reset {rows.Count} sheet đã chọn ({keys.Count} SP)?\n\n"
                + "Chỉ các SP nằm TRONG DẢI \"Từ dòng – Đến dòng\" của từng sheet; dòng ngoài dải không bị đụng.\n\n"
                + "Các SP này bị gỡ khỏi danh sách \"đã lấy\" và \"lỗi\" trên máy này (kể cả khi chúng nằm ở sheet / file khác) và tiến độ "
                + $"của sheet bị xoá — lượt Chạy sau lấy lại. SP mà Hub đã có dữ liệu chưa quá {MaxAgeDays} ngày vẫn được bỏ qua.",
                "Reset tiến độ"))
            return;
        var n = await Task.Run(() =>
        {
            var cleared = rt.Outbox.ClearDone(keys);
            rt.Outbox.ClearFailed(keys);
            foreach (var r in rows)
                if (RoleStores.ScrapeProgress.Find(HarvestSource.AccountId, r.Key) is not null)
                    RoleStores.ScrapeProgress.Clear(HarvestSource.AccountId, r.Key);
            return cleared;
        });
        Status = $"Đã reset {rows.Count} sheet ({n} SP bỏ khỏi danh sách đã lấy).";
        await RefreshAsync();
    }

    /// <summary>"Cào lại dòng đã bỏ": gỡ SP của sheet đã tick khỏi danh sách lỗi vĩnh viễn + mở lại sổ bỏ qua → lượt sau thử lại. Như
    /// Reset: chỉ SP trong dải Từ dòng / Đến dòng của từng sheet.</summary>
    [RelayCommand(CanExecute = nameof(CanEditChecked))]
    private async Task RetrySkippedAsync()
    {
        if (HarvestRuntime.Current is not { } rt) { Status = "Chưa mở được kho lưu dữ liệu (harvest-outbox.db)."; return; }
        var rows = Sheets.Where(s => s.IsChecked).ToList();
        var keys = HarvestPlanning.KeysOf(rows.SelectMany(r => r.Items));
        var n = await Task.Run(() =>
        {
            var cleared = rt.Outbox.ClearFailed(keys);
            foreach (var r in rows) RoleStores.ScrapeProgress.ReopenSkipped(HarvestSource.AccountId, r.Key);
            return cleared;
        });
        Status = n > 0
            ? $"Đã mở lại {n} SP bị bỏ qua (trong dải dòng của các sheet đã chọn) — bấm Chạy để thử lại."
            : "Không có SP nào bị bỏ qua trong dải dòng của các sheet đã chọn.";
        await RefreshAsync();
    }

    // ── 3 số Hub + đẩy lại mục lỗi ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Số SP Hub đã nhận (cộng dồn bền của outbox).</summary>
    [ObservableProperty] private long _hubSaved;
    /// <summary>Số SP đang chờ đẩy Hub.</summary>
    [ObservableProperty] private int _hubPending;
    /// <summary>Số SP Hub từ chối / đẩy lỗi hẳn — "Đẩy lại mục lỗi" đưa về hàng chờ.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RequeueFailedCommand))]
    private int _hubFailed;
    [ObservableProperty] private string _hubError = "";

    private readonly CoalescingRefresher? _hubStatsRefresher;

    private void RefreshHubStats() => _ = _hubStatsRefresher?.RequestAsync();

    private bool CanRequeue() => HubFailed > 0;

    [RelayCommand(CanExecute = nameof(CanRequeue))]
    private async Task RequeueFailedAsync()
    {
        if (HarvestRuntime.Current is not { } rt) return;
        var n = await rt.RequeueFailedAsync();
        Status = n > 0 ? $"Đã đưa {n} mục lỗi về hàng chờ đẩy Hub." : "Không có mục lỗi nào.";
        RefreshHubStats();
    }
}

/// <summary>Kết quả quét một file cho màn Harvest: từng sheet CẢ SHEET (màn tự thu theo dải Từ dòng / Đến dòng rồi đếm) + tập đã
/// xong / lỗi cục bộ + Hub.</summary>
public sealed record HarvestScanResult(string Path, IReadOnlyList<(string Sheet, HarvestWorkbookRange Range)> Sheets,
    IReadOnlySet<string> Done, IReadOnlySet<string> Failed, HarvestHubLookup Hub);

/// <summary>Quét file Excel (mọi sheet, MỘT lần đọc) + tập đã xong / lỗi cục bộ + hỏi Hub một lần cho cả file (mọi link, không chỉ
/// trong dải — đổi dải đếm lại được ngay không phải hỏi lại). Không mở Brave, không ghi gì.</summary>
public static class HarvestScan
{
    public static async Task<HarvestScanResult> ScanAsync(string path, int maxAgeDays, HarvestRuntime? rt, CancellationToken ct)
    {
        var sheets = await HarvestWorkbookReader.ReadAllSheetsAsync(path, ct).ConfigureAwait(false);
        var (done, failed) = await Task.Run(() => rt is null
            ? ((IReadOnlySet<string>)new HashSet<string>(), (IReadOnlySet<string>)new HashSet<string>())
            : (rt.Outbox.DoneKeys(HarvestPlanning.DoneSince(DateTimeOffset.UtcNow, maxAgeDays)), rt.Outbox.FailedKeys()), ct)
            .ConfigureAwait(false);
        var ids = HarvestPlanning.ItemIdsToAsk(sheets.SelectMany(s => s.Range.Links?.Items ?? []), done);
        var hub = await HarvestPlanning.AskHubAsync(rt?.KnownQuery, ids, maxAgeDays, ct).ConfigureAwait(false);
        return new HarvestScanResult(path, sheets, done, failed, hub);
    }
}
