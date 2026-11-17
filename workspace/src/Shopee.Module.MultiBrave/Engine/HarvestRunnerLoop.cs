using Shopee.Core.Harvest;
using Shopee.Core.Scrape;
using Shopee.Modules.MultiBrave;

namespace OpenMultiBraveLauncherV3;

/// <summary>
/// VÒNG DÒNG của chế độ Harvest — bản song song của <see cref="LauncherRunnerLoop"/> (chế độ Scrape giữ nguyên, không
/// rẽ nhánh bên trong). Cùng nguồn link, cùng cách nạp extension/đẩy trạng thái/nghỉ giữa link/captcha/traffic/proxy;
/// khác ở chỗ mỗi dòng gọi bước <c>executeHarvestStep</c> rồi giao JSON cho <see cref="IHarvestSink"/>, và dòng CHỈ được
/// tính xong (LastCompletedRow + setDisplayState) SAU khi sink lưu xong. Không BigSeller (không needLogin, không ghi
/// ngược token, không xoá localStorage shopee-api-data, không video, không fan-out ProductScraped).
/// Quyết định theo từng kết quả bước nằm ở <see cref="HarvestStepPolicy"/> (thuần, có test).
/// </summary>
internal static class HarvestRunnerLoop
{
    private static readonly Random RestRandom = new();

    /// <summary>Chờ tối đa chừng này cho dòng "Hub: …" của bảng tổng kết — overlay chỉ là phụ, không giữ chân kết thúc khối.</summary>
    internal static readonly TimeSpan HubStatusTimeout = TimeSpan.FromSeconds(3);

    // Workbook: đọc qua HarvestWorkbookReader (chép byte vào RAM rồi đóng handle; app Update đang ghi → chờ huỷ được trong
    // khối, quá hạn ném HarvestWorkbookLockedException — tiền tố riêng → ScrapeFailurePolicy dừng job, KHÔNG cooldown tk) thay
    // cho ScrapeWorkbook.Open (giữ FileStream tới GC) — CÙNG quy tắc lọc / đánh số dòng, cùng thông điệp với ScrapeLinkSource.
    internal static async Task<int> WorkbookEndRowAsync(string workbookPath, string sheet, int startRow, CancellationToken ct)
    {
        var total = await HarvestWorkbookReader.TotalDataRowsAsync(workbookPath, sheet, ct).ConfigureAwait(false);
        if (total < startRow)
            throw new InvalidOperationException($"Từ dòng {startRow} vượt quá số dòng sheet ({total}).");
        return total;
    }

    internal static async Task<SheetLinkFetchResult> WorkbookLinksAsync(string workbookPath, string sheet, int startRow, int endRow,
        CancellationToken ct)
    {
        var res = await HarvestWorkbookReader.FetchLinksAsync(workbookPath, sheet, startRow, endRow, ct).ConfigureAwait(false);
        var items = res.Items
            .Select(it => new SheetLinkItem(
                it.RowNumber,
                it.Values.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
                it.Link))
            .ToList();
        return new SheetLinkFetchResult(items, res.SkippedMissingProductName, res.SkippedMissingLink);
    }

    /// <summary>Giữ cửa sổ sau khi hiện bảng tổng kết cuối khối: mỗi khối là một phiên Brave tự đóng ngay khi vòng trả về
    /// (AutoCloseProfileOnFinish) — không chờ thì tổng kết chỉ chớp chưa tới 1 s. Dừng job → bỏ chờ ngay. Xa dưới ngưỡng
    /// watchdog (8 phút).</summary>
    internal static readonly TimeSpan BlockSummaryHold = TimeSpan.FromSeconds(5);

    /// <summary>Dòng đầu của KHỐI gốc (InstanceConfig.StartRow do runner đặt khi chia khối) — khác dòng bắt đầu lượt này khi
    /// watchdog mở lại profile giữa khối.</summary>
    internal static int BlockStartRow(InstanceConfig config, int runStartRow) =>
        config.StartRow is > 0 ? config.StartRow.Value : runStartRow;

    /// <summary>Đầu lượt: khối chạy từ dòng đầu → đặt lại bộ đếm tổng kết; lượt mở lại giữa khối → giữ số đã đếm.</summary>
    internal static void BeginBlockTally(InstanceConfig config, int runStartRow)
    {
        if (runStartRow > BlockStartRow(config, runStartRow)) return;
        config.HarvestSavedRows = 0;
        config.HarvestFailedRows = 0;
    }

    public static async Task RunAsync(
        int cdpPort,
        DirectoryInfo profileRoot,
        InstanceConfig config,
        HarvestRunOptions options,
        HarvestPdpBreaker breaker,
        Action<string> log,
        Action onProgress,
        CancellationToken cancellationToken,
        Func<Task>? onBeforeExtensionReady = null,
        Func<Task>? onAfterExtensionReady = null,
        Action<bool>? onCaptchaState = null)
    {
        var sheet = config.DataSheet?.Trim()
            ?? throw new InvalidOperationException("Thiếu sheet.");

        var runRow = config.GetEffectiveRunRow() ?? config.StartRow;
        if (runRow is not > 0)
            throw new InvalidOperationException("Thiếu dòng tiếp theo / từ dòng.");

        if (!config.TryValidateRunRow(runRow.Value, out var rangeError))
            throw new InvalidOperationException(rangeError ?? "Dòng chạy ngoài phạm vi.");

        var startRow = runRow.Value;
        var restWindow = ScrapeRestWindow.Resolve(config.RestMinSeconds, config.RestMaxSeconds);
        BeginBlockTally(config, startRow);

        // Nguồn link y như Scrape (workbook hoặc kho Hub theo HubAccountId) — xem LauncherRunnerLoop.RunAsync.
        var useHubData = config.UseHubData;
        var hubAccountId = config.HubAccountId;
        var workbookPath = config.WorkbookPath;
        if (!useHubData && (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath)))
            throw new InvalidOperationException(
                "Workbook không tồn tại — kiểm tra cấu hình ở mục BigSeller.");

        var endRow = config.EndRow;
        if (endRow is null || endRow < startRow)
            endRow = useHubData
                ? await ScrapeLinkSource.ResolveEndRowAsync(
                    workbookPath, sheet, startRow, cancellationToken, useHubData, hubAccountId)
                : await WorkbookEndRowAsync(workbookPath, sheet, startRow, cancellationToken).ConfigureAwait(false);

        config.EndRow = endRow;

        log(useHubData
            ? $"Đang tải dữ liệu (kho Hub): {sheet} dòng {startRow}–{endRow}… (nghỉ giữa link {restWindow})"
            : $"Đang tải dữ liệu: {sheet} dòng {startRow}–{endRow}… (nghỉ giữa link {restWindow})");
        var fetch = useHubData
            ? await ScrapeLinkSource.FetchSheetLinksAsync(
                workbookPath, sheet, startRow, endRow.Value, cancellationToken, useHubData, hubAccountId)
            : await WorkbookLinksAsync(workbookPath, sheet, startRow, endRow.Value, cancellationToken).ConfigureAwait(false);
        var items = fetch.Items;

        if (fetch.SkippedMissingProductName > 0)
            log($"Bỏ qua {fetch.SkippedMissingProductName} dòng vì cột F (tên SP) trống.");
        if (fetch.SkippedMissingLink > 0)
            log($"Bỏ qua {fetch.SkippedMissingLink} dòng vì cột A không có link hợp lệ.");

        if (items.Count == 0)
        {
            // Cùng luật với Scrape: 0 link + 0 bằng chứng thiếu input → lỗi (không đóng dấu phủ khối mù); có bằng
            // chứng mọi dòng thiếu input → coi khối là đã phủ, kết thúc bình thường.
            if (fetch.SkippedMissingProductName + fetch.SkippedMissingLink == 0)
                throw new InvalidOperationException("Không có link hợp lệ trong khoảng dòng đã chọn.");

            config.LastCompletedRow = endRow.Value;
            config.NextRunRow = endRow.Value + 1;
            config.RunnerRunning = false;
            config.RunnerPhase = "finished";
            config.LastRunnerMessage = $"Khối dòng {startRow}–{endRow.Value}: 0 link hợp lệ " +
                $"(thiếu tên SP: {fetch.SkippedMissingProductName}, thiếu link: {fetch.SkippedMissingLink}) — coi như đã phủ, sang khối kế.";
            log(config.LastRunnerMessage);
            config.ProgressSyncedAt = DateTimeOffset.Now;
            onProgress();
            return;
        }

        if (onBeforeExtensionReady is not null)
            await onBeforeExtensionReady().ConfigureAwait(false);

        await ExtensionRunnerAutomation.EnsureRunnerExtensionReadyAsync(
            cdpPort, profileRoot, log, cancellationToken, onCaptchaState: onCaptchaState).ConfigureAwait(false);

        if (onAfterExtensionReady is not null)
            await onAfterExtensionReady().ConfigureAwait(false);

        await ExtensionRunnerAutomation.TrimAuxiliaryTabsAsync(cdpPort, cancellationToken)
            .ConfigureAwait(false);

        await ExtensionRunnerAutomation.TryApplyFormConfigAsync(
            cdpPort, profileRoot, sheet, config.StartRow, config.EndRow ?? endRow, cancellationToken).ConfigureAwait(false);

        config.RunLog ??= [];
        config.RunnerRunning = true;
        config.RunnerPhase = "starting";
        config.LastRunnerMessage = "Đang chuẩn bị…";

        int? tabId = null;
        var totalLinks = items.Count;

        await LauncherRunnerLoop.PushDisplayStateAsync(
            cdpPort, profileRoot, config, sheet, startRow, endRow.Value,
            0, totalLinks, null, cancellationToken).ConfigureAwait(false);

        var accountHeader = LauncherRunnerLoop.BuildAccountHeader(config);

        var processor = new HarvestRowProcessor(config, options, breaker, log);
        var host = new LoopHost(cdpPort, profileRoot, config, sheet, startRow, endRow.Value, totalLinks, onProgress,
            options.BlockErrorCodes, accountHeader);
        var tab = new System.Runtime.CompilerServices.StrongBox<int?>(tabId);

        try
        {
            if (!await RunRowsAsync(items, config, processor, host, host, restWindow, accountHeader, endRow.Value, sheet, tab,
                    log, onProgress, cancellationToken).ConfigureAwait(false))
                return;
            tabId = tab.Value;
            log(config.LastRunnerMessage ?? "");

            await LauncherRunnerLoop.PushDisplayStateAsync(
                cdpPort, profileRoot, config, sheet, startRow, endRow.Value,
                totalLinks, totalLinks, tabId, cancellationToken).ConfigureAwait(false);
            onProgress();

            // KHÔNG ẩn overlay: để bảng tổng kết nằm lại trên trang (link / khối kế tiếp tự thay overlay như thường).
            var hubLine = await TryHubStatusAsync(options.HubStatusText, cancellationToken).ConfigureAwait(false);
            var doneText = HarvestOverlayText.BlockDone(
                BlockStartRow(config, startRow), endRow.Value, config.DisplayName,
                config.HarvestSavedRows, config.HarvestFailedRows, hubLine);
            log(doneText.Replace("\n", " · "));   // log job: vẫn thấy tổng kết dù Brave đã đóng
            if (tabId is not null)
            {
                await LauncherRunnerLoop.TryShowOverlayAsync(cdpPort, profileRoot, tabId, doneText, cancellationToken)
                    .ConfigureAwait(false);
                try { await Task.Delay(BlockSummaryHold, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { /* Dừng: khối đã xong và ghi nhận, chỉ bỏ phần chờ cho người đọc */ }
            }
        }
        catch (OperationCanceledException)
        {
            tabId = tab.Value;
            config.RunnerRunning = false;
            config.RunnerPhase = "stopped";
            config.LastRunnerMessage = "Đã dừng từ Multi Brave Manager.";
            log(config.LastRunnerMessage);

            if (tabId is not null)
            {
                try
                {
                    await ExtensionRunnerAutomation.HideOverlayAsync(
                        cdpPort, profileRoot, tabId.Value, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // ignore
                }
            }

            await LauncherRunnerLoop.PushDisplayStateAsync(
                cdpPort, profileRoot, config, sheet, startRow, endRow.Value,
                config.RunLog?.Count ?? 0, totalLinks, tabId, CancellationToken.None).ConfigureAwait(false);
            onProgress();
            throw;
        }
        finally
        {
            config.RunnerRunning = false;
            config.ProgressSyncedAt = DateTimeOffset.Now;
        }
    }

    /// <summary>
    /// VÒNG DÒNG của một khối (tách khỏi <see cref="RunAsync"/> để test được bằng host / IO giả). Trả true = đã duyệt hết khối
    /// (đã xả chuỗi notProduct, CompleteBlock, báo xong cả khối); false = dừng giữa chừng (config đã có phase + thông điệp).
    /// <list type="bullet">
    /// <item>Bước "kiểm trước link kế" (captcha / URL hiện tại) CHỈ chạy sau dòng LƯU ĐƯỢC: dòng không lấy được (notProduct, hết
    /// giờ…) để tab ở trang không phải SP → extension chờ 30 s rồi báo "URL hiện tại không phải link SP" → khối dừng oan, tài
    /// khoản bị cho nghỉ, dòng chết không bao giờ vào tập lỗi. Link kế tự điều hướng tab; bước harvest kế tự dò traffic / captcha.</item>
    /// <item>Mọi lối dừng sớm bỏ chuỗi notProduct đang chờ (phiên có vấn đề → làm lại); hết khối thì xả (lỗi dòng thật).</item>
    /// <item>Duyệt hết khối → LastCompletedRow = <paramref name="endRow"/>: tiến độ Harvest dựng lại từ tập SP đã lấy mỗi lượt, dòng
    /// lỗi tạm không có trong <c>done</c> nên lượt sau tự lấy lại — để LastCompletedRow kẹt ở dòng lỗi cuối khối thì ScrapeRunner
    /// coi khối chưa xong, loại tài khoản rồi vá đúng dòng lỗi tới khi kẹt 3 lần.</item>
    /// </list>
    /// </summary>
    internal static async Task<bool> RunRowsAsync(
        IReadOnlyList<SheetLinkItem> items,
        InstanceConfig config,
        HarvestRowProcessor processor,
        IHarvestRowHost host,
        IHarvestLoopIo io,
        ScrapeRestWindow restWindow,
        string accountHeader,
        int endRow,
        string sheet,
        System.Runtime.CompilerServices.StrongBox<int?> tab,
        Action<string> log,
        Action onProgress,
        CancellationToken cancellationToken)
    {
        var totalLinks = items.Count;
        for (var index = 0; index < items.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = items[index];
            var rowNumber = item.RowNumber;
            var sku = LauncherRunnerLoop.ExtractSkuFromRow(item.RowData, rowNumber);
            // Extension neo itemId cả đầu lẫn đuôi: link dính đuôi lạ ("…i.1.2%7DD") sẽ ra notProduct → làm sạch trước.
            var link = HarvestLinks.CleanProductUrl(item.Link);
            io.CurrentLinkIndex = index + 1;

            config.CurrentRow = rowNumber;
            config.RunnerPhase = "opening";
            config.LastRunnerMessage = $"Đang mở link {index + 1}/{totalLinks} — dòng {rowNumber} (SKU: {sku}).";

            await host.ReportAsync(tab.Value, cancellationToken).ConfigureAwait(false);

            var statusText = $"{accountHeader}\nĐang lấy dữ liệu link {index + 1}/{totalLinks} — dòng {rowNumber}.";

            var step = await io.ExecuteStepAsync(link, rowNumber, statusText, tab.Value, cancellationToken).ConfigureAwait(false);

            var outcome = await processor.HandleAsync(
                step,
                new HarvestRowInput(rowNumber, sheet, item.Link, link, sku, tab.Value, io.Now),
                host,
                cancellationToken).ConfigureAwait(false);

            if (outcome.Next == HarvestRowNext.RetryRow)
            {
                tab.Value = outcome.TabId ?? tab.Value;
                index--;
                continue;
            }

            if (outcome.Next == HarvestRowNext.Stop)
            {
                await processor.ReportStopAsync(outcome, host, cancellationToken).ConfigureAwait(false);
                return false;
            }

            tab.Value = outcome.TabId ?? tab.Value;
            // "Đã xong" = đã lưu vào kho trên máy; Hub nhận sau (số Hub ở bảng tổng kết cuối khối). Harvest không tải
            // video — chỉ báo có/không và đã lưu link.
            var rowOutcomeOverlay = outcome.Saved
                ? HarvestOverlayText.RowSaved(rowNumber, HarvestOverlayText.TrySummarize(step.Json))
                : $"Dòng {rowNumber}: không lấy được dữ liệu";
            // Đếm cho bảng tổng kết (chỉ dòng đã có kết quả cuối; dòng làm lại không tính hai lần). Trên config để qua
            // được lần watchdog mở lại giữa khối.
            if (outcome.Saved) config.HarvestSavedRows++;
            else config.HarvestFailedRows++;

            // Tiến độ (lastCompletedRow) tới extension CHỈ ở đây — sau khi sink đã lưu (extension không tự ghi).
            await host.ReportAsync(tab.Value, cancellationToken).ConfigureAwait(false);

            // Báo kết quả dòng NGAY (kể cả dòng cuối khối — trước đây dòng cuối không có overlay nào).
            await io.ShowOverlayAsync(tab.Value, $"{accountHeader}\n{rowOutcomeOverlay}", cancellationToken).ConfigureAwait(false);

            if (index < items.Count - 1)
            {
                var restMs = restWindow.NextRestMs(RestRandom);
                var nextRow = rowNumber + 1;
                var nextAt = io.Now.AddMilliseconds(restMs);
                var nextMsg = $"Link tiếp theo (dòng {nextRow}) lúc {nextAt.LocalDateTime:HH:mm:ss}";
                var waitMsg = $"{accountHeader}\n{nextMsg}\n{rowOutcomeOverlay}";
                var pageUrl = step.PageUrl ?? link;

                config.RunnerPhase = "waiting";
                config.LastRunnerMessage = waitMsg;
                await io.PushDisplayStateAsync(index + 1, tab.Value, cancellationToken).ConfigureAwait(false);
                await io.ShowOverlayAsync(tab.Value, waitMsg, cancellationToken).ConfigureAwait(false);

                log(waitMsg);
                var preCheckDelay = Math.Max(0, restMs - 30_000);
                await io.SimulateRestAsync(pageUrl, preCheckDelay, cancellationToken).ConfigureAwait(false);

                // Chỉ kiểm trước link kế khi dòng vừa rồi LƯU ĐƯỢC (tab chắc đang ở trang SP) — xem chú thích hàm.
                if (tab.Value is not null && outcome.Saved)
                {
                    config.LastRunnerMessage = $"Kiểm tra captcha trước link tiếp theo — dòng {nextRow}.";
                    await io.PushDisplayStateAsync(index + 1, tab.Value, cancellationToken).ConfigureAwait(false);
                    onProgress();

                    var check = await io.CheckBeforeNextLinkAsync(tab.Value.Value, rowNumber, sku, cancellationToken)
                        .ConfigureAwait(false);

                    if (check.Aborted)
                        throw new OperationCanceledException();

                    if (check.TrafficError)
                    {
                        processor.Book.DiscardPendingPdpRows();
                        processor.DiscardPendingNotProduct();
                        LauncherRunnerLoop.MarkTrafficBlock(config, rowNumber);
                        await io.PushDisplayStateAsync(index + 1, check.TabId ?? tab.Value, cancellationToken).ConfigureAwait(false);
                        onProgress();
                        log(config.LastRunnerMessage ?? "");
                        return false;
                    }

                    if (!check.Ok)
                    {
                        processor.Book.DiscardPendingPdpRows();
                        processor.DiscardPendingNotProduct();
                        config.RunnerPhase = check.Captcha ? "paused" : "error";
                        if (check.Captcha) config.CaptchaError = true;
                        if (check.Captcha) config.CaptchaUrl = pageUrl;
                        config.LastRunnerMessage =
                            check.Message ?? (check.Captcha
                                ? $"Dừng vì captcha trước link tiếp theo - {config.DisplayName}, dòng {rowNumber} (SKU: {sku})."
                                : $"URL hiện tại không hợp lệ trước link tiếp theo - dòng {rowNumber}.");
                        await io.PushDisplayStateAsync(index + 1, check.TabId ?? tab.Value, cancellationToken).ConfigureAwait(false);
                        onProgress();
                        log(config.LastRunnerMessage);
                        return false;
                    }

                    if (check.Waited)
                    {
                        log($"Đã giải captcha trước link tiếp theo - dòng {rowNumber}.");
                        await io.ShowOverlayAsync(tab.Value, $"Đã giải captcha - chuẩn bị mở link tiếp theo (dòng {nextRow})",
                            cancellationToken).ConfigureAwait(false);
                    }
                }

                var remainingDelay = (int)Math.Max(0, (nextAt - io.Now).TotalMilliseconds);
                if (remainingDelay > 0)
                    await io.DelayAsync(remainingDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        await processor.FlushPendingNotProductAsync(cancellationToken).ConfigureAwait(false);   // chuỗi notProduct chưa tới ngưỡng = lỗi dòng thật
        processor.CompleteBlock(totalLinks, sheet);
        // Duyệt hết khối: báo xong CẢ khối (dòng lỗi không kẹt khối — xem chú thích hàm).
        config.LastCompletedRow = endRow;
        config.NextRunRow = endRow + 1;
        return true;
    }

    /// <summary>Dòng "Hub: …" cho bảng tổng kết: null khi app không cung cấp, ném, hoặc quá <see cref="HubStatusTimeout"/>.
    /// Chỉ huỷ của job (nút Dừng) mới được ném ra.</summary>
    internal static async Task<string?> TryHubStatusAsync(
        Func<CancellationToken, Task<string?>>? hubStatusText, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        if (hubStatusText is null) return null;
        var limit = timeout ?? HubStatusTimeout;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(limit);
            var task = hubStatusText(cts.Token);
            var done = await Task.WhenAny(task, Task.Delay(limit, cancellationToken)).ConfigureAwait(false);
            if (done != task)
            {
                _ = task.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);   // quan sát lỗi muộn
                return null;
            }
            return await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    /// <summary>Host thật của <see cref="HarvestRowProcessor"/>: đẩy setDisplayState + onProgress, hỏi CDP, chờ captcha.</summary>
    private sealed class LoopHost(
        int cdpPort, DirectoryInfo profileRoot, InstanceConfig config, string sheet, int startRow, int endRow,
        int totalLinks, Action onProgress, IReadOnlyList<long>? blockErrorCodes, string accountHeader) : IHarvestRowHost, IHarvestLoopIo
    {
        public DateTimeOffset Now => DateTimeOffset.Now;

        public Task<HarvestStepResult> ExecuteStepAsync(string link, int rowNumber, string statusText, int? tabId,
            CancellationToken cancellationToken) =>
            ExtensionRunnerAutomation.ExecuteHarvestStepAsync(
                cdpPort, profileRoot, link, rowNumber, statusText, config.DisplayName, tabId, blockErrorCodes, cancellationToken);

        public Task PushDisplayStateAsync(int linkIndex, int? tabId, CancellationToken cancellationToken) =>
            LauncherRunnerLoop.PushDisplayStateAsync(
                cdpPort, profileRoot, config, sheet, startRow, endRow, linkIndex, totalLinks, tabId, cancellationToken);

        public Task ShowOverlayAsync(int? tabId, string text, CancellationToken cancellationToken) =>
            LauncherRunnerLoop.TryShowOverlayAsync(cdpPort, profileRoot, tabId, text, cancellationToken);

        public Task SimulateRestAsync(string pageUrl, int delayMs, CancellationToken cancellationToken) =>
            LauncherRunnerLoop.SimulateBrowsingDuringRestAsync(cdpPort, pageUrl, delayMs, cancellationToken);

        public Task<RunnerExtensionRpc.BeforeNextLinkCheckResult> CheckBeforeNextLinkAsync(
            int tabId, int rowNumber, string sku, CancellationToken cancellationToken) =>
            ExtensionRunnerAutomation.CheckBeforeNextLinkAsync(
                cdpPort, profileRoot, tabId, rowNumber, config.DisplayName, sku, cancellationToken);

        public Task DelayAsync(int ms, CancellationToken cancellationToken) => Task.Delay(ms, cancellationToken);

        public int CurrentLinkIndex { get; set; }

        public async Task ReportAsync(int? tabId, CancellationToken cancellationToken)
        {
            await LauncherRunnerLoop.PushDisplayStateAsync(
                cdpPort, profileRoot, config, sheet, startRow, endRow,
                CurrentLinkIndex, totalLinks, tabId, cancellationToken).ConfigureAwait(false);
            onProgress();
        }

        public void Progress() => onProgress();

        public Task<bool> IsCdpReachableAsync(CancellationToken cancellationToken) =>
            RunnerExtensionTargets.IsCdpPortReachableAsync(cdpPort, cancellationToken);

        public Task<RunnerExtensionRpc.BeforeNextLinkCheckResult> CheckCaptchaAsync(
            int tabId, int rowNumber, string sku, CancellationToken cancellationToken) =>
            ExtensionRunnerAutomation.CheckBeforeNextLinkAsync(
                cdpPort, profileRoot, tabId, rowNumber, config.DisplayName, sku, cancellationToken);

        // Link mẫu: cùng bước harvest của extension (extension không tự ghi tiến độ), KHÔNG qua bộ xử lý dòng → không tính tiến
        // độ / số đếm / overlay "xong dòng".
        public Task<HarvestStepResult> ProbeSampleAsync(string link, int rowNumber, int? tabId, CancellationToken cancellationToken) =>
            ExtensionRunnerAutomation.ExecuteHarvestStepAsync(
                cdpPort, profileRoot, link, rowNumber,
                $"{accountHeader}\nĐang kiểm tra phiên bằng link mẫu (sau nhiều dòng không mở ra trang SP)…",
                config.DisplayName, tabId, blockErrorCodes, cancellationToken);
    }
}

/// <summary>Cửa ra Brave / extension / đồng hồ của <see cref="HarvestRunnerLoop.RunRowsAsync"/> (vòng thật = <c>LoopHost</c>, test
/// dùng giả).</summary>
internal interface IHarvestLoopIo
{
    int CurrentLinkIndex { set; }
    DateTimeOffset Now { get; }
    Task<HarvestStepResult> ExecuteStepAsync(string link, int rowNumber, string statusText, int? tabId, CancellationToken cancellationToken);
    Task PushDisplayStateAsync(int linkIndex, int? tabId, CancellationToken cancellationToken);
    Task ShowOverlayAsync(int? tabId, string text, CancellationToken cancellationToken);
    Task SimulateRestAsync(string pageUrl, int delayMs, CancellationToken cancellationToken);
    Task<RunnerExtensionRpc.BeforeNextLinkCheckResult> CheckBeforeNextLinkAsync(int tabId, int rowNumber, string sku,
        CancellationToken cancellationToken);
    Task DelayAsync(int ms, CancellationToken cancellationToken);
}
