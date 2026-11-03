using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shopee.Core.BigSeller;
using Shopee.Core.Coordination;
using Shopee.Core.Infrastructure;
using Shopee.Core.Progress;
using Shopee.Core.Proxy;
using Shopee.Core.Scrape;
using Shopee.Workspace.Infrastructure;
using Shopee.Workspace.Modules.Scrape;
using Shopee.Workspace.Services;
using Shopee.Modules.MultiBrave;

namespace Shopee.Workspace.Modules.Workspace;

/// <summary>
/// Đại diện cho 1 dòng tài khoản BigSeller trong màn hình Scrape Multi.
/// </summary>
public sealed partial class ScrapeMultiAccountItem : ObservableObject
{
    public WorkspaceAccountViewModel Account { get; }
    public string DisplayName => Account.DisplayName;
    public string CookieStatus => Account.CookieStatus;
    public bool HasCookie => Account.HasCookie;
    public int ShopCount => Account.ShopCount;
    public string ShopsSummary => Account.ScrapeSummary;

    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private bool _isLead;
    [ObservableProperty] private string _status = "Chờ chạy";
    [ObservableProperty] private string _proxyKey = "";
    [ObservableProperty] private int _completedCount;

    public bool HasProxy => !string.IsNullOrWhiteSpace(ProxyKey);

    public event Action? SelectionChanged;

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke();
    }

    public ScrapeMultiAccountItem(WorkspaceAccountViewModel account)
    {
        Account = account;
        ProxyKey = account.Account.KiotProxyKey ?? "";
        ComputeCompletedCount();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(CookieStatus));
        OnPropertyChanged(nameof(HasCookie));
        OnPropertyChanged(nameof(ShopCount));
        OnPropertyChanged(nameof(ShopsSummary));
        ProxyKey = Account.Account.KiotProxyKey ?? "";
        OnPropertyChanged(nameof(HasProxy));
        ComputeCompletedCount();
    }

    public void ComputeCompletedCount()
    {
        var totalDone = 0;
        foreach (var shop in Account.Shops)
        {
            var resolved = SharedWorkbookStore.Resolve(Account.Account, shop.Shop);
            var scrapeKey = SharedWorkbookCatalog.ScrapeProgressKey(SharedWorkbookStore.IsActive, Account.Account.Id, resolved.ProgressKey);
            var prog = ScrapeProgressStore.Shared.Find(Account.Account.Id, scrapeKey);
            if (prog != null)
            {
                totalDone = Math.Max(totalDone, prog.Completed.Sum(r => Math.Max(0, r.To - r.From + 1)));
            }
        }
        CompletedCount = totalDone;
    }
}

/// <summary>
/// ViewModel quản lý Scrape Multi — cào 1 link trên Lead Account trong trình duyệt Brave,
/// và tự động fan-out kết quả tới tất cả các tài khoản BigSeller được chọn thông qua proxy riêng hoặc IP máy.
/// Tự động bắt lỗi phiên cookie, tự động login lại và retry sync, lưu vết sản phẩm lỗi để sync bù.
/// </summary>
public sealed partial class ScrapeMultiViewModel : ObservableObject
{
    private readonly WorkspaceViewModel _workspace;
    private CancellationTokenSource? _cts;
    // Target của job Lead đang chạy — chỉ gán trong StartScrapeMultiAsync (null khi rảnh / khi đồng bộ lại SP lỗi, không có Brave).
    private ScrapeTargetViewModel? _activeLeadTarget;
    // Selection đã lưu chưa áp được (Accounts còn rỗng lúc ctor) — áp ở RefreshAccounts lần đầu có tài khoản.
    private HashSet<string>? _pendingSelectedIds;
    // Tk đã tự đăng nhập thất bại trong lượt chạy hiện tại — không đăng nhập lại (tránh khoá tk khi BigSeller đòi OTP).
    private readonly LoginFailureTracker _loginFailures = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _loginLocks = new();

    public ObservableCollection<ScrapeMultiAccountItem> Accounts { get; } = [];
    public LogBuffer LogLines { get; } = new("workspace-scrapemulti.log");

    public void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        UiThread.Post(() => LogLines.Add(line));
    }

    [RelayCommand]
    private void ClearLog() => LogLines.Clear();

    [RelayCommand]
    private void OpenLogFile() => ShellOpener.RevealFile(LogLines.FilePath);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotRunning))]
    private bool _isRunning;

    public bool IsNotRunning => !IsRunning;

    public string HeaderTitle => "Scrape Multi — Cào đa tài khoản";
    public string HeaderCaption =>
        "Chỉ cần cào 1 lần trên tài khoản chính (Lead) bằng Brave, hệ thống sẽ tự động đồng bộ kết quả cào " +
        "cho tất cả các tài khoản BigSeller đã chọn. Tự động kiểm tra phiên, đăng nhập lại khi mất cookie, và lưu SP lỗi để đồng bộ bù.";

    [ObservableProperty]
    private string _currentStatus = "Sẵn sàng";

    // ── Cài đặt chạy (Lưu tự động vào scrape-multi-config.json) ──
    [ObservableProperty] private int? _startRow;
    [ObservableProperty] private int? _endRow;
    [ObservableProperty] private int _maxProcess = 8;
    [ObservableProperty] private int _frameSize = 10;
    [ObservableProperty] private int _restMinSeconds = 120;
    [ObservableProperty] private int _restMaxSeconds = 240;

    partial void OnStartRowChanged(int? value) => SaveCurrentConfig();
    partial void OnEndRowChanged(int? value) => SaveCurrentConfig();
    partial void OnMaxProcessChanged(int value)
    {
        if (FrameSize < value)
            FrameSize = value;
        SaveCurrentConfig();
    }
    partial void OnFrameSizeChanged(int value) => SaveCurrentConfig();
    partial void OnRestMinSecondsChanged(int value) => SaveCurrentConfig();
    partial void OnRestMaxSecondsChanged(int value) => SaveCurrentConfig();

    // ── Tiến độ & Sổ lưu lỗi ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailedSyncs))]
    private int _failedSyncCount;

    public bool HasFailedSyncs => FailedSyncCount > 0;

    [ObservableProperty] private string _leadProgressSummary = "Chưa có tiến độ";
    [ObservableProperty] private int _leadLastRow;
    [ObservableProperty] private int _leadCompletedCount;
    [ObservableProperty] private int _nextRowToScrape = 1;

    public ScrapeMultiViewModel(WorkspaceViewModel workspace)
    {
        _workspace = workspace;

        var config = ScrapeMultiConfigStore.Shared.Load();
        _startRow = config.StartRow;
        _endRow = config.EndRow;
        _maxProcess = config.MaxProcess > 0 ? config.MaxProcess : 8;
        _frameSize = Math.Max(config.FrameSize > 0 ? config.FrameSize : 10, _maxProcess);
        _restMinSeconds = config.RestMinSeconds > 0 ? config.RestMinSeconds : 120;
        _restMaxSeconds = config.RestMaxSeconds > 0 ? config.RestMaxSeconds : 240;

        // Selection đã lưu: WorkspaceViewModel dựng ScrapeMulti TRƯỚC Rebuild() nên lúc này danh sách tk còn rỗng
        // → giữ lại làm pending, RefreshAccounts áp ở lần đầu thực sự có tài khoản.
        if (config.SelectedAccountIds != null && config.SelectedAccountIds.Count > 0)
            _pendingSelectedIds = new HashSet<string>(config.SelectedAccountIds, StringComparer.OrdinalIgnoreCase);

        RefreshAccounts();

        UpdateLeadIndicator();
        UpdateLeadProgress();

        FailedSyncCount = ScrapeMultiFailedSyncStore.Shared.Count;
        ScrapeMultiFailedSyncStore.Shared.Changed += () =>
        {
            UiThread.Post(() =>
            {
                FailedSyncCount = ScrapeMultiFailedSyncStore.Shared.Count;
                OnPropertyChanged(nameof(HasFailedSyncs));
            });
        };
    }

    public void SaveCurrentConfig()
    {
        var config = new ScrapeMultiConfig
        {
            StartRow = StartRow,
            EndRow = EndRow,
            MaxProcess = MaxProcess,
            FrameSize = FrameSize,
            RestMinSeconds = RestMinSeconds,
            RestMaxSeconds = RestMaxSeconds,
            // Selection đã lưu chưa áp được thì giữ nguyên, không ghi đè bằng danh sách rỗng.
            SelectedAccountIds = _pendingSelectedIds is not null
                ? _pendingSelectedIds.ToList()
                : Accounts.Where(a => a.IsSelected).Select(a => a.Account.Account.Id).ToList()
        };
        ScrapeMultiConfigStore.Shared.Save(config);
    }

    public void RefreshAccounts()
    {
        if (IsRunning) return;

        var selectedMap = Accounts.GroupBy(a => a.Account.Account.Id).ToDictionary(g => g.Key, g => g.First().IsSelected);
        // Có pending thì chỉ áp khi danh sách tk thật sự có phần tử; rỗng → vẫn pending.
        var pending = _workspace.Accounts.Count > 0 ? _pendingSelectedIds : null;
        Accounts.Clear();
        foreach (var acc in _workspace.Accounts)
        {
            var item = new ScrapeMultiAccountItem(acc);
            // Gán IsSelected TRƯỚC khi đăng ký SelectionChanged → không kích hoạt SaveCurrentConfig giữa chừng.
            item.IsSelected = ScrapeMultiSelection.Resolve(acc.Account.Id, pending, selectedMap);
            item.SelectionChanged += () =>
            {
                UpdateLeadIndicator();
                UpdateLeadProgress();
                SaveCurrentConfig();
            };
            Accounts.Add(item);
        }
        if (pending is not null) _pendingSelectedIds = null;   // đã áp xong
        UpdateLeadIndicator();
        UpdateLeadProgress();
    }

    private void UpdateLeadIndicator()
    {
        var first = Accounts.FirstOrDefault(a => a.IsSelected);
        foreach (var a in Accounts)
        {
            a.IsLead = ReferenceEquals(a, first);
        }
    }

    public void UpdateLeadProgress()
    {
        var lead = Accounts.FirstOrDefault(a => a.IsLead) ?? Accounts.FirstOrDefault(a => a.IsSelected);
        if (lead is null || lead.Account.Shops.Count == 0)
        {
            LeadProgressSummary = "Chưa chọn tài khoản Lead";
            return;
        }

        var shop = lead.Account.Shops[0];
        var resolved = SharedWorkbookStore.Resolve(lead.Account.Account, shop.Shop);
        var scrapeKey = SharedWorkbookCatalog.ScrapeProgressKey(SharedWorkbookStore.IsActive, lead.Account.Account.Id, resolved.ProgressKey);
        var prog = ScrapeProgressStore.Shared.Find(lead.Account.Account.Id, scrapeKey);

        if (prog is null || prog.Completed.Count == 0)
        {
            LeadCompletedCount = 0;
            LeadLastRow = 0;
            NextRowToScrape = 1;
            LeadProgressSummary = $"Tài khoản Lead [{lead.DisplayName}]: Chưa cào dòng nào";
        }
        else
        {
            LeadCompletedCount = prog.Completed.Sum(r => Math.Max(0, r.To - r.From + 1));
            LeadLastRow = prog.LastRowReached;
            var nextRow = 1;
            foreach (var r in prog.Completed.OrderBy(r => r.From))
            {
                if (r.From <= nextRow)
                    nextRow = Math.Max(nextRow, r.To + 1);
                else
                    break;
            }
            NextRowToScrape = nextRow;
            LeadProgressSummary = $"Tài khoản Lead [{lead.DisplayName}]: Đã cào {LeadCompletedCount} SP (dòng cao nhất {LeadLastRow}) · Tiếp tục từ dòng {NextRowToScrape}";
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        if (IsRunning) return;
        foreach (var a in Accounts) a.IsSelected = true;
        UpdateLeadIndicator();
        UpdateLeadProgress();
        SaveCurrentConfig();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        if (IsRunning) return;
        foreach (var a in Accounts) a.IsSelected = false;
        UpdateLeadIndicator();
        UpdateLeadProgress();
        SaveCurrentConfig();
    }

    [RelayCommand]
    public void AutoAssignProxies(bool silent = false)
    {
        var keys = KiotProxyPoolStore.Shared.Keys;
        if (keys.Count == 0)
        {
            if (!silent) Log("⚠ Kho proxy rỗng, không có key KiotProxy nào.");
            return;
        }

        var changed = false;
        for (var i = 0; i < Accounts.Count; i++)
        {
            var item = Accounts[i];
            if (string.IsNullOrWhiteSpace(item.Account.Account.KiotProxyKey))
            {
                var assignedKey = keys[i % keys.Count];
                item.Account.Account.KiotProxyKey = assignedKey;
                item.Account.Account.Region = "random";
                item.Account.Account.ProxyType = "http";
                item.ProxyKey = assignedKey;
                changed = true;
            }
            else
            {
                item.ProxyKey = item.Account.Account.KiotProxyKey;
            }
        }

        if (changed)
        {
            BigSellerStore.Shared.Save();
            if (!silent) Log($"✓ Đã tự động gán proxy KiotProxy cho các tài khoản BigSeller.");
        }
        else if (!silent)
        {
            Log($"Tất cả các tài khoản BigSeller đã có key proxy.");
        }
    }

    [RelayCommand]
    public void ClearProxies()
    {
        if (IsRunning) return;
        var changed = false;
        foreach (var item in Accounts)
        {
            if (!string.IsNullOrWhiteSpace(item.Account.Account.KiotProxyKey))
            {
                item.Account.Account.KiotProxyKey = "";
                item.Account.Account.ProxyType = "";
                item.Account.Account.Region = "";
                item.ProxyKey = "";
                changed = true;
            }
        }

        if (changed)
        {
            BigSellerStore.Shared.Save();
            Log("✓ Đã reset tất cả proxy về IP máy trực tiếp.");
        }
        else
        {
            Log("Tất cả tài khoản BigSeller đã đang chạy bằng IP máy.");
        }
    }

    [RelayCommand]
    private async Task ResetSelectedProgressAsync()
    {
        if (IsRunning) return;
        var selected = Accounts.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0) return;

        foreach (var item in selected)
        {
            foreach (var shop in item.Account.Shops)
            {
                var resolved = SharedWorkbookStore.Resolve(item.Account.Account, shop.Shop);
                var scrapeKey = SharedWorkbookCatalog.ScrapeProgressKey(SharedWorkbookStore.IsActive, item.Account.Account.Id, resolved.ProgressKey);
                ScrapeProgressStore.Shared.BeginFresh(item.Account.Account.Id, scrapeKey, item.DisplayName, 0);
                shop.RefreshStatus();
            }
            item.Status = "Đã reset tiến độ";
            item.CompletedCount = 0;
        }

        UpdateLeadProgress();
        Log($"Đã reset tiến độ Scrape của {selected.Count} tài khoản được chọn.");
        await Task.CompletedTask;
    }

    public IAsyncRelayCommand ResetSelectedProgressAsyncCommand => ResetSelectedProgressCommand;

    [RelayCommand]
    public void ClearFailedSync()
    {
        if (IsRunning) return;
        ScrapeMultiFailedSyncStore.Shared.Clear();
        Log("✓ Đã xoá toàn bộ danh sách sản phẩm lỗi trong sổ lưu.");
    }

    /// <summary>
    /// Đồng bộ lại các sản phẩm bị lỗi trực tiếp từ ScrapeMultiFailedSyncStore.
    /// Không cần chạy lại Brave hay cào Shopee vì đã lưu sẵn dữ liệu JSON bóc tách được!
    /// </summary>
    [RelayCommand]
    public async Task RetryFailedSyncAsync()
    {
        if (IsRunning) return;
        var allFailed = ScrapeMultiFailedSyncStore.Shared.GetAll();
        if (allFailed.Count == 0)
        {
            Log("Không có sản phẩm nào bị lỗi trong sổ lưu.");
            return;
        }

        IsRunning = true;
        CurrentStatus = $"Đang đồng bộ lại {allFailed.Count} sản phẩm lỗi...";
        Log("════════════════════════════════════════════════════════════════════════");
        Log($"=== BẮT ĐẦU ĐỒNG BỘ LẠI {allFailed.Count} SẢN PHẨM LỖI TỪ SỔ LƯU ===");

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _loginFailures.Clear();

        var successCount = 0;
        try
        {
            foreach (var item in allFailed)
            {
                ct.ThrowIfCancellationRequested();
                var target = Accounts.FirstOrDefault(a => a.Account.Account.Id == item.AccountId);
                if (target is null)
                {
                    Log($"⚠ Bỏ qua dòng {item.Row}: không tìm thấy tài khoản {item.AccountName} ({item.AccountId}).");
                    continue;
                }

                Log($"[{target.DisplayName}] Đang đồng bộ bù dòng {item.Row} (URL: {item.Url})…");
                var ok = await SyncAccountWithRetryAsync(target, item.Row, item.Url, item.JsonData, ct);
                if (ok)
                {
                    successCount++;
                }
                await Task.Delay(300, ct);
            }

            Log($"=== HOÀN TẤT ĐỒNG BỘ LẠI: {successCount}/{allFailed.Count} SP THÀNH CÔNG ===");
            CurrentStatus = $"Đã đồng bộ lại xong {successCount}/{allFailed.Count} SP";
        }
        catch (OperationCanceledException)
        {
            CurrentStatus = "Đã dừng đồng bộ lại SP lỗi";
            Log("Đồng bộ lại SP lỗi đã dừng.");
        }
        catch (Exception ex)
        {
            CurrentStatus = "Lỗi đồng bộ lại: " + ex.Message;
            Log($"✘ Lỗi đồng bộ lại SP lỗi: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            UiThread.Post(() => UpdateLeadProgress());
        }
    }

    /// <summary>
    /// Đồng bộ 1 sản phẩm sang tài khoản BigSeller mục tiêu:
    /// Nếu lỗi mất phiên (mã 2001, 401006, hết cookie), tự động kiểm tra, đăng nhập lại và sync lại ngay.
    /// Nếu vẫn không sync được, tự động lưu vào ScrapeMultiFailedSyncStore.
    /// </summary>
    private async Task<bool> SyncAccountWithRetryAsync(
        ScrapeMultiAccountItem target,
        int row,
        string url,
        string jsonData,
        CancellationToken ct)
    {
        try
        {
            return await SyncAccountCoreAsync(target, row, url, jsonData, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // MỌI lỗi (kể cả huỷ/Dừng) đều phải ghi dòng vào sổ lỗi — nếu không Lead coi dòng là xong và resume không thử lại.
            var reason = ex is OperationCanceledException
                ? "Bị huỷ/dừng trước khi sync xong"
                : "Lỗi không mong đợi: " + ex.Message;
            target.Status = $"✘ Lỗi sync dòng {row}";
            Log($"[{target.DisplayName}] ✘ Không sync được dòng {row}: {reason} → Đã lưu vào sổ lỗi.");
            RecordFailedSync(target, row, url, jsonData, reason);
            return false;
        }
    }

    /// <summary>Một dòng log mỗi lần probe phiên dẫn tới quyết định — để chẩn đoán sau này khi mất phiên thật
    /// (chưa biết getUserAccount.json trả gì cho phiên chết).</summary>
    private void LogProbe(ScrapeMultiAccountItem target, int row, string phase, BigSellerSessionProbe probe, ScrapeMultiSyncAction action) =>
        Log($"[{target.DisplayName}] Probe phiên ({phase}) dòng {row}: {probe} → {action}");

    private static void RecordFailedSync(ScrapeMultiAccountItem target, int row, string url, string jsonData, string error)
    {
        ScrapeMultiFailedSyncStore.Shared.AddOrUpdate(new FailedSyncItem
        {
            AccountId = target.Account.Account.Id,
            AccountName = target.DisplayName,
            Row = row,
            Url = url,
            JsonData = jsonData,
            Error = error,
            FailedAt = DateTimeOffset.Now
        });
    }

    /// <summary>Ghi thẳng dòng vào sổ lỗi cho tk đã đăng nhập tự động thất bại trong lượt này (không gọi mạng / không đăng nhập lại).</summary>
    private bool FailFastLoginFailed(ScrapeMultiAccountItem target, int row, string url, string jsonData)
    {
        const string error = "Đăng nhập tự động thất bại trong lượt chạy này — cần đăng nhập thủ công (Mở Profile) rồi bấm đồng bộ lại SP lỗi.";
        target.Status = $"✘ Chưa đăng nhập (dòng {row})";
        if (_loginFailures.TryLogOnce(target.Account.Account.Id))
            Log($"[{target.DisplayName}] ✘ Tự đăng nhập đã thất bại trong lượt chạy này → các SP sync tới tài khoản này sẽ ghi thẳng vào sổ lỗi (không đăng nhập lại). Hãy đăng nhập thủ công rồi bấm đồng bộ lại SP lỗi.");
        RecordFailedSync(target, row, url, jsonData, error);
        return false;
    }

    private async Task<bool> SyncAccountCoreAsync(
        ScrapeMultiAccountItem target,
        int row,
        string url,
        string jsonData,
        CancellationToken ct)
    {
        if (_loginFailures.IsFailed(target.Account.Account.Id))
            return FailFastLoginFailed(target, row, url, jsonData);

        target.Status = $"Đang sync dòng {row}…";
        var res = await BigSellerFanOutClient.PostCrawlAsync(
            target.Account.Account, url, jsonData, msg => Log(msg), ct).ConfigureAwait(false);

        // NẾU LỖI: Kiểm tra xem có phải do mất cookie/phiên hết hạn không
        if (!res.Success)
        {
            var isSessionIssue = res.Code == 2001 ||
                                 res.Message.Contains("401006") ||
                                 res.Message.Contains("401002") ||
                                 res.Message.Contains("hết hạn phiên") ||
                                 res.Message.Contains("chưa có file cookie") ||
                                 res.Message.Contains("muc_token");

            string? proxy = null;
            if (target.Account.Account.HasProxy)
            {
                try
                {
                    proxy = await OpenMultiBraveLauncherV3.BigSellerProxyResolver.ResolveServerAsync(
                        target.Account.Account.KiotProxyKey, target.Account.Account.Region, target.Account.Account.ProxyType, Log).ConfigureAwait(false);
                }
                catch { }
            }

            // Lỗi phiên đã rõ (2001/401006…) → bỏ probe trước khoá; probe TRONG khoá lo phần còn lại.
            var needLock = true;
            if (!isSessionIssue)
            {
                var probe0 = await BigSellerCookieEngine.ProbeSessionAsync(target.Account.Account, proxy, ct).ConfigureAwait(false);
                var action0 = ScrapeMultiSessionDecision.AfterProbe(false, probe0, inLock: false);
                needLock = action0 == ScrapeMultiSyncAction.Login;
                LogProbe(target, row, "trước khoá", probe0, action0);
            }

            if (needLock)
            {
                var sem = _loginLocks.GetOrAdd(target.Account.Account.Id, _ => new SemaphoreSlim(1, 1));
                await sem.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    // Luồng khác (đang giữ lock trước đó) có thể vừa đăng nhập thất bại → không đăng nhập lại.
                    if (_loginFailures.IsFailed(target.Account.Account.Id))
                        return FailFastLoginFailed(target, row, url, jsonData);

                    // Kiểm tra lại sau khi vào lock phòng khi luồng khác vừa hoàn thành đăng nhập
                    var probeNow = await BigSellerCookieEngine.ProbeSessionAsync(target.Account.Account, proxy, ct).ConfigureAwait(false);
                    var actionNow = ScrapeMultiSessionDecision.AfterProbe(isSessionIssue, probeNow, inLock: true);
                    LogProbe(target, row, "trong khoá", probeNow, actionNow);
                    if (actionNow == ScrapeMultiSyncAction.Repost)
                    {
                        Log($"[{target.DisplayName}] ✓ Đã có phiên mới hợp lệ (từ luồng trước). Thử sync lại dòng {row}…");
                        target.Status = $"Sync lại dòng {row}…";
                        res = await BigSellerFanOutClient.PostCrawlAsync(
                            target.Account.Account, url, jsonData, msg => Log(msg), ct).ConfigureAwait(false);
                    }
                    else if (actionNow == ScrapeMultiSyncAction.Book)
                    {
                        // Không hỏi được BigSeller (mạng) và server chưa báo lỗi phiên → KHÔNG đăng nhập, KHÔNG MarkFailed.
                        // Dòng rơi xuống nhánh ghi sổ lỗi bên dưới (lỗi thật), các dòng sau vẫn chạy bình thường.
                        Log($"[{target.DisplayName}] ⚠ Không kết nối được BigSeller để kiểm tra phiên (mạng) → KHÔNG đăng nhập lại; dòng {row} vào sổ lỗi.");
                    }
                    else
                    {
                        Log($"[{target.DisplayName}] ⚠ Phát hiện phiên BigSeller bị hết hạn/mất khi sync dòng {row} ({res.Message}) → Đang tự động đăng nhập lại…");
                        target.Status = "Tự đăng nhập lại…";

                        var outcome = await OpenMultiBraveLauncherV3.BigSellerAutoLogin.LoginHeadlessAsync(
                            target.Account.Account.Id,
                            target.Account.Account.Email,
                            target.Account.Account.Password,
                            target.Account.Account.CookieFile,
                            proxy,
                            Log,
                            ct).ConfigureAwait(false);

                        // Success: probe xác minh cookie mới; không Success: probe lại 1 lần để phân biệt mạng với phiên.
                        var loginOk = outcome == OpenMultiBraveLauncherV3.AutoLoginOutcome.Success;
                        var probeLogin = await BigSellerCookieEngine.ProbeSessionAsync(target.Account.Account, proxy, ct).ConfigureAwait(false);
                        var afterLogin = ScrapeMultiSessionDecision.AfterLogin(loginOk, probeLogin);
                        LogProbe(target, row, "sau đăng nhập", probeLogin, afterLogin);

                        if (afterLogin == ScrapeMultiSyncAction.Repost)
                        {
                            target.Account.NotifyCookieChanged();
                            Log(loginOk
                                ? $"[{target.DisplayName}] ✓ Đã tự đăng nhập lại thành công! Thử sync lại dòng {row}…"
                                : $"[{target.DisplayName}] ⚠ Đăng nhập tự động không thành công ({outcome}) nhưng phiên BigSeller vẫn dùng được → thử sync lại dòng {row}…");
                            target.Status = $"Sync lại dòng {row}…";
                            res = await BigSellerFanOutClient.PostCrawlAsync(
                                target.Account.Account, url, jsonData, msg => Log(msg), ct).ConfigureAwait(false);
                        }
                        else if (afterLogin == ScrapeMultiSyncAction.Book)
                        {
                            // Không hỏi được BigSeller (mạng) → không kết luận phiên chết: KHÔNG MarkFailed, dòng vào sổ lỗi (nhánh dưới).
                            Log($"[{target.DisplayName}] ⚠ Không kết nối được BigSeller (mạng) sau bước đăng nhập ({outcome}) → KHÔNG đánh dấu tk lỗi; dòng {row} vào sổ lỗi, các dòng sau thử lại bình thường.");
                        }
                        else if (loginOk)
                        {
                            Log($"[{target.DisplayName}] ✘ Đăng nhập thành công nhưng cookie chưa được BigSeller chấp nhận.");
                            _loginFailures.MarkFailed(target.Account.Account.Id);
                        }
                        else
                        {
                            Log($"[{target.DisplayName}] ✘ Tự đăng nhập thất bại ({outcome}). Cần đăng nhập thủ công qua Profile.");
                            _loginFailures.MarkFailed(target.Account.Account.Id);
                        }
                    }
                }
                finally
                {
                    sem.Release();
                }
            }
        }

        if (res.Success)
        {
            int doneCount;
            lock (target) { doneCount = ++target.CompletedCount; }   // fan-out chạy nhiều luồng nền
            target.Status = $"✓ Xong dòng {row} ({doneCount} SP)";
            Log($"[{target.DisplayName}] ✓ Sync thành công dòng {row}.");
            Log($"✓ Đã copy scrape cho tk {target.DisplayName}");

            // Xoá khỏi danh sách lỗi nếu trước đó từng bị lỗi
            ScrapeMultiFailedSyncStore.Shared.Remove(target.Account.Account.Id, row);

            // Đánh dấu hoàn thành trong ScrapeProgressStore cho mọi shop của tài khoản này — CHỈ khi đang dùng
            // workbook chung (mọi shop cùng 1 sheet nên số dòng khớp nhau). Không có workbook chung thì mỗi shop
            // 1 sheet riêng, dòng `row` của Lead không tương ứng dòng nào của shop đích → không đánh dấu (tránh tiến độ giả).
            if (SharedWorkbookStore.IsActive)
            {
                foreach (var s in target.Account.Shops)
                {
                    var resolved = SharedWorkbookStore.Resolve(target.Account.Account, s.Shop);
                    var scrapeKey = SharedWorkbookCatalog.ScrapeProgressKey(SharedWorkbookStore.IsActive, target.Account.Account.Id, resolved.ProgressKey);
                    ScrapeProgressStore.Shared.MarkCompleted(target.Account.Account.Id, scrapeKey, row, row);
                }
            }

            await UiThread.InvokeAsync(() =>
            {
                foreach (var s in target.Account.Shops)
                    s.RefreshStatus();
            });

            return true;
        }
        else
        {
            target.Status = $"✘ Lỗi sync dòng {row}";
            Log($"[{target.DisplayName}] ✘ Không sync được dòng {row}: {res.Message} → Đã lưu vào sổ lỗi.");

            // Lưu vào ScrapeMultiFailedSyncStore
            RecordFailedSync(target, row, url, jsonData, res.Message);

            return false;
        }
    }

    [RelayCommand]
    public async Task StartScrapeMultiAsync()
    {
        if (IsRunning) return;

        var selected = Accounts.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0)
        {
            CurrentStatus = "Chưa chọn tài khoản nào";
            Log("⚠ Vui lòng tick chọn ít nhất 1 tài khoản để chạy Scrape Multi.");
            return;
        }

        SaveCurrentConfig();
        UpdateLeadIndicator();
        var leadItem = selected.First();
        var leadAcc = leadItem.Account;
        var otherSelected = selected.Skip(1).ToList();

        if (leadAcc.Shops.Count == 0)
        {
            CurrentStatus = "Tài khoản chính không có shop nào";
            Log($"⚠ Tài khoản Lead [{leadAcc.DisplayName}] không có shop nào.");
            return;
        }

        var leadShop = leadAcc.Shops[0];
        leadAcc.ScrapeTarget.SelectedShop = leadShop.Shop;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsRunning = true;
        _loginFailures.Clear();
        _activeLeadTarget = leadAcc.ScrapeTarget;

        Action<string> onSubLog = msg => Log(msg);
        _workspace.Scrape.LogAppended += onSubLog;

        Action<ScrapeRunner>? onRunnerCreated = null;

        try
        {
            // ── BƯỚC 1: BẮT BUỘC KIỂM TRA TÀI KHOẢN CHÍNH (LEAD) ĐẦU TIÊN ──
            CurrentStatus = $"Đang kiểm tra tài khoản Lead [{leadAcc.DisplayName}]...";
            Log("════════════════════════════════════════════════════════════════════════");
            Log($"=== [1/2] KIỂM TRA TÀI KHOẢN CHÍNH (LEAD): {leadAcc.DisplayName} ===");

            var leadReady = await EnsureSingleSessionAsync(leadItem, ct);
            if (leadReady == SessionEnsureResult.NetworkUnreachable)
            {
                CurrentStatus = "Không kết nối được BigSeller (mạng)";
                Log($"✘ DỪNG SCRAPE: Không kết nối được BigSeller để kiểm tra phiên tài khoản chính [{leadAcc.DisplayName}] (lỗi mạng/timeout) — KHÔNG phải thiếu cookie.");
                Log("👉 Kiểm tra kết nối mạng/proxy rồi bấm chạy lại.");
                return;
            }
            if (leadReady != SessionEnsureResult.Ready)
            {
                CurrentStatus = "Tài khoản Lead chưa có cookie hợp lệ";
                Log($"✘ DỪNG SCRAPE: Tài khoản chính [{leadAcc.DisplayName}] chưa có cookie BigSeller hợp lệ và không thể tự đăng nhập.");
                Log($"👉 Vui lòng vào tab 'Cấu hình BigSeller', chọn [{leadAcc.DisplayName}] và bấm 'Mở Profile Bigseller' để đăng nhập!");
                return;
            }
            Log($"✓ Tài khoản chính [{leadAcc.DisplayName}] đã sẵn sàng!");

            // ── BƯỚC 2: TIẾP TỤC VỚI CÁC TÀI KHOẢN ĐỒNG BỘ ──
            if (otherSelected.Count > 0)
            {
                CurrentStatus = $"Đang kiểm tra {otherSelected.Count} tài khoản đồng bộ...";
                Log($"=== [2/2] KIỂM TRA CÁC TÀI KHOẢN ĐỒNG BỘ ({otherSelected.Count} TÀI KHOẢN) ===");
                foreach (var item in otherSelected)
                {
                    ct.ThrowIfCancellationRequested();
                    var ok = await EnsureSingleSessionAsync(item, ct);
                    if (ok == SessionEnsureResult.NetworkUnreachable)
                    {
                        Log($"⚠ [{item.DisplayName}] Không kết nối được BigSeller để kiểm tra phiên (mạng) — vẫn chạy bình thường; SP nào sync lỗi sẽ vào sổ lỗi để đồng bộ bù sau.");
                    }
                    else if (ok != SessionEnsureResult.Ready)
                    {
                        Log($"⚠ [{item.DisplayName}] Chưa có phiên hợp lệ và tự đăng nhập thất bại — trong lượt này sẽ KHÔNG đăng nhập lại; các SP sync tới tài khoản này được ghi thẳng vào sổ lỗi để đồng bộ bù sau khi đăng nhập thủ công.");
                    }
                }
            }
            Log("════════════════════════════════════════════════════════════════════════");

            CurrentStatus = $"Đang chạy Scrape Multi ({selected.Count} tài khoản)...";
            Log($"=== BẮT ĐẦU SCRAPE MULTI ({selected.Count} TÀI KHOẢN ĐƯỢC CHỌN) ===");
            Log($"Tài khoản chính (Lead cào Brave): {leadAcc.DisplayName}");
            if (otherSelected.Count > 0)
            {
                Log($"Các tài khoản Fan-out đồng bộ ({otherSelected.Count}): " +
                    string.Join(", ", otherSelected.Select(x => $"{x.DisplayName} (proxy: {(string.IsNullOrEmpty(x.ProxyKey) ? "máy" : x.ProxyKey[..Math.Min(8, x.ProxyKey.Length)] + "…")})")));
            }
            Log($"════════════════════════════════════════════════════════════════════════");

            foreach (var a in Accounts)
            {
                if (a.IsSelected)
                {
                    a.Status = a.IsLead ? "Lead (Đang cào…)" : "Chờ đồng bộ…";
                }
                else
                {
                    a.Status = "Bỏ qua (không chọn)";
                }
            }

            // Đặt SelectedAccount trên Workspace
            await UiThread.InvokeAsync(() => _workspace.SelectedAccount = leadAcc);

            // Bắt sự kiện tạo runner để đăng ký ProductScraped với cơ chế tự động login lại & lưu lỗi
            onRunnerCreated = runner =>
            {
                // Session có thể chứa runner của tk BigSeller khác (job chạy tay song song) → chỉ nghe runner của Lead.
                if (!string.Equals(runner.BigSellerAccountId, leadAcc.Account.Id, StringComparison.OrdinalIgnoreCase))
                    return;

                runner.ProductScraped += async (row, url, jsonData) =>
                {
                    if (otherSelected.Count == 0) return;

                    Log($"────────────────────────────────────────────────────────");
                    Log($"[Fan-out] Dòng {row} đã cào xong trên Lead → Bắt đầu sync tới {otherSelected.Count} tài khoản…");

                    var tasks = otherSelected.Select(target => SyncAccountWithRetryAsync(target, row, url, jsonData, ct));
                    await Task.WhenAll(tasks).ConfigureAwait(false);

                    Log($"[Fan-out] Hoàn tất đồng bộ dòng {row} cho các tài khoản phụ.");
                    await UiThread.InvokeAsync(() => UpdateLeadProgress());
                };
            };

            _workspace.Scrape.RunnerCreated += onRunnerCreated;

            // Khởi động chạy Lead Scrape với chế độ resume = true
            var effectiveFrameSize = Math.Max(FrameSize, MaxProcess);
            ct.ThrowIfCancellationRequested();
            var jobOutcome = await _workspace.Scrape.RunSingleAsync(
                leadAcc.ScrapeTarget,
                resume: true,
                silent: false,
                startRow: StartRow,
                endRow: EndRow,
                processes: MaxProcess,
                frameSize: effectiveFrameSize,
                restMinSeconds: RestMinSeconds,
                restMaxSeconds: RestMaxSeconds);

            // Kết quả THẬT của job Lead (không suy từ text Status của ScrapeViewModel).
            if (jobOutcome.Status == ScrapeJobStatus.Stopped)
            {
                CurrentStatus = "Đã dừng Scrape Multi";
                Log("Scrape Multi đã dừng theo yêu cầu người dùng.");
                MarkInterrupted(selected, "Đã dừng");
            }
            else if (jobOutcome.Status != ScrapeJobStatus.Completed)
            {
                var what = jobOutcome.Status == ScrapeJobStatus.NotStarted ? "Không khởi động được scrape Lead" : "Scrape Lead thất bại";
                CurrentStatus = $"{what}: {jobOutcome.Reason}";
                Log($"✘ Scrape Multi KHÔNG hoàn tất — {what}: {jobOutcome.Reason}");
                MarkInterrupted(selected, "Chưa hoàn tất");
            }
            else
            {
                var failedTotal = 0;
                foreach (var a in selected)
                {
                    var failedCount = ScrapeMultiFailedSyncStore.Shared.CountFor(a.Account.Account.Id);
                    failedTotal += failedCount;
                    a.Status = failedCount > 0
                        ? $"⚠ Xong, còn {failedCount} dòng lỗi ({a.CompletedCount} SP)"
                        : $"✓ Hoàn tất ({a.CompletedCount} SP)";
                    foreach (var s in a.Account.Shops) s.RefreshStatus();
                }
                if (failedTotal > 0)
                {
                    CurrentStatus = $"Scrape Multi xong, còn {failedTotal} dòng lỗi cần đồng bộ lại";
                    Log($"=== SCRAPE MULTI XONG CHO {selected.Count} TÀI KHOẢN, CÒN {failedTotal} DÒNG LỖI CẦN ĐỒNG BỘ LẠI ===");
                }
                else
                {
                    CurrentStatus = "Scrape Multi hoàn tất";
                    Log($"=== HOÀN TẤT SCRAPE MULTI CHO {selected.Count} TÀI KHOẢN ===");
                }
            }
        }
        catch (OperationCanceledException)
        {
            CurrentStatus = "Đã dừng Scrape Multi";
            Log("Scrape Multi đã dừng theo yêu cầu người dùng.");
            MarkInterrupted(selected, "Đã dừng");
        }
        catch (Exception ex)
        {
            CurrentStatus = "Lỗi Scrape Multi: " + ex.Message;
            Log($"✘ Lỗi Scrape Multi: {ex.Message}");
        }
        finally
        {
            _activeLeadTarget = null;
            if (onRunnerCreated is not null)
                _workspace.Scrape.RunnerCreated -= onRunnerCreated;
            _workspace.Scrape.LogAppended -= onSubLog;
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            UiThread.Post(() => UpdateLeadProgress());
        }
    }

    /// <summary>Các tk còn đang chạy dở (Lead đang cào / chờ đồng bộ / đang sync) → đặt trạng thái không phải "hoàn tất".</summary>
    private static void MarkInterrupted(IEnumerable<ScrapeMultiAccountItem> items, string status)
    {
        foreach (var a in items)
        {
            if (a.Status.Contains("Đang", StringComparison.OrdinalIgnoreCase) ||
                a.Status.Contains("Chờ đồng bộ", StringComparison.OrdinalIgnoreCase) ||
                a.Status.Contains("Sync lại", StringComparison.OrdinalIgnoreCase))
                a.Status = status;
        }
    }

    private async Task<SessionEnsureResult> EnsureSingleSessionAsync(ScrapeMultiAccountItem item, CancellationToken ct)
    {
        var acc = item.Account.Account;

        if (string.IsNullOrWhiteSpace(acc.CookieFile))
        {
            acc.CookieFile = Path.Combine(SuitePaths.ModuleDir("shared"), "bigseller-cookies", acc.Id + ".json");
            BigSellerStore.Shared.Save();
        }

        string? proxy = null;
        if (acc.HasProxy)
        {
            try
            {
                proxy = await OpenMultiBraveLauncherV3.BigSellerProxyResolver.ResolveServerAsync(
                    acc.KiotProxyKey, acc.Region, acc.ProxyType, Log).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"[{item.DisplayName}] Lỗi lấy proxy: {ex.Message} — thử IP máy.");
            }
        }
        else
        {
            Log($"[{item.DisplayName}] Kết nối bằng IP máy trực tiếp (không qua proxy).");
        }

        item.Status = "Kiểm tra phiên…";
        var probe = await BigSellerCookieEngine.ProbeSessionAsync(acc, proxy, ct).ConfigureAwait(false);
        if (probe == BigSellerSessionProbe.Alive)
        {
            item.Status = item.IsLead ? "Lead (Đã có cookie ✓)" : "Đã có cookie ✓";
            Log($"[{item.DisplayName}] ✓ Phiên BigSeller đang sống (đã xác thực{(proxy is null ? " qua IP máy" : " qua proxy")}).");
            return SessionEnsureResult.Ready;
        }
        if (probe == BigSellerSessionProbe.Unreachable)
        {
            // Rớt mạng KHÔNG phải mất phiên: không đăng nhập (sẽ hỏng theo), không MarkFailed.
            item.Status = "Không kết nối được BigSeller (mạng)";
            Log($"[{item.DisplayName}] ⚠ Không kết nối được BigSeller để kiểm tra phiên (lỗi mạng/timeout{(proxy is null ? ", IP máy" : ", qua proxy")}) — KHÔNG đăng nhập lại. Probe phiên: {probe}.");
            return SessionEnsureResult.NetworkUnreachable;
        }
        Log($"[{item.DisplayName}] Probe phiên (khởi động): {probe} → đăng nhập lại.");

        Log($"[{item.DisplayName}] ⚠ Chưa có cookie hoặc phiên không khớp IP{(proxy is null ? " máy" : " proxy")} → Đang thử tự đăng nhập…");
        item.Status = "Đang tự đăng nhập…";

        var outcome = await OpenMultiBraveLauncherV3.BigSellerAutoLogin.LoginHeadlessAsync(
            acc.Id, acc.Email, acc.Password, acc.CookieFile, proxy, Log, ct).ConfigureAwait(false);

        if (outcome == OpenMultiBraveLauncherV3.AutoLoginOutcome.Success)
        {
            var verified = await BigSellerCookieEngine.ProbeSessionAsync(acc, proxy, ct).ConfigureAwait(false);
            if (verified == BigSellerSessionProbe.Alive)
            {
                item.Account.NotifyCookieChanged();
                item.Status = item.IsLead ? "Lead (Đã đăng nhập ✓)" : "Đã đăng nhập ✓";
                Log($"[{item.DisplayName}] ✓ Tự đăng nhập và xác thực thành công, đã lưu cookie mới!");
                return SessionEnsureResult.Ready;
            }
            else if (verified == BigSellerSessionProbe.Unreachable)
            {
                item.Status = "Không kết nối được BigSeller (mạng)";
                Log($"[{item.DisplayName}] ⚠ Đăng nhập xong nhưng không kết nối được BigSeller để xác thực (lỗi mạng/timeout) — KHÔNG đánh dấu tk lỗi. Probe phiên: {verified}.");
                return SessionEnsureResult.NetworkUnreachable;
            }
            else
            {
                item.Status = "Phiên chưa hợp lệ";
                Log($"[{item.DisplayName}] ⚠ Tự đăng nhập xong nhưng cookie chưa được BigSeller chấp nhận (vui lòng vào Cấu hình Bigseller → 'Mở Profile' để đăng nhập bằng tay).");
                _loginFailures.MarkFailed(acc.Id);
                return SessionEnsureResult.Failed;
            }
        }
        else if (outcome == OpenMultiBraveLauncherV3.AutoLoginOutcome.NeedsOtp)
        {
            item.Status = "Cần OTP email";
            Log($"[{item.DisplayName}] ⚠ BigSeller đòi mã xác thực email (thiết bị mới). Vui lòng vào Cấu hình Bigseller → 'Mở Profile' để đăng nhập.");
            _loginFailures.MarkFailed(acc.Id);
            return SessionEnsureResult.Failed;
        }
        else
        {
            item.Status = "Chưa có cookie";
            Log($"[{item.DisplayName}] ✘ Chưa có cookie hợp lệ trên IP máy. Vui lòng vào tab 'Cấu hình Bigseller' → click 'Mở Profile' của tài khoản này để đăng nhập 1 lần.");
            _loginFailures.MarkFailed(acc.Id);
            return SessionEnsureResult.Failed;
        }
    }

    [RelayCommand]
    public void StopScrapeMulti()
    {
        if (!IsRunning) return;
        CurrentStatus = "Đang dừng Scrape Multi...";
        try
        {
            _cts?.Cancel();
        }
        catch { }

        // Chỉ dừng job Lead của Scrape Multi (không dừng cả phiên scrape của các tk khác).
        // _activeLeadTarget = null khi đang đồng bộ lại SP lỗi (không có Brave) → chỉ cần huỷ _cts.
        var lead = _activeLeadTarget;
        if (lead is not null)
            TaskExt.FireAndForget(_workspace.Scrape.StopSingleAsync(lead), "dừng Lead Scrape Multi");
    }
}
