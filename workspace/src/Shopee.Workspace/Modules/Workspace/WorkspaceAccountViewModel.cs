using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Shopee.Core.BigSeller;
using Shopee.Workspace.Infrastructure;
using Shopee.Workspace.Modules.BigSeller;
using Shopee.Workspace.Modules.Scrape;
using Shopee.Workspace.Modules.UpdateProduct;

namespace Shopee.Workspace.Modules.Workspace;

/// <summary>
/// Gộp 3 "đích" của CÙNG 1 tk BigSeller về một chỗ: cấu hình (BigSeller) + scrape + update. Màn gộp v1.1
/// xoay quanh đối tượng này — bên trái là danh sách các tk, bên phải là chi tiết + lưới shop. KHÔNG sở
/// hữu engine: chỉ bọc 3 ViewModel cũ (dùng chung với 3 màn cũ) nên trạng thái luôn đồng bộ.
/// </summary>
public sealed partial class WorkspaceAccountViewModel : ObservableObject
{
    public BigSellerAccount Account { get; }
    public BigSellerAccountItemViewModel Bs { get; }
    public ScrapeTargetViewModel ScrapeTarget { get; }
    public UpdateRunTargetViewModel UpdateTarget { get; }
    public UpdateProductViewModel UpdateVm { get; }

    /// <summary>Log scrape RIÊNG của tk này (buffer + file riêng, bền qua rebuild VM) — tab "Theo dõi Scrape" bind vào.</summary>
    public LogBuffer ScrapeLog { get; }
    /// <summary>Log import/update/tên SP RIÊNG của tk này — tab "Theo dõi Update" bind vào.</summary>
    public LogBuffer UpdateLog { get; }

    public ObservableCollection<WorkspaceShopViewModel> Shops { get; } = [];

    public WorkspaceAccountViewModel(
        BigSellerAccountItemViewModel bs, ScrapeTargetViewModel scrape, UpdateRunTargetViewModel update,
        UpdateProductViewModel updateVm, LogBuffer scrapeLog, LogBuffer updateLog)
    {
        Bs = bs;
        ScrapeTarget = scrape;
        UpdateTarget = update;
        UpdateVm = updateVm;
        ScrapeLog = scrapeLog;
        UpdateLog = updateLog;
        Account = bs.Model;

        foreach (var s in Account.Shops) Shops.Add(new WorkspaceShopViewModel(this, s));

        // Scrape bắn ShopStatuses → refresh chip scrape; Update bắn JobsChanged (start/finish) → đổi nút
        // Import/Update/Tên SP ⇄ Dừng + khoá các shop CÙNG tk (1 tk chỉ 1 workflow update tại 1 thời điểm).
        ScrapeTarget.PropertyChanged += OnScrapeChanged;
        UpdateVm.JobsChanged += OnUpdateJobsChanged;
        SharedWorkbookStore.Changed += OnSharedWorkbookChanged;
        ShopLiveLine.Changed += OnLiveLine;
    }

    private void OnSharedWorkbookChanged() => Services.UiThread.Post(() =>
    {
        OnPropertyChanged(nameof(WorkbookPath));
        OnPropertyChanged(nameof(WorkbookHint));
        foreach (var s in Shops) s.RefreshStatus();
    });

    private void OnFleetChanged() => Services.UiThread.Post(RefreshFleetAll);

    private void OnLiveLine()
    {
        foreach (var s in Shops) s.NotifyLiveLine();
    }

    private void RefreshFleetAll() { foreach (var s in Shops) s.RefreshFleet(); }

    private void OnUpdateJobsChanged()
    {
        foreach (var s in Shops) s.RefreshUpdateState();
        // JobsChanged bắn trên UI thread (RaiseJobsChanged dùng UiThread.Post) → raise thẳng được.
        OnPropertyChanged(nameof(HasRunningWork));
        SyncActiveLog();
    }

    private void OnScrapeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScrapeTargetViewModel.ShopStatuses)
            or nameof(ScrapeTargetViewModel.SelectedShop)
            or nameof(ScrapeTargetViewModel.ProgressText))
        {
            OnPropertyChanged(nameof(IsScraping));
            OnPropertyChanged(nameof(HasRunningWork));
            foreach (var s in Shops) s.RefreshStatus();
            OnPropertyChanged(nameof(ScrapeSummary));
            SyncActiveLog();
        }
    }

    // Việc vừa bắt đầu thắng. Khi chỉ còn một phía chạy thì hiện đúng phía đó. Cả hai dừng thì giữ
    // log vừa xem, để đọc nốt kết quả.
    private bool _wasScraping;
    private bool _wasUpdating;
    private bool _showUpdate;

    private void SyncActiveLog()
    {
        var scraping = IsScraping;
        var updating = UpdateVm.IsUpdateRunning(Account.Id);
        if (scraping && !_wasScraping) _showUpdate = false;
        if (updating && !_wasUpdating) _showUpdate = true;
        if (scraping && !updating) _showUpdate = false;
        else if (updating && !scraping) _showUpdate = true;
        _wasScraping = scraping;
        _wasUpdating = updating;
        OnPropertyChanged(nameof(ActiveLog));
        OnPropertyChanged(nameof(ActiveLogCaption));
    }

    /// <summary>Log đang hiện: scrape khi đang cào, update/import/tên SP khi đang chạy các việc đó.</summary>
    public LogBuffer ActiveLog => _showUpdate ? UpdateLog : ScrapeLog;

    /// <summary>Nhãn cửa sổ log, không gồm tên tài khoản.</summary>
    public string ActiveLogCaption => _showUpdate ? "Log update" : "Log scrape";

    /// <summary>true nếu tk này đang scrape 1 shop nào đó (job LIVE) → khoá ▶/⏯ các shop còn lại
    /// (1 shop/account: đang chạy 1 shop thì các shop khác không cho chạy chồng).</summary>
    public bool IsScraping => Shops.Any(s => ScrapeTarget.IsShopRunning?.Invoke(s.Shop) ?? false);

    /// <summary>Điều khiển nút "■ Dừng việc shop này" — ẩn khi acc không có action nào chạy (scrape LẪN update).</summary>
    public bool HasRunningWork => IsScraping || UpdateVm.IsUpdateRunning(Account.Id);

    /// <summary>Tick để chạy BATCH — đồng bộ cả scrape lẫn update (1 lần tick dùng cho cả 2 giai đoạn).</summary>
    public bool IsSelected
    {
        get => ScrapeTarget.IsSelected;
        set
        {
            if (ScrapeTarget.IsSelected == value) return;
            ScrapeTarget.IsSelected = value;   // tự Persist xuống ScrapeTargetConfigStore
            UpdateTarget.IsSelected = value;
            OnPropertyChanged();
        }
    }

    public string DisplayName => Account.DisplayName;
    public string CookieStatus => Bs.CookieStatus;
    /// <summary>Đã có cookie BigSeller chưa — chỉ để TÔ MÀU (pill xanh / vàng). Raise kèm CookieStatus.</summary>
    public bool HasCookie => Account.HasCookie;
    /// <summary>Bản NGẮN của <see cref="CookieStatus"/> cho thẻ tk cột trái (chỉ rộng 292px, phải đứng cùng
    /// dòng với "x/y shop đã scrape").</summary>
    public string CookieShort => Account.HasCookie ? "✓ cookie" : "⚠ chưa đăng nhập";
    /// <summary>Đường dẫn workbook riêng, hoặc khi đang gộp: tên file chung và các sheet đã chọn.</summary>
    public string WorkbookPath
    {
        get
        {
            if (!SharedWorkbookStore.IsActive) return Account.WorkbookPath;
            var snap = SharedWorkbookStore.Snapshot();
            var name = string.IsNullOrWhiteSpace(snap.SourceName) ? "Workbook chung" : snap.SourceName;
            var sheets = snap.SelectedSheets.Count == 0 ? "(chưa chọn sheet)" : string.Join(", ", snap.SelectedSheets);
            return $"Workbook: {name}{Environment.NewLine}Sheet: {sheets}";
        }
    }

    public string WorkbookHint => SharedWorkbookStore.IsActive
        ? "Mọi shop của mọi tài khoản đang đọc các sheet này."
        : "Mỗi shop = 1 sheet — bấm nút trên dòng shop để chạy đúng shop đó.";
    /// <summary>Hub-mode → ẨN hàng "Workbook" ở tab Shop &amp; cấu hình (kho SP ở Hub, không dùng workbook Excel).
    /// Excel-mode hiện như cũ. Cố định theo vòng đời VM (đổi chế độ trên Hub sẽ re-sync dựng lại VM).</summary>
    // (Hub data mode removed — App local always uses workbook.)
    public int ShopCount => Shops.Count;

    /// <summary>Tóm tắt "đã scrape X/Y shop" cho dòng tài khoản bên trái (theo dõi nhanh).</summary>
    public string ScrapeSummary
    {
        get
        {
            var total = Shops.Count;
            if (total == 0) return "chưa có shop";
            var done = Shops.Count(s => s.ScrapeStatusText.Contains("đã scrape"));
            return $"{done}/{total} shop đã scrape";
        }
    }

    public void RefreshAll()
    {
        Bs.NotifyCookieChanged();
        foreach (var s in Shops) { s.RefreshStatus(); s.RefreshUpdateState(); }
        OnPropertyChanged(nameof(CookieStatus));
        OnPropertyChanged(nameof(HasCookie));
        OnPropertyChanged(nameof(CookieShort));
        OnPropertyChanged(nameof(ScrapeSummary));
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(WorkbookPath));
        OnPropertyChanged(nameof(WorkbookHint));
    }

    /// <summary>Bỏ lắng nghe khi rebuild để không rò sự kiện / cập nhật dòng đã xoá.</summary>
    public void Detach()
    {
        ScrapeTarget.PropertyChanged -= OnScrapeChanged;
        UpdateVm.JobsChanged -= OnUpdateJobsChanged;
        SharedWorkbookStore.Changed -= OnSharedWorkbookChanged;
        ShopLiveLine.Changed -= OnLiveLine;
        // App local: không đăng ký Hub.Changed.
    }
}
