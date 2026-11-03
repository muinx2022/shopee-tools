using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace UpdateProduct;

/// <summary>Kết cục "Clear nháp": NothingToClear = không có gì để xoá (không thấy mục Clear / hộp xác nhận không hiện, VÀ bảng nháp rỗng),
/// chưa xoá gì và KHÔNG reset bảng import; Cleared = đã Confirm và nháp về 0; Aborted = dừng TRƯỚC khi Confirm có hiệu lực (chưa xoá gì,
/// trạng thái lạ; nếu bảng import đã reset trước Confirm thì Message nói rõ: đã reset bảng nhưng chưa chắc đã xoá nháp); TimedOut = đã
/// Confirm (không dừng được) nhưng hết giờ chờ nháp về 0.</summary>
internal enum DraftClearStatus { NothingToClear, Cleared, Aborted, TimedOut }

/// <summary>Kết quả dò menu Bulk Actions để tìm mục "Clear".</summary>
internal enum ClearMenuProbe { ButtonMissing, MenuNotOpened, NoClearItem, ClearDisabled, Found }

internal sealed record DraftClearResult(DraftClearStatus Status, string Message);

/// <summary>
/// Xoá sạch nháp BigSeller (Bulk Actions → Clear → Confirm) của TÀI KHOẢN đang đăng nhập, chạy TRƯỚC import mỗi shop.
/// Thao tác trang gom hết ở đây (selector + text một chỗ); phần quyết định thuần là các hàm <c>internal static</c> để test.
/// Hợp đồng DOM (bản tiếng Anh, từ DOM user dán): nút <c>.dropdown_box button.ant-btn-primary</c> chữ "Bulk Actions" (hover mở menu) →
/// <c>.ant-dropdown:visible li.ant-dropdown-menu-item</c> chữ "Clear" → hộp <c>.ant-modal-confirm</c> có câu "...clearing all filtered
/// products..." → nút <c>.ant-modal-confirm-btns button.ant-btn-primary</c> chữ "Confirm".
/// LUÔN thử Clear, kể cả khi bảng nháp trông rỗng (đếm dòng DOM chỉ dùng để log, và để phân xử khi KHÔNG thấy nút/menu): bảng "trông rỗng"
/// có thể chỉ vì đang tải.
/// Giới hạn: chưa có cách tin cậy đọc TỔNG số nháp (code không có selector phân trang tổng số bản ghi cho trang nháp, cũng không có API
/// response của trang này; nhãn "X / Y" của Listing là số TRANG), nên chờ "về 0" = bảng nháp không có dòng nào ở
/// <see cref="RequiredEmptyReads"/> lần đọc liên tiếp, MỖI lần sau khi tải lại trang và bảng đã sẵn sàng.
/// </summary>
internal sealed class BigSellerDraftClearer
{
    public const string DraftListingUrl = "https://www.bigseller.com/web/listing/shopee/index.htm?bsStatus=1";

    // Bảng nháp + trạng thái rỗng (bản sao ListingReadySelector / ListingRows của BigSellerProductUpdateRunner — cùng trang listing;
    // dòng placeholder của bảng ant bị loại khỏi phép đếm vì nó là <tr> nhưng không phải SP).
    private const string ReadySelector = "tr.product_native_row, tr.vxe-body--row, tbody.ant-table-tbody tr, a.action_btn.addEditProduct, .ant-empty, .ant-table-placeholder, .vxe-table--empty-block, .vxe-table--empty-placeholder";
    private const string RowsSelector = "tr.product_native_row, tr.vxe-body--row, tbody.ant-table-tbody tr:not(.ant-table-placeholder)";

    private const string BulkButtonSelector = ".dropdown_box button.ant-btn-primary";
    private const string BulkButtonText = "Bulk Actions";
    private const string MenuItemSelector = ".ant-dropdown:visible li.ant-dropdown-menu-item";
    private const string ConfirmModalSelector = ".ant-modal-confirm:visible";
    private const string ConfirmButtonsSelector = ".ant-modal-confirm-btns button";
    private const string ConfirmPrimarySelector = ".ant-modal-confirm-btns button.ant-btn-primary";
    private const string ConfirmButtonText = "Confirm";
    private const string ClearAllPhrase = "clearing all filtered products";
    // Toast antd (selector có sẵn trong BigSellerProductUpdateRunner.Selectors): đọc nguyên văn để log khi hộp xác nhận không hiện.
    private const string ToastSelector = "div.ant-message .ant-message-notice-content, .ant-notification-notice";

    private const int PollSeconds = 10;
    public const int WaitEmptyMaxMinutes = 10;
    private const int ConfirmBoxWaitMs = 10000;
    /// <summary>Số lần đọc LIÊN TIẾP thấy 0 dòng (mỗi lần sau reload + bảng sẵn sàng) mới coi nháp đã về 0.</summary>
    public const int RequiredEmptyReads = 3;

    private readonly IPage _page;
    private readonly Action<string> _log;
    private readonly Func<int, CancellationToken, Task> _delay;

    public BigSellerDraftClearer(IPage page, Action<string> log, Func<int, CancellationToken, Task> delay)
    {
        _page = page;
        _log = log;
        _delay = delay;
    }

    // ── Phần thuần (test trực tiếp) ──────────────────────────────────────────────

    private static string Squash(string? text) => Regex.Replace(text ?? "", @"\s+", " ").Trim();

    /// <summary>Hộp confirm có đúng câu của thao tác Clear-tất-cả ("...clearing all filtered products...", không phân biệt hoa thường).
    /// Hộp khác (xoá từng SP, hộp lạ) → false: caller phải Cancel, không đoán.</summary>
    internal static bool IsClearAllConfirmText(string? text) =>
        Squash(text).Contains(ClearAllPhrase, StringComparison.OrdinalIgnoreCase);

    /// <summary>Mục menu đúng là "Clear" (không phải "Clear filter"... — khớp nguyên chữ sau khi gọn khoảng trắng).</summary>
    internal static bool IsClearMenuText(string? text) =>
        string.Equals(Squash(text), "Clear", StringComparison.OrdinalIgnoreCase);

    internal static bool IsConfirmButtonText(string? text) =>
        string.Equals(Squash(text), ConfirmButtonText, StringComparison.OrdinalIgnoreCase);

    /// <summary>Số lần đọc LIÊN TIẾP thấy 0 dòng: 0 dòng thì +1, có dòng thì về 0.</summary>
    internal static int NextZeroStreak(int streak, int rowCount) => rowCount <= 0 ? streak + 1 : 0;

    /// <summary>Rỗng chỉ khi 0 dòng ở <see cref="RequiredEmptyReads"/> lần đọc liên tiếp (một hoặc hai lần 0 có thể là bảng đang tải).</summary>
    internal static bool IsEmptyConfirmed(int zeroStreak) => zeroStreak >= RequiredEmptyReads;

    /// <summary>Không tìm được mục "Clear" để bấm thì kết cục là gì. Menu mở được mà không có "Clear" (BigSeller có thể ẩn mục khi không có nháp,
    /// nhưng cũng có thể đổi menu), không thấy nút Bulk Actions, menu không mở, "Clear" bị khoá: chỉ coi là "không có gì để xoá" khi bảng nháp
    /// cũng rỗng (<paramref name="tableEmpty"/>), còn bảng có dòng thì Aborted (không import vào trạng thái lạ).</summary>
    internal static DraftClearStatus DecideWithoutClearItem(ClearMenuProbe probe, bool tableEmpty) => probe switch
    {
        ClearMenuProbe.NoClearItem or ClearMenuProbe.ButtonMissing or ClearMenuProbe.MenuNotOpened or ClearMenuProbe.ClearDisabled =>
            tableEmpty ? DraftClearStatus.NothingToClear : DraftClearStatus.Aborted,
        _ => DraftClearStatus.Aborted,   // Found: caller không được gọi hàm này
    };

    /// <summary>Bấm Clear mà hộp xác nhận KHÔNG hiện sau thời gian chờ thì kết cục là gì. Chỉ "không có gì để xoá" khi bảng nháp rỗng (số dòng đọc
    /// trước Clear = 0 VÀ đọc lại vẫn rỗng). Bảng còn dòng (đọc trước Clear, hoặc đọc lại) thì hộp vắng mặt là bất thường (mạng chậm, giao diện
    /// đổi...) → Aborted: nháp còn đó mà coi như đã sạch thì import vào trạng thái lạ.</summary>
    internal static DraftClearStatus DecideWhenConfirmBoxMissing(int rowsBeforeClear, bool tableEmptyNow) =>
        rowsBeforeClear > 0 || !tableEmptyNow ? DraftClearStatus.Aborted : DraftClearStatus.NothingToClear;

    /// <summary>Có coi đã Confirm không (bảng import ĐÃ reset từ trước khi bấm Confirm). Click trả về bình thường → đã Confirm. Click ném lỗi:
    /// hộp confirm đã biến mất → vẫn coi là đã Confirm (BigSeller có thể đã nhận; đi tiếp chờ nháp về 0); hộp vẫn hiện → chưa Confirm
    /// (Aborted: bảng đã reset nhưng chưa chắc đã xoá nháp — nháp trùng sẽ bị xoá ở lần Clear sau, còn không reset thì mất SP).</summary>
    internal static bool ConfirmClickCountsAsDone(bool clickThrew, bool confirmBoxStillVisible) => !clickThrew || !confirmBoxStillVisible;

    /// <summary>Gộp các toast đã đọc được (bỏ rỗng/trùng, gọn khoảng trắng) thành một chuỗi log nguyên văn.</summary>
    internal static string MergeToastTexts(IEnumerable<string?> seen) =>
        string.Join(" | ", seen.Select(Squash).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal));

    // ── Thao tác trang ──────────────────────────────────────────────────────────

    /// <summary>Chạy toàn bộ: mở trang nháp → Bulk Actions → Clear → kiểm hộp confirm → reset bảng → Confirm → chờ nháp về 0. LUÔN thử Clear
    /// kể cả khi bảng trông rỗng. <paramref name="resetBeforeConfirm"/> chạy ngay khi hộp confirm đã đúng câu và nút chính đúng "Confirm",
    /// TRƯỚC khi bấm Confirm (lệch về phía reset: không reset mà Confirm rồi mới hỏng thì mất SP, còn reset rồi Confirm hỏng chỉ khiến SP bị
    /// import lại, nháp trùng bị xoá ở lần Clear sau); trả false = reset không trọn vẹn → Cancel hộp, Aborted. Sau khi reset đã chạy mà không bấm
    /// được Confirm thì Aborted kèm ghi chú "đã reset bảng nhưng chưa chắc đã xoá nháp". Lỗi/khác thường TRƯỚC Confirm → Aborted; mọi lỗi sau
    /// Confirm chỉ là tạm thời trong lúc chờ.</summary>
    public async Task<DraftClearResult> ClearAsync(Func<Task<bool>> resetBeforeConfirm, CancellationToken ct)
    {
        var resetRan = false;
        try
        {
            if (!await OpenDraftPageAsync(forceReload: false, ct).ConfigureAwait(false))
                return new(DraftClearStatus.Aborted, "không mở được trang nháp BigSeller.");

            var rows = await CountRowsAsync().ConfigureAwait(false);
            _log($"Trang nháp hiện {rows} dòng (trang 1; chỉ để log, vẫn mở Bulk Actions → Clear) — Bulk Actions → Clear...");

            var (probe, clearItem, menuTexts) = await ProbeClearMenuAsync(ct).ConfigureAwait(false);
            if (probe != ClearMenuProbe.Found)
            {
                var tableEmpty = await IsDraftEmptyAsync(ct).ConfigureAwait(false);
                var status = DecideWithoutClearItem(probe, tableEmpty);
                var why = probe switch
                {
                    ClearMenuProbe.NoClearItem => $"menu Bulk Actions mở được nhưng không có mục 'Clear' (các mục: \"{menuTexts}\")",
                    ClearMenuProbe.ClearDisabled => "mục 'Clear' trong menu Bulk Actions đang bị khoá (disabled)",
                    ClearMenuProbe.MenuNotOpened => "hover Bulk Actions nhưng menu không mở",
                    _ => $"không thấy nút '{BulkButtonText}' (giao diện đổi / không phải tiếng Anh?)",
                };
                if (status == DraftClearStatus.NothingToClear)
                    return new(status, $"{why}; bảng nháp rỗng — không có gì để xoá.");
                return new(status, $"{why} dù bảng nháp còn dòng — dừng, không import vào trạng thái lạ.");
            }
            await clearItem!.ClickAsync(new() { Timeout = 5000 }).ConfigureAwait(false);

            var (boxShown, toasts) = await WaitConfirmBoxAsync(ct).ConfigureAwait(false);
            if (!boxShown)
            {
                var toastNote = toasts.Length > 0 ? $"toast BigSeller: \"{toasts}\"" : "không thấy toast nào";
                // Đọc lại bảng nháp (chỉ khi số dòng đọc trước Clear là 0 — còn dòng thì đã đủ để Aborted).
                var emptyNow = rows <= 0 && await IsDraftEmptyAsync(ct).ConfigureAwait(false);
                if (DecideWhenConfirmBoxMissing(rows, emptyNow) == DraftClearStatus.NothingToClear)
                    return new(DraftClearStatus.NothingToClear,
                        $"đã bấm Clear nhưng hộp xác nhận không hiện sau {ConfirmBoxWaitMs / 1000}s ({toastNote}) và bảng nháp rỗng — coi là không có gì để xoá, không reset bảng import.");
                return new(DraftClearStatus.Aborted,
                    $"đã bấm Clear nhưng hộp xác nhận không hiện sau {ConfirmBoxWaitMs / 1000}s ({toastNote}) trong khi bảng nháp còn dòng (trước Clear: {rows} dòng) — dừng, không import vào trạng thái lạ, chưa reset bảng import.");
            }

            var confirm = _page.Locator(ConfirmModalSelector).Last;
            var confirmText = Squash(await confirm.InnerTextAsync(new() { Timeout = 3000 }).ConfigureAwait(false));
            _log($"Hộp xác nhận Clear: \"{confirmText}\"");

            if (!IsClearAllConfirmText(confirmText))
            {
                await CancelConfirmAsync(confirm).ConfigureAwait(false);
                return new(DraftClearStatus.Aborted, $"hộp xác nhận KHÔNG phải hộp 'clearing all filtered products' — đã bấm Cancel, không xoá gì. Nội dung: \"{confirmText}\"");
            }

            var primary = confirm.Locator(ConfirmPrimarySelector).First;
            var primaryText = await primary.CountAsync().ConfigureAwait(false) > 0
                ? await primary.InnerTextAsync(new() { Timeout = 3000 }).ConfigureAwait(false) : "";
            if (!IsConfirmButtonText(primaryText))
            {
                await CancelConfirmAsync(confirm).ConfigureAwait(false);
                return new(DraftClearStatus.Aborted, $"nút chính của hộp xác nhận không phải '{ConfirmButtonText}' (là \"{Squash(primaryText)}\") — đã bấm Cancel, không xoá gì.");
            }

            // Hộp đúng câu + nút đúng "Confirm": reset bảng import NGAY, trước khi bấm Confirm. Reset không trọn vẹn thì Cancel, không xoá nháp.
            resetRan = true;
            if (!await resetBeforeConfirm().ConfigureAwait(false))
            {
                await CancelConfirmAsync(confirm).ConfigureAwait(false);
                return new(DraftClearStatus.Aborted, "reset bảng import KHÔNG trọn vẹn trước khi Confirm — đã bấm Cancel, chưa xoá nháp.");
            }

            try
            {
                await primary.ClickAsync(new() { Timeout = 5000 }).ConfigureAwait(false);
            }
            catch (Exception clickEx) when (clickEx is not OperationCanceledException)
            {
                // Click ném lỗi (timeout chờ actionable, hộp đóng giữa chừng...) nhưng có thể BigSeller ĐÃ nhận: xem hộp còn hiện không.
                // Kiểm tra này mà cũng ném thì rơi xuống catch ngoài → Aborted (không rõ trạng thái).
                var stillVisible = await _page.Locator(ConfirmModalSelector).CountAsync().ConfigureAwait(false) > 0;
                if (!ConfirmClickCountsAsDone(clickThrew: true, confirmBoxStillVisible: stillVisible))
                    return new(DraftClearStatus.Aborted, $"bấm Confirm lỗi và hộp xác nhận VẪN hiện — {ResetNote(resetRan)}: {clickEx.Message}");
                _log($"⚠ Bấm Confirm ném lỗi nhưng hộp xác nhận đã biến mất — coi như ĐÃ Confirm (bảng import đã reset từ trước): {clickEx.Message}");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new(DraftClearStatus.Aborted, $"lỗi trước khi Confirm có hiệu lực — {ResetNote(resetRan)}: {ex.Message}");
        }

        // ── Từ đây Clear ĐÃ được xác nhận, không dừng được (bảng import đã reset từ trước). ──
        _log("Đã bấm Confirm xoá sạch nháp (BigSeller xử lý nền, không dừng được).");
        return await WaitDraftEmptyAfterConfirmAsync(ct).ConfigureAwait(false);
    }

    private static string ResetNote(bool resetRan) =>
        resetRan ? "đã reset bảng import nhưng chưa chắc đã xoá nháp" : "chưa xoá gì, chưa reset bảng import";

    private async Task<DraftClearResult> WaitDraftEmptyAfterConfirmAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMinutes(WaitEmptyMaxMinutes);
        var zeroStreak = 0;
        var polls = 0;
        while (DateTime.UtcNow < deadline)
        {
            await _delay(PollSeconds * 1000, ct).ConfigureAwait(false);
            polls++;
            try
            {
                // Mỗi lần đọc: tải lại trang + bảng sẵn sàng (OpenDraftPageAsync), nên 0 dòng ở đây không phải bảng cũ/đang tải.
                if (!await OpenDraftPageAsync(forceReload: true, ct).ConfigureAwait(false))
                {
                    zeroStreak = 0;
                    _log("  (chờ nháp về 0: chưa tải lại được trang nháp, thử lại...)");
                    continue;
                }
                var rows = await CountRowsAsync().ConfigureAwait(false);
                zeroStreak = NextZeroStreak(zeroStreak, rows);
                if (IsEmptyConfirmed(zeroStreak))
                    return new(DraftClearStatus.Cleared, $"nháp đã về 0 ({RequiredEmptyReads} lần đọc liên tiếp sau tải lại, sau ~{polls * PollSeconds}s).");
                if (polls % 6 == 0)
                    _log($"  (chờ nháp về 0: trang 1 còn {rows} dòng, đã chờ ~{polls * PollSeconds}s/{WaitEmptyMaxMinutes * 60}s)");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                zeroStreak = 0;
                _log($"  (chờ nháp về 0: lỗi đọc trang nháp, thử lại: {ex.Message})");
            }
        }
        return new(DraftClearStatus.TimedOut, $"đã Confirm nhưng nháp chưa về 0 sau {WaitEmptyMaxMinutes} phút.");
    }

    private async Task<bool> OpenDraftPageAsync(bool forceReload, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (forceReload && _page.Url.Contains("bsStatus=1", StringComparison.OrdinalIgnoreCase)
                    && _page.Url.Contains("/web/listing/shopee/", StringComparison.OrdinalIgnoreCase))
                    await _page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 }).ConfigureAwait(false);
                else
                    await _page.GotoAsync(DraftListingUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 }).ConfigureAwait(false);

                await _delay(1500, ct).ConfigureAwait(false);
                await _page.WaitForSelectorAsync(ReadySelector, new() { State = WaitForSelectorState.Visible, Timeout = 10000 }).ConfigureAwait(false);
                await _delay(1000, ct).ConfigureAwait(false);
                // Popup hướng dẫn đổi ngôn ngữ che nút Bulk Actions — dọn ngay khi vừa vào trang (như luồng Update).
                await BigSellerCrawlHelper.DismissLanguageGuideAsync(_page, _log, ct).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                try { await BigSellerCrawlHelper.StopPageLoadingAsync(_page).ConfigureAwait(false); } catch { }
                await _delay(3000, ct).ConfigureAwait(false);
            }
        }
        return false;
    }

    private Task<int> CountRowsAsync() =>
        _page.EvaluateAsync<int>(
            @"sel => Array.from(document.querySelectorAll(sel))
                .filter(row => { const r = row.getBoundingClientRect(); return r.width > 0 && r.height > 0; }).length",
            RowsSelector);

    // Chỉ dùng để phân xử khi KHÔNG thấy nút/menu Clear (không để quyết định bỏ qua Clear): đọc dòng đến khi đủ
    // RequiredEmptyReads lần liên tiếp 0 dòng (cách nhau 2s, bảng đã sẵn sàng ở OpenDraftPageAsync); thấy dòng là có nháp ngay.
    private async Task<bool> IsDraftEmptyAsync(CancellationToken ct)
    {
        var streak = 0;
        for (var i = 0; i < RequiredEmptyReads + 3; i++)
        {
            var rows = await CountRowsAsync().ConfigureAwait(false);
            if (rows > 0) return false;
            streak = NextZeroStreak(streak, rows);
            if (IsEmptyConfirmed(streak)) return true;
            await _delay(2000, ct).ConfigureAwait(false);
        }
        return false;
    }

    // Hover Bulk Actions → dò menu (thử lại tối đa 3 lần: menu antd đóng khi chuột rời). Phân biệt: không thấy nút / menu không mở /
    // menu mở mà không có "Clear" (kèm chữ các mục để log) / "Clear" bị khoá / thấy "Clear" dùng được.
    private async Task<(ClearMenuProbe Probe, ILocator? Item, string MenuTexts)> ProbeClearMenuAsync(CancellationToken ct)
    {
        var bulk = _page.Locator(BulkButtonSelector).Filter(new() { HasTextRegex = new Regex(BulkButtonText) }).First;
        try { await bulk.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 }).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return (ClearMenuProbe.ButtonMissing, null, ""); }

        var items = _page.Locator(MenuItemSelector);
        var clearItem = items.Filter(new() { HasTextRegex = new Regex(@"^\s*Clear\s*$") }).First;
        var menuTexts = "";
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await bulk.HoverAsync(new() { Timeout = 5000 }).ConfigureAwait(false);
            try
            {
                await clearItem.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3000 }).ConfigureAwait(false);
                var text = await clearItem.InnerTextAsync(new() { Timeout = 2000 }).ConfigureAwait(false);
                if (IsClearMenuText(text))
                {
                    var itemClass = await clearItem.GetAttributeAsync("class").ConfigureAwait(false) ?? "";
                    return itemClass.Contains("disabled", StringComparison.OrdinalIgnoreCase)
                        ? (ClearMenuProbe.ClearDisabled, clearItem, "")
                        : (ClearMenuProbe.Found, clearItem, "");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }

            try
            {
                var texts = await items.AllInnerTextsAsync().ConfigureAwait(false);
                if (texts.Count > 0) menuTexts = string.Join(" / ", texts.Select(Squash));
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
            await _delay(500, ct).ConfigureAwait(false);
        }
        return menuTexts.Length > 0
            ? (ClearMenuProbe.NoClearItem, null, menuTexts)
            : (ClearMenuProbe.MenuNotOpened, null, "");
    }

    // Sau khi bấm Clear: chờ hộp xác nhận hiện (tối đa ConfirmBoxWaitMs), trong lúc chờ gom toast (toast antd chỉ sống vài giây nên phải
    // đọc dọc đường, không chờ hết giờ mới đọc). Trả (hộp có hiện không, toast nguyên văn đã gặp).
    private async Task<(bool Shown, string Toasts)> WaitConfirmBoxAsync(CancellationToken ct)
    {
        var seen = new List<string>();
        var deadline = DateTime.UtcNow.AddMilliseconds(ConfirmBoxWaitMs);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (await _page.Locator(ConfirmModalSelector).CountAsync().ConfigureAwait(false) > 0) return (true, "");
            try
            {
                var texts = await _page.EvaluateAsync<string[]>(
                    @"sel => Array.from(document.querySelectorAll(sel))
                        .filter(el => { const r = el.getBoundingClientRect(); return r.width > 0 && r.height > 0; })
                        .map(el => el.innerText || '')",
                    ToastSelector).ConfigureAwait(false);
                seen.AddRange(texts);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
            if (DateTime.UtcNow >= deadline) return (false, MergeToastTexts(seen));
            await _delay(400, ct).ConfigureAwait(false);
        }
    }

    private async Task CancelConfirmAsync(ILocator confirm)
    {
        try
        {
            var cancel = confirm.Locator(ConfirmButtonsSelector + ":not(.ant-btn-primary)").First;
            if (await cancel.CountAsync().ConfigureAwait(false) > 0)
            {
                await cancel.ClickAsync(new() { Timeout = 3000 }).ConfigureAwait(false);
                return;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { }
        try { await _page.Keyboard.PressAsync("Escape").ConfigureAwait(false); } catch { }
    }
}
