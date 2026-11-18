using Shopee.Core.BigSeller;
using Shopee.Core.Coordination;
using Shopee.Core.Scrape;
using Shopee.Core.Progress;
using System.Windows.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shopee.Core.Accounts;
using Shopee.Core.Infrastructure;
using Shopee.Core.Proxy;
using Shopee.Workspace.Infrastructure;
using Shopee.Workspace.Modules.Accounts;
using Shopee.Workspace.Modules.BigSeller;
using Shopee.Workspace.Modules.Harvest;
using Shopee.Workspace.Modules.Scrape;
using Shopee.Workspace.Modules.Settings;
using Shopee.Workspace.Modules.UpdateProduct;
using Shopee.Workspace.Modules.Workspace;
using Shopee.Workspace.Services;

namespace Shopee.Workspace.ViewModels;

/// <summary>
/// ViewModel gốc của shell ShopeeWorkspace (app cục bộ, không có Hub/Orders/Fleet).
/// 4 tab: Workspace | Cấu hình BigSeller | Tài khoản &amp; Proxy | Cài đặt.
/// App Harvest (<see cref="BuildHarvestShell"/>): 3 tab Lấy dữ liệu | Tài khoản &amp; Proxy | Cài đặt — không có gì của BigSeller.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    // null ở app Harvest (không dựng màn Workspace / Cấu hình BigSeller / Update).
    private readonly WorkspaceViewModel? _workspace;
    private readonly ScrapeViewModel _scrape;
    private readonly UpdateProductViewModel? _update;
    private readonly AccountsViewModel _accounts;
    private readonly BigSellerViewModel? _bigSeller;
    private readonly HarvestViewModel? _harvest;

    public string WindowTitle => WorkspaceAppConfig.WindowTitle;
    public string WindowIcon => WorkspaceAppConfig.WindowIcon;
    public WorkspaceViewModel Workspace => _workspace ?? throw new InvalidOperationException("App Harvest không có màn Workspace.");
    public BigSellerViewModel BigSeller => _bigSeller ?? throw new InvalidOperationException("App Harvest không có màn BigSeller.");
    /// <summary>Màn chính của app Harvest (null ở app khác).</summary>
    public HarvestViewModel? Harvest => _harvest;
    /// <summary>Nhóm Kết nối Hub trong màn Cài đặt của app Harvest (null ở app khác).</summary>
    public HarvestHubViewModel? HarvestHub { get; private set; }
    /// <summary>Màn Cài đặt riêng của app Harvest (null ở app khác).</summary>
    public HarvestSettingsViewModel? HarvestSettings { get; private set; }

    public ObservableCollection<RibbonTab> Tabs { get; } = new();

    public string TabShortcutHint => Tabs.Count <= 1
        ? "Ctrl + 1 để chuyển tab"
        : $"Ctrl + 1–{Tabs.Count} để chuyển tab";

    [ObservableProperty] private RibbonTab? _selectedTab;

    private readonly Dictionary<RibbonTab, object> _screenByTab = new();

    private RibbonTab? _workspaceTab;
    private RibbonScreenItem? _workspaceHomeScreen;
    private RibbonScreenItem? _autoRunScreen;

    public object? CurrentScreen =>
        SelectedTab is not null && _screenByTab.TryGetValue(SelectedTab, out var vm) ? vm : null;

    partial void OnSelectedTabChanged(RibbonTab? value)
    {
        OnPropertyChanged(nameof(CurrentScreen));
        WorkspaceStatus?.Refresh();
    }

    public bool ShowShopeeStatus => false;
    public bool ShowWorkspaceStatus => true;
    /// <summary>Cụm "N tài khoản BigSeller · M shop" trên thanh trạng thái — app Harvest không có khái niệm này.</summary>
    public bool ShowBigSellerStatus => !WorkspaceAppConfig.IsHarvest;
    public bool ShowScrape => WorkspaceAppConfig.ShowScrape;
    public bool ShowStatusSeparator => false;
    public bool ShowOrdersBrowserStatus => false;
    public object? ShopeeStatusVm => null;

    public WorkspaceStatusViewModel? WorkspaceStatus { get; }

    public string AppVersionText => $"v{AppInfo.Version}";

    [ObservableProperty] private string _jobStatusText = "Rảnh — không có job";

    public string ZoomText => $"Thu phóng {UiZoom.PercentText}";
    public bool ShowZoom => Math.Abs(UiZoom.Current - 1.0) > 1e-6;

    public ShellViewModel()
    {
        if (WorkspaceAppConfig.IsHarvest)
        {
            _scrape = new ScrapeViewModel();
            _accounts = new AccountsViewModel();
            WorkspaceStatus = new WorkspaceStatusViewModel();
            _harvest = new HarvestViewModel(_scrape);
            BuildHarvestShell(_harvest);
            return;
        }

        var bigSeller = new BigSellerViewModel();
        _bigSeller = bigSeller;
        _scrape = new ScrapeViewModel();
        _update = new UpdateProductViewModel();
        bigSeller.JobsAreBusy = () => _scrape.IsBusy || _update.IsRunning || _update.HasActiveWsJob;
        _workspace = new WorkspaceViewModel(bigSeller, _scrape, _update);
        var workspace = _workspace;
        _accounts = new AccountsViewModel();
        var accounts = _accounts;
        var localSettings = new LocalSettingsViewModel();

        WorkspaceStatus = new WorkspaceStatusViewModel();

        // ── Tab 1: Workspace ──
        var wsWorkspace = new RibbonScreenItem("Workspace", AppIcons.Dashboard, workspace,
            toolTip: "BigSeller Workspace — Scrape · Import · Update theo từng shop");
        var wsScreens = new List<object> { wsWorkspace };
        _workspaceHomeScreen = wsWorkspace;

        if (!WorkspaceAppConfig.IsScrapeOnly)
        {
            var wsAutoRun = new RibbonScreenItem("Auto Run", AppIcons.PlayCircle, workspace.AutoRun,
                toolTip: "Tự động chạy Scrape · Import · Update tuần tự các tài khoản");
            wsScreens.Add(wsAutoRun);
            _autoRunScreen = wsAutoRun;
        }

        if (WorkspaceAppConfig.ShowScrape)
        {
            var wsScrapeMulti = new RibbonScreenItem("Scrape Multi", AppIcons.CloudDownload, workspace.ScrapeMulti,
                toolTip: "Cào 1 link Shopee đồng thời cho nhiều tài khoản BigSeller");
            wsScreens.Add(wsScrapeMulti);

            var wsAccounts = new RibbonScreenItem("Tài khoản & Proxy", AppIcons.People, accounts,
                toolTip: "Kho tài khoản Shopee dùng chung · Check tài khoản");
            wsScreens.Add(wsAccounts);
        }

        var wsActions = new List<object>
        {
            new RibbonActionItem("Dừng jobs", "IconStop", StopWorkspaceJobsCommand,
                "Dừng các việc đang chạy (Scrape · Update)"),
        };
        if (WorkspaceAppConfig.ShowScrape)
        {
            wsActions.Add(new RibbonActionItem("Đẩy video", "IconSync", PushVideosToHubCommand,
                "Đẩy video trong thư mục video (30 ngày qua) lên hub: chỉ file hub thiếu/khác, hoặc đẩy lại toàn bộ"));
        }

        var workspaceTab = new RibbonTab("Workspace", new List<RibbonGroup>
        {
            new RibbonGroup("Màn hình", wsScreens),
            new RibbonGroup("Hành động", wsActions),
        });
        _workspaceTab = workspaceTab;

        // ── Tab 2: Cấu hình BigSeller ──
        var bsScreen = new RibbonScreenItem("Cấu hình BigSeller", AppIcons.Database, bigSeller,
            toolTip: "Tài khoản · workbook · cookie · shop · proxy");
        var bigSellerTab = new RibbonTab("Cấu hình BigSeller", new List<RibbonGroup>
        {
            new RibbonGroup("Màn hình", new object[] { bsScreen }),
            new RibbonGroup("Hành động", new object[]
            {
                new RibbonActionItem("Đăng nhập tất cả", "IconLogin", bigSeller.LoginAllCommand,
                    "Tự đăng nhập headless mỗi tài khoản để lấy cookie"),
                new RibbonActionItem("Dừng", "IconStop", bigSeller.StopLoginAllCommand,
                    "Dừng tiến trình đăng nhập tất cả"),
                new RibbonActionItem("Workbook local", "IconSave", bigSeller.UseLocalWorkbookCommand,
                    "Đổi tài khoản BigSeller đang chọn từ kho Hub sang workbook Excel local"),
            }),
        });
        _bigSellerTab = bigSellerTab;

        // Workspace bấm "Đăng nhập / cấu hình" → nhảy sang tab "Cấu hình BigSeller".
        workspace.RequestNavigate = _ => { SelectedTab = _bigSellerTab; };

        // ── Tab 3: Tài khoản & Proxy (màn hình độc lập) ──
        if (WorkspaceAppConfig.ShowScrape)
        {
            var accScreen = new RibbonScreenItem("Tài khoản & Proxy", AppIcons.People, accounts,
                toolTip: "Kho tài khoản Shopee · proxy · trạng thái");
            var accountsTab = new RibbonTab("Tài khoản & Proxy", new List<RibbonGroup>
            {
                new RibbonGroup("Màn hình", new object[] { accScreen }),
            });
            _accountsTab = accountsTab;
        }

        // ── Tab 4: Cài đặt ──
        var setScreen = new RibbonScreenItem("Cài đặt", AppIcons.Settings, localSettings,
            toolTip: "Hiệu năng · AI (OpenAI key) · Kho tài khoản");
        var settingsTab = new RibbonTab("Cài đặt", new List<RibbonGroup>
        {
            new RibbonGroup("Màn hình", new object[] { setScreen }),
        });

        Tabs.Add(workspaceTab);
        Tabs.Add(bigSellerTab);
        if (_accountsTab is not null)
            Tabs.Add(_accountsTab);
        Tabs.Add(settingsTab);

        WireScreens();

        SelectedTab = workspaceTab;

        // Job status
        _scrape.PropertyChanged += OnJobStateChanged;
        _update.PropertyChanged += OnJobStateChanged;
        _workspace.AutoRun.PropertyChanged += OnJobStateChanged;
        _workspace.ScrapeMulti.PropertyChanged += OnJobStateChanged;
        _update.JobsChanged += () => UiThread.Post(RefreshJobStatus);
        RefreshJobStatus();

        WireZoom();
    }

    /// <summary>App Harvest: 3 tab — Lấy dữ liệu (chọn file Excel, sheet, chạy) | Tài khoản &amp; Proxy (kho tk Shopee mở trang) |
    /// Cài đặt. Không dựng màn Workspace / Cấu hình BigSeller / Update.</summary>
    private void BuildHarvestShell(HarvestViewModel harvest)
    {
        var harvestScreen = new RibbonScreenItem("Lấy dữ liệu", AppIcons.CloudDownload, harvest,
            toolTip: "Chọn file Excel danh sách link · lấy dữ liệu SP Shopee · lưu lên Hub");
        var harvestTab = new RibbonTab("Lấy dữ liệu", new List<RibbonGroup>
        {
            new RibbonGroup("Màn hình", new object[] { harvestScreen }),
            new RibbonGroup("Hành động", new object[]
            {
                new RibbonActionItem("Dừng jobs", "IconStop", StopWorkspaceJobsCommand, "Dừng lượt lấy dữ liệu đang chạy"),
            }),
        });
        _workspaceTab = harvestTab;
        _workspaceHomeScreen = harvestScreen;

        var accScreen = new RibbonScreenItem("Tài khoản & Proxy", AppIcons.People, _accounts,
            toolTip: "Kho tài khoản Shopee · proxy · trạng thái");
        _accountsTab = new RibbonTab("Tài khoản & Proxy", new List<RibbonGroup>
        {
            new RibbonGroup("Màn hình", new object[] { accScreen }),
        });

        // MỘT màn Cài đặt riêng của Harvest: Kết nối Hub (đầu) · Hiệu năng · Thu phóng · Kho tài khoản & Proxy · Extension — không AI.
        // App Scrape / Update vẫn dùng màn chung LocalSettingsViewModel (có AI).
        HarvestSettings = new HarvestSettingsViewModel();
        HarvestHub = HarvestSettings.Hub;
        var setScreen = new RibbonScreenItem("Cài đặt", AppIcons.Settings, HarvestSettings,
            toolTip: "Kết nối Hub · Hiệu năng · Thu phóng · Kho tài khoản & Proxy · Extension");
        var settingsTab = new RibbonTab("Cài đặt", new List<RibbonGroup>
        {
            new RibbonGroup("Màn hình", new object[] { setScreen }),
        });

        Tabs.Add(harvestTab);
        Tabs.Add(_accountsTab);
        Tabs.Add(settingsTab);
        WireScreens();
        SelectedTab = harvestTab;

        // Màn Lấy dữ liệu báo trạng thái Hub + nút đi tới Kết nối Hub; lưu Hub xong thì cập nhật ngay.
        harvest.OpenHubSettings = () =>
        {
            SelectedTab = settingsTab;
            ActivateScreen(setScreen);   // nhóm Kết nối Hub ở đầu màn Cài đặt
        };
        HarvestHub.Saved += harvest.OnHubSaved;

        _scrape.PropertyChanged += OnJobStateChanged;
        RefreshJobStatus();
        WireZoom();
    }

    private void WireScreens()
    {
        // Gán lệnh điều hướng cho mọi RibbonScreenItem
        foreach (var tab in Tabs)
            foreach (var scr in tab.Groups.SelectMany(g => g.Items).OfType<RibbonScreenItem>())
            {
                scr.OwnerTab = tab;
                var s = scr;
                s.ActivateCommand = new RelayCommand(() => ActivateScreen(s));
            }

        // Mặc định: mỗi tab mở màn đầu tiên
        foreach (var tab in Tabs)
        {
            var first = tab.Groups.SelectMany(g => g.Items).OfType<RibbonScreenItem>().FirstOrDefault();
            if (first is not null) { _screenByTab[tab] = first.ScreenVm; first.IsActive = true; }
        }
    }

    private void WireZoom()
    {
        // Zoom text
        UiZoom.Changed += () =>
        {
            OnPropertyChanged(nameof(ZoomText));
            OnPropertyChanged(nameof(ShowZoom));
        };
    }

    private RibbonTab? _bigSellerTab;
    private RibbonTab? _accountsTab;

    private void ActivateScreen(RibbonScreenItem item)
    {
        var tab = item.OwnerTab;
        if (tab is null) return;
        _screenByTab[tab] = item.ScreenVm;
        foreach (var s in tab.Groups.SelectMany(g => g.Items).OfType<RibbonScreenItem>())
            s.IsActive = ReferenceEquals(s, item);
        if (ReferenceEquals(tab, SelectedTab)) OnPropertyChanged(nameof(CurrentScreen));

        if (item.ScreenVm is AutoRunViewModel autoRun)
        {
            autoRun.RefreshAccounts();
        }
        else if (item.ScreenVm is ScrapeMultiViewModel scrapeMulti)
        {
            scrapeMulti.RefreshAccounts();
        }
    }

    private void OnJobStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScrapeViewModel.IsBusy)
            or nameof(UpdateProductViewModel.IsRunning)
            or nameof(AutoRunViewModel.IsRunning))
            UiThread.Post(RefreshJobStatus);
    }

    private void RefreshJobStatus()
    {
        var n = 0;
        if (_scrape.IsBusy) n++;
        if (_update is null || _workspace is null) { SetJobStatus(n); return; }   // app Harvest: chỉ có lượt lấy dữ liệu
        if (_update.IsRunning) n++;
        if (_workspace.AutoRun.IsRunning) n++;
        if (_workspace.ScrapeMulti.IsRunning) n++;
        if (_isPushingVideos) n++;
        foreach (var acct in Shopee.Core.BigSeller.BigSellerStore.Shared.Accounts)
            if (_update.IsUpdateRunning(acct.Id)) n++;
        SetJobStatus(n);
    }

    private void SetJobStatus(int n)
    {
        JobStatusText = n > 0 ? $"Đang chạy — {n} job" : "Rảnh — không có job";
    }

    [RelayCommand]
    private void SelectTabAt(string? index)
    {
        if (!int.TryParse(index, out var i) || i < 0 || i >= Tabs.Count) return;
        SelectedTab = Tabs[i];
    }

    [RelayCommand]
    private void ShowAutoRun()
    {
        if (_workspaceTab is null || _autoRunScreen is null) return;
        SelectedTab = _workspaceTab;
        ActivateScreen(_autoRunScreen);
    }

    [RelayCommand]
    private void StopWorkspaceJobs()
    {
        _workspace?.AutoRun.Stop();
        if (WorkspaceAppConfig.ShowImportUpdate && _update is not null && (_update.IsRunning || _update.HasActiveWsJob))
            _update.StopAllSingle();
        if (WorkspaceAppConfig.ShowScrape && _scrape.IsBusy && _scrape.StopCommand.CanExecute(null))
            _scrape.StopCommand.Execute(null);
        _videoPushCts?.Cancel();
    }

    private CancellationTokenSource? _videoPushCts;
    private bool _isPushingVideos;

    /// <summary>
    /// Đẩy video trong thư mục video (30 ngày qua) lên hub. Không chạy khi đang scrape. Quét trước để biết X và Y,
    /// rồi hỏi chế độ qua <see cref="PushVideosDialog"/>. Một lô một lần bấm (AsyncRelayCommand tự chặn bấm chồng).
    /// </summary>
    [RelayCommand]
    private async Task PushVideosToHubAsync()
    {
        if (_workspace is null) return;   // app Harvest không có video (lệnh không có trên ribbon)
        if (CoordinationRuntime.Client is null)
        {
            await Dialogs.InfoAsync("Chưa nối hub, không đẩy được video.");
            return;
        }
        if (_scrape.IsBusy || _workspace.ScrapeMulti.IsRunning || _workspace.AutoRun.IsRunning)
        {
            await Dialogs.InfoAsync("Đang scrape. Đợi scrape xong rồi bấm lại.");
            return;
        }
        var folder = _workspace.VideoDir;
        if (string.IsNullOrWhiteSpace(folder) || !await Task.Run(() => Directory.Exists(folder)))
        {
            await Dialogs.InfoAsync("Thư mục video trống hoặc không tồn tại. Chọn thư mục video trước khi đẩy.");
            return;
        }

        var cts = new CancellationTokenSource();
        _videoPushCts = cts;
        _isPushingVideos = true;
        UiThread.Post(RefreshJobStatus);
        try
        {
            await RunVideoPushAsync(folder, cts.Token);
        }
        finally
        {
            _isPushingVideos = false;
            _videoPushCts = null;
            cts.Dispose();
            UiThread.Post(RefreshJobStatus);
        }
    }

    private async Task RunVideoPushAsync(string folder, CancellationToken ct)
    {
        var log = Workspace.ScrapeMulti;

        (IReadOnlyList<LocalVideo> All, IReadOnlyList<LocalVideo> Missing) scan;
        try
        {
            scan = await HubVideoBulkPush.ScanAsync(folder, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            log.Log("Đã dừng đẩy video khi đang quét thư mục");
            return;
        }
        catch (HubVideoScanException ex)
        {
            await Dialogs.InfoAsync(ex.Message);
            return;
        }

        if (scan.All.Count == 0)
        {
            await Dialogs.InfoAsync("Thư mục không có video nào trong 30 ngày qua.");
            return;
        }

        var dialog = new PushVideosDialog(scan.All.Count, scan.Missing.Count);
        await WindowHost.ShowDialogAsync(dialog);
        await dialog.WaitClosedAsync();
        var items = dialog.Choice switch
        {
            PushVideosChoice.Missing => scan.Missing,
            PushVideosChoice.All => scan.All,
            _ => null,
        };
        if (items is null) return;

        var total = items.Count;
        var ok = 0;
        var fail = 0;
        log.Log($"Bắt đầu đẩy {total} video lên hub");
        try
        {
            await HubVideoBulkPush.PushToHubAsync(items, (video, success, reason) =>
            {
                if (success)
                {
                    ok++;
                    return;
                }
                fail++;
                var file = Path.GetFileName(video.Path);
                log.Log(reason is null
                    ? $"Không đẩy được {file} lên hub"
                    : $"Không đẩy được {file} lên hub ({reason})");
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            log.Log($"Đã dừng đẩy video: {ok}/{total} xong");
            return;
        }

        var summary = $"Đã đẩy {ok}/{total} video lên hub, lỗi {fail}";
        log.Log(summary);
        await Dialogs.InfoAsync(summary);
    }

    /// <summary>Cửa sổ được kích hoạt lại: nạp DB chung, trừ khi đang có job.</summary>
    public void OnWindowActivated()
    {
        if (_workspace is null || _update is null)
        {
            // App Harvest: chỉ nạp lại kho tk Shopee (app khác có thể vừa sửa) khi không chạy.
            _accounts.ReloadFromDbIfIdle(_scrape.IsBusy);
            return;
        }
        BigSellerStore.Shared.ReloadIfExternalChange();
        ScrapeProgressStore.Shared.ReloadIfExternalChange();
        OpProgressStore.Shared.ReloadIfExternalChange();
        Workspace.RefreshAccountCookies();
        Workspace.RefreshSharedWorkbookIndicator();
        BigSeller.RefreshSharedUi();
        foreach (var item in BigSeller.Items)
            item.NotifyCookieChanged();

        if (WorkspaceAppConfig.ShowScrape)
        {
            var busy = _scrape.IsBusy || _update.IsRunning || _update.HasActiveWsJob;
            _accounts.ReloadFromDbIfIdle(busy);
        }
    }

    [RelayCommand]
    private void GoHome()
    {
        if (_workspaceTab is null) return;
        SelectedTab = _workspaceTab;
        if (_workspaceHomeScreen is not null) ActivateScreen(_workspaceHomeScreen);
    }
}
