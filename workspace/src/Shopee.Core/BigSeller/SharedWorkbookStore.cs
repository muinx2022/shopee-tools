using System.Text.Json;
using Shopee.Core.Infrastructure;

namespace Shopee.Core.BigSeller;

/// <summary>Cấu hình workbook chung đã lưu. <see cref="Enabled"/> chỉ đúng sau khi gộp xong.</summary>
public sealed class SharedWorkbookState
{
    public bool Enabled { get; set; }
    public List<string> SelectedSheets { get; set; } = [];
    public int MergedRowCount { get; set; }
    public List<string> ColumnNotes { get; set; } = [];
    /// <summary>Tên file user đã chọn, chỉ để hiện trên màn hình.</summary>
    public string SourceName { get; set; } = "";
}

/// <summary>
/// File cấu hình và hai workbook nằm cạnh <c>bigseller.json</c>.
/// Chọn file mới thì tắt <see cref="SharedWorkbookState.Enabled"/> cho đến khi gộp lại.
/// Ngừng dùng chỉ tắt cờ, không xóa workbook riêng của tài khoản.
/// </summary>
public static class SharedWorkbookStore
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string ConfigPath => Path.Combine(SuitePaths.ModuleDir("shared"), "shared-workbook.json");
    public static string SourcePath => Path.Combine(SuitePaths.ModuleDir("shared"), "shared-workbook-source.xlsx");
    public static string MergedPath => Path.Combine(SuitePaths.ModuleDir("shared"), "shared-workbook-merged.xlsx");

    public static SharedWorkbookState Current { get; private set; } = Load();

    /// <summary>Bắn sau khi chọn file, gộp, hoặc ngừng dùng — UI Workspace đọc lại dòng workbook.</summary>
    public static event Action? Changed;

    /// <summary>Đang bật và file gộp còn trên đĩa. Thiếu file thì job không được đọc workbook riêng.</summary>
    public static bool IsActive
    {
        get { lock (Gate) return Current.Enabled && File.Exists(MergedPath); }
    }

    public static SharedWorkbookTarget Resolve(BigSellerAccount account, BigSellerShop shop)
    {
        lock (Gate)
            return SharedWorkbookCatalog.Resolve(Current.Enabled, MergedPath, account, shop);
    }

    /// <summary>
    /// null = không dùng workbook chung, caller tự kiểm workbook từng tài khoản.
    /// Chuỗi = workbook chung đang bật nhưng file gộp không còn.
    /// </summary>
    public static string? MissingMergedProblem()
    {
        lock (Gate)
        {
            if (!Current.Enabled) return null;
            if (File.Exists(MergedPath)) return null;
            return "Workbook chung không còn file gộp, hãy gộp lại ở Cấu hình BigSeller";
        }
    }

    public static void ReplaceSource(string originalFileName)
    {
        lock (Gate)
        {
            Current = new SharedWorkbookState
            {
                Enabled = false,
                SourceName = originalFileName ?? "",
                SelectedSheets = [],
                MergedRowCount = 0,
                ColumnNotes = [],
            };
            SaveLocked();
        }
        Changed?.Invoke();
    }

    public static void MarkMerged(IReadOnlyList<string> sheets, int dataRowCount, IReadOnlyList<string> notes)
    {
        lock (Gate)
        {
            Current.Enabled = File.Exists(MergedPath);
            Current.SelectedSheets = sheets.ToList();
            Current.MergedRowCount = dataRowCount;
            Current.ColumnNotes = notes.ToList();
            SaveLocked();
        }
        Changed?.Invoke();
    }

    public static void Disable()
    {
        lock (Gate)
        {
            Current.Enabled = false;
            SaveLocked();
        }
        Changed?.Invoke();
    }

    public static SharedWorkbookState Snapshot()
    {
        lock (Gate)
            return new SharedWorkbookState
            {
                Enabled = Current.Enabled,
                SelectedSheets = Current.SelectedSheets.ToList(),
                MergedRowCount = Current.MergedRowCount,
                ColumnNotes = Current.ColumnNotes.ToList(),
                SourceName = Current.SourceName,
            };
    }

    private static SharedWorkbookState Load()
    {
        var loaded = JsonAtomicFile.TryLoad<SharedWorkbookState>(ConfigPath, JsonOpts);
        return loaded ?? new SharedWorkbookState();
    }

    private static void SaveLocked() => JsonAtomicFile.Save(ConfigPath, Current, JsonOpts);
}
