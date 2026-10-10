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
using Shopee.Workspace.Modules.Scrape;
using Shopee.Workspace.Modules.Settings;
using Shopee.Workspace.Modules.UpdateProduct;
using Shopee.Workspace.Modules.Workspace;
using Shopee.Workspace.Services;

namespace Shopee.Workspace.ViewModels;

/// <summary>
/// ViewModel gốc của shell ShopeeWorkspace (app cục bộ, không có Hub/Orders/Fleet).
/// 4 tab: Workspace | Cấu hình BigSeller | Tài khoản &amp; Proxy | Cài đặt.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly ScrapeViewModel _scrape;
    private readonly UpdateProductViewModel _update;
    private readonly AccountsViewModel _accounts;

    public ObservableCollection<RibbonTab> Tabs { get; } = new();

    public string TabShortcutHint => Tabs.Count <= 1
        ? "Ctrl + 1 để chuyển tab"
        : $"Ctrl + 1–{Tabs.Count} để chuyển tab";

    [ObservableProperty] private RibbonTab? _selectedTab;

    private readonly Dictionary<RibbonTab, object> _screenByTab = new();

    private RibbonTab? _workspaceTab;
    private RibbonScreenItem? _workspaceHomeScreen;

    public object? CurrentScreen =>
        SelectedTab is not null && _screenByTab.TryGetValue(SelectedTab, out var vm) ? vm : null;

    partial void OnSelectedTabChanged(RibbonTab? value)
    {
        OnPropertyChanged(nameof(CurrentScreen));
        WorkspaceStatus?.Refresh();
    }

    public bool ShowShopeeStatus => false;
    public bool ShowWorkspaceStatus => true;
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
        var bigSeller = new BigSellerViewModel();
        _scrape = new ScrapeViewModel();
        _update = new UpdateProductViewModel();
        bigSeller.JobsAreBusy = () => _scrape.IsBusy || _update.IsRunning || _update.HasActiveWsJob;
        var workspace = new WorkspaceViewModel(bigSeller, _scrape, _update);
        _accounts = new AccountsViewModel();
        var accounts = _accounts;
        var localSettings = new LocalSettingsViewModel();

        WorkspaceStatus = new WorkspaceStatusViewModel();

        // ── Tab 1: Workspace ──
        var wsWorkspace = new RibbonScreenItem("Workspace", AppIcons.Dashboard, workspace,
            toolTip: "BigSeller Workspace — Scrape · Import · Update theo từng shop");
        var wsAccounts = new RibbonScreenItem("Tài khoản & Proxy", AppIcons.People, accounts,
            toolTip: "Kho tài khoản Shopee dùng chung · Check tài khoản");
        _workspaceHomeScreen = wsWorkspace;

        var workspaceTab = new RibbonTab("Workspace", new List<RibbonGroup>
        {
            new RibbonGroup("Màn hình", new object[] { wsWorkspace, wsAccounts }),
            new RibbonGroup("Hành động", new object[]
            {
                new RibbonActionItem("Dừng jobs", "IconStop", StopWorkspaceJobsCommand,
                    "Dừng các việc đang chạy (Scrape · Update)"),
            }),
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
                new RibbonActionItem("Dừng", "IconStop", bigSeller.StopLoginCommand,
                    "Dừng tiến trình đăng nhập tất cả"),
                new RibbonActionItem("Workbook local", "IconSave", bigSeller.UseLocalWorkbookCommand,
                    "Đổi tài khoản BigSeller đang chọn từ kho Hub sang workbook Excel local"),
            }),
        });
        _bigSellerTab = bigSellerTab;

        // Workspace bấm "Đăng nhập / cấu hình" → nhảy sang tab "Cấu hình BigSeller".
        workspace.RequestNavigate = _ => { SelectedTab = _bigSellerTab; };

        // ── Tab 3: Tài khoản & Proxy (màn hình độc lập) ──
        var accScreen = new RibbonScreenItem("Tài khoản & Proxy", AppIcons.People, accounts,
            toolTip: "Kho tài khoản Shopee · proxy · trạng thái");
        var accountsTab = new RibbonTab("Tài khoản & Proxy", new List<RibbonGroup>
        {
            new RibbonGroup("Màn hình", new object[] { accScreen }),
        });
        _accountsTab = accountsTab;

        // ── Tab 4: Cài đặt ──
        var setScreen = new RibbonScreenItem("Cài đặt", AppIcons.Settings, localSettings,
            toolTip: "Hiệu năng · AI (OpenAI key) · Kho tài khoản");
        var settingsTab = new RibbonTab("Cài đặt", new List<RibbonGroup>
        {
            new RibbonGroup("Màn hình", new object[] { setScreen }),
        });

        Tabs.Add(workspaceTab);
        Tabs.Add(bigSellerTab);
        Tabs.Add(accountsTab);
        Tabs.Add(settingsTab);

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

        SelectedTab = workspaceTab;

        // Job status
        _scrape.PropertyChanged += OnJobStateChanged;
        _update.PropertyChanged += OnJobStateChanged;
        _update.JobsChanged += () => UiThread.Post(RefreshJobStatus);
        RefreshJobStatus();

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
    }

    private void OnJobStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScrapeViewModel.IsBusy) or nameof(UpdateProductViewModel.IsRunning))
            UiThread.Post(RefreshJobStatus);
    }

    private void RefreshJobStatus()
    {
        var n = 0;
        if (_scrape.IsBusy) n++;
        if (_update.IsRunning) n++;
        foreach (var acct in Shopee.Core.BigSeller.BigSellerStore.Shared.Accounts)
            if (_update.IsUpdateRunning(acct.Id)) n++;
        JobStatusText = n > 0 ? $"Đang chạy — {n} job" : "Rảnh — không có job";
    }

    [RelayCommand]
    private void SelectTabAt(string? index)
    {
        if (!int.TryParse(index, out var i) || i < 0 || i >= Tabs.Count) return;
        SelectedTab = Tabs[i];
    }

    [RelayCommand]
    private void StopWorkspaceJobs()
    {
        _update.StopAllSingle();
        if (_scrape.StopCommand.CanExecute(null)) _scrape.StopCommand.Execute(null);
    }

    /// <summary>Cửa sổ được kích hoạt lại: nạp DB chung, trừ khi đang có job.</summary>
    public void OnWindowActivated()
    {
        var busy = _scrape.IsBusy || _update.IsRunning || _update.HasActiveWsJob;
        _accounts.ReloadFromDbIfIdle(busy);
    }

    [RelayCommand]
    private void GoHome()
    {
        if (_workspaceTab is null) return;
        SelectedTab = _workspaceTab;
        if (_workspaceHomeScreen is not null) ActivateScreen(_workspaceHomeScreen);
    }
}
