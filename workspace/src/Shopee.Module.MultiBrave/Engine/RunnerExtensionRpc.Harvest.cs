using System.Net.WebSockets;
using System.Text.Json;
using Shopee.Core.Cdp;
using Shopee.Core.Harvest;

namespace OpenMultiBraveLauncherV3;

/// <summary>
/// Bước HARVEST (<c>__launcherExecuteHarvestStep</c> của extension <c>shopee-harvest</c>). Đi đường RIÊNG, không qua
/// <see cref="EvaluateExtensionRawAsync"/> của scrape, vì đường đó có hai chỗ gọi lại chồng một bước dài
/// (P-20261115-5, P-20261115-8): (1) SW-direct hết giờ/lỗi → rơi xuống popup có race 6 s → báo lỗi trong khi bước vẫn
/// chạy, vòng <c>attempt</c> gọi lại; (2) <c>maxAttempts:2</c> gọi lại sau khi hết 600 s.
/// <para>Luật ở đây: chỉ gọi lại khi CHẮC lệnh CHƯA tới extension (không có target SW/popup, attach hỏng, hàm chưa
/// định nghĩa, "Receiving end does not exist"). Lệnh ĐÃ gửi mà hết giờ / đứt kết nối / lỗi giữa chừng → trả lỗi bước,
/// KHÔNG gọi lại; vòng ngoài coi là lỗi dòng rồi sang dòng kế — lệnh kế tự làm bước cũ (nếu còn chạy) dừng ở điểm
/// kiểm kế tiếp với <c>superseded</c>, nên gọi TUẦN TỰ sau khi hết giờ là an toàn. Popup dùng biểu thức KHÔNG race 6 s.</para>
/// </summary>
internal static partial class RunnerExtensionRpc
{
    /// <summary>Trần chờ một bước harvest phía C#: lớn hơn trần tổng 540 s của extension (STEP_TOTAL_TIMEOUT_MS) để
    /// C# không bỏ chờ trước khi extension kịp tự kết thúc bước.</summary>
    internal static readonly TimeSpan HarvestStepReceiveTimeout = TimeSpan.FromSeconds(600);

    /// <summary>Số lần thử GỬI khi lệnh chắc chắn chưa tới extension (SW chưa lên / chưa nạp hook).</summary>
    private const int MaxHarvestDeliveryAttempts = 3;

    /// <summary>Payload bước harvest. <paramref name="blockErrorCodes"/> null/rỗng → BỎ khoá để extension dùng mặc định.
    /// Không có khoá ghi tiến độ: extension harvest không ghi lastCompletedRow, C# ghi sau khi sink lưu xong.</summary>
    internal static string BuildHarvestStepPayload(
        string link, int rowNumber, string statusText, string instanceName, int? tabId,
        IReadOnlyList<long>? blockErrorCodes)
    {
        var payload = new Dictionary<string, object?>
        {
            ["link"] = link,
            ["rowNumber"] = rowNumber,
            ["statusText"] = statusText,
            ["instanceName"] = instanceName,
            ["tabId"] = tabId,
        };
        if (blockErrorCodes is { Count: > 0 })
            payload["blockErrorCodes"] = blockErrorCodes;
        return JsonSerializer.Serialize(payload);
    }

    public static async Task<HarvestStepResult> ExecuteHarvestStepAsync(
        int cdpPort,
        DirectoryInfo profileRoot,
        string link,
        int rowNumber,
        string statusText,
        string instanceName,
        int? tabId,
        IReadOnlyList<long>? blockErrorCodes,
        CancellationToken cancellationToken = default)
    {
        var payload = BuildHarvestStepPayload(link, rowNumber, statusText, instanceName, tabId, blockErrorCodes);
        try
        {
            var lastReason = "";
            for (var attempt = 0; attempt < MaxHarvestDeliveryAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (attempt > 0)
                {
                    // Lệnh trước CHƯA tới extension → dựng lại SW (đánh thức / reload hook) rồi gửi lại: không chồng bước.
                    await RunnerSwLifecycle.EnsureRunnerExtensionReadyAsync(cdpPort, profileRoot, null, cancellationToken)
                        .ConfigureAwait(false);
                }

                var extensionId = await RunnerSwLifecycle.ResolveExtensionIdAsync(cdpPort, profileRoot, cancellationToken)
                    .ConfigureAwait(false);
                if (extensionId is null)
                {
                    lastReason = $"không tìm thấy extension {RunnerExtensionPaths.ExtensionDisplayName}";
                    continue;
                }

                var outcome = await DeliverHarvestStepAsync(cdpPort, extensionId, payload, cancellationToken)
                    .ConfigureAwait(false);
                switch (outcome.Kind)
                {
                    case HarvestDelivery.Answered:
                        return HarvestStepResult.Parse(outcome.Value, tabId, link);
                    case HarvestDelivery.SentNoAnswer:
                        return HarvestStepResult.Failure(outcome.Reason, tabId, link);
                    default:
                        lastReason = outcome.Reason;
                        continue;
                }
            }

            // "No SW" → vòng Harvest ném lỗi extension → phiên tự mở lại profile (như bước scrape).
            return HarvestStepResult.Failure($"No SW: bước harvest không gửi được tới extension ({lastReason}).", tabId, link);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            if (msg.Contains("No tab with id", StringComparison.OrdinalIgnoreCase))
                return HarvestStepResult.Failure("Tab harvest tạm mất kết nối — giữ tab để thử lại.", tabId, link);
            // Lỗi khi dựng lại SW (EnsureRunnerExtensionReady ném) = lệnh chưa hề tới extension → "No SW" để mở lại profile.
            return HarvestStepResult.Failure($"No SW: {msg}", tabId, link);
        }
    }

    internal enum HarvestDelivery { NotDelivered, SentNoAnswer, Answered }

    internal readonly record struct HarvestDeliveryOutcome(HarvestDelivery Kind, JsonElement Value, string Reason)
    {
        public static HarvestDeliveryOutcome NotDelivered(string reason) => new(HarvestDelivery.NotDelivered, default, reason);
        public static HarvestDeliveryOutcome SentNoAnswer(string reason) => new(HarvestDelivery.SentNoAnswer, default, reason);
        public static HarvestDeliveryOutcome Answered(JsonElement value) => new(HarvestDelivery.Answered, value, "");
    }

    /// <summary>Gửi MỘT lần: SW-direct trước, chỉ khi lệnh chưa tới SW mới thử qua popup. Không vòng lặp gọi lại.
    /// <paramref name="method"/>: lệnh DÀI của extension (bước harvest, hoặc <c>prepareHarvestClick</c> của GĐ7).</summary>
    private static async Task<HarvestDeliveryOutcome> DeliverHarvestStepAsync(
        int cdpPort, string extensionId, string payloadJson, CancellationToken ct, string method = "executeHarvestStep")
    {
        var payloadExpr = PayloadExpression(payloadJson);
        var timeoutMs = (int)HarvestStepReceiveTimeout.TotalMilliseconds;

        var sw = await TryHarvestOnServiceWorkerAsync(
            cdpPort, extensionId, BuildServiceWorkerMethodExpression(method, payloadExpr), timeoutMs, ct)
            .ConfigureAwait(false);
        if (sw.Kind != HarvestDelivery.NotDelivered)
            return sw;

        var popupWsUrl = await RunnerExtensionTargets.FindExtensionPopupDebuggerUrlAsync(cdpPort, extensionId, ct)
            .ConfigureAwait(false);
        if (popupWsUrl is null)
        {
            await RunnerSwLifecycle.TryWakeServiceWorkerAsync(cdpPort, extensionId, ct).ConfigureAwait(false);
            return HarvestDeliveryOutcome.NotDelivered($"{sw.Reason}; chưa có popup extension");
        }

        using var socket = new ClientWebSocket();
        try
        {
            await socket.ConnectAsync(new Uri(popupWsUrl), ct).ConfigureAwait(false);
            await CdpClient.SendAsync(socket, 1, "Runtime.enable", null, ct, receiveTimeoutMs: CdpReceiveTimeoutMs)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return HarvestDeliveryOutcome.NotDelivered($"không nối được popup: {ex.Message}");
        }

        JsonElement popupResult;
        try
        {
            popupResult = await CdpClient.SendAsync(socket, 2, "Runtime.evaluate", new
            {
                expression = BuildPopupLongInvokeExpression(method, payloadExpr),
                awaitPromise = true,
                returnByValue = true,
            }, ct, receiveTimeoutMs: timeoutMs).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return HarvestDeliveryOutcome.SentNoAnswer(SentNoAnswerMessage(ex));
        }

        return ClassifyHarvestEvalResult(popupResult);
    }

    /// <summary>SW-direct có theo dõi "đã gửi chưa": mọi lỗi TRƯỚC khi gửi Runtime.evaluate = chưa tới; sau đó = đã gửi.</summary>
    private static async Task<HarvestDeliveryOutcome> TryHarvestOnServiceWorkerAsync(
        int cdpPort, string extensionId, string expression, int timeoutMs, CancellationToken ct)
    {
        string? swTargetId;
        try { swTargetId = await RunnerExtensionTargets.GetSwTargetIdFromListAsync(cdpPort, extensionId, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { return HarvestDeliveryOutcome.NotDelivered($"không đọc được target SW: {ex.Message}"); }
        if (string.IsNullOrWhiteSpace(swTargetId))
            return HarvestDeliveryOutcome.NotDelivered("SW không có trong /json/list");

        var hub = PortCdpHub.For(cdpPort);
        string? sess = null;
        try
        {
            return await SendTrackedAsync(
                async () => sess = await hub.AttachAsync(swTargetId, ct).ConfigureAwait(false),
                session => hub.SendAsync("Runtime.enable", null, session, ct),
                session => hub.SendAsync(
                    "Runtime.evaluate",
                    new { expression, awaitPromise = true, returnByValue = true },
                    session, ct, timeoutMs),
                ct).ConfigureAwait(false);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(sess))
                try { await hub.SendAsync("Target.detachFromTarget", new { sessionId = sess }, null, CancellationToken.None).ConfigureAwait(false); } catch { }
        }
    }

    /// <summary>
    /// Lõi "đã gửi chưa" của SW-direct, tách ra để test bằng giả lập: attach → (Runtime.enable, lỗi bỏ qua) → đánh dấu
    /// ĐÃ GỬI → Runtime.evaluate. Lỗi attach / attach rỗng = CHƯA tới (được gửi lại). Lỗi từ lúc gọi evaluate trở đi =
    /// có thể bước đã chạy → <see cref="HarvestDelivery.SentNoAnswer"/>, không gửi lại. Huỷ của người dùng ném tiếp.
    /// </summary>
    internal static async Task<HarvestDeliveryOutcome> SendTrackedAsync(
        Func<Task<string?>> attach,
        Func<string, Task> enableRuntime,
        Func<string, Task<JsonElement>> evaluate,
        CancellationToken ct)
    {
        var sent = false;
        try
        {
            var sess = await attach().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(sess))
                return HarvestDeliveryOutcome.NotDelivered("không attach được SW");
            try { await enableRuntime(sess).ConfigureAwait(false); } catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; } catch { }

            sent = true;
            var result = await evaluate(sess).ConfigureAwait(false);
            // CdpSession (kết nối dùng chung) KHÔNG ném khi CDP trả lỗi giao thức: nó trả nguyên gói
            // {"id":…,"error":{code,message}}. Lỗi đó là phản hồi CỦA CHÍNH lệnh evaluate — CDP từ chối lệnh vì
            // session/target không còn → lệnh không chạy → được gửi lại. Khác hẳn socket đứt trong lúc chờ (CdpSession
            // huỷ pending → ném OperationCanceled, nhánh catch dưới) — khi đó bước có thể đã chạy → SentNoAnswer.
            if (TryGetProtocolError(result, out var protocolError))
                return IsEvaluateRejectedByCdp(protocolError)
                    ? HarvestDeliveryOutcome.NotDelivered($"CDP từ chối Runtime.evaluate: {protocolError}")
                    : HarvestDeliveryOutcome.SentNoAnswer($"CDP báo lỗi cho bước harvest ({protocolError}) — không gọi lại.");
            return ClassifyHarvestEvalResult(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return sent
                ? HarvestDeliveryOutcome.SentNoAnswer(SentNoAnswerMessage(ex))
                : HarvestDeliveryOutcome.NotDelivered($"không attach được SW: {ex.Message}");
        }
    }

    /// <summary>Gói phản hồi là lỗi giao thức CDP (<c>{"error":{"code":…,"message":…}}</c>, không có <c>result</c>).</summary>
    private static bool TryGetProtocolError(JsonElement response, out string message)
    {
        message = "";
        if (response.ValueKind != JsonValueKind.Object ||
            !response.TryGetProperty("error", out var err) || err.ValueKind != JsonValueKind.Object)
            return false;
        message = err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString() ?? ""
            : err.GetRawText();
        return true;
    }

    /// <summary>Lỗi giao thức CDP trả cho lệnh evaluate mà nghĩa là lệnh KHÔNG được chạy (session/target đã mất).</summary>
    internal static bool IsEvaluateRejectedByCdp(string protocolError) =>
        protocolError.Contains("Session with given id not found", StringComparison.OrdinalIgnoreCase) ||
        protocolError.Contains("No target with given id", StringComparison.OrdinalIgnoreCase) ||
        protocolError.Contains("Target closed", StringComparison.OrdinalIgnoreCase);

    /// <summary>Phân loại phản hồi Runtime.evaluate của bước harvest. Ngoại lệ JS chỉ coi là "chưa tới" khi CHẮC hàm
    /// bước chưa chạy (hàm chưa định nghĩa / không có context / không có người nhận message); còn lại là đã gửi.</summary>
    internal static HarvestDeliveryOutcome ClassifyHarvestEvalResult(JsonElement evalResult)
    {
        if (evalResult.TryGetProperty("exceptionDetails", out var exDetails) &&
            exDetails.ValueKind == JsonValueKind.Object)
        {
            var message = FormatCdpException(exDetails);
            return IsHarvestNotStarted(message)
                ? HarvestDeliveryOutcome.NotDelivered(message)
                : HarvestDeliveryOutcome.SentNoAnswer(message);
        }

        if (evalResult.TryGetProperty("result", out var res) &&
            res.TryGetProperty("value", out var val) &&
            val.ValueKind == JsonValueKind.Object)
            return HarvestDeliveryOutcome.Answered(val.Clone());

        return HarvestDeliveryOutcome.SentNoAnswer("Extension không phản hồi.");
    }

    internal static bool IsHarvestNotStarted(string message) =>
        message.Contains("is not a function", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Cannot find context", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Receiving end does not exist", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Could not establish connection", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("POPUP_CONTEXT_DEAD", StringComparison.OrdinalIgnoreCase);

    private static string SentNoAnswerMessage(Exception ex) => ex is OperationCanceledException
        ? $"Bước harvest quá {(int)HarvestStepReceiveTimeout.TotalSeconds}s chưa trả kết quả — không gọi lại (bước kế sẽ thay bước cũ)."
        : $"Mất kết nối khi chờ bước harvest ({ex.Message}) — không gọi lại.";

    /// <summary>Cầu popup cho lệnh DÀI: như <see cref="BuildPopupInvokeExpression"/> nhưng KHÔNG đua 6 s — chờ đúng phản
    /// hồi của SW (trần thời gian do receive-timeout CDP quyết).</summary>
    internal static string BuildPopupLongInvokeExpression(string method, string payloadExpression) =>
        "(async () => {" +
        "if (!(globalThis.chrome && chrome.runtime && chrome.runtime.sendMessage)) " +
        "throw new Error('POPUP_CONTEXT_DEAD: chrome.runtime không khả dụng (popup bị orphan)');" +
        "const response = await chrome.runtime.sendMessage({" +
        $"type:'LAUNCHER_INVOKE',method:{JsonSerializer.Serialize(method)},payload:{payloadExpression}" +
        "});" +
        "if (!response?.ok) throw new Error(response?.error || 'Extension không phản hồi');" +
        "return response.result;" +
        "})()";
}
