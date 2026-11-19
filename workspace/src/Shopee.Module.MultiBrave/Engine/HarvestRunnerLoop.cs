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
                "File Excel không tồn tại — chọn lại file ở màn Lấy dữ liệu.");

        var endRow = config.EndRow;
        if (endRow is null || endRow < startRow)
            endRow = useHubData
                ? await ScrapeLinkSource.ResolveEndRowAsync(
                    workbookPath, sheet, startRow, cancellationToken, useHubData, hubAccountId)
                : await WorkbookEndRowAsync(workbookPath, sheet, startRow, cancellationToken).ConfigureAwait(false);

        config.EndRow = endRow;

        log(useHubData
            ? $"Đang tải dữ liệu (kho Hub): {sheet} dòng {options.Row(startRow)}–{options.Row(endRow.Value)}… (nghỉ giữa link {restWindow})"
            : $"Đang tải dữ liệu: {sheet} dòng {options.Row(startRow)}–{options.Row(endRow.Value)}… (nghỉ giữa link {restWindow})");
        var fetch = useHubData
            ? await ScrapeLinkSource.FetchSheetLinksAsync(
                workbookPath, sheet, startRow, endRow.Value, cancellationToken, useHubData, hubAccountId)
            : await WorkbookLinksAsync(workbookPath, sheet, startRow, endRow.Value, cancellationToken).ConfigureAwait(false);
        var items = fetch.Items;

        if (fetch.SkippedMissingProductName > 0)
            log($"Bỏ qua {fetch.SkippedMissingProductName} dòng vì cột F (tên SP) trống.");
        if (fetch.SkippedMissingLink > 0)
            log($"Bỏ qua {fetch.SkippedMissingLink} dòng vì không có link (cột link của sheet trống).");

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
            config.LastRunnerMessage = $"Khối dòng {options.Row(startRow)}–{options.Row(endRow.Value)}: 0 link hợp lệ " +
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

        // Harvest không có tài khoản BigSeller: overlay ghi sheet đang lấy thay cho "Bigseller Account".
        var accountHeader = LauncherRunnerLoop.BuildAccountHeader(config, "Sheet", sheet);

        var processor = new HarvestRowProcessor(config, options, breaker, log);
        var host = new LoopHost(cdpPort, profileRoot, config, sheet, startRow, endRow.Value, totalLinks, onProgress,
            options.BlockErrorCodes, accountHeader, options);
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
                options.Row(BlockStartRow(config, startRow)), options.Row(endRow.Value), config.DisplayName,
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
        var warnedShortRest = false;
        for (var index = 0; index < items.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = items[index];
            var rowNumber = item.RowNumber;
            var sku = LauncherRunnerLoop.ExtractSkuFromRow(item.RowData, rowNumber);
            // Extension neo itemId cả đầu lẫn đuôi: link dính đuôi lạ ("…i.1.2%7DD") sẽ ra notProduct → làm sạch trước.
            var link = HarvestLinks.CleanProductUrl(item.Link);
            io.CurrentLinkIndex = index + 1;
            io.CurrentSourceLink = item.Link;

            config.CurrentRow = rowNumber;
            config.RunnerPhase = "opening";
            config.LastRunnerMessage = $"Đang mở link {index + 1}/{totalLinks} — dòng {processor.Row(rowNumber)} (SKU: {sku}).";

            await host.ReportAsync(tab.Value, cancellationToken).ConfigureAwait(false);

            var statusText = $"{accountHeader}\nĐang lấy dữ liệu link {index + 1}/{totalLinks} — dòng {processor.Row(rowNumber)}.";

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
                ? HarvestOverlayText.RowSaved(processor.Row(rowNumber), HarvestOverlayText.TrySummarize(step.Json))
                : $"Dòng {processor.Row(rowNumber)}: không lấy được dữ liệu";
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
                var nextMsg = $"Link tiếp theo (dòng {processor.Row(nextRow)}) lúc {nextAt.LocalDateTime:HH:mm:ss}";
                var waitMsg = $"{accountHeader}\n{nextMsg}\n{rowOutcomeOverlay}";
                var pageUrl = step.PageUrl ?? link;

                // GĐ9: lượt ảnh gốc chạy SONG SONG với phần nghỉ (lướt trang + kiểm captcha), chờ xong ngay trước khi chờ nốt tới
                // nextAt — mốc mở link kế KHÔNG đổi (driver tự cắt mọi RPC ở hạn chót + grace). Nghỉ quá ngắn → chỉ hỏi số ảnh còn chờ.
                // Lối dừng sớm vẫn quan sát task (finally) để lỗi không lạc.
                var imageBudget = HarvestImageBudget.For(restMs);
                var images = imageBudget > 0
                    ? RunSourceImagesSafeAsync(io, imageBudget, cancellationToken)
                    : MissingOnlyAsync(io, cancellationToken);
                try
                {
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
                        config.LastRunnerMessage = $"Kiểm tra captcha trước link tiếp theo — dòng {processor.Row(nextRow)}.";
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
                                    ? $"Dừng vì captcha trước link tiếp theo - {config.DisplayName}, dòng {processor.Row(rowNumber)} (SKU: {sku})."
                                    : $"URL hiện tại không hợp lệ trước link tiếp theo - dòng {processor.Row(rowNumber)}.");
                            await io.PushDisplayStateAsync(index + 1, check.TabId ?? tab.Value, cancellationToken).ConfigureAwait(false);
                            onProgress();
                            log(config.LastRunnerMessage);
                            return false;
                        }

                        if (check.Waited)
                        {
                            log($"Đã giải captcha trước link tiếp theo - dòng {processor.Row(rowNumber)}.");
                            await io.ShowOverlayAsync(tab.Value, $"Đã giải captcha - chuẩn bị mở link tiếp theo (dòng {processor.Row(nextRow)})",
                                cancellationToken).ConfigureAwait(false);
                        }
                    }

                    if (images is not null)
                    {
                        var rest = await images.ConfigureAwait(false);
                        images = null;
                        if (rest.Round?.LogLine() is { } imageLine) log(imageLine);
                        if (imageBudget == 0 && rest.Missing is > 0 and var waiting && !warnedShortRest)
                        {
                            warnedShortRest = true;   // một lần mỗi khối
                            log(SourceImageRound.PendingWarning(waiting, SourceImageRound.WhyShortRest));
                        }
                    }

                    var remainingDelay = (int)Math.Max(0, (nextAt - io.Now).TotalMilliseconds);
                    if (remainingDelay > 0)
                        await io.DelayAsync(remainingDelay, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    if (images is not null) ObserveLate(images);
                }
            }
        }

        // GĐ9: hết khối mà hàng đợi ảnh còn việc (SP cuối khối không có khoảng nghỉ sau) → MỘT lượt dọn ngắn trước khi đóng khối
        // (không còn SP kế nên không kéo dài nghỉ giữa SP); vẫn còn thì cảnh báo — các khoảng nghỉ của khối sau đẩy tiếp.
        await FinishBlockImagesAsync(io, log, cancellationToken).ConfigureAwait(false);

        await processor.FlushPendingNotProductAsync(cancellationToken).ConfigureAwait(false);   // chuỗi notProduct chưa tới ngưỡng = lỗi dòng thật
        processor.CompleteBlock(totalLinks, sheet);
        // Duyệt hết khối: báo xong CẢ khối (dòng lỗi không kẹt khối — xem chú thích hàm).
        config.LastCompletedRow = endRow;
        config.NextRunRow = endRow + 1;
        return true;
    }

    /// <summary>Lượt ảnh gốc của khoảng nghỉ — lỗi bất kỳ (kể cả IO ném đồng bộ) thành null: lượt ảnh không bao giờ làm hỏng dòng.
    /// Chỉ huỷ của job mới được ném ra.</summary>
    internal static async Task<RestImages> RunSourceImagesSafeAsync(IHarvestLoopIo io, int budgetMs,
        CancellationToken cancellationToken)
    {
        try
        {
            var round = await io.RunSourceImagesAsync(budgetMs, cancellationToken).ConfigureAwait(false);
            return new RestImages(round, round?.QueueIds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return new RestImages(null, null); }
    }

    /// <summary>Chỉ hỏi số ảnh còn chờ (không chạy lượt) — lỗi bất kỳ thành null. Chỉ huỷ của job mới được ném ra.</summary>
    internal static async Task<RestImages> MissingOnlyAsync(IHarvestLoopIo io, CancellationToken cancellationToken)
    {
        try { return new RestImages(null, await io.SourceImagesMissingAsync(cancellationToken).ConfigureAwait(false)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return new RestImages(null, null); }
    }

    /// <summary>Ngân sách lượt dọn ảnh cuối khối.</summary>
    internal const int BlockEndImageBudgetMs = 30_000;

    /// <summary>Cuối khối: hỏi hàng đợi ảnh; còn việc → một lượt dọn <see cref="BlockEndImageBudgetMs"/>; vẫn còn → cảnh báo.</summary>
    internal static async Task FinishBlockImagesAsync(IHarvestLoopIo io, Action<string> log, CancellationToken cancellationToken)
    {
        var missing = (await MissingOnlyAsync(io, cancellationToken).ConfigureAwait(false)).Missing;
        if (missing is not > 0) return;
        var rest = await RunSourceImagesSafeAsync(io, BlockEndImageBudgetMs, cancellationToken).ConfigureAwait(false);
        if (rest.Round?.LogLine() is { } line) log(line);
        var left = rest.Round is not null ? rest.Missing : missing;
        if (left is > 0 and var n) log(SourceImageRound.PendingWarning(n, SourceImageRound.WhyBlockEnd));
    }

    /// <summary>Kết quả phần ảnh của một khoảng nghỉ: lượt (null = không chạy / không có kết quả) + số ảnh còn chờ (null = không biết).</summary>
    internal readonly record struct RestImages(SourceImageRound? Round, int? Missing);

    /// <summary>Task không còn ai chờ (lối dừng sớm): quan sát lỗi / huỷ muộn để không thành UnobservedTaskException.</summary>
    private static void ObserveLate(Task task) =>
        _ = task.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

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
        int totalLinks, Action onProgress, IReadOnlyList<long>? blockErrorCodes, string accountHeader,
        HarvestRunOptions options) : IHarvestRowHost, IHarvestLoopIo
    {
        private static readonly Random ClickRandom = new();

        public DateTimeOffset Now => DateTimeOffset.Now;

        public string? CurrentSourceLink { get; set; }

        // GĐ7: ExtensionPush → app mở trang, bấm nút "Lấy dữ liệu" của hộp nổi bằng chuột thật qua CDP, extension tự đẩy Hub kèm
        // nguồn (HarvestClickStep). Không thì bước cũ (JSON về app).
        public Task<HarvestStepResult> ExecuteStepAsync(string link, int rowNumber, string statusText, int? tabId,
            CancellationToken cancellationToken)
        {
            if (options.ExtensionPush is not { } push)
                return ExtensionRunnerAutomation.ExecuteHarvestStepAsync(
                    cdpPort, profileRoot, link, rowNumber, statusText, config.DisplayName, tabId, blockErrorCodes, cancellationToken);
            if (push.HubAccess() is not { } hub)
                return Task.FromResult(HarvestStepResult.Failure(
                    $"{HarvestSinkFailure.Prefix}app chưa nối Hub — extension không có URL / token để tự đẩy dữ liệu (dòng {options.Row(rowNumber)} chưa tính xong).",
                    tabId, link));
            var request = new HarvestClickRequest(link, rowNumber, statusText, config.DisplayName, tabId, blockErrorCodes,
                config.HubAccountId, sheet, CurrentSourceLink, options.RunId, hub, DisplayRow: options.Row(rowNumber));
            return HarvestClickStep.RunAsync(new CdpHarvestClickIo(cdpPort, profileRoot), request, ClickRandom, cancellationToken);
        }

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

        // GĐ9: chỉ ở luồng "extension tự đẩy Hub" (extension có cấu hình Hub của app) và khi app chưa biết Hub hết chỗ. Khoá số liệu
        // theo THƯ MỤC profile: mỗi profile Brave một extension, một hàng đợi ảnh riêng, dùng lại qua các lượt. Hạn chót tính NGAY lúc
        // gọi (= đầu khoảng nghỉ), không đợi start trả lời.
        public Task<SourceImageRound?> RunSourceImagesAsync(int budgetMs, CancellationToken cancellationToken)
        {
            var deadline = DateTimeOffset.UtcNow.AddMilliseconds(budgetMs);
            if (options.ExtensionPush is not { } push || push.HubAccess() is not { } hub || options.SourceImages is not { } progress)
                return Task.FromResult<SourceImageRound?>(null);
            return SourceImageRoundDriver.RunAsync(
                (clear, budget, ct) => RunnerExtensionRpc.StartSourceImagesAsync(cdpPort, profileRoot, hub, budget, clear, ct),
                ct => RunnerExtensionRpc.GetSourceImagesStatusAsync(cdpPort, profileRoot, ct),
                Task.Delay, () => DateTimeOffset.UtcNow, deadline, progress, profileRoot.FullName, cancellationToken);
        }

        public Task<int?> SourceImagesMissingAsync(CancellationToken cancellationToken)
        {
            if (options.ExtensionPush is null || options.SourceImages is not { } progress) return Task.FromResult<int?>(null);
            return SourceImageRoundDriver.RefreshMissingAsync(
                ct => RunnerExtensionRpc.GetSourceImagesStatusAsync(cdpPort, profileRoot, ct), progress, profileRoot.FullName,
                cancellationToken);
        }

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
    /// <summary>Link GỐC của dòng đang chạy (GĐ7: extension gửi Hub làm <c>sourceUrl</c>). Mặc định bỏ qua.</summary>
    string? CurrentSourceLink { set { } }
    DateTimeOffset Now { get; }
    Task<HarvestStepResult> ExecuteStepAsync(string link, int rowNumber, string statusText, int? tabId, CancellationToken cancellationToken);
    Task PushDisplayStateAsync(int linkIndex, int? tabId, CancellationToken cancellationToken);
    Task ShowOverlayAsync(int? tabId, string text, CancellationToken cancellationToken);
    Task SimulateRestAsync(string pageUrl, int delayMs, CancellationToken cancellationToken);
    Task<RunnerExtensionRpc.BeforeNextLinkCheckResult> CheckBeforeNextLinkAsync(int tabId, int rowNumber, string sku,
        CancellationToken cancellationToken);
    Task DelayAsync(int ms, CancellationToken cancellationToken);
    /// <summary>GĐ9: một lượt ảnh gốc trong khoảng nghỉ (ngân sách <paramref name="budgetMs"/>), chờ tới khi xong. null = không chạy /
    /// không có kết quả. Không ném trừ huỷ của job. Mặc định không chạy.</summary>
    Task<SourceImageRound?> RunSourceImagesAsync(int budgetMs, CancellationToken cancellationToken) =>
        Task.FromResult<SourceImageRound?>(null);
    /// <summary>GĐ9: hỏi riêng số ảnh còn chờ đẩy (không chạy lượt) — khi nghỉ quá ngắn / hết khối. null = không hỏi được / không
    /// dùng ảnh gốc. Không ném trừ huỷ của job. Mặc định null.</summary>
    Task<int?> SourceImagesMissingAsync(CancellationToken cancellationToken) => Task.FromResult<int?>(null);
}
