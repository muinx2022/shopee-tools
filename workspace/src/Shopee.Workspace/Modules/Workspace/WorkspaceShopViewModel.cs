using Shopee.Workspace.Infrastructure;
using Shopee.Workspace.Modules.Scrape;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shopee.Core.BigSeller;
using Shopee.Core.Coordination;
using Shopee.Core.Progress;
using Shopee.Core.Scrape;
using Shopee.Workspace.Modules.UpdateProduct;
using Shopee.Workspace;

namespace Shopee.Workspace.Modules.Workspace;

/// <summary>
/// 1 dòng SHOP trong lưới pipeline của màn gộp v1.1: tên + sheet + trạng thái scrape (đã/đang/chưa) +
/// nút hành động (scrape / import / update / tên SP / map). Trỏ về <see cref="WorkspaceAccountViewModel"/>
/// cha để các lệnh biết chạy cho tk BigSeller nào (giữ mô hình 1 shop/account như hiện tại).
/// </summary>
public sealed partial class WorkspaceShopViewModel : ObservableObject
{
    public WorkspaceAccountViewModel Parent { get; }
    public BigSellerShop Shop { get; }

    public WorkspaceShopViewModel(WorkspaceAccountViewModel parent, BigSellerShop shop)
    {
        Parent = parent;
        Shop = shop;
        RefreshStatus();
    }

    public string Name => Shop.DisplayName;
    public string DisplayName => Name;

    /// <summary>Đang chạy thì hiện tiến độ trực tiếp; nếu đã update xong thì hiện tổng số dòng &amp; khoảng dòng đã cập nhật.</summary>
    public string ProgressLine
    {
        get
        {
            var live = ShopLiveLine.For(Parent.Account.Id, Shop.Id);
            if (!string.IsNullOrEmpty(live)) return live;
            return UpdateDone ? GetCompletedUpdateProgressText() : "";
        }
    }

    public string ProgressTooltip
    {
        get
        {
            var live = ShopLiveLine.TooltipFor(Parent.Account.Id, Shop.Id);
            if (!string.IsNullOrEmpty(live)) return live;
            return UpdateDone ? GetCompletedUpdateProgressText() : "";
        }
    }

    private string GetCompletedUpdateProgressText()
    {
        var account = Parent.Account;
        var resolved = SharedWorkbookStore.ResolveForUpdate(account, Shop);
        var summary = OpProgressStore.Shared.TryGetSummary(
            account.Id,
            new[] { resolved.ProgressKey, Shop.ShopeeDataSheet, Shop.Name },
            AssignmentOps.Update);

        if (summary is not { DoneCount: > 0 } s)
            return "Đã cập nhật 0 dòng";

        if (s.MinRow > 0 && s.MaxRow > 0)
            return $"Đã cập nhật {s.DoneCount} dòng (từ dòng {s.MinRow} đến dòng {s.MaxRow})";

        WorkbookRowRangeResolver.QueueResolveIfMissing(
            account.Id, s.Sheet, AssignmentOps.Update,
            resolved.DataPath, resolved.DataSheet,
            Shop.ColumnMap.ItemIdColumn, Shop.ColumnMap.LinkColumn,
            s.DoneCount);

        return $"Đã cập nhật {s.DoneCount} dòng";
    }

    /// <summary>Tooltip cho ô icon trạng thái Scrape (chỉ xem ở app Import &amp; Update).</summary>
    public string ScrapeReadOnlyTip => ScrapeDone
        ? (string.IsNullOrWhiteSpace(ScrapeTooltip) ? "Scrape: Đã hoàn thành" : $"Scrape: Đã hoàn thành · {ScrapeTooltip}")
        : (string.IsNullOrWhiteSpace(ScrapeTooltip) ? "Scrape: Chưa xong" : $"Scrape: Chưa xong · {ScrapeTooltip}");

    public void NotifyLiveLine()
    {
        OnPropertyChanged(nameof(ProgressLine));
        OnPropertyChanged(nameof(ProgressTooltip));
    }
    public string Sheet
    {
        get
        {
            if (WorkspaceAppConfig.IsImportUpdateOnly || SharedWorkbookStore.HasSharedSource)
            {
                var resolved = SharedWorkbookStore.ResolveForUpdate(Parent.Account, Shop);
                if (!string.IsNullOrWhiteSpace(resolved.DataSheet))
                    return resolved.DataSheet;
            }
            return string.IsNullOrWhiteSpace(Shop.ShopeeDataSheet) ? "- chưa gán sheet" : Shop.ShopeeDataSheet;
        }
    }


    [ObservableProperty] private string _scrapeStatusText = "";
    [ObservableProperty] private Brush _scrapeBackground = Brushes.Transparent;
    [ObservableProperty] private Brush _scrapeForeground = Brushes.Black;
    [ObservableProperty] private string _scrapeTooltip = "";

    /// <summary>true nếu shop này đang là shop được chọn của tk (1 shop/account) → tô đậm dòng đang chọn.</summary>
    public bool IsCurrent => ReferenceEquals(Parent.ScrapeTarget.SelectedShop, Shop);

    /// <summary>true nếu CHÍNH shop này đang scrape (job LIVE) → bật nút ■, các shop khác tắt ▶/⏯.</summary>
    public bool IsScraping => Parent.ScrapeTarget.IsShopRunning?.Invoke(Shop) ?? false;

    /// <summary>true khi tk KHÔNG có shop nào đang scrape (máy này) VÀ không máy khác bận tk này → mới cho chạy
    /// (1 shop/account + khoá khi máy khác đang chạy tk).</summary>
    public bool CanScrape => !Parent.IsScraping && !OtherMachineBusy;

    // ── Trạng thái workflow UPDATE (import/update/tên SP) — đổi nút ⇄ ■ Dừng + khoá các shop cùng tk ──
    public bool IsImporting => RunningUpdate(UpdateKind.Import);
    public bool IsUpdatingShop => RunningUpdate(UpdateKind.Update);
    public bool IsRewriting => RunningUpdate(UpdateKind.Rewrite);

    private bool RunningUpdate(UpdateKind kind) =>
        Parent.UpdateVm.TryGetRunningUpdate(Parent.Account.Id, out var sid, out var k)
        && k == kind && string.Equals(sid, Shop.Id, StringComparison.Ordinal);

    /// <summary>true khi tk CHƯA chạy workflow update nào (máy này) VÀ không máy khác bận tk này → mới cho bấm
    /// Import/Update/Tên SP (1 workflow update/tk + khoá khi máy khác đang chạy tk).</summary>
    public bool CanStartUpdate => !Parent.UpdateVm.IsUpdateRunning(Parent.Account.Id) && !OtherMachineBusy;

    public void RefreshUpdateState()
    {
        OnPropertyChanged(nameof(IsImporting));
        OnPropertyChanged(nameof(IsUpdatingShop));
        OnPropertyChanged(nameof(IsRewriting));
        OnPropertyChanged(nameof(CanStartUpdate));
        RefreshFleet();
    }

    // ── Nút TOGGLE 1-nút/op: bấm chạy ↔ bấm lại dừng. "Đang chạy" tính cả máy khác (fleet). ──

    /// <summary>Trạng thái 1 op của shop này: chạy không + máy nào. Ưu tiên local (tức thời), rồi fleet.</summary>
    /// <summary>Tr\u1ea1ng th\u00e1i 1 op c\u1ee7a shop n\u00e0y: ch\u1ec9 ki\u1ec3m tra local.</summary>
    private static (bool running, string by) OpState(CoordOp op, bool localRunning)
        => localRunning ? (true, "m\u00e1y n\u00e0y") : (false, "");







    /// <summary>true nếu MÁY KHÁC đang giữ bất kỳ lease nào trên tài khoản này → khoá MỌI action ở máy này (kể cả
    /// <summary>App ch\u1ea1y standalone \u2014 kh\u00f4ng c\u00f3 m\u00e1y kh\u00e1c, lu\u00f4n false.</summary>
    private static bool OtherMachineBusy => false;












    // Đang chạy (máy này HOẶC máy khác) — XAML dựa vào đây để đổi nút sang ICON CAM + VIỀN CAM (nền giữ trắng
    // theo luật nút mới; bấm lại để dừng — tooltip nói rõ).
    // Icon nút cố định theo OP (đặt thẳng trong WorkspaceView.axaml: IconPlay/IconImport/IconEdit/IconSparkle),
    // GIỮ NGUYÊN cả khi đang chạy — đổi icon lúc chạy làm nút "nhảy" hình.
    public bool ScrapeRunning => OpState(CoordOp.Scrape, IsScraping).running;
    public bool ImportRunning => OpState(CoordOp.Import, IsImporting).running;
    public bool UpdateRunning => OpState(CoordOp.Update, IsUpdatingShop).running;
    public bool RewriteRunning => OpState(CoordOp.Rewrite, IsRewriting).running;

    // Bật nút khi: MÁY NÀY đang chạy op đó (để dừng) HOẶC được phép bắt đầu (theo quy tắc khoá).
    public bool ScrapeToggleEnabled => IsScraping || CanScrape;
    public bool ImportToggleEnabled => IsImporting || CanStartUpdate;
    public bool UpdateToggleEnabled => IsUpdatingShop || CanStartUpdate;
    public bool RewriteToggleEnabled => IsRewriting || CanStartUpdate;

    public string ScrapeToggleTip => ToggleTip("scrape", IsScraping, CoordOp.Scrape, "Scrape shop này (tiếp tục dòng còn thiếu)");
    public string ImportToggleTip => ToggleTip("import", IsImporting, CoordOp.Import, "Import to store cho shop này");
    public string UpdateToggleTip => ToggleTip("update", IsUpdatingShop, CoordOp.Update, "Update product cho shop này");
    public string RewriteToggleTip => ToggleTip("đặt tên SP", IsRewriting, CoordOp.Rewrite, "Update tên SP (AI) cho shop này");

    private string ToggleTip(string verb, bool localRunning, CoordOp op, string idleText)
    {
        if (localRunning) return $"Đang {verb} — bấm để dừng";
        var (r, by) = OpState(op, false);
        return r ? $"Đang {verb} (máy {by}) — dừng tại máy đó" : idleText;
    }

    /// <summary>Action đã chạy xong cho shop này (tiến độ local, không qua Hub).</summary>
    private bool IsOpDone(CoordOp op)
    {
        var account = Parent.Account;
        if (op == CoordOp.Scrape)
        {
            var scrapeResolved = SharedWorkbookStore.Resolve(account, Shop);
            var key = SharedWorkbookCatalog.ScrapeProgressKey(SharedWorkbookStore.IsActive, account.Id, scrapeResolved.ProgressKey);
            var progress = ScrapeProgressStore.Shared.Find(account.Id, key);
            if (progress is not null && string.Equals(progress.Status, LedgerStatus.Completed, StringComparison.OrdinalIgnoreCase))
                return true;

            var updateResolved = SharedWorkbookStore.ResolveForUpdate(account, Shop);
            if (!string.IsNullOrWhiteSpace(updateResolved.ProgressKey))
            {
                var altKey = SharedWorkbookCatalog.ScrapeProgressKey(SharedWorkbookStore.IsActive, account.Id, updateResolved.ProgressKey);
                var altProg = ScrapeProgressStore.Shared.Find(account.Id, altKey);
                if (altProg is not null && string.Equals(altProg.Status, LedgerStatus.Completed, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        var opName = op switch
        {
            CoordOp.Import => AssignmentOps.Import,
            CoordOp.Update => AssignmentOps.Update,
            CoordOp.Rewrite => AssignmentOps.Rewrite,
            _ => "",
        };
        if (opName.Length == 0) return false;

        var resolved = SharedWorkbookStore.ResolveForUpdate(account, Shop);
        foreach (var row in OpProgressStore.Shared.Snapshot())
        {
            if (row.acc == account.Id
                && (string.Equals(row.sheet, resolved.ProgressKey, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(Shop.ShopeeDataSheet) && string.Equals(row.sheet, Shop.ShopeeDataSheet, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrWhiteSpace(Shop.Name) && string.Equals(row.sheet, Shop.Name, StringComparison.OrdinalIgnoreCase)))
                && row.op == opName
                && string.Equals(row.status, LedgerStatus.Completed, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }









    public bool ScrapeDone => !ScrapeRunning && IsOpDone(CoordOp.Scrape);
    public bool ImportDone => !ImportRunning && IsOpDone(CoordOp.Import);
    public bool UpdateDone => !UpdateRunning && IsOpDone(CoordOp.Update);
    public bool RewriteDone => !RewriteRunning && IsOpDone(CoordOp.Rewrite);

    /// <summary>true khi shop này đang chạy 1 workflow update TRÊN MÁY NÀY (giữ cho chỗ khác nếu còn dùng).</summary>
    public bool IsUpdatingAnyLocal => IsImporting || IsUpdatingShop || IsRewriting;

    public bool CanReset => !ScrapeRunning && !ImportRunning && !UpdateRunning && !RewriteRunning;

    /// <summary>Làm mới màu/tooltip/enabled/icon nút action (gọi khi hub đổi fleet + khi trạng thái local đổi).</summary>
    public void RefreshFleet()
    {
        foreach (var n in new[]
        {
            nameof(ScrapeRunning), nameof(ImportRunning), nameof(UpdateRunning), nameof(RewriteRunning),
            nameof(ScrapeToggleEnabled), nameof(ImportToggleEnabled), nameof(UpdateToggleEnabled), nameof(RewriteToggleEnabled),
            nameof(ScrapeToggleTip), nameof(ImportToggleTip), nameof(UpdateToggleTip), nameof(RewriteToggleTip),
            nameof(CanScrape), nameof(CanStartUpdate), nameof(IsUpdatingAnyLocal),
            nameof(ScrapeDone), nameof(ImportDone), nameof(UpdateDone), nameof(RewriteDone),
            nameof(ScrapeReadOnlyTip), nameof(ProgressLine), nameof(ProgressTooltip),
            nameof(CanReset),
        }) OnPropertyChanged(n);
    }

    // ── Xoá tiến độ RESUME per-SP của shop này ("chạy lại từ đầu") — ContextMenu 2-bước trên nút Import/Update.
    //    Đặt command Ở ĐÂY (không phải WorkspaceViewModel) để MenuItem trong ContextMenu bind qua DataContext
    //    kế thừa (= shop này); popup của ContextMenu KHÔNG nằm trong visual tree UserControl nên $parent[...]
    //    không tra ngược tới WorkspaceViewModel được. Fire-and-forget: ResetOpProgressAsync tự nuốt lỗi Hub. ──
    [RelayCommand]
    private void MarkScrapeDone()
    {
        var account = Parent.Account;
        var resolved = SharedWorkbookStore.Resolve(account, Shop);
        var progressKey = !string.IsNullOrWhiteSpace(resolved.ProgressKey)
            ? resolved.ProgressKey
            : SharedWorkbookStore.ResolveForUpdate(account, Shop).ProgressKey;
        var key = SharedWorkbookCatalog.ScrapeProgressKey(SharedWorkbookStore.IsActive, account.Id, progressKey);
        ScrapeProgressStore.Shared.MarkCompleted(account.Id, key);
        RefreshStatus();
        Parent.ScrapeTarget.RefreshProgress();
    }

    [RelayCommand]
    private void ResetScrapeProgress()
    {
        var account = Parent.Account;
        var resolved = SharedWorkbookStore.Resolve(account, Shop);
        var key = SharedWorkbookCatalog.ScrapeProgressKey(SharedWorkbookStore.IsActive, account.Id, resolved.ProgressKey);
        ScrapeProgressStore.Shared.Clear(account.Id, key);
        var updateResolved = SharedWorkbookStore.ResolveForUpdate(account, Shop);
        if (!string.Equals(resolved.ProgressKey, updateResolved.ProgressKey, StringComparison.OrdinalIgnoreCase))
        {
            var altKey = SharedWorkbookCatalog.ScrapeProgressKey(SharedWorkbookStore.IsActive, account.Id, updateResolved.ProgressKey);
            ScrapeProgressStore.Shared.Clear(account.Id, altKey);
        }
        RefreshStatus();
        Parent.ScrapeTarget.RefreshProgress();
    }

    [RelayCommand]
    private void MarkImportDone()
    {
        var account = Parent.Account;
        var resolved = SharedWorkbookStore.ResolveForUpdate(account, Shop);
        var key = !string.IsNullOrWhiteSpace(resolved.ProgressKey)
            ? resolved.ProgressKey
            : (!string.IsNullOrWhiteSpace(Shop.ShopeeDataSheet) ? Shop.ShopeeDataSheet : Shop.Name);
        OpProgressStore.Shared.MarkCompleted(account.Id, key, AssignmentOps.Import, account.DisplayName);
        RefreshFleet();
    }

    [RelayCommand]
    private void MarkUpdateDone()
    {
        var account = Parent.Account;
        var resolved = SharedWorkbookStore.ResolveForUpdate(account, Shop);
        var key = !string.IsNullOrWhiteSpace(resolved.ProgressKey)
            ? resolved.ProgressKey
            : (!string.IsNullOrWhiteSpace(Shop.ShopeeDataSheet) ? Shop.ShopeeDataSheet : Shop.Name);
        OpProgressStore.Shared.MarkCompleted(account.Id, key, AssignmentOps.Update, account.DisplayName);
        RefreshFleet();
    }

    [RelayCommand]
    private void MarkRewriteDone()
    {
        var account = Parent.Account;
        var resolved = SharedWorkbookStore.ResolveForUpdate(account, Shop);
        var key = !string.IsNullOrWhiteSpace(resolved.ProgressKey)
            ? resolved.ProgressKey
            : (!string.IsNullOrWhiteSpace(Shop.ShopeeDataSheet) ? Shop.ShopeeDataSheet : Shop.Name);
        OpProgressStore.Shared.MarkCompleted(account.Id, key, AssignmentOps.Rewrite, account.DisplayName);
        RefreshFleet();
    }

    [RelayCommand] private void ResetImportProgress() => ResetOpProgress(AssignmentOps.Import);
    [RelayCommand] private void ResetUpdateProgress() => ResetOpProgress(AssignmentOps.Update);
    [RelayCommand] private void ResetRewriteProgress() => ResetOpProgress(AssignmentOps.Rewrite);

    private void ResetOpProgress(string op)
    {
        var account = Parent.Account;
        var resolved = SharedWorkbookStore.ResolveForUpdate(account, Shop);
        if (!string.IsNullOrWhiteSpace(resolved.ProgressKey))
            OpProgressStore.Shared.Clear(account.Id, resolved.ProgressKey, op);
        if (!string.IsNullOrWhiteSpace(Shop.ShopeeDataSheet))
            OpProgressStore.Shared.Clear(account.Id, Shop.ShopeeDataSheet, op);
        if (!string.IsNullOrWhiteSpace(Shop.Name))
            OpProgressStore.Shared.Clear(account.Id, Shop.Name, op);
        Parent.UpdateTarget.SelectedShop = Shop;   // ResetOpProgressAsync đọc target.SelectedShop
        _ = Parent.UpdateVm.ResetOpProgressAsync(Parent.UpdateTarget, op);
        RefreshFleet();
    }

    public void ResetScrapeProgressOnly()
    {
        var account = Parent.Account;
        var resolved = SharedWorkbookStore.Resolve(account, Shop);
        var scrapeKey = SharedWorkbookCatalog.ScrapeProgressKey(SharedWorkbookStore.IsActive, account.Id, resolved.ProgressKey);

        ScrapeProgressStore.Shared.Clear(account.Id, scrapeKey);
        ShopLiveLine.Clear(account.Id, Shop.Id);
        NotifyLiveLine();

        RefreshStatus();
        RefreshFleet();
        Parent.ScrapeTarget.RefreshProgress();
    }

    public void ResetImportUpdateProgressOnly()
    {
        var account = Parent.Account;
        var resolved = SharedWorkbookStore.Resolve(account, Shop);
        var resolvedUpdate = SharedWorkbookStore.ResolveForUpdate(account, Shop);

        OpProgressStore.Shared.ClearAllOps(account.Id, resolvedUpdate.ProgressKey);
        if (!string.Equals(resolved.ProgressKey, resolvedUpdate.ProgressKey, StringComparison.OrdinalIgnoreCase))
            OpProgressStore.Shared.ClearAllOps(account.Id, resolved.ProgressKey);

        RefreshStatus();
        RefreshFleet();
        Parent.ScrapeTarget.RefreshProgress();
    }

    public void ResetAllProgress()
    {
        var account = Parent.Account;
        var resolved = SharedWorkbookStore.Resolve(account, Shop);
        var resolvedUpdate = SharedWorkbookStore.ResolveForUpdate(account, Shop);
        var scrapeKey = SharedWorkbookCatalog.ScrapeProgressKey(SharedWorkbookStore.IsActive, account.Id, resolved.ProgressKey);

        ScrapeProgressStore.Shared.Clear(account.Id, scrapeKey);
        OpProgressStore.Shared.ClearAllOps(account.Id, resolved.ProgressKey);
        if (!string.Equals(resolved.ProgressKey, resolvedUpdate.ProgressKey, StringComparison.OrdinalIgnoreCase))
            OpProgressStore.Shared.ClearAllOps(account.Id, resolvedUpdate.ProgressKey);

        ShopLiveLine.Clear(account.Id, Shop.Id);
        NotifyLiveLine();

        RefreshStatus();
        RefreshFleet();
        Parent.ScrapeTarget.RefreshProgress();
    }

    public void RefreshProgress()
    {
        RefreshStatus();
        RefreshFleet();
        Parent.ScrapeTarget.RefreshProgress();
    }

    [RelayCommand(CanExecute = nameof(CanReset))]
    private async Task ResetAllProgressAsync()
    {
        var account = Parent.Account;
        var resolved = SharedWorkbookStore.Resolve(account, Shop);
        var key = resolved.ProgressKey;

        if (WorkspaceAppConfig.IsScrapeOnly)
        {
            var scrapeKey = SharedWorkbookCatalog.ScrapeProgressKey(SharedWorkbookStore.IsActive, account.Id, key);
            if (await ScrapeProgressReset.ConfirmAndResetAsync(account.Id, scrapeKey, Shop, Parent.DisplayName))
            {
                ShopLiveLine.Clear(account.Id, Shop.Id);
                NotifyLiveLine();
                RefreshProgress();
                Parent.RefreshProgressSummary();
            }
        }
        else if (WorkspaceAppConfig.IsImportUpdateOnly)
        {
            if (await Dialogs.ConfirmAsync($"Xóa tiến độ Import / Update của shop \"{DisplayName}\" ({Parent.DisplayName}) để chạy lại từ đầu?", "Reset tiến độ"))
            {
                var updateKey = SharedWorkbookStore.ResolveForUpdate(account, Shop).ProgressKey;
                OpProgressStore.Shared.ClearAllOps(account.Id, updateKey);
                if (!string.Equals(key, updateKey, StringComparison.OrdinalIgnoreCase))
                    OpProgressStore.Shared.ClearAllOps(account.Id, key);
                RefreshProgress();
                Parent.RefreshProgressSummary();
            }
        }
        else
        {
            if (!await Dialogs.ConfirmAsync($"Reset toàn bộ tiến độ (Scrape · Import · Update) của shop '{Name}' về trạng thái ban đầu?", "Reset tiến độ shop"))
                return;

            ResetAllProgress();
            Parent.RefreshProgressSummary();
        }
    }

    public IAsyncRelayCommand ResetAllProgressAsyncCommand => ResetAllProgressCommand;

    /// <summary>Tính lại trạng thái từ tiến độ scrape (mượn nguyên logic chip của ScrapeTargetViewModel).</summary>
    public void RefreshStatus()
    {
        var st = Parent.ScrapeTarget.StatusFor(Shop);
        ScrapeStatusText = st.StatusText;
        ScrapeBackground = st.Background;
        ScrapeForeground = st.Foreground;
        ScrapeTooltip = st.Tooltip;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Sheet));
        OnPropertyChanged(nameof(IsCurrent));
        OnPropertyChanged(nameof(IsScraping));
        OnPropertyChanged(nameof(CanScrape));
        RefreshFleet();
    }

}
