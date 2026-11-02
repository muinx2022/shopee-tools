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

    /// <summary>True khi MỌI item id trong khoảng dòng của sheet đã import (đã lưu từ trước + vừa gửi lượt này).
    /// Sheet không có id nào → false (không có gì để coi là "xong").</summary>
    public bool AllImported => _importIds.Count > 0 && _importedIds.Count >= _importIds.Count;

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
        if (_importIds.Count == 0)
        {
            _log("Sheet không có item id nào trong khoảng dòng đã đặt — không có gì để import. Kết thúc.");
            return;   // AllImported = false (cần _importIds.Count > 0) → LastRunComplete = false → shop ghi "stopped", KHÔNG phải completed
        }

        // RESUME: seed _importedIds từ tiến độ ĐÃ LƯU (kill/dừng giữa chừng rồi chạy lại → KHÔNG import trùng —
        // chốt chặn cũ chỉ là HashSet trong RAM, mất khi thoát). Chỉ lấy id CÒN trong _importIds (khoảng dòng /
        // tập id có thể đã đổi giữa 2 lần chạy). Đủ hết ngay từ tiến độ → khỏi mở Brave.
        var doneImport = OpProgressStore.Shared.GetDone(_settings.AccountId, _settings.ProgressKey, AssignmentOps.Import);
        var seeded = 0;
        foreach (var id in doneImport.Keys)
            if (_importIds.Contains(id)) { _importedIds.Add(id); seeded++; }
        if (seeded > 0) _log($"Bỏ qua {seeded} SP đã import trước đó (tiến độ đã lưu).");
        if (_importedIds.Count >= _importIds.Count)
        {
            _log($"✔ Đã import đủ {_importedIds.Count}/{_importIds.Count} SP theo tiến độ đã lưu — không mở Brave. Kết thúc.");
            return;
        }

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

                        if (checkedIds.Length == 0)
                        {
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
                            await SelectImportShopAndConfirmAsync(page, modal, _settings.ShopName, cancellationToken);
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
    private sealed class ImportOutcomeUncertainException : Exception
    {
        public ImportOutcomeUncertainException(string message) : base(message) { }
    }

    // Phân loại kết quả (ném = không MarkDone; trả về bình thường = lô đã gửi):
    //  • lỗi TRƯỚC khi bấm, hoặc ClickAsync timeout → exception thường: CHƯA gửi, caller chạy tiếp, lượt sau thử lại.
    //  • Click trả về + modal import đóng → theo dõi ≤5s (MonitorPostClickAsync, ImportPostClickClassifier): hết 5s không thấy
    //    gì bất thường, hoặc gặp hộp "Hộp nháp" không kèm lỗi → trả về bình thường (đã gửi).
    //  • Click trả về rồi lỗi ĐIỀU HƯỚNG THẬT (CdpErrors.IsRealNavigationError) khi chờ → cũng đi qua bước theo dõi ở trên.
    //  • MỌI ca còn lại sau khi bắt đầu bấm (lỗi lạ trong ClickAsync, modal không đóng 15s, "Target closed"/"WebSocket"/
    //    Brave chết, URL lệch, toast lỗi, hộp lạ, hộp "Hộp nháp" kèm lỗi, trang điều hướng mà không thấy hộp "Hộp nháp",
    //    không đọc được URL) → ImportOutcomeUncertainException (CHƯA CHẮC).
    // Giới hạn: chưa biết endpoint/thông báo thành công của BigSeller nên "modal đóng" chỉ là tín hiệu gián tiếp.
    private async Task SelectImportShopAndConfirmAsync(IPage page, ILocator modal, string shopName, CancellationToken ct)
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

            await MonitorPostClickAsync(page, () => Volatile.Read(ref navCount) > 0, selectedLabel, toastsBeforeClick, ct);
        }
        finally
        {
            page.FrameNavigated -= onNavigated;
        }
    }

    // Theo dõi tối đa 5s (poll ~300ms) sau khi modal import đóng, TRƯỚC DismissPostImportDialogsAsync (hàm đó đóng sạch mọi
    // hộp, kể cả hộp lỗi → mất dấu vết). Mỗi lần đọc: URL, các modal hiển thị, toast lỗi/thông báo. Toast lỗi, URL lệch,
    // hộp lạ, hộp "Hộp nháp" kèm lỗi → chưa chắc NGAY. Gặp hộp "Hộp nháp" (đã xét lỗi) → thoát sớm = đã gửi. Hết 5s không
    // thấy gì: có điều hướng của main frame mà không có hộp "Hộp nháp" → chưa chắc; không thì đã gửi.
    private async Task MonitorPostClickAsync(
        IPage page, Func<bool> navigated, string selectedLabel, HashSet<string> toastsBeforeClick, CancellationToken ct)
    {
        var end = DateTime.UtcNow.AddSeconds(5);
        string? lastBoxLogged = null;
        var loggedInfoToasts = new HashSet<string>(StringComparer.Ordinal);
        ImportPostClickVerdict verdict;
        bool urlOnCrawl; List<OpenModalInfo> modals; List<string> toasts;
        while (true)
        {
            List<string> infoToasts;
            (urlOnCrawl, modals, toasts, infoToasts) = await ReadPostClickStateAsync(page, toastsBeforeClick, ct);

            // Thông báo KHÔNG phải lỗi/cảnh báo: chỉ log nội dung (một lần mỗi nội dung), không làm chưa chắc.
            foreach (var info in infoToasts)
                if (loggedInfoToasts.Add(info))
                    _log($"Thông báo sau Import (không tính lỗi): \"{ImportPostClickClassifier.Trim(info)}\"");

            // LUÔN log nội dung hộp kết quả (cắt 300 ký tự) — kể cả khi coi là đã gửi — để soi hộp thật khi chạy thật.
            foreach (var box in modals.Where(m => m.HasDraftBoxLink))
            {
                var text = ImportPostClickClassifier.Trim(box.Text);
                if (text == lastBoxLogged) continue;
                lastBoxLogged = text;
                _log($"Hộp kết quả sau Import: \"{text}\"");
            }

            // Chưa tính điều hướng ở đây: hộp "Hộp nháp" có thể hiện muộn hơn một nhịp so với lúc điều hướng.
            verdict = ImportPostClickClassifier.Classify(urlOnCrawl, modals, toasts, navigated: false);
            if (!verdict.Sent) break;
            if (modals.Any(m => m.HasDraftBoxLink)) return;   // hộp kết quả đã biết, không lỗi → đã gửi
            if (DateTime.UtcNow >= end)
            {
                verdict = ImportPostClickClassifier.Classify(urlOnCrawl, modals, toasts, navigated());
                break;
            }
            await DelayAsync(300, ct);
        }
        if (!verdict.Sent)
            throw new ImportOutcomeUncertainException($"{verdict.Reason} (shop '{selectedLabel}', URL={page.Url})");
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
                // Link "Hộp nháp" so không phân biệt hoa thường (giống :has-text ở DismissPostImportDialogsAsync).
                var raw = await page.Locator(".ant-modal-content:visible").EvaluateAllAsync<string[][]>(
                    @"els => els.map(el => [
                        (el.innerText || el.textContent || '').replace(/\s+/g, ' ').trim(),
                        Array.from(el.querySelectorAll('a.has_underline')).some(a => (a.textContent || '').toLowerCase().includes('hộp nháp')) ? '1' : '0'
                    ])");
                var modals = (raw ?? [])
                    .Select(m => new OpenModalInfo(m.Length > 0 ? m[0] : "", m.Length > 1 && m[1] == "1"))
                    .ToList();
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
