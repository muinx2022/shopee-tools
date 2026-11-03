using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shopee.Core.BigSeller;
using Shopee.Core.Coordination;
using Shopee.Core.Infrastructure;
using Shopee.Core.Progress;
using Shopee.Core.Scrape;
using Shopee.Workspace.Infrastructure;
using Shopee.Workspace.Services;
using Shopee.Workspace;

namespace Shopee.Workspace.Modules.Workspace;

/// <summary>
/// Đại diện cho 1 dòng tài khoản BigSeller trong hộp thoại Auto Run.
/// </summary>
public sealed partial class AutoRunAccountItem : ObservableObject
{
    public WorkspaceAccountViewModel Account { get; }
    public string DisplayName => Account.DisplayName;
    public string CookieStatus => Account.CookieStatus;
    public bool HasCookie => Account.HasCookie;
    public int ShopCount => Account.ShopCount;
    public string ShopsSummary => Account.ScrapeSummary;

    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private string _status = "Chờ chạy";

    public AutoRunAccountItem(WorkspaceAccountViewModel account)
    {
        Account = account;
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(CookieStatus));
        OnPropertyChanged(nameof(HasCookie));
        OnPropertyChanged(nameof(ShopCount));
        OnPropertyChanged(nameof(ShopsSummary));
    }
}

/// <summary>
/// ViewModel quản lý quy trình tự động tuần tự: Scrape -> Import -> Update cho các tài khoản được chọn.
/// </summary>
public sealed partial class AutoRunViewModel : ObservableObject
{
    private readonly WorkspaceViewModel _workspace;
    private CancellationTokenSource? _cts;
    private volatile bool _skipCurrentShop;
    private WorkspaceAccountViewModel? _currentRunningAccount;
    private WorkspaceShopViewModel? _currentRunningShop;
    private DateTime? _lastRunEndedUtc;

    /// <summary>Sau khi lượt Auto Run kết thúc, nút "Bắt đầu" nhảy vào đúng chỗ nút "Dừng" — bấm Dừng vào lúc đó trúng Bắt đầu.
    /// Lệnh Bắt đầu đến trong khoảng này sau mốc kết thúc là bấm nhầm → bỏ qua.</summary>
    internal static readonly TimeSpan StartGuardWindow = TimeSpan.FromSeconds(3);

    internal static bool ShouldIgnoreStart(DateTime? lastRunEndedUtc, DateTime nowUtc) =>
        lastRunEndedUtc is { } ended && nowUtc - ended < StartGuardWindow;

    /// <summary>Lượt Import của shop không thêm được SP nào (0 SP mới, tổng 0, shop chưa Import xong: kể cả ca bị cổng Clear chặn hoặc lô
    /// chưa chắc). Chỉ để chọn câu log/trạng thái: dù đúng hay sai, bước Update của shop VẪN chạy (trừ UpdateDone / Dừng / Next shop).</summary>
    internal static bool IsZeroImportRun(int imported, int after, bool importDone) => imported == 0 && after == 0 && !importDone;

    public ObservableCollection<AutoRunAccountItem> Accounts { get; } = [];
    public LogBuffer LogLines { get; } = new("workspace-autorun.log");

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

    public string HeaderTitle => WorkspaceAppConfig.Role switch
    {
        WorkspaceRole.Scrape => "Auto Run — Tự động Scrape",
        WorkspaceRole.ImportUpdate => "Auto Run — Tự động Import & Update",
        _ => "Auto Run Workspace"
    };

    public string HeaderCaption => WorkspaceAppConfig.Role switch
    {
        WorkspaceRole.Scrape => "Chọn các tài khoản BigSeller cần chạy. Mỗi tài khoản sẽ tự động thực hiện Scrape (1 shop đại diện/tài khoản).",
        WorkspaceRole.ImportUpdate => "Chọn các tài khoản BigSeller cần chạy. Mỗi tài khoản sẽ tuần tự thực hiện: Import (1 shop/lần, tab Tất cả) → Update (từng shop).",
        _ => "Tự động chạy tuần tự: Scrape (chỉ 1 lần/acc) → Import (tab Tất cả, 1 shop/lần) → Update cho danh sách tài khoản đã chọn."
    };

    [ObservableProperty]
    private string _currentStatus = "Sẵn sàng";

    public AutoRunViewModel(WorkspaceViewModel workspace)
    {
        _workspace = workspace;
        RefreshAccounts();
    }

    public void RefreshAccounts()
    {
        if (IsRunning) return;

        var selectedMap = Accounts.GroupBy(a => a.Account.Account.Id).ToDictionary(g => g.Key, g => g.First().IsSelected);
        Accounts.Clear();
        foreach (var acc in _workspace.Accounts)
        {
            var item = new AutoRunAccountItem(acc);
            if (selectedMap.TryGetValue(acc.Account.Id, out var prevSelected))
            {
                item.IsSelected = prevSelected;
            }
            Accounts.Add(item);
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var a in Accounts) a.IsSelected = true;
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var a in Accounts) a.IsSelected = false;
    }

    [RelayCommand]
    private async Task ResetSelectedProgressAsync()
    {
        var selected = Accounts.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0) return;

        string prompt;
        if (WorkspaceAppConfig.IsScrapeOnly)
            prompt = $"Reset tiến độ Scrape của tất cả shop thuộc {selected.Count} tài khoản đang chọn?";
        else if (WorkspaceAppConfig.IsImportUpdateOnly)
            prompt = $"Reset tiến độ Import / Update của tất cả shop thuộc {selected.Count} tài khoản đang chọn?";
        else
            prompt = $"Reset toàn bộ tiến độ (Scrape · Import · Update) của tất cả shop thuộc {selected.Count} tài khoản đang chọn?";

        if (!await Dialogs.ConfirmAsync(prompt, "Reset tiến độ"))
            return;

        foreach (var accItem in selected)
        {
            foreach (var shop in accItem.Account.Shops)
            {
                if (WorkspaceAppConfig.IsScrapeOnly)
                    shop.ResetScrapeProgressOnly();
                else if (WorkspaceAppConfig.IsImportUpdateOnly)
                    shop.ResetImportUpdateProgressOnly();
                else
                    shop.ResetAllProgress();
            }
            accItem.Status = "Đã reset";
        }
        var scopeDesc = WorkspaceAppConfig.IsScrapeOnly ? "tiến độ Scrape" : (WorkspaceAppConfig.IsImportUpdateOnly ? "tiến độ Import / Update" : "toàn bộ tiến độ");
        Log($"Đã reset {scopeDesc} cho {selected.Count} tài khoản được chọn.");
    }

    [RelayCommand]
    private async Task ResetAccountProgressAsync(AutoRunAccountItem? item)
    {
        if (item is null) return;

        string prompt;
        if (WorkspaceAppConfig.IsScrapeOnly)
            prompt = $"Reset tiến độ Scrape của {item.Account.Shops.Count} shop thuộc tài khoản '{item.DisplayName}'?";
        else if (WorkspaceAppConfig.IsImportUpdateOnly)
            prompt = $"Reset tiến độ Import / Update của {item.Account.Shops.Count} shop thuộc tài khoản '{item.DisplayName}'?";
        else
            prompt = $"Reset toàn bộ tiến độ (Scrape · Import · Update) của {item.Account.Shops.Count} shop thuộc tài khoản '{item.DisplayName}'?";

        if (!await Dialogs.ConfirmAsync(prompt, "Reset tài khoản"))
            return;

        foreach (var shop in item.Account.Shops)
        {
            if (WorkspaceAppConfig.IsScrapeOnly)
                shop.ResetScrapeProgressOnly();
            else if (WorkspaceAppConfig.IsImportUpdateOnly)
                shop.ResetImportUpdateProgressOnly();
            else
                shop.ResetAllProgress();
        }
        item.Status = "Đã reset";
        var scopeDesc = WorkspaceAppConfig.IsScrapeOnly ? "tiến độ Scrape" : (WorkspaceAppConfig.IsImportUpdateOnly ? "tiến độ Import / Update" : "toàn bộ tiến độ");
        Log($"[{item.DisplayName}] Đã reset {scopeDesc} các shop.");
    }

    public IAsyncRelayCommand ResetSelectedProgressAsyncCommand => ResetSelectedProgressCommand;
    public IAsyncRelayCommand<AutoRunAccountItem?> ResetAccountProgressAsyncCommand => ResetAccountProgressCommand;

    [RelayCommand]
    private async Task StartAutoRunAsync()
    {
        if (IsRunning) return;
        if (ShouldIgnoreStart(_lastRunEndedUtc, DateTime.UtcNow))
        {
            Log("Bỏ qua Bắt đầu vì lượt trước vừa kết thúc (dưới 3 giây) — có thể là bấm nhầm khi định bấm Dừng.");
            return;
        }
        BigSellerStore.Shared.ReloadIfExternalChange();
        ScrapeProgressStore.Shared.ReloadIfExternalChange();
        OpProgressStore.Shared.ReloadIfExternalChange();
        var targets = Accounts.Where(a => a.IsSelected).ToList();
        if (targets.Count == 0)
        {
            CurrentStatus = "Chưa chọn tài khoản nào để chạy.";
            return;
        }

        IsRunning = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            await RunAsync(targets, ct);
        }
        catch (OperationCanceledException)
        {
            CurrentStatus = "Đã dừng Auto Run.";
        }
        catch (Exception ex)
        {
            CurrentStatus = $"Lỗi Auto Run: {ex.Message}";
        }
        finally
        {
            _lastRunEndedUtc = DateTime.UtcNow;
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    public void StopAutoRun() => Stop();

    public void Stop()
    {
        if (!IsRunning)
        {
            Log("Người dùng bấm Dừng Auto Run nhưng không có lượt Auto Run nào đang chạy.");
            return;
        }
        Log($"Người dùng bấm Dừng Auto Run (token hủy: {(_cts is null ? "không có" : "có")}).");
        CurrentStatus = "Đang dừng Auto Run...";
        try
        {
            _cts?.Cancel();
        }
        catch { }

        var stoppedJobs = _workspace.Update.StopAllSingle();
        Log($"Dừng Auto Run: đã hủy {stoppedJobs} job Update/Import đang chạy.");
        if (_workspace.Scrape.StopCommand.CanExecute(null))
            _workspace.Scrape.StopCommand.Execute(null);
    }

    [RelayCommand]
    public void NextShop()
    {
        if (!IsRunning) return;
        _skipCurrentShop = true;
        var shopName = _currentRunningShop?.Name ?? "hiện tại";
        Log($"⏭ Người dùng bấm 'Next shop' → Đang dừng shop '{shopName}' và chuyển tiếp...");
        if (_currentRunningAccount is { } curAcc)
        {
            _workspace.Update.StopSingle(curAcc.Account.Id);
            _ = _workspace.Scrape.StopSingleAsync(curAcc.ScrapeTarget);
        }
    }

    private async Task MarkShopDoneAndAdvanceAsync(WorkspaceAccountViewModel acc, WorkspaceShopViewModel shop)
    {
        var resolved = SharedWorkbookStore.Resolve(acc.Account, shop.Shop);
        var progressKey = resolved.ProgressKey;
        var scrapeKey = SharedWorkbookCatalog.ScrapeProgressKey(SharedWorkbookStore.IsActive, acc.Account.Id, resolved.ProgressKey);

        if (WorkspaceAppConfig.ShowScrape)
        {
            ScrapeProgressStore.Shared.MarkCompleted(acc.Account.Id, scrapeKey);
        }
        if (WorkspaceAppConfig.ShowImportUpdate)
        {
            var updateResolved = SharedWorkbookStore.ResolveForUpdate(acc.Account, shop.Shop);
            OpProgressStore.Shared.FinishRun(acc.Account.Id, updateResolved.ProgressKey, AssignmentOps.Import, completed: true);
            OpProgressStore.Shared.FinishRun(acc.Account.Id, updateResolved.ProgressKey, AssignmentOps.Update, completed: true);
            if (!string.Equals(progressKey, updateResolved.ProgressKey, StringComparison.OrdinalIgnoreCase))
            {
                OpProgressStore.Shared.FinishRun(acc.Account.Id, progressKey, AssignmentOps.Import, completed: true);
                OpProgressStore.Shared.FinishRun(acc.Account.Id, progressKey, AssignmentOps.Update, completed: true);
            }
        }

        await UiThread.InvokeAsync(() =>
        {
            shop.RefreshStatus();
            shop.RefreshFleet();
        });

        Log($"[{acc.DisplayName}] Đã đánh dấu shop '{shop.Name}' hoàn thành (Next shop).");
        _skipCurrentShop = false;
        await Task.Delay(500);
    }

    /// <summary>Khung xử lý Import + Update của MỘT tài khoản: <paramref name="shopStep"/> (Import 1 shop rồi Update) chạy lần lượt từng shop.
    /// Update luôn chạy sau Import, kết quả Import (đủ, 0 SP, chưa đủ) không ảnh hưởng. Dừng (<paramref name="ct"/>) thì không chạy tiếp;
    /// <paramref name="shopStep"/> trả false = dừng vòng lặp shop.</summary>
    internal static async Task RunAccountShopsAsync<T>(
        IReadOnlyList<T> shops, Func<T, int, Task<bool>> shopStep, CancellationToken ct)
    {
        for (var i = 0; i < shops.Count; i++)
        {
            if (ct.IsCancellationRequested) break;
            if (!await shopStep(shops[i], i)) break;
        }
    }

    // Import RIÊNG shop này (UpdateTarget.SelectedShop đã trỏ vào shop). Trả false = dừng vòng lặp shop.
    // Trạng thái xong/chưa do RunOneWorkflowAsync ghi (completed chỉ khi import đủ mọi item id; lỗi/hủy/thiếu thì stopped) — KHÔNG ghi đè ở đây.
    private async Task<bool> ImportSingleShopAsync(AutoRunAccountItem accItem, WorkspaceShopViewModel shop, int shopIdx, CancellationToken ct)
    {
        var acc = accItem.Account;
        if (_skipCurrentShop) { await MarkShopDoneAndAdvanceAsync(acc, shop); return true; }
        if (shop.ImportDone)
        {
            CurrentStatus = $"[{acc.DisplayName}] ({shopIdx + 1}/{acc.Shops.Count} - {shop.Name}) Import đã xong - Bỏ qua";
            Log($"[{acc.DisplayName}] Shop '{shop.Name}': Import đã hoàn tất trước đó → Bỏ qua Import.");
            return true;
        }

        CurrentStatus = $"[{acc.DisplayName}] ({shopIdx + 1}/{acc.Shops.Count} - {shop.Name}) Đang Import...";
        accItem.Status = $"Import ({shop.Name})";
        Log($"[{acc.DisplayName}] Đang Import thẳng cho shop '{shop.Name}'...");

        var progressKey = SharedWorkbookStore.ResolveForUpdate(acc.Account, shop.Shop).ProgressKey;
        var initialBefore = OpProgressStore.Shared.GetDone(acc.Account.Id, progressKey, AssignmentOps.Import).Count;

        // Runner quét tab «Tất cả» trong 1 lượt nên không import lượt hai.
        try
        {
            await _workspace.Update.RunImportSingleAsync(acc.UpdateTarget, silent: true, importFromClaimedTab: false, externalCt: ct);
        }
        catch (OperationCanceledException) when (_skipCurrentShop) { }

        if (_skipCurrentShop) { await MarkShopDoneAndAdvanceAsync(acc, shop); return true; }
        if (ct.IsCancellationRequested) return false;

        var after = OpProgressStore.Shared.GetDone(acc.Account.Id, progressKey, AssignmentOps.Import).Count;
        var imported = after - initialBefore;   // có thể ÂM nếu tiến độ bị reset giữa lượt

        var importBlocked = _workspace.Update.GetImportBlockedReason(acc.Account.Id);
        if (importBlocked is not null)
            Log($"[{acc.DisplayName}] ⛔ Import bị chặn: {importBlocked}. Shop '{shop.Name}' chưa import được lượt này.");

        if (IsZeroImportRun(imported, after, shop.ImportDone))
        {
            // KHÔNG bỏ qua shop: lô "chưa chắc đã gửi" vẫn có thể đã thành nháp, và Update làm việc trên nháp theo sheet.
            CurrentStatus = importBlocked is not null
                ? $"[{acc.DisplayName}] ({shop.Name}) Import bị chặn - vẫn chạy Update"
                : $"[{acc.DisplayName}] ({shop.Name}) 0 SP import - vẫn chạy Update";
            if (importBlocked is null)
                Log($"[{acc.DisplayName}] Import không được SP nào (0 SP) → vẫn chạy Update cho shop '{shop.Name}'.");
            else
                Log($"[{acc.DisplayName}] Shop '{shop.Name}' (Import bị chặn, chưa có SP nào) → vẫn chạy Update.");
        }

        if (imported > 0 || after > 0)
        {
            if (shop.ImportDone)
                Log($"[{acc.DisplayName}] Import hoàn tất ({Math.Max(0, imported)} SP mới, tổng {after} SP) cho shop '{shop.Name}'.");
            else
                Log($"[{acc.DisplayName}] ⚠ Import chưa đủ ({after} SP) — vẫn Update phần đã có, lần chạy sau import tiếp.");
        }
        await UiThread.InvokeAsync(() => shop.RefreshFleet());
        return true;
    }

    // Một shop của vòng lặp. withImport = false: chỉ Update (Import đã chạy chung ở ImportAccountAsync). withImport = true: flow cũ — Import 1 shop rồi Update.
    // Trả false = dừng vòng lặp shop (đã Dừng).
    private async Task<bool> ShopStepAsync(AutoRunAccountItem accItem, WorkspaceShopViewModel shop, int shopIdx, bool withImport, CancellationToken ct)
    {
        var acc = accItem.Account;
        _currentRunningAccount = acc;
        _currentRunningShop = shop;
        _skipCurrentShop = false;

        await UiThread.InvokeAsync(() =>
        {
            acc.UpdateTarget.SelectedShop = shop.Shop;
            shop.RefreshStatus();
        });

        Log($"[{acc.DisplayName}] Shop '{shop.Name}': Import: {(shop.ImportDone ? "Đã xong" : "Chưa")}, Update: {(shop.UpdateDone ? "Đã xong" : "Chưa")}");

        // Kiểm tra skip cả shop: nếu shop.ImportDone && shop.UpdateDone
        if (shop.ImportDone && shop.UpdateDone)
        {
            CurrentStatus = $"[{acc.DisplayName}] ({shopIdx + 1}/{acc.Shops.Count} - {shop.Name}) Đã hoàn thành (Import & Update) - Bỏ qua";
            Log($"[{acc.DisplayName}] Shop '{shop.Name}' đã hoàn thành cả Import & Update → Bỏ qua.");
            return true;
        }

        if (withImport && !await ImportSingleShopAsync(accItem, shop, shopIdx, ct)) return false;
        if (ct.IsCancellationRequested) return false;

        // c) UPDATE
        if (_skipCurrentShop) { await MarkShopDoneAndAdvanceAsync(acc, shop); return true; }
        if (shop.UpdateDone)
        {
            CurrentStatus = $"[{acc.DisplayName}] ({shopIdx + 1}/{acc.Shops.Count} - {shop.Name}) Update đã xong - Bỏ qua";
            Log($"[{acc.DisplayName}] Shop '{shop.Name}': Update đã hoàn tất trước đó → Bỏ qua Update.");
            return true;
        }

        CurrentStatus = $"[{acc.DisplayName}] ({shopIdx + 1}/{acc.Shops.Count} - {shop.Name}) Đang Update...";
        accItem.Status = $"Update ({shop.Name})";
        Log($"[{acc.DisplayName}] Đang Update cho shop '{shop.Name}'...");

        try
        {
            await _workspace.Update.RunUpdateSingleAsync(acc.UpdateTarget, silent: true, externalCt: ct);
        }
        catch (OperationCanceledException) when (_skipCurrentShop) { }
        catch (Exception ex)
        {
            Log($"[{acc.DisplayName}] ✘ Lỗi Update shop '{shop.Name}': {ex.Message}");
        }

        if (_skipCurrentShop) { await MarkShopDoneAndAdvanceAsync(acc, shop); return true; }
        if (ct.IsCancellationRequested) return false;

        await UiThread.InvokeAsync(() => shop.RefreshFleet());
        if (shop.UpdateDone)
            Log($"[{acc.DisplayName}] Update shop '{shop.Name}' hoàn tất.");
        else
            Log($"[{acc.DisplayName}] ⚠ Shop '{shop.Name}' chưa hoàn thành Update (kiểm tra log chi tiết / ảnh update).");
        return true;
    }

    public Task RunAsync(CancellationToken ct = default)
    {
        var targets = Accounts.Where(a => a.IsSelected).ToList();
        return RunAsync(targets, ct);
    }

    public async Task RunAsync(List<AutoRunAccountItem> targets, CancellationToken ct)
    {
        Action<string> onSubLog = msg => Log(msg);
        _workspace.Scrape.LogAppended += onSubLog;
        _workspace.Update.LogAppended += onSubLog;
        try
        {
            CurrentStatus = "Bắt đầu Auto Run...";
            Log($"=== BẮT ĐẦU AUTO RUN ({targets.Count} tài khoản được chọn) ===");
            Log("Chế độ Import: từng shop Import (1 shop) rồi Update, lần lượt.");

            foreach (var accItem in Accounts)
            {
                if (accItem.IsSelected)
                    accItem.Status = "Chờ chạy";
                else
                    accItem.Status = "Bỏ qua (không chọn)";
            }

            for (int accIdx = 0; accIdx < targets.Count; accIdx++)
            {
                if (ct.IsCancellationRequested) break;

                var accItem = targets[accIdx];
                var acc = accItem.Account;
                accItem.Status = "Đang chạy...";

                Log($"────────────────────────────────────────");
                Log($"[{acc.DisplayName}] Bắt đầu xử lý ({acc.Shops.Count} shop)...");

                // Đặt tài khoản đó làm Workspace.SelectedAccount
                await UiThread.InvokeAsync(() =>
                {
                    _workspace.SelectedAccount = acc;
                });

                if (acc.Shops.Count == 0)
                {
                    accItem.Status = "Không có shop nào";
                    Log($"[{acc.DisplayName}] Không có shop nào → Bỏ qua.");
                    continue;
                }

                // a) BƯỚC 1: SCRAPE:
                if (WorkspaceAppConfig.ShowScrape)
                {
                    bool accScrapeDone = acc.Shops.Any(s => s.ScrapeDone);

                    for (int shopIdx = 0; shopIdx < acc.Shops.Count; shopIdx++)
                    {
                        if (ct.IsCancellationRequested) break;

                        var shop = acc.Shops[shopIdx];
                        _currentRunningAccount = acc;
                        _currentRunningShop = shop;
                        _skipCurrentShop = false;

                        await UiThread.InvokeAsync(() =>
                        {
                            acc.ScrapeTarget.SelectedShop = shop.Shop;
                            shop.RefreshStatus();
                        });

                        Log($"[{acc.DisplayName}] Shop '{shop.Name}': Scrape status: {(shop.ScrapeDone ? "Đã xong" : "Chưa")}");

                        if (_skipCurrentShop) { await MarkShopDoneAndAdvanceAsync(acc, shop); continue; }
                        if (shop.ScrapeDone)
                        {
                            accScrapeDone = true;
                            Log($"[{acc.DisplayName}] Shop '{shop.Name}' đã scrape xong.");
                        }
                        else if (!accScrapeDone)
                        {
                            CurrentStatus = $"[{acc.DisplayName}] ({shopIdx + 1}/{acc.Shops.Count} - {shop.Name}) Đang Scrape...";
                            accItem.Status = $"Scrape ({shop.Name})";
                            Log($"[{acc.DisplayName}] Đang Scrape shop '{shop.Name}'...");

                            try
                            {
                                await _workspace.Scrape.RunSingleAsync(acc.ScrapeTarget, resume: true);
                            }
                            catch (OperationCanceledException) when (_skipCurrentShop)
                            {
                            }
                            catch (Exception ex)
                            {
                                Log($"[{acc.DisplayName}] ✘ Lỗi Scrape shop '{shop.Name}': {ex.Message}");
                            }

                            if (_skipCurrentShop) { await MarkShopDoneAndAdvanceAsync(acc, shop); continue; }

                            await UiThread.InvokeAsync(() => shop.RefreshStatus());
                            if (shop.ScrapeDone)
                            {
                                accScrapeDone = true;
                                Log($"[{acc.DisplayName}] Scrape shop '{shop.Name}' hoàn tất.");
                            }
                            else
                            {
                                Log($"[{acc.DisplayName}] ⚠ Scrape shop '{shop.Name}' chưa hoàn tất.");
                            }
                            if (ct.IsCancellationRequested) break;
                        }

                        if (accScrapeDone)
                            break;
                    }
                }

                if (!WorkspaceAppConfig.ShowImportUpdate)
                {
                    if (ct.IsCancellationRequested)
                    {
                        accItem.Status = "Đã dừng";
                        break;
                    }
                    accItem.Status = "Hoàn thành";
                    Log($"[{acc.DisplayName}] Đã hoàn tất Scrape cho tài khoản.");
                    continue;
                }

                // Từng shop Import (1 shop, quét tab «Tất cả») rồi Update, lần lượt.
                await RunAccountShopsAsync(
                    acc.Shops.ToList(),
                    shopStep: (shop, shopIdx) => ShopStepAsync(accItem, shop, shopIdx, withImport: true, ct),
                    ct);

                if (ct.IsCancellationRequested)
                {
                    accItem.Status = "Đã dừng";
                    break;
                }

                accItem.Status = "Hoàn thành";
                Log($"[{acc.DisplayName}] Đã xử lý xong tài khoản.");
            }

            if (ct.IsCancellationRequested)
            {
                CurrentStatus = "Đã dừng Auto Run.";
                Log($"=== ĐÃ DỪNG AUTO RUN ===");
            }
            else
            {
                CurrentStatus = "Hoàn tất Auto Run tất cả tài khoản.";
                Log($"=== HOÀN TẤT AUTO RUN TOÀN BỘ TÀI KHOẢN ===");
            }
        }
        finally
        {
            _workspace.Scrape.LogAppended -= onSubLog;
            _workspace.Update.LogAppended -= onSubLog;
            _currentRunningAccount = null;
            _currentRunningShop = null;
            _skipCurrentShop = false;
            _lastRunEndedUtc = DateTime.UtcNow;
            IsRunning = false;
        }
    }
}
