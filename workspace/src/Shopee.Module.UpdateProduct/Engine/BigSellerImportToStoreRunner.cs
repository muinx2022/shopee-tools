using System.Text.Json;
using Microsoft.Playwright;
using Shopee.Core.Ai;
using Shopee.Core.BigSeller;
using Shopee.Core.Browser;
using Shopee.Core.Cdp;
using Shopee.Core.Coordination;
using Shopee.Core.Progress;

namespace UpdateProduct;

internal sealed class BigSellerImportToStoreRunner : BigSellerBraveRunner
{
    private readonly int _laneIndex;
    private readonly int _laneCount;

    // Tập item id CẦN import = các dòng StartRow→EndRow của sheet shop (nạp 1 lần trước vòng lặp).
    private HashSet<string> _importIds = new(StringComparer.Ordinal);
    // Item id ĐÃ gửi import (chốt chặn: bỏ qua ở các lượt quét sau → không import trùng + đảm bảo kết thúc,
    // độc lập với việc SP có rời tab hay không).
    private readonly HashSet<string> _importedIds = new(StringComparer.Ordinal);
    // item id → dòng sheet (dòng đầu gặp id đó) — để khi import xong 1 id, báo ledger đúng DÒNG nào đã import.
    private Dictionary<string, int> _rowByImportId = new(StringComparer.Ordinal);

    // Đếm để soi vì sao Thống kê Hub thiếu dòng import: _ledgerHit = số dòng ĐÃ báo (RowsDone bắn) · _ledgerMiss =
    // số SP import xong nhưng KHÔNG khớp được dòng sheet (id crawl ≠ id sheet) → không có dòng để báo.
    private int _ledgerHitCount;
    private int _ledgerMissCount;

    /// <summary>Bắn (rowIndex, rowIndex) mỗi khi 1 dòng sheet vừa được IMPORT XONG (item id gửi lên store) →
    /// caller đẩy lên ledger Hub để Thống kê biết "shop này đã import những dòng nào".</summary>
    public event Action<int, int>? RowsDone;

    // Bước Clear nháp đã chốt: Cleared / không có gì để xoá và bảng import reset trọn vẹn, HOẶC không cần Clear (mọi SP đã import đều đã Update
    // → Clear không bỏ gì khỏi bảng import). Clear kết thúc kiểu khác (Aborted, hết giờ chờ, reset lỗi) thì AllImported KHÔNG được true.
    private bool _clearSettled;

    // Mục tiêu Update theo khoá tiến độ, đọc bằng LoadUpdateTargetsAsync cho MỌI khoá còn SP chờ Update của tài khoản: reset bảng import sau Clear chỉ
    // bỏ id thuộc tập này. Khoá không có ở đây (shared:... của shop khác, sheet không đọc được, file lỗi) là khoá KHÔNG BIẾT mục tiêu → không được
    // Clear (ImportScanPolicy.DecideClearGate) vì Clear sẽ xoá nháp của khoá đó mà bảng import không biết SP nào phải import lại.
    private IReadOnlyDictionary<string, IReadOnlySet<string>> _updateTargetsByKey = new Dictionary<string, IReadOnlySet<string>>();
    // Các khoá đã đọc mục tiêu THÀNH CÔNG ở lần đọc gần nhất (đọc lỗi thì rỗng, lần sau đọc lại) — để không đọc file lớn hai lần trong một lượt chạy.
    private HashSet<string> _targetKeysRead = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True khi MỌI item id trong khoảng dòng của sheet đã import (đã lưu từ trước + vừa gửi lượt này) VÀ bước Clear nháp đã chốt.
    /// Sheet không có id nào → false (không có gì để coi là "xong").</summary>
    public bool AllImported => ImportScanPolicy.IsAllImported(_importIds.Count, _importedIds.Count, _clearSettled);

    /// <summary>Khác null khi import shop dừng vì cổng Clear chặn (có khoá còn SP chờ Update mà không biết mục tiêu Update, nên Clear sẽ làm mất SP): lý do ngắn
    /// để caller (Auto Run) báo "Import bị chặn" thay vì "0 SP". Null = không bị chặn (kể cả khi dừng vì lý do khác).</summary>
    public string? ClearBlockedReason { get; private set; }

    // Workbook chung đang dùng? Khoá tiến độ shared:... chỉ sinh ra khi workbook chung BẬT (SharedWorkbookCatalog.ResolveForUpdate theo SharedWorkbookState.Enabled).
    // Khi TẮT, khoá shared:... trong op-progress là dữ liệu cũ: không luồng nào Update chúng nên không tính là "chờ Update" cho cổng Clear và không bị reset.
    // Khoá của chính shop đang chạy mà là shared:... thì workbook chung chắc chắn đang dùng (cờ có thể đã đổi sau khi settings dựng).
    private bool SharedKeysInUse => SharedWorkbookStore.Snapshot().Enabled || SharedWorkbookCatalog.IsSharedKey(_settings.ProgressKey);
    private bool _ignoredSharedKeysLogged;

    // ── Bắc cầu DOM ↔ item id qua API (BigSeller KHÔNG render source id ra HTML) ──
    // Crawl list nạp rows từ API /product/crawl/pageList.json; mỗi row có mainImage + crawlUrl. Ta bắt response
    // đó, trích shopee id từ crawlUrl (i.<shop>.<id> / product/<shop>/<id>), map theo KHOÁ ẢNH (đuôi mainImage)
    // vì DOM chỉ có <img src> để nhận diện dòng — ô .vxe-cell--label giờ là email, link tiêu đề không có href.
    private readonly object _apiLock = new();
    private readonly Dictionary<string, string> _apiImageKeyToShopeeId = new(StringComparer.Ordinal);
    private IPage? _capturePage;

    // ── "Trang đã xác nhận" (chống kết luận "0 khớp" khi DOM còn dòng cũ / đang tải) ──
    // Mọi biến dưới ĐỀU đọc/ghi trong _apiLock. Số thứ tự tính theo thứ tự response ĐẾN (không phải lúc đọc xong):
    //  • _respArrivalSeq: tăng mỗi khi 1 response pageList.json đến.
    //  • _latestSeq/_latestKeys: response MỚI NHẤT (theo thứ tự đến) đã đọc xong + tập khoá ảnh của nó (null = không đọc
    //    được response → không bao giờ xác nhận được trang).
    //  • _pageMark: _respArrivalSeq chụp NGAY TRƯỚC thao tác đổi trang (đổi tab / Next / tải lại); trang chỉ được xác nhận
    //    khi _latestSeq > _pageMark, tức đã có response SINH RA SAU thao tác đó.
    private int _respArrivalSeq;
    private int _latestSeq;
    private HashSet<string>? _latestKeys;
    private int _pageMark;

    // Gọi NGAY TRƯỚC mỗi thao tác làm đổi nội dung bảng crawl (đổi tab, bấm Next, tải lại Crawl List).
    private void MarkPageAction()
    {
        lock (_apiLock) _pageMark = _respArrivalSeq;
    }

    protected override string StartUrl => BigSellerCrawlHelper.ResolveCrawlUrl(_settings.CrawlUrl);

    // Import xoá các file session-tab của profile TRƯỚC khi phóng Brave (tránh Brave khôi phục tab cũ).
    protected override void PrepareProfileBeforeLaunch() =>
        BraveCachePolicy.PrepareProfileForLaunch(_settings.ProfileDir, ProfileLaunchPrep.ClearSessionRestore);

    public BigSellerImportToStoreRunner(
        BigSellerWorkflowSettings settings,
        Action<string> log,
        WorkflowPauseToken? pauseToken = null,
        int laneIndex = 0,
        int laneCount = 1,
        ClaimStore? claim = null,
        bool exportCookie = true)
        : base(settings, log, pauseToken, exportCookie)
    {
        _laneIndex = Math.Max(0, laneIndex);
        _laneCount = Math.Max(1, laneCount);
        _ = claim;   // (cũ: chống trùng đa-lane) — import giờ 1 process/1 lô nên không dùng nữa
    }

    /// <summary>Phase 4 — ĐẦU PHIÊN: đảm bảo có token BigSeller TƯƠI (tự mint) cho máy này. Ủy thác cho
    /// <see cref="BigSellerAutoLogin.EnsureFreshSessionAsync"/> (dùng chung với Update/Scrape).</summary>
    private Task EnsureFreshLoginAsync(IPage page, CancellationToken ct)
        => BigSellerAutoLogin.EnsureFreshSessionAsync(
            page, _settings.AccountId, _settings.Email, _settings.Password,
            _settings.BigSellerCookieFile, _settings.DebugPort, _exportCookie, _log, ct);

    // Nạp tập item id cần import từ sheet của shop, khoảng dòng StartRow→EndRow (giống Update, nhưng KHÔNG
    // đòi cột "Tên đã sửa" vì import không cần). Khoá file khi đọc để serialize với lúc "Update tên SP" đang ghi.
    private async Task LoadImportItemIdSetAsync(CancellationToken ct)
    {
        // HUB-MODE: tập item id lấy từ kho Hub (Postgres) — KHÔNG ánh xạ cột, KHÔNG khoá file. Excel-mode giữ nguyên.
        if (_settings.UseHubData) { await LoadImportItemIdSetFromHubAsync(ct).ConfigureAwait(false); return; }

        if (_settings.ItemIdColumn <= 0 && _settings.LinkColumn <= 0)
            throw new InvalidOperationException("Cần map ít nhất 'Item ID' hoặc 'Link' để lấy item id import (mục BigSeller → Ánh xạ cột).");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var rowById = new Dictionary<string, int>(StringComparer.Ordinal);
        var (start, end) = await WorkbookSheetReader.ForEachDataRowAsync(_settings, (row, r) =>
        {
            var colE = WorkbookSheetReader.Cell(row, _settings.ItemIdColumn);
            var link = WorkbookSheetReader.Cell(row, _settings.LinkColumn);
            var id = WorkbookSheetReader.RowId(colE, link);
            if (!string.IsNullOrWhiteSpace(id)) { ids.Add(id); rowById.TryAdd(id, r); }
        }, ct).ConfigureAwait(false);

        _importIds = ids;
        _rowByImportId = rowById;
        _log($"Nạp {ids.Count} item id từ sheet '{_settings.DataSheet}' (dòng {start}→{(end >= start ? end : start)}).");
        LogWorkbookIds();
    }

    // HUB-MODE của LoadImportItemIdSetAsync: server trả dòng có itemId non-blank HOẶC link non-blank trong
    // [StartRow..EndRow] → dựng CÙNG tập (itemId ưu tiên ItemId, rỗng thì ExtractShopeeId(Link)) + map id→dòng đầu.
    private async Task LoadImportItemIdSetFromHubAsync(CancellationToken ct)
    {
        var (client, start, end) = WorkbookSheetReader.BeginHubRead(_settings);
        var rows = await client.GetProductImportIdsAsync(_settings.AccountId, _settings.DataSheet, start, end, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("⛔ Hub chưa sẵn sàng (kho sản phẩm Postgres) — thử lại sau.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var rowById = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            var id = WorkbookSheetReader.RowId(r.ItemId, r.Link);
            if (!string.IsNullOrWhiteSpace(id)) { ids.Add(id); rowById.TryAdd(id, r.RowNo); }
        }
        _importIds = ids;
        _rowByImportId = rowById;
        _log($"Nạp {ids.Count} item id từ kho Hub — sheet '{_settings.DataSheet}' (dòng {start}→{(end > 0 ? end.ToString() : "hết")}).");
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settings.BravePath))
            throw new FileNotFoundException($"Khong tim thay Brave: {_settings.BravePath}");

        // Cache item id của sheet (dòng StartRow→EndRow) TRƯỚC khi mở Brave — import CHỈ tick đúng các id này.
        await LoadImportItemIdSetAsync(cancellationToken).ConfigureAwait(false);

        // Mỗi lượt import đều Clear nháp (xem ClearDraftsBeforeImportAsync) và Clear bỏ khỏi Import Done mọi SP chưa Update xong. Vì vậy
        // "đủ hết theo tiến độ → khỏi mở Brave" CHỈ còn đúng khi Clear không đổi được gì cho shop này. Hub-mode: Hub đã lọc SP đã import nên tập id
        // rỗng chưa chắc là "hết việc" — còn SP chờ Update thì vẫn phải đi qua Clear để Hub/local được reset.
        var pendingKeys = GetPendingUpdateKeys();
        var hubPending = _settings.UseHubData && CountImportedWithoutUpdate() > 0;
        if (ImportScanPolicy.NothingToImport(_importIds.Count, hubPending))
        {
            _log("Sheet không có item id nào trong khoảng dòng đã đặt — không có gì để import. Kết thúc.");
            return;   // AllImported = false (cần _importIds.Count > 0) → LastRunComplete = false → shop ghi "stopped", KHÔNG phải completed
        }

        // RESUME: seed _importedIds từ tiến độ ĐÃ LƯU (kill/dừng giữa chừng rồi chạy lại → KHÔNG import trùng —
        // chốt chặn cũ chỉ là HashSet trong RAM, mất khi thoát). Chỉ lấy id CÒN trong _importIds (khoảng dòng /
        // tập id có thể đã đổi giữa 2 lần chạy).
        SeedImportedFromProgress();

        // Đọc mục tiêu Update của MỌI khoá còn SP chờ Update (không chỉ khoá của shop này) rồi mới quyết: thoát sớm (khỏi mở Brave) / bị cổng Clear chặn /
        // đi tiếp. Lần đọc này được dùng lại trước Clear (ClearDraftsBeforeImportAsync quyết lại bằng cùng hàm vì tiến độ có thể đổi trong lúc mở Brave).
        await LoadUpdateTargetsAsync(pendingKeys, cancellationToken).ConfigureAwait(false);
        if (!ApplyStartDecision(DecideStart(pendingKeys, hubPending), braveOpen: false)) return;

        StartBrave();
        _log($"Da goi Brave PID={_braveProcess?.Id.ToString() ?? "unknown"}, cho CDP port {_settings.DebugPort}...");
        await EnsureCdpReadyAsync(30,
            $"CDP port {_settings.DebugPort} khong san sang. Hay dong Brave BigSeller profile cu roi chay lai.",
            cancellationToken).ConfigureAwait(false);

        // Profile BigSeller là persistent — nếu còn phiên sống thì phiên trong profile luôn "tươi" hơn file tĩnh;
        // ghi đè bằng file cũ sẽ làm văng phiên -> chỉ import khi mất phiên (logic chung ở EnsureCookieAsync).
        var crawlUrl = BigSellerCrawlHelper.ResolveCrawlUrl(_settings.CrawlUrl);
        await EnsureCookieAsync(cancellationToken).ConfigureAwait(false);

        _log($"Ket noi CDP port {_settings.DebugPort}...");
        await ConnectBrowserAsync(cancellationToken).ConfigureAwait(false);

        var context = _browser!.Contexts.FirstOrDefault()
            ?? throw new InvalidOperationException("Brave chưa có browser context.");

        var page = await FindBigSellerPageAsync(context, cancellationToken)
            ?? throw new InvalidOperationException("Không tìm thấy tab BigSeller.");
        AttachApiCapture(page);   // bắt pageList.json để đọc item id (DOM không có id)

        await page.BringToFrontAsync();
        await EnsureFreshLoginAsync(page, cancellationToken).ConfigureAwait(false);   // Phase 4: đầu phiên tự mint token tươi

        // Xoá sạch nháp của tài khoản TRƯỚC khi import (cả thủ công lẫn Auto Run) — sau đăng nhập, trước Crawl List.
        if (!await ClearDraftsBeforeImportAsync(page, cancellationToken).ConfigureAwait(false))
            return;

        _log($"Crawl URL: {crawlUrl}");
        MarkPageAction();   // tải lại Crawl List → trang đầu của tab đầu cũng phải qua cơ chế xác nhận response mới
        if (!await BigSellerCrawlHelper.GoToCrawlPageAsync(page, forceReload: true, targetUrl: crawlUrl, log: _log))
            throw new InvalidOperationException("Không mở được Crawl List.");
        _log(new string('=', 50));
        _log("BẮT ĐẦU IMPORT TO STORE");
        _log(new string('=', 50));

        var crawlTabs = new[]
        {
            (Tab: BigSellerCrawlTab.Unclaimed, Name: "Chưa nhận"),
            (Tab: BigSellerCrawlTab.Claimed, Name: "Đã nhận")
        };

        var importCount = 0;
        if (_laneCount > 1)
            _log($"Lane #{_laneIndex + 1}/{_laneCount}: MỖI SP chỉ 1 lane nhận (claim chống trùng) — các lane import SP KHÁC nhau.");

        var consecutiveErrors = 0;
        const int maxConsecutiveErrors = 8;

        // Tối đa ImportScanPolicy.MaxPasses vòng quét 2 tab. Sang vòng mới (tải lại Crawl List, về trang 1, xoá tick) khi còn
        // thiếu VÀ vòng vừa rồi có lô đã gửi (danh sách đổi, SP dồn trang) HOẶC có tab/trang CHƯA XÁC NHẬN (có thể sót).
        // _importedIds chặn gửi lại SP đã gửi; ca "chưa chắc đã gửi" vẫn thoát RunAsync ngay (không quét lại).
        var pass = 1;
        var hasUnconfirmed = false;
        while (true)
        {
            var sentThisPass = false;
            hasUnconfirmed = false;
            if (pass > 1)
            {
                _log("");
                _log($"▶ VÒNG QUÉT {pass}/{ImportScanPolicy.MaxPasses}: mới import {_importedIds.Count}/{_importIds.Count} SP → tải lại Crawl List, quét lại từ trang 1.");
                MarkPageAction();
                if (!await BigSellerCrawlHelper.GoToCrawlPageAsync(page, forceReload: true, targetUrl: crawlUrl, log: _log))
                {
                    _log("⚠ Không tải lại được Crawl List cho vòng quét mới — kết thúc.");
                    hasUnconfirmed = true;
                    break;
                }
            }

            foreach (var (currentTab, tabName) in crawlTabs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_importedIds.Count >= _importIds.Count)
                {
                    _log($"✔ Đã import đủ {_importedIds.Count}/{_importIds.Count} SP — dừng ngay, không cần quét tiếp.");
                    LogLedgerSummary();
                    return;
                }

                _log("");
                _log(new string('=', 50));
                _log($"BẮT ĐẦU QUÉT TAB '{tabName.ToUpper()}'");
                _log(new string('=', 50));

                var switched = await SwitchToTabAsync(page, currentTab, cancellationToken).ConfigureAwait(false);
                if (!switched)
                {
                    _log($"⚠ Không thể chuyển sang tab '{tabName}', bỏ qua tab này (tab CHƯA XÁC NHẬN).");
                    hasUnconfirmed = true;
                    continue;
                }

                var pageNum = 1;
                var tabSkips = 0;
                var unconfirmedNoMatchStreak = 0;   // số trang liên tiếp vừa chưa xác nhận vừa không có SP khớp (phanh)
                string[]? reloadedAfterBatch = null;   // lô vừa import trên tab 'Chưa nhận' mà trang được tải lại thay vì bấm Next (để log đúng vòng sau)

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await WaitIfNotPausedAsync(cancellationToken).ConfigureAwait(false);
                        if (_importedIds.Count >= _importIds.Count)
                        {
                            _log($"✔ Đã import đủ {_importedIds.Count}/{_importIds.Count} SP — dừng ngay, không cần quét tiếp.");
                            LogLedgerSummary();
                            return;
                        }

                        await MaybeWriteBackBigSellerTokenAsync(cancellationToken).ConfigureAwait(false);

                        // Không còn ở Crawl List → GoToCrawlPageAsync sẽ điều hướng lại: đó là thao tác đổi trang, cần mốc mới.
                        if (!BigSellerCrawlHelper.IsCrawlPage(page, crawlUrl)) MarkPageAction();
                        if (!await BigSellerCrawlHelper.GoToCrawlPageAsync(page, targetUrl: crawlUrl, log: _log))
                        {
                            await DelayAsync(5000, cancellationToken);
                            continue;
                        }

                        await BigSellerCrawlHelper.WaitForCrawlListContentAsync(page);

                        var isActive = await BigSellerCrawlHelper.IsCrawlTabActiveAsync(page, currentTab);
                        if (!isActive)
                        {
                            if (++tabSkips >= 12)
                            {
                                _log($"⚠ Tab '{tabName}' liên tục không giữ được trạng thái active (quá 12 lần) — chuyển tab tiếp theo (tab CHƯA XÁC NHẬN).");
                                hasUnconfirmed = true;
                                break;
                            }
                            _log($"Chưa ở tab '{tabName}' (reload đổi ngôn ngữ?) — chọn lại ({tabSkips}/12), thử lại...");
                            await SwitchToTabAsync(page, currentTab, cancellationToken).ConfigureAwait(false);
                            await DelayAsync(2000, cancellationToken);
                            continue;
                        }
                        tabSkips = 0;

                        // XÁC NHẬN (response pageList.json mới + DOM thuộc response đó) CHỈ quyết định việc được kết luận "0 khớp /
                        // đã quét sạch". Nó KHÔNG chặn tick: tick theo map ảnh→id vốn đúng SP, nên trang chưa xác nhận mà có
                        // SP khớp vẫn gửi lô như thường (và vẫn đánh dấu chưa xác nhận để vòng sau quét lại).
                        int rowCount;
                        string[] checkedIds;
                        bool confirmed, noNewPageData;
                        try
                        {
                            (confirmed, noNewPageData) = await WaitForConfirmedPageAsync(page, cancellationToken);
                            rowCount = await GetBodyRowCountAsync(page);
                            checkedIds = await CheckMatchingRowsOnPageAsync(page);
                        }
                        catch (Exception ex) when (CdpErrors.IsTransientNavigationError(ex))
                        {
                            _log($"Trang đang reload, thử lại: {ex.Message}");
                            await DelayAsync(3000, cancellationToken);
                            continue;
                        }
                        consecutiveErrors = 0;
                        if (!confirmed) hasUnconfirmed = true;
                        // Phanh chỉ đếm trang THẬT SỰ không có dữ liệu mới (không response mới / response null / DOM không giao với
                        // response); trang chỉ lạ vài ảnh vẫn là chưa xác nhận (hasUnconfirmed) nhưng không tăng streak.
                        unconfirmedNoMatchStreak = ImportScanPolicy.NextUnconfirmedNoMatchStreak(unconfirmedNoMatchStreak, confirmed, checkedIds.Length > 0, noNewPageData);

                        var batchBeforeReload = reloadedAfterBatch;
                        reloadedAfterBatch = null;

                        if (checkedIds.Length == 0)
                        {
                            if (batchBeforeReload is not null)
                                _log(await PageShowsOnlyImportedAsync(page, batchBeforeReload)
                                    ? $"Tải lại trang {pageNum} sau lô: vẫn đúng tập SP vừa import (SP không rời tab) → coi như trang không còn SP khớp, bấm Next."
                                    : $"Tải lại trang {pageNum} sau lô: không còn SP khớp → bấm Next.");

                            if (confirmed)
                                _log($"Trang {pageNum} (tab {tabName}): {rowCount} SP — không có SP nào cần import (0 SP khớp).");
                            else
                                _log($"⚠ Trang {pageNum} (tab {tabName}): {rowCount} SP, 0 SP khớp nhưng trang CHƯA XÁC NHẬN — KHÔNG kết luận đã quét sạch; vòng sau quét lại nếu còn thiếu ({unconfirmedNoMatchStreak}/{ImportScanPolicy.MaxUnconfirmedNoMatchPages} trang liên tiếp).");

                            // Phanh: nhiều trang liên tiếp vừa chưa xác nhận vừa không khớp → bỏ phần còn lại của tab.
                            if (ImportScanPolicy.ShouldAbandonTab(unconfirmedNoMatchStreak))
                            {
                                _log($"⚠ {unconfirmedNoMatchStreak} trang liên tiếp chưa xác nhận và không có SP khớp — BỎ phần còn lại của tab '{tabName}' (tab CHƯA XÁC NHẬN, vòng sau thử lại).");
                                hasUnconfirmed = true;
                                break;
                            }

                            var (advanced, nextError) = await ClickNextPageAsync(page);
                            if (advanced)
                            {
                                pageNum++;
                                continue;
                            }
                            if (nextError is not null)
                            {
                                // Lỗi khi sang trang kế KHÔNG phải "hết trang": phần sau của tab chưa được quét.
                                hasUnconfirmed = true;
                                _log($"⚠ Lỗi khi sang trang kế của tab '{tabName}' (đã quét {pageNum} trang) — tab CHƯA XÁC NHẬN, không coi là hết trang: {nextError}");
                                break;
                            }

                            _log($"{(hasUnconfirmed ? "⚠ Hết trang (có trang chưa xác nhận)" : "✔ Hết trang")} tab '{tabName}' (đã quét {pageNum} trang) — {_importedIds.Count}/{_importIds.Count} SP đã import.");
                            break;
                        }

                        if (!confirmed)
                            _log($"⚠ Trang {pageNum} (tab {tabName}) CHƯA XÁC NHẬN nhưng có {checkedIds.Length} SP khớp — vẫn gửi lô (tick theo ảnh→id), vòng sau quét lại nếu còn thiếu.");
                        _log($"Trang {pageNum} (tab {tabName}) có {rowCount} SP — {checkedIds.Length} SP khớp item id → Import to Stores…");

                        // committed chỉ bật khi SelectImportShopAndConfirmAsync trả về bình thường = lô ĐÃ GỬI (click trả về + modal đóng
                        // và theo dõi sau click không thấy gì bất thường; điều hướng sau click chỉ tính là đã gửi nếu có hộp "Hộp nháp") — KHÔNG bật
                        // trước khi click như bản cũ (click timeout mà vẫn MarkDone cả lô). Ca CHƯA CHẮC: xem bên dưới.
                        var committed = false;
                        ImportOutcomeUncertainException? uncertain = null;
                        try
                        {
                            await ClickToolbarImportAsync(page);

                            var modal = page.Locator(".ant-modal-content:visible").Last;
                            await modal.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20000 });
                            await SelectImportShopAndConfirmAsync(page, modal, _settings.ShopName, checkedIds.Length, cancellationToken);
                            committed = true;

                            importCount++;
                            _log($"Đã Import {checkedIds.Length} SP theo item id (#{importCount}).");
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (ImportOutcomeUncertainException ex)
                        {
                            uncertain = ex;
                        }
                        catch (Exception ex)
                        {
                            // Lỗi TRƯỚC khi bấm hoặc click timeout → chưa gửi, chạy tiếp như cũ. Lô này bị bỏ qua trong vòng
                            // này nên đánh dấu để vòng sau (nếu còn) thử lại.
                            hasUnconfirmed = true;
                            _log($"Import lô lỗi — CHƯA gửi, không MarkDone (giữ phiên, thử lại vòng/lượt sau): {ex.Message}");
                        }

                        if (uncertain is not null)
                        {
                            // CHƯA CHẮC lô đã gửi hay chưa: không MarkDone và DỪNG import shop này ngay — không sang trang/tab kế,
                            // không tick lại, để cùng lượt không gửi trùng. AllImported = false → shop ghi stopped; người dùng
                            // kiểm tra BigSeller rồi chạy lại (hoặc "Mark as done" nếu lô đã vào).
                            // BigSeller báo thất bại (hộp tiến trình có Failed > 0) cũng đi đường dừng này nhưng log riêng, khỏi lẫn với "chưa chắc".
                            if (uncertain.ReportedFailure)
                                _log($"✖ BigSeller BÁO THẤT BẠI lô import — KHÔNG MarkDone, DỪNG import shop '{_settings.ShopName}' (lô có thể chưa vào hoặc vào một phần, kiểm tra BigSeller trước khi chạy lại): {uncertain.Message}");
                            else
                                _log($"⚠ Import lô CHƯA CHẮC đã gửi — KHÔNG MarkDone, DỪNG import shop '{_settings.ShopName}' (CÓ THỂ BigSeller đã nhận lô): {uncertain.Message}");
                            _log($"  Lô gồm {checkedIds.Length} SP (item id): {string.Join(", ", checkedIds)}");
                            LogLedgerSummary();
                            return;
                        }

                        if (committed)
                        {
                            sentThisPass = true;
                            foreach (var id in checkedIds)
                            {
                                _importedIds.Add(id);
                                if (_rowByImportId.TryGetValue(id, out var row))
                                {
                                    RowsDone?.Invoke(row, row);
                                    _ledgerHitCount++;
                                }
                                else
                                {
                                    _ledgerMissCount++;
                                    if (_ledgerMissCount == 1 || _ledgerMissCount % 10 == 0)
                                        _log($"⚠ SP import xong nhưng không khớp được dòng sheet (import-id={id}) → Thống kê Hub sẽ thiếu dòng ({_ledgerMissCount} SP như vậy).");
                                }
                            }

                            OpProgressStore.Shared.MarkDone(_settings.AccountId, _settings.ProgressKey, AssignmentOps.Import,
                                checkedIds.Select(id => new KeyValuePair<string, string?>(id, null)));
                            if (_settings.UseHubData) _ = MarkImportedHubAsync(checkedIds);
                        }

                        if (_importedIds.Count >= _importIds.Count)
                        {
                            _log($"✔ Đã import đủ {_importedIds.Count}/{_importIds.Count} SP — dừng ngay, không cần quét tiếp.");
                            LogLedgerSummary();
                            return;
                        }

                        await DelayAsync(2000, cancellationToken);
                        await BigSellerCrawlHelper.DismissPostImportDialogsAsync(page, _log);

                        // Còn modal hiển thị sau khi đã đóng hộp tiến trình (Finish/Escape/X không đóng nổi): không chạy lô sau kẻo
                        // hộp cũ lẫn vào kết quả của lô mới hoặc chặn thao tác. Lô vừa rồi đã MarkDone nên dừng ở đây không mất gì.
                        var leftoverWraps = await page.Locator(".ant-modal-wrap:visible").CountAsync();
                        if (leftoverWraps > 0)
                        {
                            var leftoverTexts = string.Join(" | ", (await ReadVisibleModalsAsync(page)).Select(m => m.Text));
                            _log($"✖ Còn {leftoverWraps} hộp thoại sau khi đóng hộp tiến trình import — DỪNG import shop '{_settings.ShopName}', không chạy lô sau: \"{ImportPostClickClassifier.Trim(leftoverTexts)}\"");
                            LogLedgerSummary();
                            return;
                        }

                        // Tab 'Chưa nhận' + lô ĐÃ GỬI (committed): SP vừa import rời tab nên danh sách dồn lên — bấm Next sẽ BỎ SÓT SP vừa dồn
                        // vào trang này. Tải lại Crawl List (qua cơ chế xác nhận trang; GoToCrawlPageAsync(forceReload) mở lại URL Crawl List nên
                        // về TRANG 1, không phải trang hiện tại → pageNum đặt lại 1), quay lại đầu vòng: còn SP khớp thì gửi lô tiếp, hết SP khớp
                        // mới bấm Next như luồng cũ (nhánh 0 SP khớp ở trên). Không lặp vô hạn: SP đã gửi nằm trong _importedIds nên không được
                        // tick lại, mỗi lô mới thêm ≥ 1 id. Lô lỗi thường (chưa gửi, không SP nào rời tab) tải lại sẽ quay về đúng lô đó và lặp
                        // mãi → bấm Next như trước. Tab 'Đã nhận': SP ở lại tab → giữ Next.
                        if (ImportScanPolicy.NextStepAfterBatch(committed, currentTab == BigSellerCrawlTab.Unclaimed) == AfterBatchStep.ReloadFirstPage)
                        {
                            _log($"Tab '{tabName}': SP đã import rời tab nên tải lại Crawl List (về trang 1; đang ở trang {pageNum}, không bấm Next) để xét tiếp các SP vừa dồn lên.");
                            reloadedAfterBatch = checkedIds;
                            MarkPageAction();
                            if (await BigSellerCrawlHelper.GoToCrawlPageAsync(page, forceReload: true, targetUrl: crawlUrl, log: _log))
                                pageNum = 1;
                            else
                                _log("⚠ Chưa tải lại được Crawl List sau lô — đầu vòng sẽ thử lại.");
                            continue;
                        }

                        var (advancedAfterImport, nextErrorAfterImport) = await ClickNextPageAsync(page);
                        if (advancedAfterImport)
                        {
                            pageNum++;
                            continue;
                        }
                        if (nextErrorAfterImport is not null)
                        {
                            hasUnconfirmed = true;
                            _log($"⚠ Lỗi khi sang trang kế của tab '{tabName}' (đã quét {pageNum} trang) — tab CHƯA XÁC NHẬN, không coi là hết trang: {nextErrorAfterImport}");
                            break;
                        }

                        _log($"✔ Hết trang tab '{tabName}' (đã quét {pageNum} trang) — {_importedIds.Count}/{_importIds.Count} SP đã import.");
                        break;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        consecutiveErrors++;
                        _log($"⚠ Lỗi vòng lặp ({consecutiveErrors}/{maxConsecutiveErrors}) — giữ phiên, thử hồi phục rồi chạy tiếp: {ex.Message}");
                        if (consecutiveErrors >= maxConsecutiveErrors)
                        {
                            _log($"✖ Quá {maxConsecutiveErrors} lỗi liên tiếp không đọc nổi danh sách — dừng phiên. Lỗi cuối: {ex.Message}");
                            throw;
                        }
                        try { page = await RecoverPageAsync(page, crawlUrl, currentTab, cancellationToken).ConfigureAwait(false); }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception rex) { _log($"  (chưa hồi phục được tab/CDP lần này: {rex.Message})"); }
                        await DelayAsync(3000, cancellationToken);
                    }
                }
            }

            if (!ImportScanPolicy.ShouldRescan(pass, ImportScanPolicy.MaxPasses, AllImported, sentThisPass, hasUnconfirmed)) break;
            pass++;
        }

        int finalCacheCount; lock (_apiLock) finalCacheCount = _apiImageKeyToShopeeId.Count;
        if (finalCacheCount == 0)
            _log("⚠ Không đọc được item id nào từ API crawl (pageList.json) — BigSeller đổi endpoint/response? Không import được gì.");

        if (_importedIds.Count < _importIds.Count)
            LogFoundVsWorkbook();

        if (hasUnconfirmed)
            _log($"⚠ Kết thúc sau {pass}/{ImportScanPolicy.MaxPasses} vòng nhưng còn tab/trang CHƯA XÁC NHẬN — KHÔNG phải đã quét sạch; tổng cộng {_importedIds.Count}/{_importIds.Count} SP đã import.");
        else
            _log($"✔ Đã quét xong cả 2 tab (Chưa nhận & Đã nhận) sau {pass}/{ImportScanPolicy.MaxPasses} vòng — tổng cộng {_importedIds.Count}/{_importIds.Count} SP đã import. Kết thúc.");
        LogLedgerSummary();
    }

    // Seed _importedIds từ tiến độ Import ĐÃ LƯU của shop (xoá seed cũ trước: gọi lại được sau khi Clear đã bỏ id khỏi tiến độ).
    private void SeedImportedFromProgress()
    {
        _importedIds.Clear();
        var doneImport = OpProgressStore.Shared.GetDone(_settings.AccountId, _settings.ProgressKey, AssignmentOps.Import);
        var seeded = 0;
        foreach (var id in doneImport.Keys)
            if (_importIds.Contains(id)) { _importedIds.Add(id); seeded++; }
        if (seeded > 0) _log($"Bỏ qua {seeded} SP đã import trước đó (tiến độ đã lưu).");
    }

    // Số SP ở Import Done của shop mà CHƯA có trong Update Done = đúng những SP mà lần Clear nháp sắp tới sẽ bỏ khỏi bảng import.
    private int CountImportedWithoutUpdate()
    {
        var imported = OpProgressStore.Shared.GetDone(_settings.AccountId, _settings.ProgressKey, AssignmentOps.Import);
        var updated = OpProgressStore.Shared.GetDone(_settings.AccountId, _settings.ProgressKey, AssignmentOps.Update);
        return imported.Keys.Count(id => !updated.ContainsKey(id));
    }

    // Khoá Import của tài khoản còn SP chờ Update (Update chưa completed), đã lọc: workbook chung TẮT thì khoá shared:... cũ không tính (và log MỘT dòng
    // liệt kê chúng, kèm tên shop nếu tìm được shop theo id trong account.Shops).
    private IReadOnlyList<string> GetPendingUpdateKeys()
    {
        var all = OpProgressStore.Shared.ImportKeysPendingUpdate(_settings.AccountId);
        if (SharedKeysInUse) return all;
        var kept = OpProgressStore.Shared.ImportKeysPendingUpdate(_settings.AccountId, ignoreSharedKeys: true);
        var ignored = all.Where(k => !kept.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
        if (ignored.Count > 0 && !_ignoredSharedKeysLogged)
        {
            _ignoredSharedKeysLogged = true;
            _log($"↷ Workbook chung đang TẮT — bỏ qua {ignored.Count} khoá tiến độ cũ dạng shared:... (dữ liệu cũ, không tính là chờ Update, không reset): " +
                 string.Join(", ", ignored.Select(DescribeKey)) + ".");
        }
        return kept;
    }

    // Tên khoá tiến độ để log: khoá shared:{shopId} kèm tên shop nếu tìm được shop theo id trong account.Shops, không thì giữ nguyên (đã chứa id).
    private string DescribeKey(string key)
    {
        IEnumerable<BigSellerShop>? shops = null;
        try { shops = BigSellerStore.Shared.Find(_settings.AccountId)?.Shops; } catch { }
        return ImportScanPolicy.DescribeProgressKey(key, shops);
    }

    // Gom đầu vào cho ImportScanPolicy.DecideImportStart từ trạng thái runner (phải gọi SAU LoadUpdateTargetsAsync cho đúng pendingKeys).
    private ImportStartDecision DecideStart(IReadOnlyList<string> pendingKeys, bool hubPending) =>
        ImportScanPolicy.DecideImportStart(_importIds.Count, _importedIds.Count, hubPending, _settings.ProgressKey,
            CountImportRemovableOfShop(), pendingKeys, _updateTargetsByKey.Keys.ToList());

    // Thi hành quyết định điều phối. true = đi tiếp (Clear rồi import); false = kết thúc import shop (đã log lý do).
    private bool ApplyStartDecision(ImportStartDecision d, bool braveOpen)
    {
        switch (d.Kind)
        {
            case ImportStartKind.ExitEarly when d.ExitReason == ImportExitReason.NoItemIds:
                _log("Sheet không có item id nào trong khoảng dòng đã đặt — không có gì để import. Kết thúc.");
                return false;   // AllImported = false → shop ghi "stopped"
            case ImportStartKind.ExitEarly:
                _clearSettled = true;   // Clear sẽ không bỏ id nào của shop khỏi bảng import → không cần Clear, "đủ" là đủ thật
                _log(braveOpen
                    ? $"✔ Đã import đủ {_importedIds.Count}/{_importIds.Count} SP và Clear không làm mất SP nào của shop (mọi SP đã Update) — không Clear, không import thêm. Kết thúc."
                    : $"✔ Đã import đủ {_importedIds.Count}/{_importIds.Count} SP theo tiến độ đã lưu và mọi SP đã Update — không mở Brave. Kết thúc.");
                return false;
            case ImportStartKind.Blocked:
                var keys = string.Join(", ", d.BlockedKeys.Select(DescribeKey));
                ClearBlockedReason = $"không biết mục tiêu Update của {d.BlockedKeys.Count} khoá còn SP đã import mà chưa Update xong ({keys}), Clear sẽ làm mất SP của các khoá đó";
                _log($"✖ KHÔNG Clear nháp — DỪNG import shop '{_settings.ShopName}' ({(braveOpen ? "chưa mở menu Bulk Actions" : "chưa mở Brave")}, chưa xoá gì, không reset bảng import, không ghi completed). " +
                     $"Không biết mục tiêu Update của {d.BlockedKeys.Count} khoá còn SP đã import mà chưa Update xong: {keys}. " +
                     "Clear sẽ xoá nháp của các khoá này mà bảng import không biết SP nào phải import lại (mất SP vĩnh viễn). " +
                     "Nguyên nhân thường gặp: khoá shared:... của shop khác trong workbook chung, sheet không có trong file nguồn, file nguồn đang bị khoá/lỗi (xem dòng log phía trên). " +
                     "Xử lý: chạy Update cho các khoá đó đến khi xong, hoặc sửa file nguồn rồi chạy lại.");
                return false;
            default:
                return true;
        }
    }

    // Số SP của shop mà Clear sẽ làm mất khỏi bảng import: ở Import Done, thuộc mục tiêu Update, chưa có Update Done, Update của khoá chưa completed.
    // Khoá của shop không có trong _updateTargetsByKey (sheet không đọc được) → 0: Clear sẽ không bỏ id nào của khoá đó (và lúc đó Clear bị chặn bởi DecideClearGate).
    private int CountImportRemovableOfShop() =>
        _updateTargetsByKey.TryGetValue(_settings.ProgressKey, out var targets)
            ? OpProgressStore.Shared.CountImportRemovable(_settings.AccountId, _settings.ProgressKey, targets)
            : 0;

    // Đọc mục tiêu Update (dòng workbook có tên đã sửa — cùng định nghĩa và ánh xạ cột của luồng Update) của các khoá Import có id thiếu Update Done
    // (OpProgressStore.ImportKeysPendingUpdate). Khoá của chính shop đang chạy đọc theo DataSheet (workbook chung: khoá là shared:..., không phải tên sheet).
    // Lỗi đọc → từ điển rỗng, tức mọi khoá chờ Update là "không biết mục tiêu" và Clear bị chặn (DecideClearGate); lần gọi sau đọc lại.
    private async Task LoadUpdateTargetsAsync(IReadOnlyList<string> keys, CancellationToken ct)
    {
        if (keys.Count == 0) return;
        if (keys.All(_targetKeysRead.Contains)) return;   // lượt chạy này đã đọc thành công cho đúng các khoá này
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _log($"Đọc mục tiêu Update của {keys.Count} khoá ({string.Join(", ", keys)}) trong file nguồn — để biết SP nào mất khi Clear phải import lại (file lớn, có thể chậm)...");
        try
        {
            var targets = await WorkbookRecordCache.LoadUpdateTargetsByKeyAsync(_settings, _settings.ProgressKey, keys, _log, ct).ConfigureAwait(false);
            _updateTargetsByKey = targets;
            _targetKeysRead = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
            _log($"Đã đọc mục tiêu Update của {targets.Count}/{keys.Count} khoá trong {sw.Elapsed.TotalSeconds:0.0}s.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _updateTargetsByKey = new Dictionary<string, IReadOnlySet<string>>();
            _targetKeysRead = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _log($"⚠ Không đọc được mục tiêu Update sau {sw.Elapsed.TotalSeconds:0.0}s ({ex.Message}) — các khoá còn SP chờ Update coi như KHÔNG biết mục tiêu.");
        }
    }

    // Clear nháp trước import (BigSellerDraftClearer). true = đi tiếp import; false = kết thúc import shop (đã log lý do):
    //  • TRƯỚC khi mở menu Bulk Actions: đọc mục tiêu Update của MỌI khoá còn SP chờ Update; có khoá không biết mục tiêu (shared:... của shop khác,
    //    sheet không có, file lỗi) → không Clear, không reset bảng, không ghi completed (ImportScanPolicy.DecideClearGate);
    //  • lỗi/hộp lạ TRƯỚC Confirm → chưa xoá gì, KHÔNG import vào trạng thái lạ;
    //  • hộp confirm đúng câu + nút đúng Confirm → NGAY LẬP TỨC, TRƯỚC khi bấm Confirm, bỏ khỏi Import Done mọi SP mục tiêu Update chưa Update xong của cả
    //    tài khoản (nháp của chúng sắp bị xoá), hub-mode thì reset cả mốc "đã import" phía Hub. Không bấm được Confirm sau đó → Aborted, log rõ "đã reset
    //    bảng nhưng chưa chắc đã xoá nháp" (không hoàn tác reset: SP chỉ bị import lại, nháp trùng bị xoá ở lần Clear sau);
    //  • hết 10 phút mà nháp chưa về 0 → dừng;
    //  • sau cùng mới nạp lại danh sách (hub-mode) và seed _importedIds.
    private async Task<bool> ClearDraftsBeforeImportAsync(IPage page, CancellationToken ct)
    {
        _log(new string('=', 50));
        _log("XOÁ SẠCH NHÁP TRƯỚC KHI IMPORT (Bulk Actions → Clear)");
        _log(new string('=', 50));

        // Clear xoá nháp của CẢ tài khoản. Mọi khoá có SP đã import mà chưa Update xong phải biết mục tiêu Update thì bảng import mới reset đúng;
        // đọc ngay đây (không phụ thuộc shop đang chạy có SP chờ Update hay không) và chặn trước khi chạm vào BigSeller.
        var pendingKeys = GetPendingUpdateKeys();
        await LoadUpdateTargetsAsync(pendingKeys, ct).ConfigureAwait(false);
        if (!ApplyStartDecision(DecideStart(pendingKeys, _settings.UseHubData && CountImportedWithoutUpdate() > 0), braveOpen: true)) return false;

        var progressResetOk = true;
        var resetRan = false;
        var clearer = new BigSellerDraftClearer(page, _log, DelayAsync);
        DraftClearResult result;
        try
        {
            result = await clearer.ClearAsync(async () =>
            {
                resetRan = true;
                progressResetOk = false;   // chỉ true khi reset chạy xong trọn vẹn (ném lỗi giữa chừng vẫn là chưa trọn vẹn)
                progressResetOk = await ResetImportProgressBeforeConfirmAsync().ConfigureAwait(false);
                return progressResetOk;
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            // Bảng import local đã (cố) reset: _importedIds (seed từ trước Clear) giờ LỆCH với bảng. Seed lại trước MỌI lối ra (Confirm không
            // bấm được, TimedOut, reset lỗi, Hub lỗi, hủy...) để AllImported / các nhánh "đủ" không dựa vào số liệu cũ.
            if (resetRan) SeedImportedFromProgress();
        }

        switch (result.Status)
        {
            case DraftClearStatus.Aborted:
                if (resetRan)
                    _log($"✖ Đã reset bảng import nhưng chưa chắc đã xoá nháp — DỪNG import shop '{_settings.ShopName}' (không import vào trạng thái lạ; kiểm tra BigSeller rồi chạy lại): {result.Message}");
                else
                    _log($"✖ Không Clear được nháp — DỪNG import shop '{_settings.ShopName}' (chưa xoá gì, không import vào trạng thái lạ): {result.Message}");
                return false;
            case DraftClearStatus.TimedOut:
                _log($"✖ {result.Message} DỪNG import shop '{_settings.ShopName}' (bảng import đã reset; chạy lại sau khi BigSeller xoá xong).");
                return false;
            case DraftClearStatus.NothingToClear:
                _log($"✔ Không có gì để xoá — chưa xoá gì và KHÔNG reset bảng import: {result.Message}");
                break;
            default:
                _log($"✔ Đã xoá sạch nháp: {result.Message}");
                break;
        }

        if (!progressResetOk)
        {
            _log($"✖ Reset bảng import sau Clear KHÔNG trọn vẹn — DỪNG import shop '{_settings.ShopName}' để khỏi bỏ sót SP âm thầm (xem dòng lỗi phía trên; sửa rồi chạy lại).");
            return false;
        }

        if (result.Status == DraftClearStatus.Cleared)
        {
            // Hub-mode: tập id lấy từ Hub (đã lọc SP đã import) → phải nạp lại sau khi mốc Hub được reset. Excel-mode: tập id không đổi.
            if (_settings.UseHubData) await LoadImportItemIdSetAsync(ct).ConfigureAwait(false);
            SeedImportedFromProgress();
        }
        _clearSettled = true;   // tới đây: Cleared hoặc NothingToClear, bảng import (nếu có reset) đã reset trọn vẹn

        if (_importIds.Count == 0)
        {
            _log("Không có item id nào để import sau khi Clear. Kết thúc.");
            return false;
        }
        if (_importedIds.Count >= _importIds.Count)
        {
            _log($"✔ Đã import đủ {_importedIds.Count}/{_importIds.Count} SP — không cần quét Crawl List. Kết thúc.");
            return false;
        }
        return true;
    }

    // Ngay TRƯỚC khi bấm Confirm Clear: bỏ khỏi Import Done mọi SP mục tiêu Update chưa có Update Done (cho MỌI khoá của tài khoản mà biết được mục tiêu;
    // khoá Update đã completed thì giữ nguyên). true = xong trọn vẹn.
    private async Task<bool> ResetImportProgressBeforeConfirmAsync()
    {
        try
        {
            // Khoá tiến độ mà runner biết thuộc shop này: ProgressKey (khoá runner ghi) + tên shop (một trong các khoá ứng viên mà
            // WorkspaceShopViewModel xét "Import xong"). Không thêm DataSheet: ở workbook chung nó là sheet gộp dùng chung nhiều shop.
            var shopKeys = new[] { _settings.ProgressKey, _settings.ShopName };
            var removed = OpProgressStore.Shared.ResetImportWithoutUpdate(_settings.AccountId, out var sheets, out var skipped, shopKeys, _updateTargetsByKey,
                ignoreSharedKeys: !SharedKeysInUse);   // workbook chung tắt: khoá shared:... cũ không bị đụng (và không tính là "không biết mục tiêu")
            var detail = sheets.Count == 0 ? "" : " (" + string.Join(", ", sheets.Select(s => $"{s.Sheet}: {s.Removed}")) + ")";
            _log($"↺ Bảng import: đã bỏ {removed} SP mục tiêu Update chưa Update xong khỏi Import Done của tài khoản{detail} — sẽ import lại.");
            if (skipped.Count > 0)
            {
                // Cổng Clear lẽ ra đã chặn ca này; tới đây là tiến độ đổi giữa lúc kiểm cổng và lúc reset. Clear sẽ xoá nháp của khoá không biết mục tiêu → KHÔNG Confirm.
                _log($"✖ {skipped.Count} khoá còn SP chờ Update mà không biết mục tiêu Update (Import Done giữ nguyên): {string.Join(", ", skipped.Select(DescribeKey))} — KHÔNG Confirm Clear, nháp của chúng sẽ mất mà bảng import không biết.");
                return false;
            }
        }
        catch (Exception ex)
        {
            _log($"✖ Không reset được bảng import local sau Clear: {ex.Message}");
            return false;
        }
        return !_settings.UseHubData || await ResetHubImportMarksAsync().ConfigureAwait(false);
    }

    // Hub-mode: Hub giữ mốc "đã import" riêng (product_rows.store_imported_at) và GetImportIds lọc theo mốc đó, nên nếu không reset thì SP bị
    // Clear sẽ KHÔNG bao giờ được import lại. API duy nhất có sẵn là reset-store-progress theo (tài khoản + sheet + op) — xoá mốc của CẢ sheet — nên
    // sau đó đánh dấu lại các SP mà tiến độ local biết là đã xong (Import Done + Update Done còn lại). Chỉ làm được cho sheet của shop đang
    // chạy (tên sheet Hub của khoá khác không suy ra được từ tiến độ local).
    private async Task<bool> ResetHubImportMarksAsync()
    {
        try
        {
            var client = CoordinationRuntime.Client;
            if (client is null)
            {
                _log("✖ Hub-mode nhưng không có kết nối Hub để reset mốc 'đã import' — SP vừa bị Clear sẽ bị Hub lọc bỏ.");
                return false;
            }
            var reset = await client.ResetProductStoreProgressAsync(_settings.AccountId, _settings.DataSheet, AssignmentOps.Import).ConfigureAwait(false);
            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in OpProgressStore.Shared.GetDone(_settings.AccountId, _settings.ProgressKey, AssignmentOps.Import).Keys) keep.Add(id);
            foreach (var id in OpProgressStore.Shared.GetDone(_settings.AccountId, _settings.ProgressKey, AssignmentOps.Update).Keys) keep.Add(id);
            var ids = keep.ToArray();
            for (var i = 0; i < ids.Length; i += 1000)
                await client.MarkProductImportedAsync(_settings.AccountId, _settings.DataSheet, ids[i..Math.Min(i + 1000, ids.Length)]).ConfigureAwait(false);
            _log($"↺ Hub: đã xoá mốc 'đã import' của sheet '{_settings.DataSheet}' ({reset} dòng) rồi đánh dấu lại {ids.Length} SP đã xong (Import/Update).");
            return true;
        }
        catch (Exception ex)
        {
            _log($"✖ Reset mốc 'đã import' trên Hub lỗi: {ex.Message}");
            return false;
        }
    }

    // Brave chết hẳn (tab crash/OOM/đóng) → khởi động lại Brave cùng profile (phiên login bền trên đĩa),
    // chờ CDP rồi kết nối lại. Dùng khi vòng lặp gặp lỗi mà tiến trình Brave đã thoát.
    private async Task RestartBrowserAsync(CancellationToken ct)
    {
        if (_browser is not null)
        {
            try { await _browser.DisposeAsync(); } catch { }
            _browser = null;
        }
        // Kịch bản kill + reap dùng chung (Core). Brave fork browser thật rồi stub thoát → Kill no-op, browser
        // cũ giữ profile lock: phải diệt orphan theo --user-data-dir TRƯỚC khi mở lại cùng profile, kẻo
        // instance mới đụng lock/port cũ.
        BraveTeardown.KillAndReap(ref _braveProcess, _settings.ProfileDir, new BraveTeardownOptions { Log = _log });

        StartBrave();
        _log($"  Đã khởi động lại Brave PID={_braveProcess?.Id.ToString() ?? "unknown"}, chờ CDP port {_settings.DebugPort}…");
        await EnsureCdpReadyAsync(30,
            $"CDP port {_settings.DebugPort} không sẵn sàng sau khi khởi động lại Brave.", ct).ConfigureAwait(false);

        await ConnectBrowserAsync(ct).ConfigureAwait(false);
    }

    // Hồi phục sau lỗi vòng lặp: Brave chết → khởi động lại; rớt CDP → kết nối lại; tab chết → mở/tìm lại
    // tab BigSeller. Trả về 1 page sống đang ở Crawl List để vòng lặp chạy tiếp.
    private async Task<IPage> RecoverPageAsync(IPage current, string crawlUrl, BigSellerCrawlTab currentTab, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (_braveProcess is null || _braveProcess.HasExited)
        {
            _log("  Brave đã thoát — khởi động lại…");
            await RestartBrowserAsync(ct).ConfigureAwait(false);
        }
        else if (_browser is null || !_browser.IsConnected)
        {
            _log("  Mất kết nối CDP — kết nối lại…");
            await ConnectBrowserAsync(ct).ConfigureAwait(false);
        }

        var context = _browser!.Contexts.FirstOrDefault()
            ?? throw new InvalidOperationException("Brave chưa có context sau khi hồi phục.");
        var page = await FindBigSellerPageAsync(context, ct).ConfigureAwait(false);
        if (page is null || page.IsClosed)
            page = await context.NewPageAsync().ConfigureAwait(false);
        AttachApiCapture(page);   // tab mới sau hồi phục → gắn lại listener pageList.json

        await page.BringToFrontAsync().ConfigureAwait(false);
        MarkPageAction();   // tải lại Crawl List → trang đầu phải qua cơ chế xác nhận response mới
        await BigSellerCrawlHelper.GoToCrawlPageAsync(page, forceReload: true, targetUrl: crawlUrl, log: _log).ConfigureAwait(false);
        await SwitchToTabAsync(page, currentTab, ct).ConfigureAwait(false);
        _log("  ✓ Đã hồi phục tab BigSeller — chạy tiếp.");
        return page;
    }

    /// <summary>Chọn tab Crawl List ("Chưa nhận" hoặc "Đã nhận").</summary>
    private async Task<bool> SwitchToTabAsync(IPage page, BigSellerCrawlTab tab, CancellationToken cancellationToken)
    {
        var tabName = tab == BigSellerCrawlTab.Unclaimed ? "Chưa nhận" : "Đã nhận";
        _log($"--- Chọn tab '{tabName}' ---");
        // Mốc cho "trang đã xác nhận": CHỈ khi thật sự đổi tab. Tab đã active thì bấm lại có thể không tải lại bảng →
        // giữ mốc cũ (response của lần tải/tải lại trước đó vẫn là dữ liệu hiện hành), kẻo chờ response không bao giờ đến.
        var alreadyActive = false;
        try { alreadyActive = await BigSellerCrawlHelper.IsCrawlTabActiveAsync(page, tab); } catch { }
        if (!alreadyActive) MarkPageAction();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Gỡ guide "đổi ngôn ngữ" TRƯỚC khi click tab — chính nó hay gây reload đưa trang về tab "new".
            await BigSellerCrawlHelper.DismissGuideMasksAsync(page, _log);
            // Popup hướng dẫn đổi ngôn ngữ (KHÔNG phải mask/ant-modal) cũng chặn click — thử chọn Tiếng Việt/đóng.
            await BigSellerCrawlHelper.DismissLanguageGuideAsync(page, _log, cancellationToken);
            var ok = await BigSellerCrawlHelper.SelectCrawlTabAsync(page, tab, _log);
            if (ok)
            {
                await DelayAsync(1000, cancellationToken);
                return true;
            }

            _log($"Tab '{tabName}' chưa chọn được (lần {attempt}/3), chờ trang load...");
            await DelayAsync(2000, cancellationToken);

            if (attempt == 2)
            {
                _log("Reload trang để thử lại chọn tab...");
                await BigSellerCrawlHelper.GoToCrawlPageAsync(page, forceReload: true,
                    targetUrl: BigSellerCrawlHelper.ResolveCrawlUrl(_settings.CrawlUrl), log: _log);
                await DelayAsync(2000, cancellationToken);
            }
        }

        _log($"Cảnh báo: Không chọn được tab '{tabName}'.");
        return false;
    }

    // ── HỢP ĐỒNG DOM: modal "chọn shop import" ──────────────
    // Selector các label shop trong modal (3 mức hẹp → rộng) — dùng chung các hàm eval.
    private const string ShopLabelSelector =
        ".cont_btm.btmOut label.ant-checkbox-wrapper, .btmOut label.ant-checkbox-wrapper, label.ant-checkbox-wrapper";

    // Khối khai báo JS DÙNG CHUNG cho các hàm eval bên dưới: dựng danh sách `stores` = label shop HIỂN THỊ (bỏ
    // "select all") theo thứ tự DOM + 2 helper đọc nhãn/trạng thái tick. KHÔNG so tên shop ở JS: C# đọc nhãn,
    // ImportShopMatcher.Pick chọn CHỈ SỐ, JS chỉ tick theo chỉ số → mọi hàm eval PHẢI dựng `stores`
    // bằng đúng khối này (lệch thứ tự/bộ lọc = tick nhầm shop). Hợp đồng với DOM BigSeller — ĐỪNG "tiện tay" sửa.
    private const string StoreListJs = @"
                const normalize = value => (value || '')
                    .normalize('NFD')
                    .replace(/[̀-ͯ]/g, '')
                    .replace(/đ/g, 'd')
                    .replace(/Đ/g, 'd')
                    .replace(/\s+/g, ' ')
                    .trim()
                    .toLowerCase();
                const labelText = label => (label.textContent || '').replace(/\s+/g, ' ').trim();
                const isVisible = el => {
                    const rect = el.getBoundingClientRect();
                    return rect.width > 0 && rect.height > 0;
                };
                const stores = Array.from(root.querySelectorAll('" + ShopLabelSelector + @"'))
                    .filter(label => isVisible(label) && label.querySelector('input[type=checkbox]'))
                    .filter(label => !normalize(labelText(label)).includes('select all'));
                const isChecked = label => {
                    const input = label.querySelector('input[type=checkbox]');
                    const box = label.querySelector('.ant-checkbox');
                    return !!(input?.checked || box?.classList.contains('ant-checkbox-checked') || label.classList.contains('ant-checkbox-wrapper-checked'));
                };";

    // Nhãn các shop đang hiển thị trong modal, đúng thứ tự `stores` của StoreListJs.
    private static Task<string[]> ReadImportShopLabelsAsync(ILocator modal) =>
        modal.EvaluateAsync<string[]>(
            "root => {" + StoreListJs + @"
                return stores.map(labelText);
            }");

    // Đã bắt đầu bấm "Import to Stores" nhưng KHÔNG biết lô đã gửi hay chưa (lỗi lạ ngay trong ClickAsync, modal không
    // đóng sau 15s, lỗi khác sau click). Caller KHÔNG MarkDone và DỪNG import shop này ngay (không gửi lại trong lượt).
    // ReportedFailure = chính BigSeller báo thất bại trên hộp tiến trình (Failed > 0): cùng đường dừng, chỉ khác dòng log.
    private sealed class ImportOutcomeUncertainException : Exception
    {
        public bool ReportedFailure { get; }
        public ImportOutcomeUncertainException(string message, bool reportedFailure = false) : base(message) { ReportedFailure = reportedFailure; }
    }

    // Phân loại kết quả (ném = không MarkDone; trả về bình thường = lô đã gửi):
    //  • lỗi TRƯỚC khi bấm, hoặc ClickAsync timeout → exception thường: CHƯA gửi, caller chạy tiếp, lượt sau thử lại.
    //  • Click trả về + modal import đóng → theo dõi (MonitorPostClickAsync, ImportPostClickClassifier): hộp tiến trình
    //    "Importing… Finish" chờ tới trạng thái cuối (≤120s), Successfully = Total = expectedCount (số SP đã tick) và Failed = 0 →
    //    trả về bình thường (đã gửi); gặp hộp "Hộp nháp"/"Draft Listing" không kèm lỗi → đã gửi; KHÔNG thấy hộp nào sau 5s → chưa chắc.
    //  • Click trả về rồi lỗi ĐIỀU HƯỚNG THẬT (CdpErrors.IsRealNavigationError) khi chờ → cũng đi qua bước theo dõi ở trên.
    //  • MỌI ca còn lại sau khi bắt đầu bấm (lỗi lạ trong ClickAsync, modal không đóng 15s, "Target closed"/"WebSocket"/
    //    Brave chết, URL lệch, toast lỗi, hộp lạ, hộp "Hộp nháp" kèm lỗi, trang điều hướng mà không thấy hộp "Hộp nháp",
    //    không đọc được URL) → ImportOutcomeUncertainException (CHƯA CHẮC).
    // Giới hạn: chưa biết endpoint/thông báo thành công của BigSeller nên "modal đóng" chỉ là tín hiệu gián tiếp.
    private async Task SelectImportShopAndConfirmAsync(IPage page, ILocator modal, string shopName, int expectedCount, CancellationToken ct)
    {
        _log($"Chon shop import: {shopName}");
        var labels = await ReadImportShopLabelsAsync(modal);
        var index = ImportShopMatcher.Pick(labels, shopName);
        // Không chắc chắn đúng 1 shop thì KHÔNG import (ném TRƯỚC mọi click Import → lô không bị đánh dấu gửi).
        if (index < 0)
        {
            var available = string.Join(" | ", labels.Where(l => !string.IsNullOrWhiteSpace(l)));
            throw new InvalidOperationException(index == ImportShopMatcher.Ambiguous
                ? $"Nhieu shop import cung khop '{shopName}', khong chac chon shop nao nen KHONG import. Available: {available}"
                : $"Khong tim thay shop import: {shopName}. Available: {available}");
        }
        var pickedLabel = labels[index];

        var selectedLabel = await modal.EvaluateAsync<string>(
            "(root, pick) => {" + StoreListJs + @"
                const targetLabel = stores[pick.index];
                if (!targetLabel || labelText(targetLabel) !== pick.label)
                    throw new Error('Danh sach shop trong modal da doi giua luc doc va luc chon, thu lai lan sau.');
                const clickLabel = label => {
                    const target = label.querySelector('.ant-checkbox-inner') ||
                        label.querySelector('.ant-checkbox') ||
                        label.querySelector('input[type=checkbox]') ||
                        label;
                    target.click();
                };
                for (const label of stores) {
                    if (label === targetLabel) continue;
                    if (isChecked(label)) clickLabel(label);
                }
                if (!isChecked(targetLabel)) clickLabel(targetLabel);
                return labelText(targetLabel) || pick.label;
            }",
            new { index, label = pickedLabel });

        await DelayAsync(500, CancellationToken.None);
        var isChecked = await IsImportShopCheckedAsync(modal, index, pickedLabel);
        if (!isChecked)
            throw new InvalidOperationException($"Khong chon duoc shop import: {shopName}");

        _log($"Da chon shop import: {selectedLabel}");
        var importButton = modal.Locator("button.ant-btn-primary:has-text('Import to Stores'), button:has-text('Import to Stores')").First;

        // Chụp các toast tính-lỗi ĐANG có TRƯỚC click (toast cũ chưa tắt) để loại khỏi kết quả sau click. Đọc lỗi ở đây = lỗi
        // TRƯỚC khi bấm (chưa gửi), không phải ca chưa chắc.
        var toastsBeforeClick = new HashSet<string>((await ReadToastsAsync(page)).Counted, StringComparer.Ordinal);
        // Tương tự với hộp thoại đang hiện (kể cả chính modal chọn shop): Monitor bỏ qua chúng, chỉ xét hộp MỚI xuất hiện sau click.
        var modalsBeforeClick = (await ReadVisibleModalsAsync(page)).Select(m => m.Text).ToList();

        // Đếm điều hướng của MAIN FRAME từ TRƯỚC click đến hết cửa sổ theo dõi (gỡ handler ở finally). Driver Playwright hay
        // nuốt lỗi "context destroyed" nên không thể chỉ dựa vào exception để biết trang vừa reload/điều hướng.
        var navCount = 0;
        EventHandler<IFrame> onNavigated = (_, frame) => { if (frame.ParentFrame is null) Interlocked.Increment(ref navCount); };
        page.FrameNavigated += onNavigated;
        try
        {
            // TỪ ĐÂY là vùng "đang bấm": mọi lỗi không phải click-timeout đều là CHƯA CHẮC (xem ImportOutcomeUncertainException).
            try
            {
                await importButton.ClickAsync(new() { Force = true, Timeout = 10000 });
            }
            catch (Exception ex) when (IsPlaywrightTimeout(ex))
            {
                throw;   // click timeout = chưa bấm được → chưa gửi (lỗi thường, caller thử lại vòng/lượt sau)
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Lỗi khác ném NGAY TRONG ClickAsync (kể cả điều hướng): không biết chuột đã nhả chưa → chưa chắc.
                throw new ImportOutcomeUncertainException($"lỗi trong lúc bấm 'Import to Stores' (shop '{selectedLabel}'): {ex.Message}");
            }

            // Click đã TRẢ VỀ. Chờ modal đóng bằng locator CẤP PAGE theo nội dung (không qua `modal` = `.Last`: locator
            // tính lại mỗi lần poll, hộp confirm/lỗi đè lên thì `.Last` đổi sang hộp đó, không còn nút → "đã đóng" giả).
            // Dùng CountAsync thay WaitFor(Hidden) để khỏi dính strict-mode khi có >1 phần tử khớp.
            var openModal = page.Locator(".ant-modal-content:visible:has(button:has-text('Import to Stores'))");
            var deadline = DateTime.UtcNow.AddSeconds(15);
            try
            {
                while (await openModal.CountAsync() > 0)
                {
                    if (DateTime.UtcNow >= deadline)
                        throw new ImportOutcomeUncertainException($"đã bấm 'Import to Stores' nhưng modal không đóng sau 15s (shop '{selectedLabel}').");
                    await DelayAsync(300, ct);
                }
            }
            catch (ImportOutcomeUncertainException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (CdpErrors.IsRealNavigationError(ex))
            {
                // Trang điều hướng sau click (context bị huỷ): chưa đủ để coi là đã gửi → đi tiếp sang bước theo dõi bên dưới.
                _log($"Trang điều hướng sau khi bấm Import ({ex.Message}) — theo dõi URL/hộp thoại trước khi coi lô đã gửi.");
            }
            catch (Exception ex)
            {
                // Mọi lỗi khác (kể cả "Target closed"/"WebSocket"/Brave chết) KHÔNG chứng minh lô đã gửi → chưa chắc.
                throw new ImportOutcomeUncertainException($"lỗi sau khi bấm 'Import to Stores' (shop '{selectedLabel}'): {ex.Message}");
            }

            await MonitorPostClickAsync(page, () => Volatile.Read(ref navCount) > 0, selectedLabel, toastsBeforeClick, modalsBeforeClick, expectedCount, ct);
        }
        finally
        {
            page.FrameNavigated -= onNavigated;
        }
    }

    // Sau khi modal "Import to Stores" đóng, BigSeller hiện hộp tiến trình "Importing… Finish" (Total / Successfully / Failed).
    // Theo dõi (poll ~300ms) TRƯỚC DismissPostImportDialogsAsync (hàm đó đóng sạch mọi hộp, kể cả hộp lỗi → mất dấu vết). Mỗi
    // lần đọc: URL, các modal hiển thị, toast lỗi/thông báo; ImportPostClickClassifier.Classify xét (expectedCount = số SP đã tick):
    //  • Hộp tiến trình còn chạy → chờ tiếp tối đa 120s; hết giờ → chưa chắc. Hộp biến mất khi chưa thấy trạng thái cuối → chưa chắc.
    //  • Hộp tiến trình đủ số: Successfully = Total = expectedCount, Failed = 0 → đã gửi (trả về). Failed > 0 → BigSeller báo thất
    //    bại (ReportedFailure); Total lệch expectedCount → chưa chắc.
    //  • Toast lỗi, URL lệch, hộp lạ, hộp "Hộp nháp"/"Draft Listing" kèm lỗi → chưa chắc NGAY. Gặp hộp "Hộp nháp" (đã xét lỗi) → đã gửi.
    //  • Chưa thấy hộp nào sau 5s → chưa chắc (không có bằng chứng đã gửi); có điều hướng của main frame mà không có hộp "Hộp nháp" → chưa chắc.
    //  • Hộp đã hiện TRƯỚC khi bấm (modalsBeforeClick, so theo nội dung) bị bỏ qua: hộp cũ chưa đóng không làm lô này "đã gửi" oan.
    private async Task MonitorPostClickAsync(
        IPage page, Func<bool> navigated, string selectedLabel, HashSet<string> toastsBeforeClick, List<string> modalsBeforeClick, int expectedCount, CancellationToken ct)
    {
        const int quietSeconds = 5, progressMaxSeconds = 120;
        var start = DateTime.UtcNow;
        var quietEnd = start.AddSeconds(quietSeconds);
        var progressEnd = start.AddSeconds(progressMaxSeconds);
        string? lastBoxLogged = null;
        string? lastProgressLogged = null;
        var progressSeen = false;
        var loggedInfoToasts = new HashSet<string>(StringComparer.Ordinal);
        ImportPostClickVerdict verdict;
        bool urlOnCrawl; List<OpenModalInfo> modals; List<string> toasts;
        while (true)
        {
            List<string> infoToasts;
            (urlOnCrawl, modals, toasts, infoToasts) = await ReadPostClickStateAsync(page, toastsBeforeClick, ct);
            modals = ImportPostClickClassifier.ExcludeExistingModals(modals, modalsBeforeClick);

            // Thông báo KHÔNG phải lỗi/cảnh báo: chỉ log nội dung (một lần mỗi nội dung), không làm chưa chắc.
            foreach (var info in infoToasts)
                if (loggedInfoToasts.Add(info))
                    _log($"Thông báo sau Import (không tính lỗi): \"{ImportPostClickClassifier.Trim(info)}\"");

            // LUÔN log nội dung hộp kết quả (cắt 300 ký tự) — kể cả khi coi là đã gửi — để soi hộp thật khi chạy thật.
            foreach (var box in modals.Where(m => m.HasDraftBoxLink && !ImportPostClickClassifier.IsImportProgressBox(m.Text)))
            {
                var text = ImportPostClickClassifier.Trim(box.Text);
                if (text == lastBoxLogged) continue;
                lastBoxLogged = text;
                _log($"Hộp kết quả sau Import: \"{text}\"");
            }

            // Hộp tiến trình "Importing… Finish": log mỗi khi nội dung đổi (lấy mẫu từng trạng thái, kể cả trạng thái cuối).
            var progressBoxes = modals.Where(m => ImportPostClickClassifier.IsImportProgressBox(m.Text)).ToList();
            foreach (var box in progressBoxes)
            {
                var text = ImportPostClickClassifier.Trim(box.Text);
                if (text == lastProgressLogged) continue;
                lastProgressLogged = text;
                _log($"Hộp tiến trình sau Import: \"{text}\"");
            }
            if (progressBoxes.Count > 0) progressSeen = true;

            // Chưa tính điều hướng ở đây: hộp "Hộp nháp" có thể hiện muộn hơn một nhịp so với lúc điều hướng.
            verdict = ImportPostClickClassifier.Classify(urlOnCrawl, modals, toasts, navigated: false, expectedCount);
            if (verdict.Kind == ImportVerdictKind.InProgress)
            {
                if (DateTime.UtcNow >= progressEnd)
                    throw new ImportOutcomeUncertainException(
                        $"hộp tiến trình import chưa có kết quả cuối sau {progressMaxSeconds}s (shop '{selectedLabel}', hộp cuối: \"{lastProgressLogged}\", URL={page.Url})");
                await DelayAsync(300, ct);
                continue;
            }
            if (!verdict.Sent) break;
            if (progressBoxes.Count > 0 || modals.Any(m => m.HasDraftBoxLink)) return;   // hộp tiến trình đã xong / hộp kết quả đã biết, không lỗi → đã gửi
            if (progressSeen)
                throw new ImportOutcomeUncertainException(
                    $"hộp tiến trình import biến mất trước khi thấy kết quả cuối (shop '{selectedLabel}', hộp cuối: \"{lastProgressLogged}\", URL={page.Url})");
            if (DateTime.UtcNow >= quietEnd)
            {
                // Hết cửa sổ mà không thấy hộp tiến trình lẫn hộp "Hộp nháp": KHÔNG có bằng chứng đã gửi → chưa chắc (requireEvidence).
                verdict = ImportPostClickClassifier.Classify(urlOnCrawl, modals, toasts, navigated(), expectedCount, requireEvidence: true);
                break;
            }
            await DelayAsync(300, ct);
        }
        if (!verdict.Sent)
            throw new ImportOutcomeUncertainException(
                $"{verdict.Reason} (shop '{selectedLabel}', URL={page.Url})", verdict.Kind == ImportVerdictKind.ReportedFailure);
    }

    // Toast đang hiển thị, chia 2: Counted = tính lỗi (ant-message lỗi — selector theo BigSellerMaterialCenterCleaner.ErrorToast;
    // ant-notification có icon lỗi/cảnh báo); InfoOnly = ant-notification loại khác (success/info/không icon), chỉ để log.
    // Tên lớp icon `.ant-notification-notice-icon-error|warning` là lớp chuẩn của ant-design, KHÔNG có bằng chứng trong repo.
    private static async Task<(List<string> Counted, List<string> InfoOnly)> ReadToastsAsync(IPage page)
    {
        var raw = await page.EvaluateAsync<string[][]>(
            @"() => {
                const vis = el => { const r = el.getBoundingClientRect(); return r.width > 0 && r.height > 0; };
                const text = el => (el.innerText || el.textContent || '').replace(/\s+/g, ' ').trim() || '(trống)';
                const out = [];
                for (const el of document.querySelectorAll('div.ant-message .ant-message-custom-content.ant-message-error'))
                    if (vis(el)) out.push([text(el), '1']);
                for (const el of document.querySelectorAll('.ant-notification-notice'))
                    if (vis(el)) out.push([text(el), el.querySelector('.ant-notification-notice-icon-error, .ant-notification-notice-icon-warning') ? '1' : '0']);
                return out;
            }");
        var counted = new List<string>();
        var info = new List<string>();
        foreach (var t in raw ?? [])
        {
            var text = t.Length > 0 ? t[0] : "";
            if (t.Length > 1 && t[1] == "1") counted.Add(text); else info.Add(text);
        }
        return (counted, info);
    }

    // URL có còn là Crawl List không + các modal đang hiển thị (nội dung, có link "Hộp nháp" không) + toast lỗi/thông báo.
    // Điều hướng đang dở (lỗi context bị huỷ) thì thử lại tối đa 3 lần; lỗi khác hoặc hết lần → chưa chắc.
    // Toasts = toast tính-lỗi MỚI (đã loại những cái có sẵn trước click); InfoToasts = thông báo loại khác (chỉ để log).
    private async Task<(bool UrlOnCrawl, List<OpenModalInfo> Modals, List<string> Toasts, List<string> InfoToasts)> ReadPostClickStateAsync(
        IPage page, HashSet<string> toastsBeforeClick, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var urlOnCrawl = BigSellerCrawlHelper.IsCrawlPage(page, _settings.CrawlUrl);
                var modals = await ReadVisibleModalsAsync(page);
                var (counted, infoOnly) = await ReadToastsAsync(page);
                return (urlOnCrawl, modals, counted.Where(t => !toastsBeforeClick.Contains(t)).ToList(), infoOnly);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (CdpErrors.IsRealNavigationError(ex) && attempt < 3)
            {
                await DelayAsync(1000, ct);
            }
            catch (Exception ex)
            {
                throw new ImportOutcomeUncertainException($"không đọc được URL/hộp thoại sau khi bấm Import: {ex.Message}");
            }
        }
    }

    // Các modal đang hiển thị (nội dung + có link "Hộp nháp"/"Draft Listing" không). Link so không phân biệt hoa thường
    // (giống :has-text ở DismissPostImportDialogsAsync).
    private static async Task<List<OpenModalInfo>> ReadVisibleModalsAsync(IPage page)
    {
        var raw = await page.Locator(".ant-modal-content:visible").EvaluateAllAsync<string[][]>(
            @"els => els.map(el => [
                (el.innerText || el.textContent || '').replace(/\s+/g, ' ').trim(),
                Array.from(el.querySelectorAll('a.has_underline')).some(a => { const t = (a.textContent || '').toLowerCase(); return t.includes('hộp nháp') || t.includes('draft listing'); }) ? '1' : '0'
            ])");
        return (raw ?? [])
            .Select(m => new OpenModalInfo(m.Length > 0 ? m[0] : "", m.Length > 1 && m[1] == "1"))
            .ToList();
    }

    private static bool IsPlaywrightTimeout(Exception ex) =>
        ex is TimeoutException || (ex is PlaywrightException && ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase));

    private static Task<bool> IsImportShopCheckedAsync(ILocator modal, int index, string label) =>
        modal.EvaluateAsync<bool>(
            "(root, pick) => {" + StoreListJs + @"
                const targetLabel = stores[pick.index];
                if (!targetLabel || labelText(targetLabel) !== pick.label) return false;
                return isChecked(targetLabel);
            }",
            new { index, label });

    // ── Helper cho IMPORT CẢ LÔ ─────────────────────────────────────────────────
    // Số dòng dữ liệu đang hiển thị trong bảng (có dữ liệu mới chọn-tất-cả + import).
    private static Task<int> GetBodyRowCountAsync(IPage page) =>
        page.EvaluateAsync<int>(
            @"() => Array.from(document.querySelectorAll('tr.vxe-body--row'))
                .filter(row => { const r = row.getBoundingClientRect(); return r.width > 0 && r.height > 0; }).length");

    // ── Bắc cầu DOM ↔ item id qua API pageList.json ─────────────────────────────
    // BigSeller KHÔNG render source item id ra HTML (ô .vxe-cell--label là email, link tiêu đề không href).
    // Nên bắt response API danh sách, trích id từ crawlUrl, và nhận diện dòng DOM qua ẢNH (mainImage ↔ <img src>).
    private void AttachApiCapture(IPage page)
    {
        if (ReferenceEquals(_capturePage, page)) return;
        if (_capturePage is not null) { try { _capturePage.Response -= OnCrawlResponse; } catch { } }
        _capturePage = page;
        page.Response += OnCrawlResponse;
    }

    private void OnCrawlResponse(object? sender, IResponse response)
    {
        if (!response.Url.Contains("/product/crawl/pageList.json", StringComparison.OrdinalIgnoreCase)) return;
        int seq;
        lock (_apiLock) seq = ++_respArrivalSeq;   // số thứ tự theo lúc ĐẾN, gán trước khi đọc body
        _ = CaptureCrawlRowsAsync(response, seq);
    }

    // Đọc rows từ 1 response pageList.json → map ẢNH(đuôi mainImage) ↔ shopee id(từ crawlUrl). Gộp dồn (ảnh là
    // duy nhất theo SP nên gộp nhiều trang vẫn đúng); best-effort, nuốt lỗi để không đụng vòng lặp import.
    // Đồng thời ghi lại "response mới nhất" (seq + MỌI khoá ảnh của nó, kể cả dòng không rút được id) để xác nhận trang.
    private async Task CaptureCrawlRowsAsync(IResponse response, int seq)
    {
        HashSet<string>? responseKeys = null;   // null = không đọc được → trang không bao giờ được xác nhận bằng response này
        try
        {
            var text = await response.TextAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return;
            JsonElement rows;
            if (!((data.TryGetProperty("page", out var pageEl) && pageEl.TryGetProperty("rows", out rows) && rows.ValueKind == JsonValueKind.Array)
                  || (data.TryGetProperty("rows", out rows) && rows.ValueKind == JsonValueKind.Array)))
                return;
            var added = 0;
            var allKeys = new HashSet<string>(StringComparer.Ordinal);
            lock (_apiLock)
            {
                foreach (var r in rows.EnumerateArray())
                {
                    var img = r.TryGetProperty("mainImage", out var mi) ? mi.GetString() : null;
                    var url = r.TryGetProperty("crawlUrl", out var cu) ? cu.GetString() : null;
                    var id = BigSellerCrawlHelper.ExtractShopeeId(url);
                    var key = ImgKey(img);
                    if (!string.IsNullOrEmpty(key)) allKeys.Add(key);
                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(id)) { _apiImageKeyToShopeeId[key] = id!; added++; }
                }
            }
            responseKeys = allKeys;
            if (added > 0) _log($"Đọc {added} item id từ API crawl (cache {_apiImageKeyToShopeeId.Count}).");
        }
        catch { responseKeys = null; /* best-effort */ }
        finally
        {
            // Response đến SAU thắng dù đọc xong TRƯỚC (seq theo lúc đến) → không bị response cũ đọc chậm đè.
            lock (_apiLock)
            {
                if (seq > _latestSeq) { _latestSeq = seq; _latestKeys = responseKeys; }
            }
        }
    }

    // Bản JS của ImgKey (chạy TRONG trang) — dùng chung 2 hàm eval quét ảnh dòng. Phải cho CÙNG kết quả
    // với <see cref="ImgKey"/> bên C#: khoá ảnh API trả về phải khớp khoá ảnh đọc từ DOM thì mới bắc cầu được.
    private const string ImgKeyJs = @"
                const key = src => { if (!src) return ''; const u = src.split('?')[0].replace(/\/+$/, ''); const p = u.split('/'); return p[p.length - 1] || ''; };";

    private void LogWorkbookIds()
    {
        var list = _rowByImportId.OrderBy(kv => kv.Value).Select(kv => $"dòng {kv.Value}={kv.Key}");
        _log("ID workbook: " + string.Join(", ", list));
    }

    /// <summary>Khi dừng mà chưa import hết: liệt kê id trang crawl và id workbook để thấy vì sao không khớp.</summary>
    private void LogFoundVsWorkbook()
    {
        List<string> found;
        lock (_apiLock) found = _apiImageKeyToShopeeId.Values.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var sheet = _importIds.OrderBy(x => x, StringComparer.Ordinal).ToList();
        var foundSet = new HashSet<string>(found, StringComparer.Ordinal);
        var onlyPage = found.Where(id => !_importIds.Contains(id)).ToList();
        var onlySheet = sheet.Where(id => !foundSet.Contains(id)).ToList();
        var already = sheet.Where(id => foundSet.Contains(id) && _importedIds.Contains(id)).ToList();
        var pending = sheet.Where(id => foundSet.Contains(id) && !_importedIds.Contains(id)).ToList();
        _log($"ID trang crawl ({found.Count}): {string.Join(", ", found)}");
        _log($"ID workbook ({sheet.Count}): {string.Join(", ", sheet)}");
        _log($"Trùng và còn cần import ({pending.Count}): {JoinOrNone(pending)}");
        _log($"Trùng nhưng đã import nên bỏ ({already.Count}): {JoinOrNone(already)}");
        _log($"Chỉ có trên trang, không có trong workbook ({onlyPage.Count}): {JoinOrNone(onlyPage)}");
        _log($"Chỉ có trong workbook, không thấy trên trang ({onlySheet.Count}): {JoinOrNone(onlySheet)}");
    }

    private static string JoinOrNone(IReadOnlyList<string> ids) => ids.Count == 0 ? "(không)" : string.Join(", ", ids);

    // Khoá ảnh = đoạn cuối URL ảnh (bỏ query) — cầu nối vì cả JSON (mainImage) lẫn DOM (<img src>) đều có.
    private static string ImgKey(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        var noQuery = url.Split('?')[0].TrimEnd('/');
        var idx = noQuery.LastIndexOf('/');
        return idx >= 0 && idx < noQuery.Length - 1 ? noQuery[(idx + 1)..] : noQuery;
    }

    // XÁC NHẬN TRANG trước khi kết luận khớp/không khớp: phải có response pageList.json MỚI hơn mốc _pageMark (sinh ra sau
    // thao tác đổi trang) VÀ ảnh trên DOM thuộc response đó (response rỗng → DOM không còn dòng). Chờ tối đa ~15s; hết giờ
    // thì đọc lại MỘT lần (sau 2s) rồi mới coi là CHƯA XÁC NHẬN → caller KHÔNG được kết luận "0 khớp / đã quét sạch"
    // (vẫn tick và gửi nếu có SP khớp) và đặt cờ chưa xác nhận. NoNewPageData = thật sự không có dữ liệu trang mới (xem
    // ImportScanPolicy.HasNoNewPageData) — dùng cho phanh.
    // (Thay WaitForApiCoverageAsync cũ: nó thoả ngay khi cache tích luỹ đã có BẤT KỲ khoá ảnh nào của DOM — dòng trang
    // cũ còn trên DOM, hay DOM rỗng khi đang tải, đều qua được → "0 khớp" giả → bấm Next bỏ sót trang.)
    private async Task<(bool Confirmed, bool NoNewPageData)> WaitForConfirmedPageAsync(IPage page, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (await IsPageConfirmedAsync(page)) return (true, false);
            if (DateTime.UtcNow >= deadline) break;
            await DelayAsync(300, ct);
        }
        await DelayAsync(2000, ct);
        if (await IsPageConfirmedAsync(page)) return (true, false);
        var (text, noNewData) = await DescribeUnconfirmedAsync(page);
        _log($"  Trang chưa xác nhận: {text}");
        return (false, noNewData);
    }

    // Chẩn đoán cho log "chưa xác nhận": cùng dữ liệu IsPageConfirmedAsync dùng, in đủ để biết sai ở response hay DOM;
    // kèm cờ "không có dữ liệu trang mới" cho phanh.
    private async Task<(string Text, bool NoNewPageData)> DescribeUnconfirmedAsync(IPage page)
    {
        bool newer; HashSet<string>? responseKeys;
        lock (_apiLock) { newer = _latestSeq > _pageMark; responseKeys = _latestKeys; }
        var rowCount = await GetBodyRowCountAsync(page);
        var domKeys = await GetVisibleImageKeysAsync(page);
        return (ImportScanPolicy.DescribeUnconfirmed(newer, responseKeys, rowCount, domKeys),
                ImportScanPolicy.HasNoNewPageData(newer, responseKeys, domKeys));
    }

    // Bấm Next trên Crawl List (chụp mốc trang TRƯỚC khi bấm). Trả (đã sang trang, lỗi): false + lỗi null = hết trang thật;
    // false + lỗi ≠ null = lỗi khi bấm/chờ (phần sau của tab chưa được quét, KHÔNG phải hết trang).
    private async Task<(bool Advanced, string? Error)> ClickNextPageAsync(IPage page)
    {
        string? error = null;
        MarkPageAction();
        var advanced = await BigSellerCrawlHelper.ClickNextCrawlPageAsync(page, _log, onError: m => error = m);
        return (advanced, advanced ? null : error);
    }

    private async Task<bool> IsPageConfirmedAsync(IPage page)
    {
        bool newer; HashSet<string>? responseKeys;
        lock (_apiLock) { newer = _latestSeq > _pageMark; responseKeys = _latestKeys; }
        if (!newer || responseKeys is null) return false;
        var rowCount = await GetBodyRowCountAsync(page);
        var domKeys = await GetVisibleImageKeysAsync(page);
        return ImportScanPolicy.IsPageConfirmed(newer, responseKeys, rowCount, domKeys);
    }

    // Chỉ lấy dòng HIỂN THỊ (cùng cách lọc với GetBodyRowCountAsync): vxe fixed-column/ẩn có thể render dòng ngoài
    // tầm nhìn với ảnh khác → khoá "lạ" làm trang không bao giờ xác nhận được.
    private static Task<string[]> GetVisibleImageKeysAsync(IPage page) =>
        page.EvaluateAsync<string[]>(
            "() => {" + ImgKeyJs + @"
                const set = new Set();
                for (const tr of Array.from(document.querySelectorAll('tr.vxe-body--row'))) {
                    const r = tr.getBoundingClientRect();
                    if (!(r.width > 0 && r.height > 0)) continue;
                    const img = tr.querySelector('img');
                    if (img) { const k = key(img.getAttribute('src')); if (k) set.add(k); }
                }
                return Array.from(set);
            }");

    // Trang hiện tại có phải CHỈ gồm các SP của lô vừa import không (mọi ảnh hiển thị đều map ra id ∈ lô, và có ít nhất một dòng)?
    // Dùng để log đúng vòng sau khi tải lại: true = SP không rời tab (trang y nguyên), false = trang đã đổi nhưng không còn SP khớp.
    private async Task<bool> PageShowsOnlyImportedAsync(IPage page, string[] batchIds)
    {
        string[] keys;
        try { keys = await GetVisibleImageKeysAsync(page); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return false; }
        if (keys.Length == 0) return false;
        var batch = new HashSet<string>(batchIds, StringComparer.Ordinal);
        lock (_apiLock)
            return keys.All(k => _apiImageKeyToShopeeId.TryGetValue(k, out var id) && batch.Contains(id));
    }

    // Tick từng dòng có item id ∈ tập cần import (và CHƯA import), nhận diện dòng qua KHOÁ ẢNH lấy từ API.
    // vxe fixed-column có thể render 2 bản CÙNG rowid: bản body chứa <img>, bản fixed-left chứa checkbox HIỂN
    // THỊ → gom theo rowid rồi ghép. Trả về mảng shopee id vừa tick (lô import).
    private async Task<string[]> CheckMatchingRowsOnPageAsync(IPage page)
    {
        Dictionary<string, string> toTick;
        lock (_apiLock)
        {
            toTick = _apiImageKeyToShopeeId
                .Where(kv => _importIds.Contains(kv.Value) && !_importedIds.Contains(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        }
        if (toTick.Count == 0) return Array.Empty<string>();

        var clicked = await page.EvaluateAsync<string[]>(
            "(map) => {" + ImgKeyJs + @"
                const vis = el => { const r = el.getBoundingClientRect(); return r.width > 0 && r.height > 0; };
                const byId = new Map();   // rowid -> { key, checkboxCell, icon }
                for (const tr of Array.from(document.querySelectorAll('tr.vxe-body--row'))) {
                    const rowid = tr.getAttribute('rowid');
                    if (!rowid) continue;
                    let e = byId.get(rowid);
                    if (!e) { e = { key: '', checkboxCell: null, icon: null }; byId.set(rowid, e); }
                    if (!e.key) {
                        const img = tr.querySelector('img');
                        if (img) { const k = key(img.getAttribute('src')); if (k && map[k]) e.key = k; }
                    }
                    if (!e.checkboxCell) {
                        const cb = Array.from(tr.querySelectorAll('.vxe-cell--checkbox')).find(vis);
                        if (cb) { e.checkboxCell = cb; e.icon = cb.querySelector('.vxe-checkbox--icon'); }
                    }
                }
                const clicked = [];
                for (const e of byId.values()) {
                    if (!e.key || !map[e.key] || !e.checkboxCell) continue;
                    const checked = e.icon && e.icon.classList.contains('vxe-icon-checkbox-checked');
                    if (!checked) e.checkboxCell.click();
                    clicked.push(map[e.key]);
                }
                return clicked;
            }",
            toTick);
        return clicked ?? Array.Empty<string>();
    }

    // Bấm 'Import to Stores' trên THANH CÔNG CỤ (cả lô) — KHÔNG phải nút từng dòng (a.action_btn) / nút trong modal.
    // Gỡ lớp phủ hướng dẫn (vd .language_switch_guide_mask) hay CHẶN click TRƯỚC; nếu click thật vẫn bị overlay
    // chặn (intercepts pointer events) → gỡ lại + click bằng JS (gọi thẳng handler, bỏ qua pointer-events).
    private async Task ClickToolbarImportAsync(IPage page)
    {
        await BigSellerCrawlHelper.DismissGuideMasksAsync(page, _log);
        var btn = page.Locator(
            ".bs-antd-button.mode-sucess button:has-text('Import to Stores'), " +
            ".rows .lt button:has-text('Import to Stores')").First;
        try
        {
            await btn.ClickAsync(new() { Timeout = 10000 });
        }
        catch
        {
            await BigSellerCrawlHelper.DismissGuideMasksAsync(page, _log);
            // Popup hướng dẫn đổi ngôn ngữ (KHÔNG phải mask/ant-modal) cũng chặn click — thử chọn Tiếng Việt/đóng.
            await BigSellerCrawlHelper.DismissLanguageGuideAsync(page, _log, CancellationToken.None);
            var ok = await page.EvaluateAsync<bool>(
                @"() => {
                    const b = Array.from(document.querySelectorAll(
                        '.bs-antd-button.mode-sucess button, .rows .lt button, button'))
                        .find(x => (x.textContent || '').includes('Import to Stores'));
                    if (!b) return false;
                    b.click();
                    return true;
                }");
            if (!ok) throw;
            _log("Đã bấm Import to Stores bằng JS (lớp phủ hướng dẫn chặn click thường).");
        }
    }

    private Task<IPage?> FindBigSellerPageAsync(IBrowserContext context, CancellationToken ct)
    {
        foreach (var p in context.Pages.ToArray())
        {
            ct.ThrowIfCancellationRequested();
            if (BigSellerCrawlHelper.IsCrawlPage(p, _settings.CrawlUrl))
                return Task.FromResult<IPage?>(p);
        }

        foreach (var p in context.Pages.ToArray())
        {
            if ((p.Url ?? "").Contains("bigseller.com", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<IPage?>(p);
        }

        return Task.FromResult<IPage?>(context.Pages.FirstOrDefault());
    }

    private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        _pauseToken?.DelayAsync(delay, cancellationToken) ?? Task.Delay(delay, cancellationToken);

    // RESUME (hub-mode): báo Hub các itemId vừa import → GetImportIds lượt sau lọc bớt. Khung chung ở base
    // (BigSellerBraveRunner.MarkStoreProgressHubAsync) — best-effort, lỗi mạng KHÔNG làm hỏng lượt chạy.
    private Task MarkImportedHubAsync(string[] ids) => MarkStoreProgressHubAsync(
        (client, items, ct) => client.MarkProductImportedAsync(_settings.AccountId, _settings.DataSheet, items, ct),
        ids, "imported", "tiến độ local");

    // Tổng kết ledger khi import kết thúc BÌNH THƯỜNG: đã báo Thống kê bao nhiêu dòng vs bao nhiêu SP import xong
    // mà không khớp được dòng sheet → soi nhanh vì sao Thống kê Hub thiếu dòng import (miss=0 mà Thống kê vẫn thiếu
    // ⇒ ngờ TryPublish/Hub, xem DiagLog; miss cao ⇒ id crawl ≠ id sheet, cần soi cách khớp _rowByImportId).
    private void LogLedgerSummary() =>
        _log($"Σ import: đã báo Thống kê {_ledgerHitCount} dòng · không khớp dòng sheet {_ledgerMissCount} SP.");

    // Detach listener API pageList.json trước khi base kill Brave (phần dọn Brave chung ở BigSellerBraveRunner).
    protected override void OnBeforeDispose()
    {
        if (_capturePage is not null) { try { _capturePage.Response -= OnCrawlResponse; } catch { } _capturePage = null; }
    }
}
