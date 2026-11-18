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
    /// <summary>Các dòng có link TRONG DẢI Từ dòng / Đến dòng đọc ở lần quét gần nhất — Reset / "Cào lại dòng đã bỏ" lấy khoá SP từ đây.</summary>
    public IReadOnlyList<ScrapeLinkItem> Items { get; private set; } = [];

    /// <summary>Cả sheet đọc ở lần quét gần nhất (chưa thu theo dải) — đổi dải thì đếm lại từ đây, không đọc lại file.</summary>
    public HarvestWorkbookRange? FullRange { get; private set; }

    private readonly Action<HarvestSheetRowViewModel>? _checkedChanged;
    private readonly Action<HarvestSheetRowViewModel, string?>? _rangeChanged;

    /// <param name="rangeChanged">User sửa Từ dòng / Đến dòng: (dòng, null) = dải mới hợp lệ đã áp; (dòng, lý do) = nhập sai, ô đã
    /// trả về dải cũ.</param>
    public HarvestSheetRowViewModel(string name, string key, bool isChecked, Action<HarvestSheetRowViewModel>? checkedChanged = null,
        Action<HarvestSheetRowViewModel, string?>? rangeChanged = null)
    {
        Name = name;
        Key = key;
        IsChecked = isChecked;   // _checkedChanged chưa gán → không ghi ngược cấu hình
        _checkedChanged = checkedChanged;
        _rangeChanged = rangeChanged;
    }

    [ObservableProperty] private bool _isChecked;
    partial void OnIsCheckedChanged(bool value) => _checkedChanged?.Invoke(this);

    // ── Dải Từ dòng / Đến dòng (số dòng Excel thật) ──────────────────────────────────────────────────────────────

    /// <summary>Dòng Excel dữ liệu đầu của sheet (1 = không tiêu đề, 2 = có) — mặc định và mức kẹp của <see cref="FromRow"/>.</summary>
    [ObservableProperty] private int _firstDataRow = 1;
    /// <summary>Từ dòng (Excel). Nhập &lt; dòng dữ liệu đầu → kẹp về dòng dữ liệu đầu ngay.</summary>
    [ObservableProperty] private int _fromRow = 1;
    /// <summary>Đến dòng (Excel); 0 = tới hết. Âm → 0.</summary>
    [ObservableProperty] private int _toRow;

    // Dải hợp lệ đang áp (nhập sai thì ô trả về đây); đang tự đặt giá trị (kẹp / trả về / nạp) → không báo ngược.
    private int _appliedFrom = 1;
    private int _appliedTo;
    private bool _settingRange;

    public bool IsDefaultRange => FromRow == FirstDataRow && ToRow == 0;

    partial void OnFromRowChanged(int value) => OnRangeEdited();
    partial void OnToRowChanged(int value) => OnRangeEdited();

    /// <summary>Đặt dải KHÔNG qua ô nhập (nạp cấu hình / quét lại): kẹp, không báo ngược. Cặp sai (Đến &lt; Từ) → Đến = 0.</summary>
    public void LoadRange(int fromRow, int toRow, int firstDataRow)
    {
        _settingRange = true;
        try
        {
            FirstDataRow = Math.Max(1, firstDataRow);
            var from = HarvestRowRange.ClampFrom(fromRow, FirstDataRow);
            var to = HarvestRowRange.ClampTo(toRow);
            if (!HarvestRowRange.IsValid(from, to)) to = 0;
            FromRow = _appliedFrom = from;
            ToRow = _appliedTo = to;
        }
        finally { _settingRange = false; }
    }

    /// <summary>Đặt dải như user gõ (nút "Áp dải cho các sheet đã tick"): cùng luật kẹp / báo lỗi như ô nhập.</summary>
    public void SetRangeFromUser(int fromRow, int toRow)
    {
        _settingRange = true;
        try { FromRow = fromRow; ToRow = toRow; }
        finally { _settingRange = false; }
        OnRangeEdited();
    }

    /// <summary>Luật nhập (chọn "báo lỗi + giữ dải cũ", KHÔNG tự đưa Đến về 0: Đến dòng gõ nhầm nhỏ hơn Từ dòng mà tự thành "tới hết"
    /// thì lượt Chạy sau lấy cả phần sheet user không định lấy): Từ &lt; dòng dữ liệu đầu → kẹp; Đến âm → 0; Đến ≠ 0 mà &lt; Từ → trả
    /// cả hai ô về dải đang áp và báo lý do.</summary>
    private void OnRangeEdited()
    {
        if (_settingRange) return;
        var from = HarvestRowRange.ClampFrom(FromRow, FirstDataRow);
        var to = HarvestRowRange.ClampTo(ToRow);
        string? error = null;
        if (!HarvestRowRange.IsValid(from, to))
        {
            error = $"Sheet {Name}: Đến dòng ({to}) nhỏ hơn Từ dòng ({from}) — giữ dải cũ ({HarvestRowRange.Describe(_appliedFrom, _appliedTo)}). "
                    + "Đến dòng = 0 là tới hết sheet.";
            (from, to) = (_appliedFrom, _appliedTo);
        }
        _settingRange = true;
        try { FromRow = from; ToRow = to; }
        finally { _settingRange = false; }
        var changed = from != _appliedFrom || to != _appliedTo;
        _appliedFrom = from;
        _appliedTo = to;
        if (error is not null || changed) _rangeChanged?.Invoke(this, error);
    }

    /// <summary>Số dòng có link trong dải Từ dòng / Đến dòng (cột link do <see cref="HarvestWorkbookReader"/> tự chọn; tên SP không bắt
    /// buộc — dòng có chữ không phải link không tính).</summary>
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

    /// <summary>Kết quả quét file (không chạy): cập nhật các số đếm. <paramref name="items"/> / <paramref name="plan"/> đã thu theo dải;
    /// <paramref name="fullRange"/> = cả sheet (để đổi dải đếm lại không đọc file).</summary>
    public void ApplyScan(IReadOnlyList<ScrapeLinkItem> items, HarvestDoneRebuildResult plan, string? warning = null,
        HarvestWorkbookRange? fullRange = null)
    {
        if (fullRange is not null) FullRange = fullRange;
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
