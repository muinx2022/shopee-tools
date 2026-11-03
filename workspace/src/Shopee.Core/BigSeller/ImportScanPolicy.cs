namespace Shopee.Core.BigSeller;

/// <summary>Việc kế tiếp sau một lô Import trên Crawl List. Tab «Tất cả»: SP vừa import vẫn nằm trên trang → luôn bấm Next
/// (không ở lại xét lại). <see cref="RecheckSamePage"/> giữ để tương thích tên cũ / test cũ nhưng policy hiện tại không trả về.</summary>
public enum AfterBatchStep { RecheckSamePage, ClickNext }

/// <summary>Quyết định trước khi Clear nháp: <see cref="CanClear"/> = được Clear; ngược lại <see cref="UnknownKeys"/> = các khoá còn SP chờ Update
/// mà không biết mục tiêu Update (Clear sẽ xoá nháp của chúng mà bảng import không biết SP nào phải import lại).</summary>
public readonly record struct ClearGate(bool CanClear, IReadOnlyList<string> UnknownKeys);

/// <summary>Việc runner làm trước khi mở menu Clear: <see cref="ExitEarly"/> = kết thúc import shop mà không Clear (xem <see cref="ImportExitReason"/>);
/// <see cref="Blocked"/> = cổng Clear chặn (<see cref="ImportStartDecision.BlockedKeys"/>); <see cref="Clear"/> = đi tiếp Clear rồi import.</summary>
public enum ImportStartKind { ExitEarly, Blocked, Clear }

/// <summary>Vì sao thoát sớm: <see cref="NoItemIds"/> = sheet không có item id nào trong khoảng dòng; <see cref="AllDone"/> = mọi id đã import và Clear
/// không làm mất id nào của shop (mọi SP đã import đều đã Update, hoặc không phải mục tiêu Update).</summary>
public enum ImportExitReason { None, NoItemIds, AllDone }

/// <summary>Kết quả <see cref="ImportScanPolicy.DecideImportStart"/>. <see cref="BlockedKeys"/> chỉ có nghĩa khi <see cref="Kind"/> = Blocked.</summary>
public readonly record struct ImportStartDecision(ImportStartKind Kind, ImportExitReason ExitReason, IReadOnlyList<string> BlockedKeys);

/// <summary>
/// Quyết định thuần của lượt Import to Store (BigSeller) để test được, không dính Playwright:
/// có quét thêm vòng không, trang crawl đã "xác nhận" (DOM khớp response pageList.json mới) chưa, và khi nào bỏ phần còn lại của tab.
/// </summary>
public static class ImportScanPolicy
{
    /// <summary>Số vòng quét tối đa (mỗi vòng = quét tab «Tất cả» từ trang 1).</summary>
    public const int MaxPasses = 3;

    /// <summary>Số trang LIÊN TIẾP vừa chưa xác nhận vừa không có SP khớp thì bỏ phần còn lại của tab (phanh).</summary>
    public const int MaxUnconfirmedNoMatchPages = 3;

    /// <summary>Có sang vòng quét mới không. Chỉ khi: chưa đủ, còn dưới <paramref name="maxPasses"/>, VÀ vòng vừa rồi
    /// có lô đã gửi (danh sách đã đổi, SP có thể dồn trang) HOẶC có tab/trang CHƯA XÁC NHẬN (có thể sót).
    /// Vòng không gửi gì và không có gì chưa xác nhận → quét lại cũng không ra thêm gì → dừng.</summary>
    public static bool ShouldRescan(int pass, int maxPasses, bool allImported, bool sentThisPass, bool hasUnconfirmed) =>
        !allImported && pass < maxPasses && (sentThisPass || hasUnconfirmed);

    /// <summary>Giữ hằng số cũ (tab Chưa nhận/Đã nhận từng dùng ở lại trang); tab «Tất cả» không dùng RecheckSamePage.</summary>
    public const int MaxSamePageRechecks = 200;

    /// <summary>Sau một lô trên tab «Tất cả»: luôn bấm Next. SP import xong vẫn nằm trong «Tất cả» → ở lại trang không dồn SP mới lên.
    /// Tham số giữ chữ ký cũ (caller/test) nhưng không ảnh hưởng kết quả.</summary>
    public static AfterBatchStep NextStepAfterBatch(bool batchCommitted, int samePageRechecks)
    {
        _ = batchCommitted;
        _ = samePageRechecks;
        return AfterBatchStep.ClickNext;
    }

    /// <summary>"Đã import đủ": có id cần import, mọi id đã import, VÀ bước Clear nháp đã chốt (Cleared / không có gì để xoá / bảng import đã
    /// reset trọn vẹn — <paramref name="clearSettled"/>). Clear kết thúc kiểu khác (hết giờ chờ, reset lỗi) thì tiến độ trong RAM có thể lệch
    /// với bảng import đã bị reset nên không được coi là đủ, dù đếm id có khớp.</summary>
    public static bool IsAllImported(int importIdCount, int importedIdCount, bool clearSettled) =>
        clearSettled && importIdCount > 0 && importedIdCount >= importIdCount;

    /// <summary>Có được Clear nháp không. Clear xoá nháp của CẢ tài khoản, nên mọi khoá có SP đã import mà chưa Update xong
    /// (<paramref name="pendingUpdateKeys"/>, từ <c>OpProgressStore.ImportKeysPendingUpdate</c>) phải biết mục tiêu Update
    /// (<paramref name="keysWithTargets"/>: khoá đọc được mục tiêu) thì bảng import mới reset đúng. Còn khoá chờ Update mà không biết mục tiêu
    /// (khoá <c>shared:...</c> của shop khác, sheet không có trong file nguồn, file không đọc được) → KHÔNG Clear, kèm danh sách khoá thiếu
    /// (không phân biệt hoa thường, bỏ rỗng/trùng, giữ thứ tự). Không có khoá nào chờ Update → được Clear.</summary>
    public static ClearGate DecideClearGate(IEnumerable<string?> pendingUpdateKeys, IEnumerable<string?> keysWithTargets)
    {
        var known = new HashSet<string>(keysWithTargets.Where(k => !string.IsNullOrWhiteSpace(k))!, StringComparer.OrdinalIgnoreCase);
        var unknown = new List<string>();
        foreach (var key in pendingUpdateKeys)
            if (!string.IsNullOrWhiteSpace(key) && !known.Contains(key) && !unknown.Contains(key, StringComparer.OrdinalIgnoreCase))
                unknown.Add(key);
        return new ClearGate(unknown.Count == 0, unknown);
    }

    /// <summary>Sheet không có item id nào trong khoảng dòng và không phải hub-mode còn SP chờ Update (Hub đã lọc SP đã import nên tập id rỗng chưa chắc là hết việc).
    /// Runner kiểm điều này TRƯỚC khi đọc mục tiêu Update (đọc file lớn) để khỏi đọc vô ích.</summary>
    public static bool NothingToImport(int importIdCount, bool hubHasPendingUpdate) => importIdCount <= 0 && !hubHasPendingUpdate;

    /// <summary>Điều phối đầu lượt import một shop (trước khi mở menu Clear), thuần để test: thoát sớm / bị cổng Clear chặn / đi tiếp Clear.
    /// Gọi SAU khi đã đọc mục tiêu Update của <paramref name="pendingKeys"/> (<paramref name="keysWithTargets"/> = khoá đọc được).
    /// <list type="number">
    /// <item>Sheet không có item id (<paramref name="importIdCount"/> = 0) và không phải hub-mode còn SP chờ Update (<paramref name="hubHasPendingUpdate"/>) → thoát sớm (NoItemIds).</item>
    /// <item>Shop đã import đủ (<paramref name="importedCount"/> ≥ <paramref name="importIdCount"/>): nếu khoá của shop (<paramref name="shopKey"/>) KHÔNG có trong
    /// <paramref name="pendingKeys"/> → Clear không làm mất id nào của shop → thoát sớm (AllDone), kể cả khi khoá của shop khác không đọc được. Khoá của shop có trong
    /// <paramref name="pendingKeys"/> mà không đọc được mục tiêu → KHÔNG được nói "đã Update hết": chặn. Đọc được mà <paramref name="shopRemovableCount"/> = 0 (không id nào
    /// Clear sẽ bỏ) → thoát sớm (AllDone).</item>
    /// <item>Còn lại: <see cref="DecideClearGate"/> — có khoá chờ Update mà không biết mục tiêu → chặn (kèm khoá), ngược lại → Clear.</item>
    /// </list>
    /// <paramref name="pendingKeys"/> phải là danh sách ĐÃ lọc (workbook chung tắt thì khoá <c>shared:...</c> cũ không có trong đó).</summary>
    public static ImportStartDecision DecideImportStart(
        int importIdCount, int importedCount, bool hubHasPendingUpdate, string? shopKey, int shopRemovableCount,
        IReadOnlyCollection<string?> pendingKeys, IReadOnlyCollection<string?> keysWithTargets)
    {
        if (NothingToImport(importIdCount, hubHasPendingUpdate))
            return new ImportStartDecision(ImportStartKind.ExitEarly, ImportExitReason.NoItemIds, Array.Empty<string>());

        var gate = DecideClearGate(pendingKeys, keysWithTargets);
        var allSeeded = importIdCount > 0 && importedCount >= importIdCount;
        if (allSeeded)
        {
            var shopWaiting = !string.IsNullOrWhiteSpace(shopKey)
                && pendingKeys.Any(k => string.Equals(k, shopKey, StringComparison.OrdinalIgnoreCase));
            var shopKnown = !string.IsNullOrWhiteSpace(shopKey)
                && keysWithTargets.Any(k => string.Equals(k, shopKey, StringComparison.OrdinalIgnoreCase));
            if (!shopWaiting || (shopKnown && shopRemovableCount == 0))
                return new ImportStartDecision(ImportStartKind.ExitEarly, ImportExitReason.AllDone, Array.Empty<string>());
            if (!shopKnown)
                return new ImportStartDecision(ImportStartKind.Blocked, ImportExitReason.None, gate.UnknownKeys);
        }
        return gate.CanClear
            ? new ImportStartDecision(ImportStartKind.Clear, ImportExitReason.None, Array.Empty<string>())
            : new ImportStartDecision(ImportStartKind.Blocked, ImportExitReason.None, gate.UnknownKeys);
    }

    /// <summary>Tên để log cho khoá tiến độ: khoá <c>shared:{shopId}</c> mà tìm được shop theo id trong <paramref name="shops"/> (<c>account.Shops</c>) thì kèm
    /// tên shop; không tìm được (hoặc khoá là tên sheet riêng) thì giữ nguyên khoá (khoá <c>shared:</c> đã chứa id).</summary>
    public static string DescribeProgressKey(string key, IEnumerable<BigSellerShop>? shops)
    {
        var id = SharedWorkbookCatalog.ShopIdFromKey(key);
        if (id is null || shops is null) return key;
        var name = shops.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))?.Name;
        return string.IsNullOrWhiteSpace(name) ? key : $"{key} (shop '{name.Trim()}')";
    }

    /// <summary>Trang crawl đã được XÁC NHẬN chưa: có response pageList.json MỚI hơn mốc
    /// (<paramref name="newerResponse"/>) đã đọc được (<paramref name="responseKeys"/> ≠ null) VÀ ảnh trên DOM thuộc đúng
    /// response đó. Response rỗng thì DOM phải không còn dòng nào. Chưa đủ điều kiện → chưa được kết luận
    /// "0 khớp / đã quét sạch" (DOM có thể còn dòng trang cũ hoặc đang tải). Xác nhận KHÔNG chặn việc tick SP khớp:
    /// tick theo map ảnh→id vốn đã đúng SP.</summary>
    public static bool IsPageConfirmed(
        bool newerResponse, IReadOnlyCollection<string>? responseKeys, int domRowCount, IReadOnlyCollection<string> domImageKeys)
    {
        if (!newerResponse || responseKeys is null) return false;
        if (responseKeys.Count == 0) return domRowCount == 0;
        if (domImageKeys.Count == 0) return false;
        foreach (var key in domImageKeys)
            if (!responseKeys.Contains(key)) return false;
        return true;
    }

    /// <summary>Trang THẬT SỰ không có dữ liệu mới: không có response mới hơn mốc, HOẶC response không đọc được (null),
    /// HOẶC không khoá ảnh DOM nào thuộc response. Có giao nhau với response (chỉ lạ vài ảnh) thì KHÔNG phải — đó là
    /// trang có dữ liệu nhưng chưa xác nhận tròn vẹn.</summary>
    public static bool HasNoNewPageData(
        bool newerResponse, IReadOnlyCollection<string>? responseKeys, IReadOnlyCollection<string> domImageKeys)
    {
        if (!newerResponse || responseKeys is null) return true;
        foreach (var key in domImageKeys)
            if (responseKeys.Contains(key)) return false;
        return true;
    }

    /// <summary>Số trang liên tiếp "chưa xác nhận, không có SP khớp VÀ không có dữ liệu trang mới" sau trang vừa quét:
    /// trang đã xác nhận, có SP khớp, hoặc có dữ liệu trang mới (<paramref name="noNewPageData"/> = false, vd chỉ lạ vài ảnh)
    /// thì về 0; ngược lại +1.</summary>
    public static int NextUnconfirmedNoMatchStreak(int streak, bool pageConfirmed, bool hadMatch, bool noNewPageData) =>
        pageConfirmed || hadMatch || !noNewPageData ? 0 : streak + 1;
    /// <summary>Phanh: đủ <see cref="MaxUnconfirmedNoMatchPages"/> trang liên tiếp chưa xác nhận mà không có SP khớp →
    /// bỏ phần còn lại của tab (vòng sau thử lại), khỏi mất ~17s/trang cho cả tab.</summary>
    public static bool ShouldAbandonTab(int unconfirmedNoMatchStreak) =>
        unconfirmedNoMatchStreak >= MaxUnconfirmedNoMatchPages;

    /// <summary>Mô tả vì sao trang chưa xác nhận (để log): có response mới không, số khoá response (null = đọc
    /// không được), số dòng, số khoá ảnh DOM, tối đa <paramref name="maxForeign"/> khoá DOM không thuộc response.</summary>
    public static string DescribeUnconfirmed(
        bool newerResponse, IReadOnlyCollection<string>? responseKeys, int domRowCount,
        IReadOnlyCollection<string> domImageKeys, int maxForeign = 5)
    {
        var foreign = responseKeys is null
            ? domImageKeys.Take(maxForeign).ToList()
            : domImageKeys.Where(k => !responseKeys.Contains(k)).Take(maxForeign).ToList();
        return $"newer={newerResponse}, khoá response={(responseKeys is null ? "null" : responseKeys.Count.ToString())}, " +
               $"dòng DOM={domRowCount}, khoá ảnh DOM={domImageKeys.Count}, khoá lạ (≤{maxForeign})=[{string.Join(", ", foreign)}]";
    }
}
