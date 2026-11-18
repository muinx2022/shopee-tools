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

    /// <summary>Chạy một bước Harvest trên LINK MẪU (kiểm chứng phiên) — không tính tiến độ / số đếm, overlay "Đang kiểm tra
    /// phiên…".</summary>
    Task<HarvestStepResult> ProbeSampleAsync(string link, int rowNumber, int? tabId, CancellationToken cancellationToken);
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
    // Dòng notProduct đang CHỜ kết luận: đủ HarvestStepPolicy.NotProductBreakerThreshold dòng liên tiếp = lỗi phiên / tài khoản
    // → bỏ cả chuỗi (làm lại); chuỗi bị cắt bởi kết quả dòng khác / hết khối → mới ghi sổ lỗi + tập lỗi vĩnh viễn.
    private readonly List<(int Row, string? Key, string Reason)> _pendingNotProduct = [];
    private readonly string _runId;
    private string? _lastSavedLink;   // link đã lưu được gần nhất trong khối — link mẫu ưu tiên số 1
    // Số lần kiểm chứng phiên trong khối (log). Không cần chặn trên: lần KHÔNG thành công dừng khối ngay (bộ xử lý sống theo
    // một lượt khối); lần thành công chỉ có sau một kết quả dòng khác chen vào giữa hai chuỗi → ≤ số dòng / (N + 1).
    private int _sessionChecks;
    private bool _sessionProven;      // link mẫu vừa chứng minh phiên ổn trong chuỗi notProduct đang chạy → khỏi kiểm lại tới khi chuỗi bị cắt
    private bool _noSampleChain;      // chuỗi đang chạy đã gặp "không có link mẫu" → phần còn lại của chuỗi bỏ qua (đã log một lần)
    private int _clickMisses;         // GĐ7: số dòng LIÊN TIẾP app bấm nút hộp nổi mà extension không nhận

    public HarvestRowProcessor(InstanceConfig config, HarvestRunOptions options, HarvestPdpBreaker breaker, Action<string> log)
    {
        _config = config;
        _options = options;
        _breaker = breaker;
        _log = log;
        Book = new HarvestRowBook(config);
        _breaker.ResetStreak();   // lượt mới (vd sau khi đổi IP) đếm lại từ đầu; mã đã bật thì cầu dao vẫn nhớ
        _runId = string.IsNullOrWhiteSpace(options.RunId) ? Guid.NewGuid().ToString("N") : options.RunId;
    }

    /// <summary>Các dòng notProduct đang chờ kết luận (cho test).</summary>
    public IReadOnlyList<int> PendingNotProductRows => _pendingNotProduct.Select(x => x.Row).ToList();

    public HarvestRowBook Book { get; }

    public async Task<HarvestRowOutcome> HandleAsync(
        HarvestStepResult step, HarvestRowInput row, IHarvestRowHost host, CancellationToken cancellationToken)
    {
        var outcome = await HandleCoreAsync(step, row, host, cancellationToken).ConfigureAwait(false);
        // Khối dừng (traffic / proxy / captcha / mất đăng nhập / chuỗi notProduct…): dòng notProduct đang chờ KHÔNG kết luận được
        // → bỏ, làm lại cùng phần dở của khối (LastCompletedRow chưa qua chúng).
        if (outcome.Next == HarvestRowNext.Stop) _pendingNotProduct.Clear();
        return outcome;
    }

    private static bool IsNotProduct(HarvestStepAction action, HarvestStepResult step) =>
        action == HarvestStepAction.RowFailed && step.NotProduct && !step.HarvestOk;

    private async Task<HarvestRowOutcome> HandleCoreAsync(
        HarvestStepResult step, HarvestRowInput row, IHarvestRowHost host, CancellationToken cancellationToken)
    {
        var rowNumber = row.RowNumber;
        var decision = HarvestStepPolicy.Decide(step, _breaker);
        // GĐ7: bấm trượt có hệ thống (zoom / bị che…) thì dòng nào cũng hỏng như nhau → N dòng liên tiếp thì dừng CẢ job (lỗi của
        // máy, không phải tài khoản / SP); chưa tới ngưỡng thì là lỗi dòng tạm như thường.
        if (step.ClickMissed)
        {
            if (++_clickMisses >= HarvestPanelClickFailure.BreakerThreshold)
            {
                Book.DiscardPendingPdpRows();
                _config.RunnerPhase = "error";
                _config.LastRunnerMessage = HarvestPanelClickFailure.Message(rowNumber, _clickMisses, step.Message);
                return new(HarvestRowNext.Stop, decision.Action, false, step.TabId ?? row.TabId);
            }
        }
        else _clickMisses = 0;
        // Kết quả dòng KHÁC notProduct (lưu / lỗi dòng / pdpError) cắt chuỗi notProduct → các dòng đang chờ là lỗi dòng thật.
        if (decision.Action is HarvestStepAction.Save or HarvestStepAction.PdpError
            || (decision.Action == HarvestStepAction.RowFailed && !IsNotProduct(decision.Action, step)))
        {
            await FlushPendingNotProductAsync(cancellationToken).ConfigureAwait(false);
            _sessionProven = false;   // chuỗi bị cắt → chuỗi notProduct sau phải kiểm chứng lại
            _noSampleChain = false;
        }

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

            case HarvestStepAction.LoginRequired:
                // Tài khoản Shopee trong profile bị đăng xuất: KHÔNG phải lỗi của SP. Dừng khối, dòng không tính xong cũng không
                // tính lỗi (làm lại bằng tài khoản khác); phase "error" → ScrapeFailurePolicy cho tài khoản nghỉ (Cooldown).
                Book.DiscardPendingPdpRows();
                _config.RunnerPhase = "error";
                // Tên tài khoản ghi ở log riêng — KHÔNG ghép vào thông điệp đi qua Classify (tên có chữ verify/captcha/proxy
                // làm phân loại sai).
                _log($"Tài khoản Shopee {_config.DisplayName} mất đăng nhập ở dòng {rowNumber}.");
                _config.LastRunnerMessage =
                    $"Tài khoản Shopee mất đăng nhập (bị chuyển tới trang đăng nhập) ở dòng {rowNumber} — " +
                    "dòng này không tính lỗi, cho tài khoản nghỉ và làm lại bằng tài khoản khác.";
                return new(HarvestRowNext.Stop, decision.Action, false, step.TabId ?? row.TabId);

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

        // GĐ7: bước báo lỗi CHỖ LƯU (vd app chưa nối Hub nên extension không tự đẩy được) → dừng job như sink lỗi (StopJob: nhả
        // tài khoản nguyên vẹn, không bỏ dòng).
        if (decision.Action == HarvestStepAction.RowFailed && HarvestSinkFailure.Is(step.Message))
        {
            Book.DiscardPendingPdpRows();
            _config.RunnerPhase = "error";
            _config.LastRunnerMessage = step.Message;
            return new(HarvestRowNext.Stop, decision.Action, false, step.TabId ?? row.TabId);
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

        if (decision.Action == HarvestStepAction.Save && _options.ExtensionPush is not null && step.HubPush is not { IsSaved: true })
        {
            // GĐ7: extension lấy được JSON nhưng Hub CHƯA nhận → dòng không xong, KHÔNG ghi done (lượt sau lấy lại / bỏ qua nếu Hub
            // đã có). Bắt được JSON = IP / phiên ổn (cầu dao đã ghi nhận như dòng lưu được).
            _config.CaptchaError = false;
            var push = step.HubPush;
            var why = HarvestStepPolicy.SafeForClassify(push?.Reason);
            switch (push?.Status)
            {
                case HarvestHubPush.Invalid:
                {
                    // Nội dung JSON của RIÊNG SP này hỏng (như HarvestItemRejectedException ở outbox) → lỗi VĨNH VIỄN (luật 2 lượt).
                    var reason = "Dữ liệu SP không xử lý được: " + why;
                    Book.RowFailed(rowNumber, reason);
                    _log($"Dòng {rowNumber}: {reason}");
                    await RecordPermanentAsync(rowNumber,
                        HarvestItemKey.ForSaved(row.SourceLink, step.ItemId!.Value, step.ShopId), reason, cancellationToken).ConfigureAwait(false);
                    break;
                }
                case HarvestHubPush.Rejected:
                {
                    // Hub từ chối dữ liệu: SP Hub từ chối KHÔNG tính là đã có (GĐ6) → lỗi tạm, lượt sau lấy lại.
                    var reason = $"Hub từ chối SP {step.ItemId}: {why} — lượt sau lấy lại.";
                    Book.RowFailed(rowNumber, reason);
                    _log($"Dòng {rowNumber}: {reason}");
                    break;
                }
                case HarvestHubPush.Queued:
                {
                    // Hub tạm lỗi: SP nằm BỀN trong hàng đợi của extension (tự gửi lại) — app không ghi done vì chưa chắc Hub
                    // nhận; lượt sau hỏi Hub (known) thấy đã có thì bỏ qua, chưa có thì lấy lại (không mất SP).
                    var reason = $"Hub chưa nhận SP {step.ItemId} ({why}) — đã vào hàng đợi của extension, tự gửi lại; lượt sau kiểm lại.";
                    Book.RowFailed(rowNumber, reason);
                    _log($"Dòng {rowNumber}: {reason}");
                    break;
                }
                default:
                    // Không gửi được mà cũng không giữ được (chưa cấu hình / thiếu quyền / token sai / hàng đợi đầy) hoặc extension
                    // không báo kết quả gửi Hub → lỗi CHỖ LƯU: dừng job (StopJob), dòng không bị bỏ.
                    _config.RunnerPhase = "error";
                    _config.LastRunnerMessage = HarvestSinkFailure.Message(rowNumber, new InvalidOperationException(
                        "extension không gửi được Hub: " + (push is null ? "extension không báo kết quả gửi Hub (extension cũ?)" : why)));
                    return new(HarvestRowNext.Stop, decision.Action, false, tabId);
            }
            if (_sourceMonitor.Record(step.Source) is { } pushReloadRatio)
                _log($"⚠ Tỉ lệ phải tải lại trang mới bắt được get_pc cao: {pushReloadRatio:P0} trong 20 dòng gần nhất " +
                    "— trang ít tự gọi get_pc lần đầu, mỗi dòng tốn thêm một lần tải.");
        }
        else if (decision.Action == HarvestStepAction.Save)
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
                step.HarvestedAtOr(row.ReceivedAt),
                _options.RecordDone);
            var saveError = await Book.SaveAsync(_options.Sink, harvested, cancellationToken).ConfigureAwait(false);
            if (saveError is HarvestItemRejectedException rejected)
            {
                // JSON của RIÊNG SP này không xử lý được → lỗi dòng như notProduct, sang dòng kế; KHÔNG dừng job (dừng
                // thì "Tiếp tục" kẹt đúng dòng này mãi).
                var reason = "Dữ liệu SP không xử lý được: " + rejected.Message;
                Book.RowFailed(rowNumber, reason);
                _log($"Dòng {rowNumber}: {reason}");
                // Lỗi VĨNH VIỄN (nội dung JSON): lượt sau không lấy lại SP này.
                await RecordPermanentAsync(rowNumber,
                    HarvestItemKey.ForSaved(row.SourceLink, step.ItemId!.Value, step.ShopId), reason, cancellationToken).ConfigureAwait(false);
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
                _lastSavedLink = row.Link;
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
        else if (IsNotProduct(decision.Action, step))
        {
            // notProduct: CHƯA ghi — chờ xem có phải chuỗi (lỗi phiên / tài khoản) không.
            var reason = step.Message ?? "không phải trang sản phẩm";
            _pendingNotProduct.Add((rowNumber, HarvestItemKey.FromLink(row.SourceLink), reason));
            _log($"Dòng {rowNumber}: {reason}");
            if (_noSampleChain)
            {
                // Chuỗi đã biết không có link mẫu (đã log): dòng này cũng bỏ qua ở lượt này, đóng chuỗi tới đây.
                SkipPendingNotProductUnresolved();
                AdvanceTo(rowNumber);
            }
            else if (_sessionProven
                ? _pendingNotProduct.Count >= HarvestStepPolicy.ProvenChainRecheckEvery
                : _pendingNotProduct.Count >= HarvestStepPolicy.NotProductBreakerThreshold)
            {
                // Chuỗi đã chứng minh phiên ổn: các dòng chết sau đó được GIỮ CHỜ (không ghi ngay, không AdvanceTo) — đủ
                // ProvenChainRecheckEvery dòng thì kiểm chứng lại; dòng chưa có lần kiểm nào "phía sau" bảo lãnh không vào tập lỗi.
                var first = _pendingNotProduct[0].Row;
                if (!_sessionProven && ResolveSample() is null)
                {
                    // Không có mẫu thì KHÔNG chứng minh được phiên hỏng: dừng khối chỉ đốt tài khoản mà lượt sau vẫn phải làm lại các
                    // dòng này. Chuỗi tính là đã duyệt (KHÔNG ghi lỗi, không có trong done → lượt sau thử lại), khối chạy tiếp, tài
                    // khoản không bị đụng. Lưu được một dòng thì link đó thành mẫu cho các chuỗi sau. Mất đăng nhập rõ ràng vẫn bắt
                    // qua loginRequired.
                    _log($"Chưa có link mẫu để kiểm phiên — {_pendingNotProduct.Count} dòng (dòng {first}–{rowNumber}) không phải " +
                         "trang SP sẽ thử lại lượt sau.");
                    SkipPendingNotProductUnresolved();
                    _noSampleChain = true;
                    AdvanceTo(rowNumber);
                    return new(HarvestRowNext.Done, decision.Action, false, step.TabId ?? row.TabId);
                }
                // Kiểm chứng phiên bằng link mẫu: link chết thật đứng liền nhau (shop nguồn bị khoá…) không được làm khối dừng ở
                // đúng chỗ đó mãi + tài khoản bị cho nghỉ oan.
                var (sessionOk, why) = await VerifySessionAsync(host, rowNumber, step.TabId ?? row.TabId, cancellationToken)
                    .ConfigureAwait(false);
                if (sessionOk == true)
                {
                    _log($"Link mẫu lấy được dữ liệu — tài khoản ổn: {_pendingNotProduct.Count} dòng (dòng {first}–{rowNumber}) là SP " +
                         "không còn / không phải trang SP thật, ghi lỗi và chạy tiếp" +
                         (_sessionProven ? $" (kiểm lại sau mỗi {HarvestStepPolicy.ProvenChainRecheckEvery} dòng)." : "."));
                    await FlushPendingNotProductAsync(cancellationToken).ConfigureAwait(false);
                    _sessionProven = true;
                    AdvanceTo(rowNumber);   // đóng chuỗi: lần vá (nếu có) đi tiếp từ sau chuỗi
                }
                else
                {
                    // Phiên đáng ngờ (hoặc không có mẫu để kiểm): dừng khối, chuỗi KHÔNG ghi lỗi (lượt sau tự thử lại vì không có
                    // trong done) nhưng VẪN tính là đã duyệt — lần vá bằng tài khoản khác đi tiếp từ sau chuỗi, không lặp tại chỗ
                    // (mỗi chuỗi cho tối đa MỘT tài khoản nghỉ).
                    var unresolved = _pendingNotProduct.Count;
                    SkipPendingNotProductUnresolved();   // cũng ghi các dòng pdpError đang chờ (AdvanceTo vượt qua chúng)
                    AdvanceTo(rowNumber);
                    if (sessionOk is null)   // mẫu gặp captcha: config đã đặt như dòng thường gặp captcha
                        return new(HarvestRowNext.Stop, decision.Action, false, step.TabId ?? row.TabId);
                    _config.RunnerPhase = "error";
                    _log($"Nghi phiên / tài khoản {_config.DisplayName} có vấn đề (dòng {first}–{rowNumber}).");
                    _config.LastRunnerMessage =
                        $"{unresolved} dòng liên tiếp (dòng {first}–{rowNumber}) không mở ra trang sản phẩm và {why} — nghi " +
                        "phiên / tài khoản có vấn đề: dừng khối; các dòng này KHÔNG tính lỗi, bỏ qua ở lượt này (lượt sau thử lại), " +
                        "phần sau làm bằng tài khoản khác.";
                    return new(HarvestRowNext.Stop, decision.Action, false, step.TabId ?? row.TabId);
                }
            }
        }
        else
        {
            // hết giờ / thiếu JSON / lỗi RPC / pdpError của mã đã bật cầu dao: ghi sổ bỏ qua rồi sang dòng kế.
            var reason = step.Message ?? "không lấy được dữ liệu";
            Book.RowFailed(rowNumber, reason);
            _log($"Dòng {rowNumber}: {reason}");
            // Mã pdpError đã xác nhận SP chết → VĨNH VIỄN (cần thấy ở nhiều lượt mới bỏ qua); còn lại là tạm: không ghi.
            if (HarvestStepPolicy.IsPermanentRowFailure(decision.Action, step)
                && HarvestItemKey.FromLink(row.SourceLink) is { } failedKey)
                await RecordPermanentAsync(rowNumber, [failedKey], reason, cancellationToken).ConfigureAwait(false);
        }

        _config.RunLog ??= [];
        _config.RunLog.Add(new RunnerLogEntry { RowNumber = rowNumber, Sku = row.Sku, ScrapeOk = saved });
        if (_config.RunLog.Count > 200)
            _config.RunLog.RemoveRange(0, _config.RunLog.Count - 200);

        _config.LastSku = row.Sku;
        _config.LastRunnerMessage = saved ? $"Xong dòng {rowNumber} - đã lưu dữ liệu." : $"Xong dòng {rowNumber}.";
        return new(HarvestRowNext.Done, decision.Action, saved, tabId);
    }

    /// <summary>Ghi SP lỗi vĩnh viễn vào tập lỗi (toàn cục theo khoá SP — nếu caller có theo dõi). Ghi hỏng chỉ log — dòng vẫn là
    /// lỗi dòng thường, lượt sau thử lại.</summary>
    private async Task RecordPermanentAsync(int rowNumber, IReadOnlyList<string> keys, string reason, CancellationToken cancellationToken)
    {
        if (_options.FailureSink is not { } sink || !_options.RecordDone || keys.Count == 0) return;
        try
        {
            await sink.RecordPermanentFailureAsync(keys, reason, _runId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _log($"Dòng {rowNumber}: không ghi được vào danh sách lỗi vĩnh viễn ({ex.Message}) — lượt sau sẽ thử lại dòng này.");
        }
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

    /// <summary>Kiểm chứng phiên: mở link mẫu (link lưu được gần nhất trong khối, không thì nguồn dự phòng của caller — SP lưu
    /// được gần nhất trong outbox) trên cùng tài khoản / Brave. true = mẫu lấy được JSON. Không có mẫu / quá số lần / mẫu hỏng →
    /// false + lý do (cho thông điệp dừng, đã làm sạch cho Classify). null = mẫu gặp captcha không giải được → đã đặt config
    /// y như dòng thường gặp captcha (paused + CaptchaError → cách ly tài khoản).</summary>
    private async Task<(bool? Ok, string Why)> VerifySessionAsync(IHarvestRowHost host, int rowNumber, int? tabId,
        CancellationToken cancellationToken)
    {
        if (ResolveSample() is not { } sample)
            return (false, "chưa có link mẫu (SP đã lấy được) để kiểm chứng phiên");
        _sessionChecks++;
        _log($"Kiểm chứng phiên (lần {_sessionChecks}) bằng link mẫu {sample}…");
        return await ProbeVerdictAsync(host, sample, rowNumber, tabId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(bool? Ok, string Why)> ProbeVerdictAsync(IHarvestRowHost host, string sample, int rowNumber, int? tabId,
        CancellationToken cancellationToken)
    {
        HarvestStepResult probe;
        try
        {
            probe = await host.ProbeSampleAsync(sample, rowNumber, tabId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return (false, $"mở link mẫu lỗi ({HarvestStepPolicy.SafeForClassify(ex.Message)})");
        }
        if (probe.Captcha && !probe.HarvestOk)
        {
            // Y như dòng thường gặp captcha: chờ giải tay MỘT lần; giải xong thì mở lại mẫu một lần, không thì dừng như captcha.
            var probeTab = probe.TabId ?? tabId;
            if (probeTab is int t)
            {
                var chk = await host.CheckCaptchaAsync(t, rowNumber, "", cancellationToken).ConfigureAwait(false);
                if (chk.Aborted) throw new OperationCanceledException();
                if (chk.Ok)
                {
                    _config.CaptchaError = false;
                    try
                    {
                        probe = await host.ProbeSampleAsync(sample, rowNumber, chk.TabId ?? t, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        return (false, $"mở lại link mẫu lỗi ({HarvestStepPolicy.SafeForClassify(ex.Message)})");
                    }
                    if (HarvestStepPolicy.SampleProvesSession(probe)) return (true, "");
                }
            }
            if (probe.Captcha && !probe.HarvestOk)
            {
                Book.DiscardPendingPdpRows();
                _config.RunnerPhase = "paused";
                _config.CaptchaError = true;
                _config.CaptchaUrl = sample;
                _config.LastRunnerMessage = $"🚫 Dừng vì captcha khi kiểm chứng phiên bằng link mẫu (dòng {rowNumber}) — giải tay rồi bấm Tiếp tục.";
                return (null, "");
            }
        }
        if (HarvestStepPolicy.SampleProvesSession(probe)) return (true, "");
        var what = probe.LoginRequired ? "tài khoản mất đăng nhập"
            : probe.NotProduct ? "cũng không phải trang SP"
            : probe.Captcha ? "gặp trang chặn cần giải tay"   // KHÔNG dùng chữ captcha/verify/proxy: Classify đọc chữ đó ra cách ly / proxy
            : probe.TrafficError ? "bị chặn traffic"
            : "không lấy được dữ liệu";
        return (false, $"link mẫu {what}");
    }

    /// <summary>Link mẫu: link lưu được gần nhất trong khối, không thì nguồn dự phòng của caller (SP lưu được gần nhất trong outbox).
    /// null = không có.</summary>
    private string? ResolveSample()
    {
        var sample = _lastSavedLink;
        if (string.IsNullOrWhiteSpace(sample))
        {
            try { sample = _options.SampleLink?.Invoke(); }
            catch (Exception ex) { _log($"Không đọc được link mẫu ({ex.Message})."); sample = null; }
        }
        return string.IsNullOrWhiteSpace(sample) ? null : sample;
    }

    /// <summary>Đóng chuỗi tới <paramref name="row"/>: tiến LastCompletedRow / NextRunRow (không lùi) — ScrapeRunner vá từ
    /// LastCompletedRow + 1, chuỗi đã kết luận không được bị làm lại ngay trong lượt.</summary>
    private void AdvanceTo(int row)
    {
        if ((_config.LastCompletedRow ?? 0) < row) _config.LastCompletedRow = row;
        if ((_config.NextRunRow ?? 0) < row + 1) _config.NextRunRow = row + 1;
    }

    /// <summary>Lý do sổ bỏ qua cho dòng "đã duyệt" mà chưa kết luận được (chuỗi notProduct không kiểm chứng được phiên).</summary>
    internal const string UnresolvedReason = "chưa kết luận (không kiểm chứng được phiên) — thử lại lượt sau";

    /// <summary>Chuỗi notProduct "đã duyệt" mà CHƯA kết luận (không có mẫu / mẫu hỏng / mẫu captcha): ghi các dòng vào sổ bỏ qua của
    /// LƯỢT (lỗi tạm — tổng kết cuối job hiện "BỎ QUA n dòng", không báo hoàn thành sai), KHÔNG vào tập lỗi vĩnh viễn, KHÔNG vào
    /// done → lượt sau thử lại. RowFailed đầu tiên cũng xả các dòng pdpError đang chờ (AdvanceTo sắp vượt qua chúng — bỏ đi là
    /// mất dấu).</summary>
    private void SkipPendingNotProductUnresolved()
    {
        Book.FlushPendingPdpRows();
        foreach (var (row, _, _) in _pendingNotProduct)
            Book.RowFailed(row, UnresolvedReason);
        _pendingNotProduct.Clear();
    }

    /// <summary>Phiên có vấn đề (khối dừng ở bước kiểm trước link kế…): bỏ chuỗi notProduct đang chờ — làm lại, không ghi lỗi.</summary>
    public void DiscardPendingNotProduct() => _pendingNotProduct.Clear();

    /// <summary>Chuỗi notProduct bị cắt (kết quả dòng khác / hết khối, chưa tới ngưỡng): các dòng đang chờ là lỗi dòng thật —
    /// ghi sổ bỏ qua + tập lỗi vĩnh viễn (cần nhiều lượt mới bị bỏ qua hẳn).</summary>
    public async Task FlushPendingNotProductAsync(CancellationToken cancellationToken)
    {
        var pending = _pendingNotProduct.ToList();
        _pendingNotProduct.Clear();
        foreach (var (row, key, reason) in pending)
        {
            Book.RowFailed(row, reason);
            if (key is not null) await RecordPermanentAsync(row, [key], reason, cancellationToken).ConfigureAwait(false);
        }
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
