using Shopee.Core.Harvest;
using Shopee.Modules.MultiBrave;

namespace OpenMultiBraveLauncherV3;

/// <summary>Cửa hẹp của <see cref="HarvestRowProcessor"/> ra Brave/CDP — vòng thật nối vào extension, test dùng giả.</summary>
internal interface IHarvestRowHost
{
    /// <summary>Đẩy trạng thái hiển thị (setDisplayState) + báo tiến độ lên UI.</summary>
    Task ReportAsync(int? tabId, CancellationToken cancellationToken);

    /// <summary>Chỉ báo tiến độ lên UI (không đụng extension).</summary>
    void Progress();

    Task<bool> IsCdpReachableAsync(CancellationToken cancellationToken);

    /// <summary>Chờ giải captcha tay trên tab (checkBeforeNextLink của extension).</summary>
    Task<RunnerExtensionRpc.BeforeNextLinkCheckResult> CheckCaptchaAsync(
        int tabId, int rowNumber, string sku, CancellationToken cancellationToken);
}

/// <summary>Vòng dòng làm gì tiếp sau một kết quả bước.</summary>
internal enum HarvestRowNext
{
    /// <summary>Dòng đã có kết luận (lưu được / lỗi dòng) → đẩy tiến độ, nghỉ, sang dòng kế.</summary>
    Done,
    /// <summary>Làm lại đúng dòng này (bị thay lần đầu / vừa giải captcha).</summary>
    RetryRow,
    /// <summary>Dừng vòng (config đã có phase + thông điệp) → đẩy trạng thái, log, kết thúc RunAsync.</summary>
    Stop,
}

internal readonly record struct HarvestRowInput(
    int RowNumber, string Sheet, string SourceLink, string Link, string Sku, int? TabId, DateTimeOffset ReceivedAt);

internal readonly record struct HarvestRowOutcome(HarvestRowNext Next, HarvestStepAction Action, bool Saved, int? TabId);

/// <summary>
/// Xử lý MỘT kết quả bước Harvest lên sổ dòng / config / phase — toàn bộ phần quyết định của
/// <see cref="HarvestRunnerLoop"/> ngoài việc gọi bước và nghỉ giữa link, tách ra để test bằng host + sink giả.
/// Một instance cho một lượt <c>HarvestRunnerLoop.RunAsync</c>; cầu dao (<see cref="HarvestPdpBreaker"/>) sống theo
/// phiên nên truyền từ ngoài vào.
/// </summary>
internal sealed class HarvestRowProcessor
{
    private readonly InstanceConfig _config;
    private readonly HarvestRunOptions _options;
    private readonly HarvestPdpBreaker _breaker;
    private readonly Action<string> _log;
    private readonly HarvestSourceMonitor _sourceMonitor = new();
    private int _captchaWaitedRow = -1;     // dòng đã CHỜ-giải-captcha 1 lần (chống lặp vô hạn)
    private int _supersededRetriedRow = -1; // dòng đã làm lại 1 lần vì bước bị thay

    public HarvestRowProcessor(InstanceConfig config, HarvestRunOptions options, HarvestPdpBreaker breaker, Action<string> log)
    {
        _config = config;
        _options = options;
        _breaker = breaker;
        _log = log;
        Book = new HarvestRowBook(config);
        _breaker.ResetStreak();   // lượt mới (vd sau khi đổi IP) đếm lại từ đầu; mã đã bật thì cầu dao vẫn nhớ
    }

    public HarvestRowBook Book { get; }

    public async Task<HarvestRowOutcome> HandleAsync(
        HarvestStepResult step, HarvestRowInput row, IHarvestRowHost host, CancellationToken cancellationToken)
    {
        var rowNumber = row.RowNumber;
        var decision = HarvestStepPolicy.Decide(step, _breaker);

        switch (decision.Action)
        {
            case HarvestStepAction.Superseded:
                // Bước bị bước khác thay → kết quả không thuộc về ta: không tính xong, không ghi lỗi dòng.
                // Làm lại đúng dòng 1 lần; bị thay lần nữa = có ai khác đang điều khiển extension → dừng khối,
                // ScrapeRunner vá lại từ dòng này (LastCompletedRow chưa qua nó nên không mất dòng).
                if (rowNumber != _supersededRetriedRow)
                {
                    _supersededRetriedRow = rowNumber;
                    _log($"Dòng {rowNumber}: bước bị thay thế ({step.Message}) — làm lại dòng này.");
                    return new(HarvestRowNext.RetryRow, decision.Action, false, row.TabId);
                }
                _config.RunnerPhase = "error";
                _config.LastRunnerMessage =
                    $"Dòng {rowNumber}: bước harvest bị thay thế 2 lần liên tiếp — dừng khối, dòng sẽ được làm lại.";
                return new(HarvestRowNext.Stop, decision.Action, false, step.TabId ?? row.TabId);

            case HarvestStepAction.Abort:
                throw new OperationCanceledException();

            case HarvestStepAction.Traffic:
            case HarvestStepAction.PdpBreakerTrip:
                // Các dòng pdpError đang chờ: KHÔNG ghi sổ — LastCompletedRow chưa qua chúng nên lượt sau
                // (IP mới / tài khoản khác) làm lại đúng các dòng đó.
                Book.DiscardPendingPdpRows();
                LauncherRunnerLoop.MarkTrafficBlock(_config, rowNumber);
                if (decision.Action == HarvestStepAction.PdpBreakerTrip)
                    _config.LastRunnerMessage =
                        $"{_breaker.Threshold} dòng liên tiếp get_pc trả cùng lỗi error={step.PdpError} (tới dòng {rowNumber}) " +
                        $"— coi như bị chặn. {_config.LastRunnerMessage}";
                else if (step.Blocked)
                    _config.LastRunnerMessage = $"{step.Message} {_config.LastRunnerMessage}";
                return new(HarvestRowNext.Stop, decision.Action, false, step.TabId);

            case HarvestStepAction.ProxyError:
                Book.DiscardPendingPdpRows();
                _config.RunnerPhase = "paused";
                _config.LastRunnerMessage = step.Message ?? $"Lỗi proxy dòng {rowNumber}.";
                return new(HarvestRowNext.Stop, decision.Action, false, step.TabId);

            case HarvestStepAction.Captcha:
            {
                // Giống hệt Scrape: báo ngay, giữ cửa sổ mở chờ giải tay 1 lần; giải xong → làm lại đúng dòng.
                _config.RunnerPhase = "paused";
                _config.CaptchaError = true;
                _config.CaptchaUrl = row.Link;
                _config.LastRunnerMessage = $"🚫 Captcha dòng {rowNumber} ({_config.DisplayName}) — GIẢI TAY trong cửa sổ Brave (đang giữ mở)…";
                await host.ReportAsync(step.TabId, cancellationToken).ConfigureAwait(false);
                _log(_config.LastRunnerMessage);

                if (step.TabId is int captchaTab && rowNumber != _captchaWaitedRow)
                {
                    _captchaWaitedRow = rowNumber;
                    var chk = await host.CheckCaptchaAsync(captchaTab, rowNumber, row.Sku, cancellationToken).ConfigureAwait(false);
                    if (chk.Aborted)
                        throw new OperationCanceledException();
                    if (chk.Ok)
                    {
                        _config.CaptchaError = false;
                        _config.RunnerPhase = "opening";
                        host.Progress();
                        _log($"Đã giải captcha dòng {rowNumber} → lấy lại dữ liệu dòng này.");
                        return new(HarvestRowNext.RetryRow, decision.Action, false, chk.TabId ?? row.TabId);
                    }
                }

                Book.DiscardPendingPdpRows();
                _config.LastRunnerMessage =
                    $"🚫 Dừng vì captcha - {_config.DisplayName}, dòng {rowNumber} (SKU: {row.Sku}). Giải tay rồi bấm Tiếp tục.";
                return new(HarvestRowNext.Stop, decision.Action, false, step.TabId);
            }
        }

        // SW chết không hồi / CDP đứt → ném lỗi extension để phiên tự mở lại profile rồi chạy tiếp ĐÚNG dòng
        // (như Scrape). Chỉ xét lỗi dòng không có pdpError: có pdpError là extension đã trả lời nên SW còn sống.
        if (decision.Action == HarvestStepAction.RowFailed && string.IsNullOrWhiteSpace(step.PdpError) &&
            ((step.Message?.Contains("No SW", StringComparison.OrdinalIgnoreCase) ?? false) ||
             (step.Message?.Contains("Brave không lắng nghe", StringComparison.OrdinalIgnoreCase) ?? false) ||
             (step.Message?.Contains("actively refused", StringComparison.OrdinalIgnoreCase) ?? false) ||
             !await host.IsCdpReachableAsync(cancellationToken).ConfigureAwait(false)))
            throw new InvalidOperationException(
                $"Extension {RunnerExtensionPaths.ExtensionDisplayName} mất kết nối CDP (No SW) tại dòng {rowNumber} — mở lại profile để dựng lại service worker.");

        var tabId = step.TabId ?? row.TabId;
        var saved = false;

        if (decision.Action == HarvestStepAction.Save)
        {
            _config.CaptchaError = false;
            _config.RunnerPhase = "saving";
            _config.LastRunnerMessage = $"Đang lưu dữ liệu dòng {rowNumber}…";
            host.Progress();

            var harvested = new HarvestedItem(
                _config.HubAccountId,
                _options.ShopId ?? "",
                row.Sheet,
                rowNumber,
                row.Link,
                row.SourceLink,
                step.ItemId!.Value,
                step.ShopId,
                step.Json!,
                step.Source ?? "",
                step.HarvestedAtOr(row.ReceivedAt));
            var saveError = await Book.SaveAsync(_options.Sink, harvested, cancellationToken).ConfigureAwait(false);
            if (saveError is HarvestItemRejectedException rejected)
            {
                // JSON của RIÊNG SP này không xử lý được → lỗi dòng như notProduct, sang dòng kế; KHÔNG dừng job (dừng
                // thì "Tiếp tục" kẹt đúng dòng này mãi).
                var reason = "Dữ liệu SP không xử lý được: " + rejected.Message;
                Book.RowFailed(rowNumber, reason);
                _log($"Dòng {rowNumber}: {reason}");
            }
            else if (saveError is not null)
            {
                // Chưa lưu được = dòng CHƯA xong (sổ không đụng LastCompletedRow/NextRunRow, không ghi bỏ qua). Lỗi chỗ
                // lưu là hạ tầng MÁY, không phải lỗi tk: phase error + thông điệp có tiền tố HarvestSinkFailure →
                // ScrapeRunner.Classify/ScrapeFailurePolicy ra StopJob (nhả tk nguyên vẹn, không tính kẹt, không bỏ dòng).
                _config.RunnerPhase = "error";
                _config.LastRunnerMessage = HarvestSinkFailure.Message(rowNumber, saveError);
                return new(HarvestRowNext.Stop, decision.Action, false, tabId);
            }
            else
            {
                saved = true;
                _log(step.Captcha
                    ? $"Đã giải captcha và lấy dữ liệu SP {step.ItemId} - dòng {rowNumber} ({step.Source})."
                    : $"Đã lấy dữ liệu SP {step.ItemId} - dòng {rowNumber} ({step.Source}).");
            }
            if (_sourceMonitor.Record(step.Source) is { } reloadRatio)
                _log($"⚠ Tỉ lệ phải tải lại trang mới bắt được get_pc cao: {reloadRatio:P0} trong 20 dòng gần nhất " +
                    "— trang ít tự gọi get_pc lần đầu, mỗi dòng tốn thêm một lần tải.");
        }
        else if (decision.Action == HarvestStepAction.PdpError)
        {
            var reason = step.Message ?? $"get_pc trả lỗi (error={step.PdpError})";
            Book.AddPdpError(rowNumber, step.PdpError!.Trim(), reason);
            _log($"Dòng {rowNumber}: {reason} [cầu dao error={decision.PdpCode} {decision.PdpStreak}/{_breaker.Threshold}]");
        }
        else
        {
            // notProduct / hết giờ / thiếu JSON / lỗi RPC / pdpError của mã đã bật cầu dao: ghi sổ bỏ qua rồi sang dòng kế.
            var reason = step.Message ?? (step.NotProduct ? "không phải trang sản phẩm" : "không lấy được dữ liệu");
            Book.RowFailed(rowNumber, reason);
            _log($"Dòng {rowNumber}: {reason}");
        }

        _config.RunLog ??= [];
        _config.RunLog.Add(new RunnerLogEntry { RowNumber = rowNumber, Sku = row.Sku, ScrapeOk = saved });
        if (_config.RunLog.Count > 200)
            _config.RunLog.RemoveRange(0, _config.RunLog.Count - 200);

        _config.LastSku = row.Sku;
        _config.LastRunnerMessage = saved ? $"Xong dòng {rowNumber} - đã lưu dữ liệu." : $"Xong dòng {rowNumber}.";
        return new(HarvestRowNext.Done, decision.Action, saved, tabId);
    }

    /// <summary>Đẩy trạng thái khi vòng dừng (<see cref="HarvestRowNext.Stop"/>) rồi log thông điệp dừng. Đẩy hỏng (SW/CDP
    /// đứt đúng lúc) KHÔNG được nuốt mất lý do dừng: phase + thông điệp (vd tiền tố lỗi lưu → StopJob) đã nằm trong
    /// config, chỉ ghi thêm một dòng log. Người dùng huỷ thì ném tiếp.</summary>
    public async Task ReportStopAsync(HarvestRowOutcome outcome, IHarvestRowHost host, CancellationToken cancellationToken)
    {
        try
        {
            await host.ReportAsync(outcome.TabId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log($"Không đẩy được trạng thái dừng lên extension ({ex.Message}) — vẫn dừng với lý do bên dưới.");
        }
        _log(_config.LastRunnerMessage ?? "");
    }

    /// <summary>Hết khối bình thường: các dòng pdpError còn chờ là lỗi dòng thật (cầu dao không bật) → ghi sổ; đặt
    /// trạng thái "finished".</summary>
    public void CompleteBlock(int totalLinks, string sheet)
    {
        Book.FlushPendingPdpRows();
        _config.RunnerRunning = false;
        _config.RunnerPhase = "finished";
        _config.LastRunnerMessage = $"Hoàn tất {totalLinks} link của sheet \"{sheet}\".";
    }
}

/// <summary>
/// Sổ dòng của một lượt Harvest:
/// <list type="bullet">
/// <item>Dòng chỉ được tính xong (LastCompletedRow/NextRunRow) SAU khi sink lưu xong; sink ném → không đụng gì.</item>
/// <item>Dòng pdpError được ghi sổ bỏ qua HOÃN: cầu dao bật / dừng giữa chừng → bỏ (vùng phủ chưa qua chúng nên chúng
/// được làm lại); chuỗi bị cắt (dòng lưu được / lỗi dòng khác / đổi mã lỗi / hết khối) → ghi (lỗi dòng thật).</item>
/// <item>Dòng làm lại thành công được gỡ khỏi sổ bỏ qua (nó từng lỗi trước khi đổi IP/tài khoản).</item>
/// </list>
/// </summary>
internal sealed class HarvestRowBook
{
    private readonly InstanceConfig _config;
    private readonly List<(int Row, string Code, string Reason)> _pendingPdpRows = [];

    public HarvestRowBook(InstanceConfig config) => _config = config;

    public IReadOnlyList<int> PendingPdpRows => _pendingPdpRows.Select(x => x.Row).ToList();

    /// <summary>Dòng pdpError chờ ghi. Mã khác mã đang chờ = chuỗi cũ bị cắt (cầu dao chỉ đếm CÙNG mã) → ghi chuỗi cũ
    /// thành lỗi dòng trước.</summary>
    public void AddPdpError(int row, string code, string reason)
    {
        if (_pendingPdpRows.Count > 0 && !string.Equals(_pendingPdpRows[^1].Code, code, StringComparison.Ordinal))
            FlushPendingPdpRows();
        _pendingPdpRows.Add((row, code, reason));
    }

    /// <summary>Lỗi dòng thường: cắt chuỗi pdpError (ghi các dòng đang chờ) rồi ghi dòng này.</summary>
    public void RowFailed(int row, string reason)
    {
        FlushPendingPdpRows();
        _config.AddFailedRow(row, reason);
    }

    public void FlushPendingPdpRows()
    {
        foreach (var (row, _, reason) in _pendingPdpRows)
            _config.AddFailedRow(row, reason);
        _pendingPdpRows.Clear();
    }

    public void DiscardPendingPdpRows() => _pendingPdpRows.Clear();

    /// <summary>Giao sink rồi mới tính dòng. Trả null = đã lưu + đã tính dòng; trả lỗi = CHƯA lưu, dòng không được tính
    /// (dòng pdpError đang chờ bị bỏ để làm lại). Huỷ của người dùng thì ném tiếp.</summary>
    public async Task<Exception?> SaveAsync(IHarvestSink sink, HarvestedItem item, CancellationToken cancellationToken)
    {
        try
        {
            await sink.SaveAsync(item, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HarvestItemRejectedException ex)
        {
            // Dữ liệu CỦA DÒNG hỏng (tất định) — không phải hạ tầng: dòng pdpError đang chờ giữ nguyên, caller ghi lỗi
            // dòng (RowFailed sẽ cắt chuỗi pdpError như mọi lỗi dòng khác).
            return ex;
        }
        catch (Exception ex)
        {
            DiscardPendingPdpRows();
            return ex;
        }

        FlushPendingPdpRows();
        _config.RemoveFailedRow(item.RowNo);   // dòng từng lỗi trước khi đổi IP/tk, nay làm lại được
        _config.LastCompletedRow = item.RowNo;
        _config.NextRunRow = item.RowNo + 1;
        return null;
    }
}
