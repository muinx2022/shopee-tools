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

        // Nguồn link y như Scrape (workbook hoặc kho Hub theo HubAccountId) — xem LauncherRunnerLoop.RunAsync.
        var useHubData = config.UseHubData;
        var hubAccountId = config.HubAccountId;
        var workbookPath = config.WorkbookPath;
        if (!useHubData && (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath)))
            throw new InvalidOperationException(
                "Workbook không tồn tại — kiểm tra cấu hình ở mục BigSeller.");

        var endRow = config.EndRow;
        if (endRow is null || endRow < startRow)
            endRow = await ScrapeLinkSource.ResolveEndRowAsync(
                workbookPath, sheet, startRow, cancellationToken, useHubData, hubAccountId);

        config.EndRow = endRow;

        log(useHubData
            ? $"Đang tải dữ liệu (kho Hub): {sheet} dòng {startRow}–{endRow}… (nghỉ giữa link {restWindow})"
            : $"Đang tải dữ liệu: {sheet} dòng {startRow}–{endRow}… (nghỉ giữa link {restWindow})");
        var fetch = await ScrapeLinkSource.FetchSheetLinksAsync(
            workbookPath, sheet, startRow, endRow.Value, cancellationToken, useHubData, hubAccountId);
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
        var host = new LoopHost(cdpPort, profileRoot, config, sheet, startRow, endRow.Value, totalLinks, onProgress);

        try
        {
            for (var index = 0; index < items.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var item = items[index];
                var rowNumber = item.RowNumber;
                var sku = LauncherRunnerLoop.ExtractSkuFromRow(item.RowData, rowNumber);
                // Extension neo itemId cả đầu lẫn đuôi: link dính đuôi lạ ("…i.1.2%7DD") sẽ ra notProduct → làm sạch trước.
                var link = HarvestLinks.CleanProductUrl(item.Link);
                host.CurrentLinkIndex = index + 1;

                config.CurrentRow = rowNumber;
                config.RunnerPhase = "opening";
                config.LastRunnerMessage = $"Đang mở link {index + 1}/{totalLinks} — dòng {rowNumber} (SKU: {sku}).";

                await host.ReportAsync(tabId, cancellationToken).ConfigureAwait(false);

                var statusText = $"{accountHeader}\nĐang lấy dữ liệu link {index + 1}/{totalLinks} — dòng {rowNumber}.";

                var step = await ExtensionRunnerAutomation.ExecuteHarvestStepAsync(
                    cdpPort,
                    profileRoot,
                    link,
                    rowNumber,
                    statusText,
                    config.DisplayName,
                    tabId,
                    options.BlockErrorCodes,
                    cancellationToken).ConfigureAwait(false);

                var outcome = await processor.HandleAsync(
                    step,
                    new HarvestRowInput(rowNumber, sheet, item.Link, link, sku, tabId, DateTimeOffset.Now),
                    host,
                    cancellationToken).ConfigureAwait(false);

                if (outcome.Next == HarvestRowNext.RetryRow)
                {
                    tabId = outcome.TabId ?? tabId;
                    index--;
                    continue;
                }

                if (outcome.Next == HarvestRowNext.Stop)
                {
                    await processor.ReportStopAsync(outcome, host, cancellationToken).ConfigureAwait(false);
                    return;
                }

                tabId = outcome.TabId ?? tabId;
                var rowOutcomeOverlay = outcome.Saved ? $"Đã lưu dữ liệu dòng {rowNumber}" : $"Dòng {rowNumber}: không lấy được dữ liệu";

                // Tiến độ (lastCompletedRow) tới extension CHỈ ở đây — sau khi sink đã lưu (extension không tự ghi).
                await host.ReportAsync(tabId, cancellationToken).ConfigureAwait(false);

                if (index < items.Count - 1)
                {
                    var restMs = restWindow.NextRestMs(RestRandom);
                    var nextRow = rowNumber + 1;
                    var nextAt = DateTimeOffset.Now.AddMilliseconds(restMs);
                    var nextMsg = $"Link tiếp theo (dòng {nextRow}) lúc {nextAt.LocalDateTime:HH:mm:ss}";
                    var waitMsg = $"{accountHeader}\n{nextMsg}\n{rowOutcomeOverlay}";
                    var pageUrl = step.PageUrl ?? link;

                    config.RunnerPhase = "waiting";
                    config.LastRunnerMessage = waitMsg;
                    await LauncherRunnerLoop.PushDisplayStateAsync(
                        cdpPort, profileRoot, config, sheet, startRow, endRow.Value,
                        index + 1, totalLinks, tabId, cancellationToken).ConfigureAwait(false);

                    await LauncherRunnerLoop.TryShowOverlayAsync(cdpPort, profileRoot, tabId, waitMsg, cancellationToken)
                        .ConfigureAwait(false);

                    log(waitMsg);
                    var preCheckDelay = Math.Max(0, restMs - 30_000);
                    await LauncherRunnerLoop.SimulateBrowsingDuringRestAsync(
                        cdpPort, pageUrl, preCheckDelay, cancellationToken).ConfigureAwait(false);

                    if (tabId is not null)
                    {
                        config.LastRunnerMessage = $"Kiểm tra captcha trước link tiếp theo — dòng {nextRow}.";
                        await LauncherRunnerLoop.PushDisplayStateAsync(
                            cdpPort, profileRoot, config, sheet, startRow, endRow.Value,
                            index + 1, totalLinks, tabId, cancellationToken).ConfigureAwait(false);
                        onProgress();

                        var check = await ExtensionRunnerAutomation.CheckBeforeNextLinkAsync(
                            cdpPort, profileRoot, tabId.Value, rowNumber, config.DisplayName, sku, cancellationToken)
                            .ConfigureAwait(false);

                        if (check.Aborted)
                            throw new OperationCanceledException();

                        if (check.TrafficError)
                        {
                            processor.Book.DiscardPendingPdpRows();
                            LauncherRunnerLoop.MarkTrafficBlock(config, rowNumber);
                            await LauncherRunnerLoop.PushDisplayStateAsync(
                                cdpPort, profileRoot, config, sheet, startRow, endRow.Value,
                                index + 1, totalLinks, check.TabId ?? tabId, cancellationToken).ConfigureAwait(false);
                            onProgress();
                            log(config.LastRunnerMessage ?? "");
                            return;
                        }

                        if (!check.Ok)
                        {
                            processor.Book.DiscardPendingPdpRows();
                            config.RunnerPhase = check.Captcha ? "paused" : "error";
                            if (check.Captcha) config.CaptchaError = true;
                            if (check.Captcha) config.CaptchaUrl = pageUrl;
                            config.LastRunnerMessage =
                                check.Message ?? (check.Captcha
                                    ? $"Dừng vì captcha trước link tiếp theo - {config.DisplayName}, dòng {rowNumber} (SKU: {sku})."
                                    : $"URL hiện tại không hợp lệ trước link tiếp theo - dòng {rowNumber}.");
                            await LauncherRunnerLoop.PushDisplayStateAsync(
                                cdpPort, profileRoot, config, sheet, startRow, endRow.Value,
                                index + 1, totalLinks, check.TabId ?? tabId, cancellationToken).ConfigureAwait(false);
                            onProgress();
                            log(config.LastRunnerMessage);
                            return;
                        }

                        if (check.Waited)
                        {
                            log($"Đã giải captcha trước link tiếp theo - dòng {rowNumber}.");
                            await LauncherRunnerLoop.TryShowOverlayAsync(
                                cdpPort, profileRoot, tabId,
                                $"Đã giải captcha - chuẩn bị mở link tiếp theo (dòng {nextRow})",
                                cancellationToken).ConfigureAwait(false);
                        }
                    }

                    var remainingDelay = (int)Math.Max(0, (nextAt - DateTimeOffset.Now).TotalMilliseconds);
                    if (remainingDelay > 0)
                        await Task.Delay(remainingDelay, cancellationToken).ConfigureAwait(false);
                }
            }

            processor.CompleteBlock(totalLinks, sheet);
            log(config.LastRunnerMessage ?? "");

            if (tabId is not null)
                await ExtensionRunnerAutomation.HideOverlayAsync(cdpPort, profileRoot, tabId.Value, cancellationToken)
                    .ConfigureAwait(false);

            await LauncherRunnerLoop.PushDisplayStateAsync(
                cdpPort, profileRoot, config, sheet, startRow, endRow.Value,
                totalLinks, totalLinks, tabId, cancellationToken).ConfigureAwait(false);
            onProgress();
        }
        catch (OperationCanceledException)
        {
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

    /// <summary>Host thật của <see cref="HarvestRowProcessor"/>: đẩy setDisplayState + onProgress, hỏi CDP, chờ captcha.</summary>
    private sealed class LoopHost(
        int cdpPort, DirectoryInfo profileRoot, InstanceConfig config, string sheet, int startRow, int endRow,
        int totalLinks, Action onProgress) : IHarvestRowHost
    {
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
    }
}
