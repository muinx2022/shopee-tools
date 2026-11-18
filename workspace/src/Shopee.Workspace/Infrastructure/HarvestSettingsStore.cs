using System.Text.Json;
using Shopee.Core.Harvest;
using Shopee.Core.Infrastructure;
using Shopee.Core.Scrape;

namespace Shopee.Workspace.Infrastructure;

/// <summary>Cấu hình của app Harvest (một bộ cho cả app — không có tài khoản / shop): file Excel đang dùng, sheet bỏ tick, hạn
/// "lấy lại", nhịp chạy. JSON <c>&lt;SuitePaths.Root&gt;\harvest\harvest-settings.json</c>.</summary>
public sealed class HarvestSettings
{
    /// <summary>File Excel đang dùng (nhớ qua lần mở sau). Rỗng = chưa chọn.</summary>
    public string WorkbookPath { get; set; } = "";
    /// <summary>Khoá (<see cref="HarvestSource.SheetKey"/>) các sheet người dùng BỎ tick — mặc định tick hết (sheet mới tự tick).</summary>
    public List<string> UncheckedSheetKeys { get; set; } = [];
    /// <summary>SP có dữ liệu (trên máy / trên Hub) cũ hơn chừng này ngày thì lấy lại.</summary>
    public int MaxAgeDays { get; set; } = HarvestPlanning.DefaultMaxAgeDays;
    /// <summary>Số cửa sổ Brave chạy song song.</summary>
    public int Processes { get; set; } = 2;
    /// <summary>Số tk Shopee đóng khung cho lượt chạy (≥ số cửa sổ).</summary>
    public int FrameSize { get; set; } = 10;
    /// <summary>Số dòng mỗi lần (mỗi tk Shopee nhận một khối liên tiếp).</summary>
    public int RowsPerAccount { get; set; } = 60;
    public int RestMinSeconds { get; set; } = ScrapeRestWindow.DefaultMinSeconds;
    public int RestMaxSeconds { get; set; } = ScrapeRestWindow.DefaultMaxSeconds;
    /// <summary>"Từ dòng / Đến dòng" (số dòng Excel) từng sheet, khoá <see cref="HarvestSource.SheetKey"/> (file + sheet). Không có
    /// = mặc định (dòng dữ liệu đầu → hết sheet).</summary>
    public Dictionary<string, HarvestSheetRowRange> SheetRanges { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Dải dòng Excel đã lưu của một sheet: <see cref="ToRow"/> = 0 → tới hết.</summary>
public sealed class HarvestSheetRowRange
{
    public int FromRow { get; set; }
    public int ToRow { get; set; }
}

public sealed class HarvestSettingsStore
{
    private static readonly Lazy<HarvestSettingsStore> _shared =
        new(() => new HarvestSettingsStore(Path.Combine(SuitePaths.ModuleDir("harvest"), "harvest-settings.json")));

    public static HarvestSettingsStore Shared => _shared.Value;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public string FilePath { get; }

    public HarvestSettings Current { get; }

    internal HarvestSettingsStore(string filePath)
    {
        FilePath = filePath;
        Current = JsonAtomicFile.TryLoad<HarvestSettings>(FilePath, JsonOpts) ?? new HarvestSettings();
        Current.UncheckedSheetKeys ??= [];
        Current.WorkbookPath ??= "";
        // JSON nạp ra Dictionary so khoá phân biệt hoa thường — khoá sheet so KHÔNG phân biệt (như UncheckedSheetKeys).
        var ranges = new Dictionary<string, HarvestSheetRowRange>(StringComparer.OrdinalIgnoreCase);
        if (Current.SheetRanges is { } loaded)
            foreach (var (k, v) in loaded) if (v is not null) ranges[k] = v;
        Current.SheetRanges = ranges;
    }

    public void Save() => JsonAtomicFile.Save(FilePath, Current, JsonOpts);
}
