using System.Text.Json;
using Shopee.Core.Harvest;

namespace OpenMultiBraveLauncherV3;

/// <summary>
/// GĐ7 — RPC của luồng "bấm nút Lấy dữ liệu" (extension <c>shopee-harvest</c>, xem <see cref="HarvestClickStep"/>):
/// <list type="bullet">
/// <item><c>prepareHarvestClick</c> — DÀI (mở link, chờ hộp; không có hộp thì extension tự chạy bước lấy tại chỗ, có thể chờ giải
/// captcha tay): đi đường tracked của bước harvest (<see cref="DeliverHarvestStepAsync"/>) — chỉ gửi lại khi CHẮC chưa tới
/// extension, không bao giờ gọi chồng.</item>
/// <item><c>getHarvestClickResult</c> / <c>panelButtonRect</c> — NGẮN, chỉ đọc: đường thường, thử lại ít.</item>
/// </list>
/// </summary>
internal static partial class RunnerExtensionRpc
{
    /// <summary>Trần chờ một lần hỏi kết quả / toạ độ nút.</summary>
    internal static readonly TimeSpan HarvestClickShortTimeout = TimeSpan.FromSeconds(15);

    public static async Task<HarvestClickPrepare> PrepareHarvestClickAsync(
        int cdpPort, DirectoryInfo profileRoot, string payloadJson, CancellationToken cancellationToken)
    {
        try
        {
            var lastReason = "";
            for (var attempt = 0; attempt < MaxHarvestDeliveryAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (attempt > 0)
                    await RunnerSwLifecycle.EnsureRunnerExtensionReadyAsync(cdpPort, profileRoot, null, cancellationToken)
                        .ConfigureAwait(false);

                var extensionId = await RunnerSwLifecycle.ResolveExtensionIdAsync(cdpPort, profileRoot, cancellationToken)
                    .ConfigureAwait(false);
                if (extensionId is null)
                {
                    lastReason = $"không tìm thấy extension {RunnerExtensionPaths.ExtensionDisplayName}";
                    continue;
                }

                var outcome = await DeliverHarvestStepAsync(cdpPort, extensionId, payloadJson, cancellationToken, "prepareHarvestClick")
                    .ConfigureAwait(false);
                switch (outcome.Kind)
                {
                    case HarvestDelivery.Answered:
                        return HarvestClickPrepare.Parse(outcome.Value);
                    case HarvestDelivery.SentNoAnswer:
                        return HarvestClickPrepare.NotReady(outcome.Reason);
                    default:
                        lastReason = outcome.Reason;
                        continue;
                }
            }
            return HarvestClickPrepare.NotReady($"No SW: lượt bấm không gửi được tới extension ({lastReason}).");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return HarvestClickPrepare.NotReady($"No SW: {ex.Message}");
        }
    }

    /// <summary>Trạng thái lượt bấm. Lỗi RPC → NÉM (người gọi đếm số lần lỗi liên tiếp).</summary>
    public static async Task<HarvestClickPoll> GetHarvestClickResultAsync(
        int cdpPort, DirectoryInfo profileRoot, string token, CancellationToken cancellationToken)
    {
        var value = await EvaluateShortAsync(cdpPort, profileRoot, "getHarvestClickResult",
            JsonSerializer.Serialize(new Dictionary<string, object?> { ["token"] = token }), cancellationToken).ConfigureAwait(false);
        if (value is not { ValueKind: JsonValueKind.Object } v) throw new InvalidOperationException("extension không trả trạng thái lượt bấm");
        var state = v.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "" : "";
        JsonElement? result = v.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.Object ? r.Clone() : null;
        return new HarvestClickPoll(state, result);
    }

    public static async Task<HarvestClickPrepare> PanelButtonRectAsync(
        int cdpPort, DirectoryInfo profileRoot, int tabId, CancellationToken cancellationToken)
    {
        try
        {
            var value = await EvaluateShortAsync(cdpPort, profileRoot, "panelButtonRect",
                JsonSerializer.Serialize(new Dictionary<string, object?> { ["tabId"] = tabId }), cancellationToken).ConfigureAwait(false);
            return value is { } v ? HarvestClickPrepare.Parse(v) : HarvestClickPrepare.NotReady("extension không trả toạ độ nút");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return HarvestClickPrepare.NotReady(HarvestStepPolicy.SafeForClassify(ex.Message));
        }
    }

    private static async Task<JsonElement?> EvaluateShortAsync(
        int cdpPort, DirectoryInfo profileRoot, string method, string payloadJson, CancellationToken cancellationToken)
    {
        var extensionId = await RunnerSwLifecycle.ResolveExtensionIdAsync(cdpPort, profileRoot, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"không tìm thấy extension {RunnerExtensionPaths.ExtensionDisplayName}");
        return await EvaluateExtensionMethodAsync(cdpPort, extensionId, method, payloadJson, cancellationToken,
            maxAttempts: 2, receiveTimeoutOverride: HarvestClickShortTimeout).ConfigureAwait(false);
    }
}
