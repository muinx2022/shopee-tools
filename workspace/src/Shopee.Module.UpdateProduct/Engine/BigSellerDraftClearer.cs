using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace UpdateProduct;

/// <summary>Kết cục "Clear nháp": NothingToClear = không có gì để xoá (không thấy mục Clear / hộp xác nhận không hiện, VÀ bảng nháp rỗng),
/// chưa xoá gì và KHÔNG reset bảng import (riêng ca trang nháp rỗng có dấu "Không có dữ liệu" — <see cref="DraftClearResult.PageEmpty"/> —
/// thì bảng import ĐÃ reset qua callback, không mở menu/Clear); Cleared = đã Confirm và nháp về 0; Aborted = dừng TRƯỚC khi Confirm có hiệu lực (chưa xoá gì,
/// trạng thái lạ; nếu bảng import đã reset trước Confirm thì Message nói rõ: đã reset bảng nhưng chưa chắc đã xoá nháp); TimedOut = đã
/// Confirm (không dừng được) nhưng hết giờ chờ nháp về 0.</summary>
internal enum DraftClearStatus { NothingToClear, Cleared, Aborted, TimedOut }

/// <summary>Kết quả dò menu Bulk Actions để tìm mục "Clear".</summary>
internal enum ClearMenuProbe { ButtonMissing, MenuNotOpened, NoClearItem, ClearDisabled, Found }

/// <param name="PageEmpty">true = NothingToClear do trang nháp rỗng thật (dấu "Không có dữ liệu" + 0 dòng): đã bỏ qua Clear và
/// ĐÃ gọi callback reset bảng import (runner coi như Cleared). false ở mọi ca khác, kể cả các NothingToClear còn lại (không reset).</param>
internal sealed record DraftClearResult(DraftClearStatus Status, string Message, bool PageEmpty = false);

/// <summary>
/// Xoá sạch nháp BigSeller (Bulk Actions → Clear → Confirm) của TÀI KHOẢN đang đăng nhập, chạy TRƯỚC import mỗi shop.
/// Thao tác trang gom hết ở đây (selector + text một chỗ); phần quyết định thuần là các hàm <c>internal static</c> để test.
/// Hợp đồng DOM (tiếng Anh và tiếng Việt cùng cấu trúc, từ DOM user dán; chữ từng ngôn ngữ gom ở <see cref="Languages"/>): nút
/// <c>.dropdown_box button.ant-btn-primary</c> chữ "Bulk Actions"/"Thao tác hàng loạt" (hover mở menu) → <c>.ant-dropdown:visible li.ant-dropdown-menu-item</c>
/// chữ "Clear"/"Xóa sạch" → hộp <c>.ant-modal-content</c> có <c>.ant-modal-confirm-btns</c> và câu "...clearing all filtered products..."/"...xóa tất cả các sản phẩm
/// đã lọc..." → nút <c>.ant-modal-confirm-btns button.ant-btn-primary</c> chữ "Confirm"/"Đồng ý" → hộp xác nhận THỨ HAI cùng cấu trúc, câu "...confirm again if
/// you want to clear..."/"...xác nhận lại nếu bạn muốn xóa sạch..." → lại nút chính chữ "Confirm"/"Đồng ý". Mục menu "Delete"/"Xóa" (xoá SP đang chọn) KHÔNG bao giờ khớp.
/// Chỉ BỎ QUA Clear khi trang nháp rỗng THẬT: ô <c>td.product_native_empty</c> ("Không có dữ liệu") đang hiện VÀ 0 dòng, đọc hai lần cách
/// nhau 1s (<see cref="ShouldSkipClear"/>). Thiếu dấu (0 dòng nhưng không có ô đó: bảng đang tải...) hoặc còn dòng thì vẫn Clear; đếm dòng
/// còn dùng để phân xử khi KHÔNG thấy nút/menu.
/// Giới hạn: chưa có cách tin cậy đọc TỔNG số nháp (code không có selector phân trang tổng số bản ghi cho trang nháp, cũng không có API
/// response của trang này; nhãn "X / Y" của Listing là số TRANG), nên chờ "về 0" = bảng nháp không có dòng nào ở
/// <see cref="RequiredEmptyReads"/> lần đọc liên tiếp, MỖI lần sau khi tải lại trang và bảng đã sẵn sàng.
/// </summary>
internal sealed class BigSellerDraftClearer
{
    public const string DraftListingUrl = "https://www.bigseller.com/web/listing/shopee/index.htm?bsStatus=1";

    // Bảng nháp + trạng thái rỗng (bản sao ListingReadySelector / ListingRows của BigSellerProductUpdateRunner — cùng trang listing;
    // dòng placeholder của bảng ant bị loại khỏi phép đếm vì nó là <tr> nhưng không phải SP).
    private const string ReadySelector = "tr.product_native_row, tr.vxe-body--row, tbody.ant-table-tbody tr, a.action_btn.addEditProduct, .ant-empty, .ant-table-placeholder, .vxe-table--empty-block, .vxe-table--empty-placeholder, td.product_native_empty";
    private const string RowsSelector = "tr.product_native_row, tr.vxe-body--row, tbody.ant-table-tbody tr:not(.ant-table-placeholder)";
    // Trạng thái rỗng thật của BigSeller (DOM user dán): <tbody><tr><td colspan="6" class="product_native_empty">Không có dữ liệu</td></tr></tbody>.
    private const string EmptyMarkerSelector = "td.product_native_empty";
    private const int EmptyRecheckMs = 1000;

    /// <summary>Chữ giao diện BigSeller của MỘT ngôn ngữ. Thêm ngôn ngữ = thêm một dòng vào <see cref="Languages"/>, không sửa chỗ khác.</summary>
    private sealed record UiLanguage(string Name, string BulkButton, string ClearItem, string ClearAllPhrase, string ClearAllSecondPhrase,
        string ConfirmButton, string CancelButton);

    // Nguyên văn từ DOM user dán (Anh: hộp 03/11; Việt: DOM 03/11). Khớp luôn qua Normalize (không phân biệt hoa thường/dấu).
    private static readonly UiLanguage[] Languages =
    {
        new("Anh", "Bulk Actions", "Clear", "clearing all filtered products", "confirm again if you want to clear", "Confirm", "Cancel"),
        new("Việt", "Thao tác hàng loạt", "Xóa sạch", "xóa tất cả các sản phẩm đã lọc", "xác nhận lại nếu bạn muốn xóa sạch", "Đồng ý", "Hủy"),
    };

    private static string[] TextsOf(Func<UiLanguage, string> pick) => Languages.Select(pick).ToArray();
    private static string[] NormalizedOf(Func<UiLanguage, string> pick) => Languages.Select(l => Normalize(pick(l))).ToArray();

    private static readonly string[] BulkButtonTexts = TextsOf(l => l.BulkButton);
    private static readonly string[] ClearItemTexts = TextsOf(l => l.ClearItem);
    private static readonly string[] ConfirmButtonTexts = TextsOf(l => l.ConfirmButton);
    private static readonly string[] ClearAllPhrases = TextsOf(l => l.ClearAllPhrase);
    private static readonly string[] ClearAllSecondPhrases = TextsOf(l => l.ClearAllSecondPhrase);
    private static readonly string[] BulkButtonNorm = NormalizedOf(l => l.BulkButton);
    private static readonly string[] ClearItemNorm = NormalizedOf(l => l.ClearItem);
    private static readonly string[] ConfirmButtonNorm = NormalizedOf(l => l.ConfirmButton);
    private static readonly string[] CancelButtonNorm = NormalizedOf(l => l.CancelButton);
    private static readonly string[] ClearAllPhraseNorm = NormalizedOf(l => l.ClearAllPhrase);
    private static readonly string[] ClearAllSecondPhraseNorm = NormalizedOf(l => l.ClearAllSecondPhrase);

    /// <summary>Chữ các ngôn ngữ nối bằng "/" để đưa vào log/thông báo (vd. "Clear/Xóa sạch").</summary>
    private static string Display(string[] texts) => string.Join("/", texts);

    private const string BulkButtonSelector = ".dropdown_box button.ant-btn-primary";
    // Chỉ lấy nút ĐANG HIỆN: trang có thể có nhiều nút khớp (thanh công cụ khác, bản ẩn) mà .First rơi vào bản ẩn thì WaitFor(Visible) hỏng.
    private const string BulkButtonVisibleSelector = BulkButtonSelector + ":visible";
    private const int DiagButtonMax = 15;
    private const int DiagButtonTextMax = 40;
    private const string MenuItemSelector = ".ant-dropdown:visible li.ant-dropdown-menu-item";
    // Dựa trên cấu trúc có bằng chứng (DOM user dán: .ant-modal-content chứa .ant-modal-confirm-btns; lớp .ant-modal-confirm không nằm trong đoạn đó).
    private const string ConfirmModalSelector = ".ant-modal-content:visible:has(.ant-modal-confirm-btns)";
    private const int DiagModalMax = 3;
    private const int DiagModalTextMax = 150;
    private const string ConfirmButtonsSelector = ".ant-modal-confirm-btns button";
    private const string ConfirmPrimarySelector = ".ant-modal-confirm-btns button.ant-btn-primary";
    // Hộp xác nhận THỨ HAI (hiện sau khi Confirm hộp một; DOM thật 03/11): "Please confirm again if you want to clear, the products cannot be recovered after clearing."
    // / "Vui lòng xác nhận lại nếu bạn muốn xóa sạch, các sản phẩm không thể khôi phục sau khi xóa." (cụm khớp ở Languages).
    // Toast antd (selector có sẵn trong BigSellerProductUpdateRunner.Selectors): đọc nguyên văn để log khi hộp xác nhận không hiện.
    private const string ToastSelector = "div.ant-message .ant-message-notice-content, .ant-notification-notice";

    private const int PollSeconds = 10;
    public const int WaitEmptyMaxMinutes = 10;
    private const int ConfirmBoxWaitMs = 10000;
    private const int SecondConfirmWaitMs = 10000;
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

    /// <summary>Chuẩn hoá để so chữ giao diện: Unicode NFC, bỏ dấu tiếng Việt (dấu thanh/dấu mũ/móc qua NFD, thêm đ→d; nên "xóa"/"xoá" như nhau),
    /// gộp khoảng trắng, không phân biệt hoa thường. Chỉ dùng để SO SÁNH, không dùng để hiển thị/log.</summary>
    internal static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var decomposed = text.Normalize(NormalizationForm.FormC).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch is 'đ' or 'Đ' ? 'd' : ch);
        }
        return Squash(sb.ToString().Normalize(NormalizationForm.FormC)).ToLowerInvariant();
    }

    private static bool EqualsAny(string? text, string[] normalizedCandidates) => Array.IndexOf(normalizedCandidates, Normalize(text)) >= 0;

    private static bool ContainsAny(string? text, string[] normalizedPhrases)
    {
        var n = Normalize(text);
        foreach (var p in normalizedPhrases) if (n.Contains(p, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>Hộp confirm có đúng câu của thao tác Clear-tất-cả ("...clearing all filtered products..."/"...xóa tất cả các sản phẩm đã lọc...",
    /// khớp chứa sau <see cref="Normalize"/>). Hộp khác (xoá từng SP, hộp lạ, câu hộp hai) → false: caller phải Cancel, không đoán.</summary>
    internal static bool IsClearAllConfirmText(string? text) => ContainsAny(text, ClearAllPhraseNorm);

    /// <summary>Hộp confirm THỨ HAI có đúng câu ("...confirm again if you want to clear..."/"...xác nhận lại nếu bạn muốn xóa sạch...",
    /// khớp chứa sau <see cref="Normalize"/>). Câu hộp một, câu lạ, rỗng → false: caller phải Cancel, không đoán.</summary>
    internal static bool IsClearAllSecondConfirmText(string? text) => ContainsAny(text, ClearAllSecondPhraseNorm);

    /// <summary>Trong các hộp confirm đang hiện, chọn hộp MỚI (hộp hai): chữ khác hộp một VÀ không phải câu hộp một (lúc hộp một đang đóng
    /// bằng animation, locator vẫn trả về nó — không được nhầm với hộp hai). Trả chỉ số hộp đầu tiên thoả, -1 nếu chưa có.</summary>
    internal static int FindNewConfirmBoxIndex(IReadOnlyList<string?> visibleBoxTexts, string firstBoxText)
    {
        var first = Squash(firstBoxText);
        for (var i = 0; i < visibleBoxTexts.Count; i++)
        {
            var t = Squash(visibleBoxTexts[i]);
            if (t.Length > 0 && !string.Equals(t, first, StringComparison.Ordinal) && !IsClearAllConfirmText(t)) return i;
        }
        return -1;
    }

    /// <summary>Mục menu đúng là "Clear"/"Xóa sạch" — khớp NGUYÊN chữ (sau <see cref="Normalize"/>), không khớp chứa: "Delete"/"Xóa" (xoá SP đang
    /// chọn), "Clear filter", "Chỉnh sửa hình ảnh" đều sai.</summary>
    internal static bool IsClearMenuText(string? text) => EqualsAny(text, ClearItemNorm);

    /// <summary>Nút xác nhận đúng là "Confirm"/"Đồng ý" (nguyên chữ sau <see cref="Normalize"/>).</summary>
    internal static bool IsConfirmButtonText(string? text) => EqualsAny(text, ConfirmButtonNorm);

    /// <summary>Nút huỷ đúng là "Cancel"/"Hủy" (nguyên chữ sau <see cref="Normalize"/>).</summary>
    internal static bool IsCancelButtonText(string? text) => EqualsAny(text, CancelButtonNorm);

    /// <summary>Nút mở menu thao tác hàng loạt: chữ chứa "Bulk Actions"/"Thao tác hàng loạt" (sau <see cref="Normalize"/>; nút có thể kèm biểu tượng/mũi tên).</summary>
    internal static bool IsBulkButtonText(string? text) => ContainsAny(text, BulkButtonNorm);

    /// <summary>Bỏ qua Clear khi và chỉ khi ô "Không có dữ liệu" đang hiện VÀ đếm dòng = 0. Thiếu một trong hai (0 dòng mà không có ô đó,
    /// hoặc có ô đó mà vẫn đếm được dòng) → false: vẫn Clear như cũ.</summary>
    internal static bool ShouldSkipClear(bool emptyMarkerVisible, int rowCount) => emptyMarkerVisible && rowCount <= 0;

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

    /// <summary>Một dòng chẩn đoán khi không thấy nút Bulk Actions: số phần tử khớp selector (tổng / đang hiện) và chữ tối đa
    /// <see cref="DiagButtonMax"/> button đang hiện trên trang (gộp khoảng trắng, cắt ngắn; button không chữ ghi "(trống)").</summary>
    internal static string FormatButtonDiagnostics(int matchedTotal, int matchedVisible, IReadOnlyList<string?> visibleButtonTexts)
    {
        var shown = visibleButtonTexts.Take(DiagButtonMax).Select(t =>
        {
            var q = Squash(t);
            if (q.Length == 0) return "(trống)";
            return q.Length > DiagButtonTextMax ? q[..DiagButtonTextMax] + "…" : q;
        });
        var more = visibleButtonTexts.Count > DiagButtonMax ? $" (+{visibleButtonTexts.Count - DiagButtonMax} nữa)" : "";
        return $"selector '{BulkButtonSelector}' chữ '{Display(BulkButtonTexts)}' khớp {matchedTotal} phần tử, đang hiện {matchedVisible}; "
            + $"{visibleButtonTexts.Count} button đang hiện trên trang: [{string.Join(" | ", shown)}]{more}";
    }

    // ── Thao tác trang ──────────────────────────────────────────────────────────

    /// <summary>Chạy toàn bộ: mở trang nháp → Bulk Actions → Clear → kiểm hộp confirm → reset bảng → Confirm → chờ nháp về 0. Trang nháp rỗng thật
    /// (<see cref="ShouldSkipClear"/>) thì bỏ qua Clear nhưng vẫn gọi <paramref name="resetBeforeConfirm"/> rồi trả NothingToClear + PageEmpty;
    /// còn lại LUÔN thử Clear. <paramref name="resetBeforeConfirm"/> chạy ngay khi hộp confirm đã đúng câu và nút chính đúng "Confirm",
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
            var emptyMarker = await IsEmptyMarkerVisibleAsync().ConfigureAwait(false);
            if (ShouldSkipClear(emptyMarker, rows))
            {
                // Đọc lại sau một nhịp: ô "Không có dữ liệu" có thể chỉ là khung chờ trước khi bảng đổ dòng; bỏ qua Clear nhầm thì import vào nháp cũ.
                await _delay(EmptyRecheckMs, ct).ConfigureAwait(false);
                rows = await CountRowsAsync().ConfigureAwait(false);
                emptyMarker = await IsEmptyMarkerVisibleAsync().ConfigureAwait(false);
                if (ShouldSkipClear(emptyMarker, rows))
                {
                    _log("Trang nháp không có dữ liệu — bỏ qua Clear");
                    // Nháp rỗng: mọi SP đã import mà chưa Update xong không còn nháp → reset bảng import như trước Confirm (cùng callback).
                    resetRan = true;
                    if (!await resetBeforeConfirm().ConfigureAwait(false))
                        return new(DraftClearStatus.Aborted, "trang nháp rỗng (bỏ qua Clear) nhưng reset bảng import KHÔNG trọn vẹn — chưa Clear gì.");
                    return new(DraftClearStatus.NothingToClear,
                        "trang nháp rỗng (ô 'Không có dữ liệu', 0 dòng) — không mở Bulk Actions, không Clear; bảng import đã reset.", PageEmpty: true);
                }
            }
            _log($"Trang nháp hiện {rows} dòng, dấu 'Không có dữ liệu': {(emptyMarker ? "có" : "không")} (trang 1) — không đủ điều kiện bỏ qua, Bulk Actions → Clear...");

            var (probe, clearItem, menuTexts) = await ProbeClearMenuAsync(ct).ConfigureAwait(false);
            if (probe != ClearMenuProbe.Found)
            {
                var tableEmpty = await IsDraftEmptyAsync(ct).ConfigureAwait(false);
                var status = DecideWithoutClearItem(probe, tableEmpty);
                var why = probe switch
                {
                    ClearMenuProbe.NoClearItem => $"menu Bulk Actions mở được nhưng không có mục '{Display(ClearItemTexts)}' (các mục: \"{menuTexts}\")",
                    ClearMenuProbe.ClearDisabled => $"mục '{Display(ClearItemTexts)}' trong menu Bulk Actions đang bị khoá (disabled)",
                    ClearMenuProbe.MenuNotOpened => "hover Bulk Actions nhưng menu không mở",
                    _ => $"không thấy nút '{Display(BulkButtonTexts)}' (giao diện đổi / không khớp chữ tiếng Anh/Việt đã biết)",
                };
                if (status == DraftClearStatus.NothingToClear)
                    return new(status, $"{why}; bảng nháp rỗng — không có gì để xoá.");
                return new(status, $"{why} dù bảng nháp còn dòng — dừng, không import vào trạng thái lạ.");
            }
            await clearItem!.ClickAsync(new() { Timeout = 5000 }).ConfigureAwait(false);

            var (boxShown, toasts) = await WaitConfirmBoxAsync(ct).ConfigureAwait(false);
            if (!boxShown)
            {
                await LogModalDiagnosticsAsync().ConfigureAwait(false);
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
                return new(DraftClearStatus.Aborted, $"hộp xác nhận KHÔNG phải hộp '{Display(ClearAllPhrases)}' — đã bấm Cancel, không xoá gì. Nội dung: \"{confirmText}\"");
            }

            var primary = confirm.Locator(ConfirmPrimarySelector).First;
            var primaryText = await primary.CountAsync().ConfigureAwait(false) > 0
                ? await primary.InnerTextAsync(new() { Timeout = 3000 }).ConfigureAwait(false) : "";
            if (!IsConfirmButtonText(primaryText))
            {
                await CancelConfirmAsync(confirm).ConfigureAwait(false);
                return new(DraftClearStatus.Aborted, $"nút chính của hộp xác nhận không phải '{Display(ConfirmButtonTexts)}' (là \"{Squash(primaryText)}\") — đã bấm Cancel, không xoá gì.");
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

            // Confirm hộp một chưa xoá gì: BigSeller hiện tiếp hộp xác nhận THỨ HAI ("confirm again..."). Phải Confirm cả hai mới xoá thật.
            var secondAbort = await HandleSecondConfirmAsync(confirmText, resetRan, ct).ConfigureAwait(false);
            if (secondAbort != null) return secondAbort;
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

    // Sau Confirm hộp một: chờ hộp xác nhận thứ hai (tối đa SecondConfirmWaitMs). Trả null = đi tiếp (đã Confirm hộp hai, hoặc không thấy hộp hai
    // trong thời gian chờ — không sai, đi tiếp chờ nháp về 0 như cũ); trả Aborted = hộp hai lạ / nút lạ (đã Cancel) hoặc Confirm hộp hai lỗi mà
    // hộp còn hiện. Ném lỗi thì rơi xuống catch ngoài của ClearAsync → Aborted.
    private async Task<DraftClearResult?> HandleSecondConfirmAsync(string firstBoxText, bool resetRan, CancellationToken ct)
    {
        var modals = _page.Locator(ConfirmModalSelector);
        ILocator? box = null;
        var deadline = DateTime.UtcNow.AddMilliseconds(SecondConfirmWaitMs);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var texts = await modals.AllInnerTextsAsync().ConfigureAwait(false);
                var idx = FindNewConfirmBoxIndex(texts, firstBoxText);
                if (idx >= 0)
                {
                    // Chỉ số có thể lệch nếu hộp một vừa bị gỡ giữa hai lần đọc: đọc lại đúng phần tử đó, khớp chữ mới nhận.
                    var candidate = modals.Nth(idx);
                    var again = await candidate.InnerTextAsync(new() { Timeout = 1000 }).ConfigureAwait(false);
                    if (Squash(again) == Squash(texts[idx])) { box = candidate; break; }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }   // đọc hỏng giữa lúc hộp đóng/mở: thử lại tới hết giờ
            if (DateTime.UtcNow >= deadline) break;
            await _delay(300, ct).ConfigureAwait(false);
        }
        if (box == null)
        {
            _log($"Không thấy hộp xác nhận lần 2 sau {SecondConfirmWaitMs / 1000}s — đi tiếp chờ nháp về 0.");
            return null;
        }

        var text = Squash(await box.InnerTextAsync(new() { Timeout = 3000 }).ConfigureAwait(false));
        _log($"Hộp xác nhận Clear lần 2: \"{text}\"");

        if (!IsClearAllSecondConfirmText(text))
        {
            await CancelConfirmAsync(box).ConfigureAwait(false);
            return new(DraftClearStatus.Aborted, $"hộp xác nhận lần 2 KHÔNG phải câu '{Display(ClearAllSecondPhrases)}' — đã bấm Cancel, {ResetNote(resetRan)}. Nội dung: \"{text}\"");
        }

        var primary = box.Locator(ConfirmPrimarySelector).First;
        var primaryText = await primary.CountAsync().ConfigureAwait(false) > 0
            ? await primary.InnerTextAsync(new() { Timeout = 3000 }).ConfigureAwait(false) : "";
        if (!IsConfirmButtonText(primaryText))
        {
            await CancelConfirmAsync(box).ConfigureAwait(false);
            return new(DraftClearStatus.Aborted, $"nút chính của hộp xác nhận lần 2 không phải '{Display(ConfirmButtonTexts)}' (là \"{Squash(primaryText)}\") — đã bấm Cancel, {ResetNote(resetRan)}.");
        }

        try
        {
            await primary.ClickAsync(new() { Timeout = 5000 }).ConfigureAwait(false);
        }
        catch (Exception clickEx) when (clickEx is not OperationCanceledException)
        {
            // Cùng luật với hộp một: click ném lỗi nhưng hộp (mới) đã biến mất thì coi là đã Confirm; còn hiện thì Aborted.
            var stillVisible = FindNewConfirmBoxIndex(await modals.AllInnerTextsAsync().ConfigureAwait(false), firstBoxText) >= 0;
            if (!ConfirmClickCountsAsDone(clickThrew: true, confirmBoxStillVisible: stillVisible))
                return new(DraftClearStatus.Aborted, $"bấm Confirm hộp xác nhận lần 2 lỗi và hộp VẪN hiện — {ResetNote(resetRan)}: {clickEx.Message}");
            _log($"⚠ Bấm Confirm hộp lần 2 ném lỗi nhưng hộp đã biến mất — coi như ĐÃ Confirm: {clickEx.Message}");
        }
        return null;
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

    private Task<bool> IsEmptyMarkerVisibleAsync() =>
        _page.EvaluateAsync<bool>(
            @"sel => Array.from(document.querySelectorAll(sel))
                .some(el => { const r = el.getBoundingClientRect(); return r.width > 0 && r.height > 0; })",
            EmptyMarkerSelector);

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

    // Chọn phần tử ĐANG HIỆN trong <all> có chữ thoả <match> (so qua Normalize ở các hàm Is*Text; Playwright không so được "không dấu"),
    // thăm dò tới hết <timeoutMs>. Trả locator Nth(chỉ số) hoặc null nếu không có.
    private async Task<ILocator?> WaitForTextMatchAsync(ILocator all, Func<string?, bool> match, int timeoutMs, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var texts = await all.AllInnerTextsAsync().ConfigureAwait(false);
                for (var i = 0; i < texts.Count; i++)
                {
                    if (!match(texts[i])) continue;
                    var candidate = all.Nth(i);
                    if (await candidate.IsVisibleAsync().ConfigureAwait(false)) return candidate;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }   // đọc hỏng giữa lúc DOM đổi: thử lại tới hết giờ
            if (DateTime.UtcNow >= deadline) return null;
            await _delay(200, ct).ConfigureAwait(false);
        }
    }

    // Hover Bulk Actions → dò menu (thử lại tối đa 3 lần: menu antd đóng khi chuột rời). Phân biệt: không thấy nút / menu không mở /
    // menu mở mà không có "Clear" (kèm chữ các mục để log) / "Clear" bị khoá / thấy "Clear" dùng được.
    private async Task<(ClearMenuProbe Probe, ILocator? Item, string MenuTexts)> ProbeClearMenuAsync(CancellationToken ct)
    {
        var bulk = await WaitForTextMatchAsync(_page.Locator(BulkButtonVisibleSelector), IsBulkButtonText, 10000, ct).ConfigureAwait(false);
        if (bulk == null)
        {
            await LogButtonDiagnosticsAsync().ConfigureAwait(false);
            return (ClearMenuProbe.ButtonMissing, null, "");
        }

        var items = _page.Locator(MenuItemSelector);
        var menuTexts = "";
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await bulk.ScrollIntoViewIfNeededAsync(new() { Timeout = 5000 }).ConfigureAwait(false);
            await bulk.HoverAsync(new() { Timeout = 5000 }).ConfigureAwait(false);
            try
            {
                var clearItem = await WaitForTextMatchAsync(items, IsClearMenuText, 3000, ct).ConfigureAwait(false);
                if (clearItem != null)
                {
                    // Đọc lại đúng phần tử đó trước khi trả: chỉ số có thể lệch nếu menu đổi giữa hai lần đọc — tuyệt đối không để "Delete"/"Xóa" lọt.
                    var text = await clearItem.InnerTextAsync(new() { Timeout = 2000 }).ConfigureAwait(false);
                    if (IsClearMenuText(text))
                    {
                        var itemClass = await clearItem.GetAttributeAsync("class").ConfigureAwait(false) ?? "";
                        return itemClass.Contains("disabled", StringComparison.OrdinalIgnoreCase)
                            ? (ClearMenuProbe.ClearDisabled, clearItem, "")
                            : (ClearMenuProbe.Found, clearItem, "");
                    }
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

    // Không thấy nút Bulk Actions: log MỘT dòng bằng chứng (số phần tử khớp + chữ các button đang hiện) cho lần sau. Lỗi đọc thì bỏ qua (chỉ là log).
    private async Task LogButtonDiagnosticsAsync()
    {
        try
        {
            var total = (await _page.Locator(BulkButtonSelector).AllInnerTextsAsync().ConfigureAwait(false)).Count(t => IsBulkButtonText(t));
            var visible = (await _page.Locator(BulkButtonVisibleSelector).AllInnerTextsAsync().ConfigureAwait(false)).Count(t => IsBulkButtonText(t));
            var texts = await _page.EvaluateAsync<string[]>(
                @"() => Array.from(document.querySelectorAll('button'))
                    .filter(el => { const r = el.getBoundingClientRect(); return r.width > 0 && r.height > 0; })
                    .map(el => el.innerText || el.textContent || '')").ConfigureAwait(false);
            _log("  (chẩn đoán nút Bulk Actions) " + FormatButtonDiagnostics(total, visible, texts));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log($"  (chẩn đoán nút Bulk Actions) không đọc được trang: {ex.Message}");
        }
    }

    // Hộp xác nhận không hiện: log MỘT dòng bằng chứng (số .ant-modal-content / .ant-modal-wrap đang hiện + chữ tối đa DiagModalMax hộp đang hiện). Lỗi đọc thì bỏ qua (chỉ là log).
    private async Task LogModalDiagnosticsAsync()
    {
        try
        {
            var contents = await _page.Locator(".ant-modal-content:visible").CountAsync().ConfigureAwait(false);
            var wraps = await _page.Locator(".ant-modal-wrap:visible").CountAsync().ConfigureAwait(false);
            var texts = await _page.Locator(".ant-modal-content:visible").AllInnerTextsAsync().ConfigureAwait(false);
            var shown = texts.Take(DiagModalMax).Select(t =>
            {
                var q = Squash(t);
                return q.Length > DiagModalTextMax ? q[..DiagModalTextMax] + "…" : q;
            });
            _log($"  (chẩn đoán hộp xác nhận) selector '{ConfirmModalSelector}' không khớp; đang hiện {contents} .ant-modal-content, {wraps} .ant-modal-wrap; chữ {Math.Min(texts.Count, DiagModalMax)} hộp đầu: [{string.Join(" | ", shown)}]");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log($"  (chẩn đoán hộp xác nhận) không đọc được trang: {ex.Message}");
        }
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
            // Ưu tiên nút có chữ Cancel/Hủy; không nút nào khớp chữ thì lùi về nút không-chính đầu tiên (như trước, theo cấu trúc DOM).
            var others = confirm.Locator(ConfirmButtonsSelector + ":not(.ant-btn-primary)");
            var cancel = others.First;
            var texts = await others.AllInnerTextsAsync().ConfigureAwait(false);
            for (var i = 0; i < texts.Count; i++)
                if (IsCancelButtonText(texts[i])) { cancel = others.Nth(i); break; }
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
