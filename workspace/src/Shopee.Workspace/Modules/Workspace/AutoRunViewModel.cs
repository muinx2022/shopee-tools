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
        WorkspaceRole.ImportUpdate => "Chọn các tài khoản BigSeller cần chạy. Mỗi tài khoản sẽ tuần tự thực hiện: Import (tất cả shop) → Update (tất cả shop).",
        _ => "Tự động chạy tuần tự: Scrape (chỉ 1 lần/acc) → Import (thử thường rồi tab Đã nhận) → Update cho danh sách tài khoản đã chọn."
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
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    public void StopAutoRun() => Stop();

    public void Stop()
    {
        if (!IsRunning) return;
        CurrentStatus = "Đang dừng Auto Run...";
        try
        {
            _cts?.Cancel();
        }
        catch { }

        _workspace.Update.StopAllSingle();
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
            OpProgressStore.Shared.FinishRun(acc.Account.Id, progressKey, AssignmentOps.Import, completed: true);
            OpProgressStore.Shared.FinishRun(acc.Account.Id, progressKey, AssignmentOps.Update, completed: true);
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

                for (int shopIdx = 0; shopIdx < acc.Shops.Count; shopIdx++)
                {
                    if (ct.IsCancellationRequested) break;

                    var shop = acc.Shops[shopIdx];
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
                        continue;
                    }

                    var resolved = SharedWorkbookStore.Resolve(acc.Account, shop.Shop);
                    var progressKey = resolved.ProgressKey;

                    // b) BƯỚC 2: IMPORT
                    if (_skipCurrentShop) { await MarkShopDoneAndAdvanceAsync(acc, shop); continue; }
                    if (shop.ImportDone)
                    {
                        CurrentStatus = $"[{acc.DisplayName}] ({shopIdx + 1}/{acc.Shops.Count} - {shop.Name}) Import đã xong - Bỏ qua";
                        Log($"[{acc.DisplayName}] Shop '{shop.Name}': Import đã hoàn tất trước đó → Bỏ qua Import.");
                    }
                    else
                    {
                        CurrentStatus = $"[{acc.DisplayName}] ({shopIdx + 1}/{acc.Shops.Count} - {shop.Name}) Đang Import...";
                        accItem.Status = $"Import ({shop.Name})";
                        Log($"[{acc.DisplayName}] Đang Import thẳng cho shop '{shop.Name}'...");

                        var initialBefore = OpProgressStore.Shared.GetDone(acc.Account.Id, progressKey, AssignmentOps.Import).Count;

                        // Lần 1: Gọi import thẳng (tab Chưa nhận)
                        try
                        {
                            await _workspace.Update.RunImportSingleAsync(acc.UpdateTarget, silent: true, importFromClaimedTab: false);
                        }
                        catch (OperationCanceledException) when (_skipCurrentShop) { }

                        if (_skipCurrentShop) { await MarkShopDoneAndAdvanceAsync(acc, shop); continue; }
                        if (ct.IsCancellationRequested) break;

                        var after = OpProgressStore.Shared.GetDone(acc.Account.Id, progressKey, AssignmentOps.Import).Count;
                        var imported = after - initialBefore;

                        int expectedRows = 0;
                        try
                        {
                            var (_, dEnd) = await Task.Run(() => ScrapeWorkbook.ExcelRowsToDenseRange(
                                resolved.DataPath, resolved.DataSheet,
                                Math.Max(2, acc.ScrapeTarget.StartRow), Math.Max(0, acc.ScrapeTarget.EndRow)), ct);
                            expectedRows = dEnd;
                        }
                        catch { }

                        if (imported == 0 || (shopIdx > 0 && expectedRows > 0 && after < expectedRows))
                        {
                            // Lần 2: Chuyển sang import tab Đã nhận (khi tab Chưa nhận 0 SP hoặc shop 2+ chưa đủ số SP của sheet)
                            if (_skipCurrentShop) { await MarkShopDoneAndAdvanceAsync(acc, shop); continue; }
                            CurrentStatus = $"[{acc.DisplayName}] ({shopIdx + 1}/{acc.Shops.Count} - {shop.Name}) Đang Import (tab Đã nhận)...";
                            Log($"[{acc.DisplayName}] Đang import bổ sung từ tab Đã nhận (hiện có {after}/{(expectedRows > 0 ? expectedRows.ToString() : "?")} SP)...");

                            try
                            {
                                await _workspace.Update.RunImportSingleAsync(acc.UpdateTarget, silent: true, importFromClaimedTab: true);
                            }
                            catch (OperationCanceledException) when (_skipCurrentShop) { }

                            if (_skipCurrentShop) { await MarkShopDoneAndAdvanceAsync(acc, shop); continue; }
                            if (ct.IsCancellationRequested) break;

                            after = OpProgressStore.Shared.GetDone(acc.Account.Id, progressKey, AssignmentOps.Import).Count;
                            imported = after - initialBefore;

                            if (imported == 0 && after == 0 && !shop.ImportDone)
                            {
                                // Bỏ qua shop đó và tiếp tục với shop khác
                                CurrentStatus = $"[{acc.DisplayName}] ({shop.Name}) 0 SP import - Bỏ qua";
                                Log($"[{acc.DisplayName}] Tab Đã nhận cũng 0 SP → Bỏ qua shop '{shop.Name}'.");
                                continue;
                            }
                        }

                        // Nếu có import (> 0) hoặc đã có sẵn SP từ trước
                        if (imported > 0 || after > 0)
                        {
                            Log($"[{acc.DisplayName}] Import hoàn tất ({imported} SP mới, tổng {after} SP) cho shop '{shop.Name}'.");
                            OpProgressStore.Shared.FinishRun(acc.Account.Id, progressKey, AssignmentOps.Import, completed: true);
                        }
                        await UiThread.InvokeAsync(() => shop.RefreshFleet());
                    }

                    if (ct.IsCancellationRequested) break;

                    // c) BƯỚC 3: UPDATE
                    if (_skipCurrentShop) { await MarkShopDoneAndAdvanceAsync(acc, shop); continue; }
                    if (shop.UpdateDone)
                    {
                        CurrentStatus = $"[{acc.DisplayName}] ({shopIdx + 1}/{acc.Shops.Count} - {shop.Name}) Update đã xong - Bỏ qua";
                        Log($"[{acc.DisplayName}] Shop '{shop.Name}': Update đã hoàn tất trước đó → Bỏ qua Update.");
                    }
                    else
                    {
                        CurrentStatus = $"[{acc.DisplayName}] ({shopIdx + 1}/{acc.Shops.Count} - {shop.Name}) Đang Update...";
                        accItem.Status = $"Update ({shop.Name})";
                        Log($"[{acc.DisplayName}] Đang Update cho shop '{shop.Name}'...");

                        try
                        {
                            await _workspace.Update.RunUpdateSingleAsync(acc.UpdateTarget, silent: true);
                        }
                        catch (OperationCanceledException) when (_skipCurrentShop) { }
                        catch (Exception ex)
                        {
                            Log($"[{acc.DisplayName}] ✘ Lỗi Update shop '{shop.Name}': {ex.Message}");
                        }

                        if (_skipCurrentShop) { await MarkShopDoneAndAdvanceAsync(acc, shop); continue; }
                        if (ct.IsCancellationRequested) break;

                        await UiThread.InvokeAsync(() => shop.RefreshFleet());
                        if (shop.UpdateDone)
                        {
                            Log($"[{acc.DisplayName}] Update shop '{shop.Name}' hoàn tất.");
                        }
                        else
                        {
                            Log($"[{acc.DisplayName}] ⚠ Shop '{shop.Name}' chưa hoàn thành Update (kiểm tra log chi tiết / ảnh update).");
                        }
                    }
                }

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
            IsRunning = false;
        }
    }
}
