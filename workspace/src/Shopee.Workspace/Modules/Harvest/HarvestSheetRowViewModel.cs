using CommunityToolkit.Mvvm.ComponentModel;
using Shopee.Core.Harvest;
using Shopee.Core.Scrape;

namespace Shopee.Workspace.Modules.Harvest;

/// <summary>Một dòng của danh sách sheet trên màn Harvest: số link, đã có, còn phải lấy, bỏ qua/lỗi, tiến độ lượt đang chạy.</summary>
public sealed partial class HarvestSheetRowViewModel : ObservableObject
{
    public string Name { get; }
    /// <summary><see cref="HarvestSource.SheetKey"/> — khoá tiến độ của sheet trong <c>harvest-progress.json</c>.</summary>
    public string Key { get; }
    /// <summary>Các dòng hợp lệ (có tên SP + link) đọc ở lần quét gần nhất — Reset / "Cào lại dòng đã bỏ" lấy khoá SP từ đây.</summary>
    public IReadOnlyList<ScrapeLinkItem> Items { get; private set; } = [];

    private readonly Action<HarvestSheetRowViewModel>? _checkedChanged;

    public HarvestSheetRowViewModel(string name, string key, bool isChecked, Action<HarvestSheetRowViewModel>? checkedChanged = null)
    {
        Name = name;
        Key = key;
        IsChecked = isChecked;   // _checkedChanged chưa gán → không ghi ngược cấu hình
        _checkedChanged = checkedChanged;
    }

    [ObservableProperty] private bool _isChecked;
    partial void OnIsCheckedChanged(bool value) => _checkedChanged?.Invoke(this);

    /// <summary>Số dòng có link (cột A) + tên SP (cột F).</summary>
    [ObservableProperty] private int _totalLinks;
    /// <summary>Dòng đã có dữ liệu: SP đã lấy trên máy này còn hạn, hoặc Hub báo đã có còn hạn (lấy ở file / sheet / máy khác).</summary>
    [ObservableProperty] private int _haveCount;
    [ObservableProperty] private int _todoCount;
    /// <summary>Dòng bỏ qua vì SP lỗi vĩnh viễn đã xác nhận (2 lượt).</summary>
    [ObservableProperty] private int _skippedCount;
    /// <summary>Tiến độ lượt đang chạy ("12/40") — rỗng khi sheet không chạy trong lượt này.</summary>
    [ObservableProperty] private string _runText = "";
    [ObservableProperty] private bool _isRunning;

    // Mốc đầu lượt: số dòng nén đã phủ trong kho tiến độ lúc dựng lại + số dòng phải lấy.
    private int _coveredAtPlan;
    private int _todoAtPlan;

    /// <summary>Cảnh báo về cột link của sheet (ít link SP / lẫn chữ — <see cref="HarvestSheetInfo.Warning"/>); rỗng = ổn.</summary>
    [ObservableProperty] private string _warning = "";

    /// <summary>Kết quả quét file (không chạy): cập nhật các số đếm.</summary>
    public void ApplyScan(IReadOnlyList<ScrapeLinkItem> items, HarvestDoneRebuildResult plan, string? warning = null)
    {
        Items = items;
        Warning = warning ?? "";
        TotalLinks = items.Count;
        ApplyPlan(plan);
        RunText = "";
    }

    /// <summary>Đầu lượt chạy sheet này: số đếm theo kết quả dựng lại + mốc cho tiến độ lượt.</summary>
    public void ApplyPlan(HarvestDoneRebuildResult plan, ScrapeProgress? progress = null)
    {
        TodoCount = plan.TodoRows;
        SkippedCount = plan.Skipped.Count;
        HaveCount = Math.Max(0, TotalLinks - plan.TodoRows - plan.Skipped.Count);
        if (progress is null) return;
        _coveredAtPlan = Covered(progress);
        _todoAtPlan = plan.TodoRows;
        RunText = _todoAtPlan == 0 ? "không còn gì" : $"0/{_todoAtPlan}";
    }

    /// <summary>Khối xong / dòng bị bỏ trong lượt: tiến độ lượt = số dòng phủ thêm so với lúc dựng lại.</summary>
    public void ApplyProgress(ScrapeProgress? progress)
    {
        if (progress is null || _todoAtPlan == 0) return;
        var doneNow = Math.Clamp(Covered(progress) - _coveredAtPlan, 0, _todoAtPlan);
        RunText = $"{doneNow}/{_todoAtPlan}";
    }

    private static int Covered(ScrapeProgress p) => p.Completed.Sum(r => r.To - r.From + 1);
}
