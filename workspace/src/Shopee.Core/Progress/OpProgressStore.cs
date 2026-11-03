using Shopee.Core.Coordination;
using Shopee.Core.Infrastructure;

namespace Shopee.Core.Progress;

/// <summary>Tóm tắt gọn tiến độ 1 op của shop (trạng thái + số dòng đã xong + khoảng dòng đầu..cuối).</summary>
public readonly record struct OpProgressSummary(string Sheet, string Status, int DoneCount, int MinRow, int MaxRow);

/// <summary>Kết quả <see cref="OpProgressStore.ResetImportWithoutUpdate"/> cho 1 khoá: sheet và số id đã bỏ khỏi Import Done.</summary>
public sealed record ImportResetSheet(string Sheet, int Removed);

/// <summary>
/// Tiến độ per-SẢN-PHẨM của một (tài khoản BigSeller + sheet + thao tác) cho 2 workflow Import-to-store và
/// Update-product. <see cref="Done"/> khoá theo ITEM ID Shopee (khớp đơn vị của runner): import → giá trị null
/// (chỉ cần biết "đã import"); update → tên đã điền lúc save (để lượt sau đổi tên là tự vào lại diện update).
/// Dùng cho Resume (kill/dừng giữa chừng rồi chạy lại KHÔNG import trùng / KHÔNG update lại SP đã xong).
/// </summary>
public sealed class OpProgress
{
    public string AccountId { get; set; } = "";     // tài khoản BigSeller
    public string Sheet { get; set; } = "";
    public string Op { get; set; } = "";             // AssignmentOps.Import | AssignmentOps.Update
    public string AccountName { get; set; } = "";    // tên hiển thị (tiện cho UI/thống kê)

    /// <summary>itemId Shopee → tên đã điền lúc save (import: null). Có mặt = SP đã xử lý xong ở op này.</summary>
    public Dictionary<string, string?> Done { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Dòng Excel nhỏ nhất và lớn nhất đã xử lý trong <see cref="Done"/> (0 = chưa ghi nhận).</summary>
    public int MinRow { get; set; }
    public int MaxRow { get; set; }

    /// <summary>Xem <see cref="LedgerStatus"/> — client đẩy thẳng chuỗi này lên sổ hoàn thành của Hub.</summary>
    public string Status { get; set; } = LedgerStatus.Idle;
    public DateTimeOffset? LastRunAt { get; set; }
}

/// <summary>
/// Kho tiến độ per-SP dùng chung, lưu tại %AppData%\ShopeeSuite\shared\op-progress.json. Khoá theo
/// (AccountId BigSeller + Sheet + Op). Thread-safe; lưu nguyên tử (file tạm → move) — bền với kill giữa chừng.
/// Mẫu y hệt <see cref="Scrape.ScrapeProgressStore"/> nhưng đơn vị là ITEM ID Shopee thay vì khoảng dòng.
/// </summary>
public sealed class OpProgressStore
{
    private static readonly Lazy<OpProgressStore> _shared = new(() => new OpProgressStore());
    public static OpProgressStore Shared => _shared.Value;

    private static readonly string FilePath = Path.Combine(SuitePaths.ModuleDir("shared"), "op-progress.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static DateTime GetFileWriteUtc() => File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;

    private readonly object _lock = new();
    private readonly List<OpProgress> _items = [];
    private DateTime _lastWriteUtc;

    public event Action? Changed;

    private OpProgressStore() => Load();

    private static bool KeyEq(OpProgress p, string accountId, string sheet, string op) =>
        p.AccountId == accountId
        && string.Equals(p.Sheet ?? "", sheet ?? "", StringComparison.OrdinalIgnoreCase)
        && string.Equals(p.Op ?? "", op ?? "", StringComparison.Ordinal);

    public void Load()
    {
        lock (_lock)
        {
            _items.Clear();
            var list = JsonAtomicFile.TryLoad<List<OpProgress>>(FilePath);
            if (list is not null) _items.AddRange(list);
            _lastWriteUtc = GetFileWriteUtc();
        }
    }

    private void SaveLocked()
    {
        JsonAtomicFile.Save(FilePath, _items, JsonOpts);
        _lastWriteUtc = GetFileWriteUtc();
    }

    public bool ReloadIfExternalChange()
    {
        lock (_lock)
        {
            var diskUtc = GetFileWriteUtc();
            if (diskUtc == _lastWriteUtc) return false;
            Load();
        }
        Changed?.Invoke();
        return true;
    }

    private OpProgress GetOrCreateLocked(string accountId, string sheet, string op, string accountName)
    {
        var p = _items.FirstOrDefault(x => KeyEq(x, accountId, sheet, op));
        if (p is null)
        {
            p = new OpProgress { AccountId = accountId, Sheet = sheet ?? "", Op = op ?? "", AccountName = accountName };
            _items.Add(p);
        }
        if (!string.IsNullOrWhiteSpace(accountName)) p.AccountName = accountName;
        return p;
    }

    /// <summary>Bản sao (đọc) các itemId đã xong của (acc + sheet + op) — rỗng nếu chưa có tiến độ.</summary>
    public IReadOnlyDictionary<string, string?> GetDone(string accountId, string sheet, string op)
    {
        lock (_lock)
        {
            var p = _items.FirstOrDefault(x => KeyEq(x, accountId, sheet, op));
            return p is null
                ? new Dictionary<string, string?>(StringComparer.Ordinal)
                : new Dictionary<string, string?>(p.Done, StringComparer.Ordinal);
        }
    }

    /// <summary>Lấy tóm tắt tiến độ của shop theo danh sách tên sheet ứng viên (ưu tiên bản ghi completed có dữ liệu).</summary>
    public OpProgressSummary? TryGetSummary(string accountId, IEnumerable<string?> candidateSheets, string op)
    {
        lock (_lock)
        {
            OpProgress? fallback = null;
            foreach (var sheet in candidateSheets)
            {
                if (string.IsNullOrWhiteSpace(sheet)) continue;
                var p = _items.FirstOrDefault(x => KeyEq(x, accountId, sheet, op));
                if (p is null) continue;
                if (string.Equals(p.Status, LedgerStatus.Completed, StringComparison.OrdinalIgnoreCase) && p.Done.Count > 0)
                    return new OpProgressSummary(p.Sheet, p.Status, p.Done.Count, p.MinRow, p.MaxRow);
                fallback ??= p;
            }
            return fallback is null
                ? null
                : new OpProgressSummary(fallback.Sheet, fallback.Status, fallback.Done.Count, fallback.MinRow, fallback.MaxRow);
        }
    }

    /// <summary>Ghi nhận N itemId ĐÃ xong (import: value=null; update: value=tên đã điền) kèm khoảng dòng Excel (nếu có).
    /// GHI FILE NGAY để bền với kill giữa chừng. Idempotent — ghi lại cùng itemId chỉ cập nhật giá trị.
    /// Import thêm id MỚI vào khoá mà Update cùng khoá đang "completed" thì Update đó về "stopped" (xem <see cref="ApplyDone"/>).</summary>
    public void MarkDone(string accountId, string sheet, string op, IEnumerable<KeyValuePair<string, string?>> items, int minRow = 0, int maxRow = 0)
    {
        var list = items as ICollection<KeyValuePair<string, string?>> ?? items.ToList();
        if (list.Count == 0 && minRow <= 0 && maxRow <= 0) return;
        lock (_lock)
        {
            var p = GetOrCreateLocked(accountId, sheet, op, "");
            ApplyDone(_items, p, list, minRow, maxRow);
            SaveLocked();
        }
        Changed?.Invoke();
    }

    /// <summary>Phần thuần của <see cref="MarkDone"/> (test trực tiếp, không đụng file): ghi <paramref name="list"/> vào <paramref name="p"/>. Op Import mà
    /// thêm ít nhất 1 id MỚI (chưa có trong Done) vào khoá mà Update cùng khoá (cùng tài khoản, tên sheet không phân biệt hoa thường) đang "completed"
    /// → Update đó về "stopped": shop đó không còn "đã Update đủ" nữa, và <see cref="ImportIdsMissingUpdate"/> không còn bỏ qua khoá này nên Clear của
    /// shop khác không làm mất SP mới (ca: shop xong Update, Import thủ công thêm SP vào cùng khoá). Id đã có thì không đụng gì tới Update.</summary>
    internal static void ApplyDone(IReadOnlyList<OpProgress> items, OpProgress p, ICollection<KeyValuePair<string, string?>> list, int minRow, int maxRow)
    {
        var addedNew = false;
        foreach (var kv in list)
        {
            if (string.IsNullOrEmpty(kv.Key)) continue;
            if (!p.Done.ContainsKey(kv.Key)) addedNew = true;
            p.Done[kv.Key] = kv.Value;
        }
        if (minRow > 0 && (p.MinRow <= 0 || minRow < p.MinRow)) p.MinRow = minRow;
        if (maxRow > 0 && maxRow > p.MaxRow) p.MaxRow = maxRow;
        if (!addedNew || !string.Equals(p.Op ?? "", AssignmentOps.Import, StringComparison.Ordinal)) return;
        var upd = items.FirstOrDefault(x => KeyEq(x, p.AccountId, p.Sheet, AssignmentOps.Update));
        if (upd is not null) StopIfCompleted(upd);
    }

    /// <summary>Cập nhật khoảng dòng (MinRow..MaxRow) đã đối chiếu từ workbook cho bản ghi sẵn có.</summary>
    public void SetRowRange(string accountId, string sheet, string op, int minRow, int maxRow)
    {
        if (minRow <= 0 || maxRow <= 0) return;
        var changed = false;
        lock (_lock)
        {
            var p = _items.FirstOrDefault(x => KeyEq(x, accountId, sheet, op));
            if (p is null) return;
            if (p.MinRow <= 0 || minRow < p.MinRow) { p.MinRow = minRow; changed = true; }
            if (maxRow > p.MaxRow) { p.MaxRow = maxRow; changed = true; }
            if (changed) SaveLocked();
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>Bắt đầu lượt CHẠY: đặt trạng thái running (GIỮ NGUYÊN Done — resume tiếp phần còn thiếu). Reset về
    /// đầu là việc riêng của <see cref="Clear"/>.</summary>
    public void BeginRun(string accountId, string sheet, string op, string accountName)
    {
        lock (_lock)
        {
            var p = GetOrCreateLocked(accountId, sheet, op, accountName);
            p.Status = LedgerStatus.Running;
            p.LastRunAt = DateTimeOffset.Now;
            SaveLocked();
        }
        Changed?.Invoke();
    }

    /// <summary>Đánh dấu hoàn thành thủ công (Mark as done) cho (acc + sheet + op) — tự tạo bản ghi nếu chưa có.</summary>
    public void MarkCompleted(string accountId, string sheet, string op, string accountName = "")
    {
        lock (_lock)
        {
            var p = GetOrCreateLocked(accountId, sheet, op, accountName);
            p.Status = LedgerStatus.Completed;
            p.LastRunAt ??= DateTimeOffset.Now;
            SaveLocked();
        }
        Changed?.Invoke();
    }

    /// <summary>Kết thúc lượt chạy: completed (chạy trọn) hoặc stopped (bị hủy giữa chừng). GIỮ NGUYÊN Done.</summary>
    public void FinishRun(string accountId, string sheet, string op, bool completed)
    {
        lock (_lock)
        {
            var p = completed
                ? GetOrCreateLocked(accountId, sheet, op, "")
                : _items.FirstOrDefault(x => KeyEq(x, accountId, sheet, op));
            if (p is null) return;
            p.Status = completed ? LedgerStatus.Completed : LedgerStatus.Stopped;
            SaveLocked();
        }
        Changed?.Invoke();
    }

    /// <summary>Sau khi BigSeller "Clear" toàn bộ nháp của tài khoản: với các khoá tiến độ (sheet) của <paramref name="accountId"/> mà
    /// CALLER đã dựng được tập "mục tiêu Update" (<paramref name="updateTargetsByKey"/>: khoá → item id mà luồng Update sẽ xử lý, tức dòng
    /// workbook có tên đã sửa), bỏ khỏi Done của Import mọi id THUỘC tập mục tiêu mà KHÔNG có trong Done của Update cùng khoá (SP đã
    /// import mà chưa Update xong = nháp vừa bị xoá → phải import lại). Id ngoài tập mục tiêu không bao giờ có Update Done nên không bị
    /// bỏ (kẻo import lại mỗi lượt). Khoá mà Update cùng khoá đã "completed" (shop xong hoặc user đã bấm Next shop) thì giữ nguyên.
    /// Khoá có id thiếu Update Done nhưng KHÔNG có trong từ điển (vd caller không đọc được sheet) → không bỏ id nào, tên khoá trả ở
    /// <paramref name="skippedKeys"/> để log; <c>updateTargetsByKey = null</c> coi như từ điển rỗng. Update Done và tài khoản khác không
    /// bị đụng. Khoá bị bỏ ít nhất 1 id thì trạng thái "completed" về "stopped" (không còn là "đủ"). <paramref name="shopKeys"/> = các
    /// khoá tiến độ mà CALLER biết thuộc shop đang chạy (store không suy ra được quan hệ khoá ↔ shop; UI xét "Import xong" trên nhiều khoá
    /// ứng viên của shop): nếu shop đó có khoá THẬT SỰ bị bỏ id thì mọi khoá Import "completed" trong danh sách này cũng về "stopped".
    /// <paramref name="ignoreSharedKeys"/> = true (workbook chung đang TẮT): khoá <c>shared:...</c> là dữ liệu cũ, không còn thuộc luồng nào → không bỏ id,
    /// không nằm trong <paramref name="skippedKeys"/>.
    /// GHI FILE NGAY (nguyên tử), thread-safe. Trả số id đã bỏ (cả tài khoản); <paramref name="sheets"/> = chi tiết theo khoá chỉ gồm khoá
    /// có id bị bỏ.</summary>
    public int ResetImportWithoutUpdate(string accountId, out IReadOnlyList<ImportResetSheet> sheets, out IReadOnlyList<string> skippedKeys,
        IReadOnlyCollection<string>? shopKeys = null, IReadOnlyDictionary<string, IReadOnlySet<string>>? updateTargetsByKey = null,
        bool ignoreSharedKeys = false)
    {
        List<ImportResetSheet> changed;
        List<string> skipped;
        lock (_lock)
        {
            changed = RemoveImportWithoutUpdate(_items, accountId, updateTargetsByKey, out skipped, shopKeys, ignoreSharedKeys);
            if (changed.Count > 0) SaveLocked();
        }
        sheets = changed;
        skippedKeys = skipped;
        var total = changed.Sum(s => s.Removed);
        if (total > 0) Changed?.Invoke();
        return total;
    }

    /// <summary>Các khoá Import của <paramref name="accountId"/> mà <see cref="ResetImportWithoutUpdate"/> CÓ THỂ bỏ id: còn id ở Import
    /// Done thiếu Update Done và Update cùng khoá chưa "completed". Caller đọc sheet của đúng các khoá này để dựng tập mục tiêu Update
    /// (không phải đọc cả workbook). <paramref name="ignoreSharedKeys"/> = true (workbook chung đang TẮT): bỏ khoá <c>shared:...</c> (dữ liệu cũ).</summary>
    public IReadOnlyList<string> ImportKeysPendingUpdate(string accountId, bool ignoreSharedKeys = false)
    {
        lock (_lock) return FindImportKeysPendingUpdate(_items, accountId, ignoreSharedKeys);
    }

    /// <summary>Số id mà <see cref="ResetImportWithoutUpdate"/> sẽ bỏ ở khoá (<paramref name="accountId"/>, <paramref name="sheet"/>) với tập mục tiêu
    /// <paramref name="targets"/> (null = không biết mục tiêu → 0, vì reset không bỏ gì).</summary>
    public int CountImportRemovable(string accountId, string sheet, IReadOnlySet<string>? targets)
    {
        lock (_lock) return CountImportRemovable(_items, accountId, sheet, targets);
    }

    /// <summary>Phần thuần của <see cref="CountImportRemovable(string, string, IReadOnlySet{string}?)"/> (test trực tiếp).</summary>
    internal static int CountImportRemovable(IReadOnlyList<OpProgress> items, string accountId, string sheet, IReadOnlySet<string>? targets)
    {
        if (targets is null) return 0;
        var imp = items.FirstOrDefault(x => KeyEq(x, accountId, sheet, AssignmentOps.Import));
        return imp is null ? 0 : ImportIdsMissingUpdate(items, imp, accountId).Count(targets.Contains);
    }

    /// <summary>Phần thuần của <see cref="ImportKeysPendingUpdate"/> (test trực tiếp).</summary>
    internal static List<string> FindImportKeysPendingUpdate(IReadOnlyList<OpProgress> items, string accountId, bool ignoreSharedKeys = false)
    {
        var keys = new List<string>();
        foreach (var imp in items)
        {
            if (imp.AccountId != accountId || !string.Equals(imp.Op ?? "", AssignmentOps.Import, StringComparison.Ordinal)) continue;
            if (ignoreSharedKeys && IsSharedProgressKey(imp.Sheet)) continue;
            if (ImportIdsMissingUpdate(items, imp, accountId).Count == 0) continue;
            if (!keys.Contains(imp.Sheet, StringComparer.OrdinalIgnoreCase)) keys.Add(imp.Sheet);
        }
        return keys;
    }

    // Id của khoá Import chưa có Update Done (cùng khoá). Rỗng khi Update cùng khoá đã "completed": shop đã xong / user đã bấm Next shop,
    // id còn thiếu ở đó là dòng không phải mục tiêu, bỏ chúng sẽ làm vòng lặp giữa các shop import lại mãi.
    private static List<string> ImportIdsMissingUpdate(IReadOnlyList<OpProgress> items, OpProgress imp, string accountId)
    {
        var upd = items.FirstOrDefault(x => KeyEq(x, accountId, imp.Sheet, AssignmentOps.Update));
        if (upd is not null && string.Equals(upd.Status, LedgerStatus.Completed, StringComparison.OrdinalIgnoreCase)) return new List<string>();
        return imp.Done.Keys.Where(id => upd is null || !upd.Done.ContainsKey(id)).ToList();
    }

    /// <summary>Khoá tiến độ dạng <c>shared:{shopId}</c> (workbook chung). Chỉ sinh ra khi workbook chung BẬT (<c>SharedWorkbookCatalog.ResolveForUpdate</c>);
    /// khi tắt, khoá này là dữ liệu cũ.</summary>
    internal static bool IsSharedProgressKey(string? sheet) =>
        !string.IsNullOrEmpty(sheet) && sheet.StartsWith(Shopee.Core.BigSeller.SharedWorkbookCatalog.ProgressPrefix, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlySet<string>? FindTargets(IReadOnlyDictionary<string, IReadOnlySet<string>>? byKey, string? sheet)
    {
        if (byKey is null) return null;
        foreach (var kv in byKey)
            if (string.Equals(kv.Key, sheet ?? "", StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return null;
    }

    /// <summary>Phần thuần của <see cref="ResetImportWithoutUpdate"/> (test trực tiếp, không đụng file): sửa tại chỗ danh sách
    /// <paramref name="items"/>. Khoá Import ghép với khoá Update CÙNG sheet (so sánh như <see cref="KeyEq"/>); không có khoá Update
    /// nghĩa là Update Done rỗng. Khoá có trong <paramref name="updateTargetsByKey"/> (so khoá không phân biệt hoa thường) thì chỉ bỏ id
    /// thuộc tập mục tiêu và chưa có Update Done; khoá không có trong từ điển (hoặc từ điển null) thì không bỏ gì và, nếu có id thiếu Update
    /// Done, được ghi vào <paramref name="skippedKeys"/>. Update cùng khoá "completed" → không bỏ gì. Chỉ khoá của
    /// <paramref name="accountId"/>. Khoá bị bỏ id mà đang "completed" → "stopped"; có khoá nằm trong <paramref name="shopKeys"/> bị bỏ id
    /// thì các khoá Import "completed" khác trong <paramref name="shopKeys"/> (kể cả khoá chẳng có id nào, vd đánh dấu tay) cũng →
    /// "stopped", để UI không còn thấy shop "xong". <paramref name="ignoreSharedKeys"/> = true: khoá <c>shared:...</c> không được xét (không bỏ id,
    /// không vào <paramref name="skippedKeys"/>).</summary>
    internal static List<ImportResetSheet> RemoveImportWithoutUpdate(
        IReadOnlyList<OpProgress> items, string accountId, IReadOnlyDictionary<string, IReadOnlySet<string>>? updateTargetsByKey,
        out List<string> skippedKeys, IReadOnlyCollection<string>? shopKeys = null, bool ignoreSharedKeys = false)
    {
        var result = new List<ImportResetSheet>();
        skippedKeys = new List<string>();
        var shopKeyTouched = false;
        foreach (var imp in items)
        {
            if (imp.AccountId != accountId || !string.Equals(imp.Op ?? "", AssignmentOps.Import, StringComparison.Ordinal)) continue;
            if (ignoreSharedKeys && IsSharedProgressKey(imp.Sheet)) continue;
            var missing = ImportIdsMissingUpdate(items, imp, accountId);
            if (missing.Count == 0) continue;
            var targets = FindTargets(updateTargetsByKey, imp.Sheet);
            if (targets is null)
            {
                if (!skippedKeys.Contains(imp.Sheet, StringComparer.OrdinalIgnoreCase)) skippedKeys.Add(imp.Sheet);
                continue;
            }
            var toRemove = missing.Where(targets.Contains).ToList();
            if (toRemove.Count == 0) continue;
            foreach (var id in toRemove) imp.Done.Remove(id);
            StopIfCompleted(imp);
            result.Add(new ImportResetSheet(imp.Sheet, toRemove.Count));
            if (IsShopKey(imp.Sheet, shopKeys)) shopKeyTouched = true;
        }
        if (shopKeyTouched)
            foreach (var imp in items)
                if (imp.AccountId == accountId && string.Equals(imp.Op ?? "", AssignmentOps.Import, StringComparison.Ordinal)
                    && IsShopKey(imp.Sheet, shopKeys))
                    StopIfCompleted(imp);
        return result;
    }

    private static void StopIfCompleted(OpProgress p)
    {
        if (string.Equals(p.Status, LedgerStatus.Completed, StringComparison.OrdinalIgnoreCase)) p.Status = LedgerStatus.Stopped;
    }

    private static bool IsShopKey(string? sheet, IReadOnlyCollection<string>? shopKeys) =>
        shopKeys is not null && shopKeys.Any(k => !string.IsNullOrWhiteSpace(k) && string.Equals(k, sheet ?? "", StringComparison.OrdinalIgnoreCase));

    /// <summary>Xoá hẳn tiến độ của 1 (acc + sheet + op) — cho nút "Chạy lại từ đầu" (làm lại toàn bộ SP).</summary>
    public void Clear(string accountId, string sheet, string op)
    {
        lock (_lock)
        {
            _items.RemoveAll(x => KeyEq(x, accountId, sheet, op));
            SaveLocked();
        }
        Changed?.Invoke();
    }

    /// <summary>Xoá toàn bộ tiến độ của 1 shop (mọi op) cho accountId + sheet.</summary>
    public void ClearAllOps(string accountId, string sheet)
    {
        lock (_lock)
        {
            _items.RemoveAll(x => string.Equals(x.AccountId, accountId, StringComparison.OrdinalIgnoreCase)
                               && string.Equals(x.Sheet, sheet, StringComparison.OrdinalIgnoreCase));
            SaveLocked();
        }
        Changed?.Invoke();
    }

    /// <summary>Danh sách gọn (acc, sheet, op, status) mọi key đang có — cho UI liệt kê tiến độ.</summary>
    public List<(string acc, string sheet, string op, string status)> Snapshot()
    {
        lock (_lock)
            return _items.Select(p => (p.AccountId, p.Sheet, p.Op, p.Status)).ToList();
    }
}
