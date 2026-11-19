using System.Text.Json;
using Shopee.Core.Harvest;

namespace OpenMultiBraveLauncherV3;

/// <summary>
/// GĐ9 — RPC lượt ảnh gốc của extension <c>shopee-harvest</c> (xem <see cref="SourceImageRoundDriver"/>): <c>startSourceImages</c>
/// (trả NGAY, lượt chạy nền trong SW) và <c>sourceImagesStatus</c> (chỉ đọc). Cả hai NGẮN — đường thường như
/// <c>getHarvestClickResult</c>. Extension cũ không có lệnh → <see cref="SourceImageStart.Unsupported"/>, không ném.
/// </summary>
internal static partial class RunnerExtensionRpc
{
    /// <summary>Payload <c>startSourceImages</c>. Có token Hub — KHÔNG ghi payload ra log.</summary>
    internal static string BuildStartSourceImagesPayload(HarvestHubAccess hub, int budgetMs, bool clearHubFull) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["budgetMs"] = budgetMs,
            ["clearHubFull"] = clearHubFull,
            ["hub"] = new Dictionary<string, object?>
            {
                ["url"] = hub.BaseUrl,
                ["token"] = hub.ApiToken,
                ["machineId"] = hub.MachineId,
            },
        });

    /// <summary>Lỗi RPC thật (mất SW, CDP đứt) → null; huỷ của job → ném.</summary>
    public static async Task<SourceImageStart?> StartSourceImagesAsync(int cdpPort, DirectoryInfo profileRoot, HarvestHubAccess hub,
        int budgetMs, bool clearHubFull, CancellationToken cancellationToken)
    {
        try
        {
            var value = await EvaluateShortAsync(cdpPort, profileRoot, "startSourceImages",
                BuildStartSourceImagesPayload(hub, budgetMs, clearHubFull), cancellationToken).ConfigureAwait(false);
            return value is { } v ? SourceImageStart.Parse(v) : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (IsUnsupportedMethodError(ex.Message))
        {
            return new SourceImageStart(false, null, "extension chưa hỗ trợ đẩy ảnh gốc", Unsupported: true);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Trạng thái lượt ảnh; lỗi RPC → null; huỷ của job → ném.</summary>
    public static async Task<SourceImageStatus?> GetSourceImagesStatusAsync(int cdpPort, DirectoryInfo profileRoot,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await EvaluateShortAsync(cdpPort, profileRoot, "sourceImagesStatus", "{}", cancellationToken).ConfigureAwait(false);
            return value is { } v ? SourceImageStatus.Parse(v) : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            return null;
        }
    }

    /// <summary>Extension cũ (đường popup) trả "Extension chưa hỗ trợ startSourceImages." cho lệnh nó không có.</summary>
    internal static bool IsUnsupportedMethodError(string? message) =>
        message is not null && message.Contains("chưa hỗ trợ", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Điều một lượt ảnh gốc từ phía app (thuần, test được bằng delegate giả). Đã biết Hub hết chỗ
/// (<see cref="HarvestSourceImageProgress.HubFullCode"/>) → không gọi gì. Còn lại: start (ngân sách = thời gian còn tới
/// <c>deadline</c>) → hỏi trạng thái mỗi <see cref="PollInterval"/>, ghi dần vào <see cref="HarvestSourceImageProgress"/> (cộng phần chênh)
/// tới khi lượt xong. MỌI lời gọi bị cắt ở <c>deadline</c> + grace (mặc định <see cref="Grace"/>) kể cả khi RPC treo / bỏ qua token —
/// vòng chính không bao giờ chờ quá mốc đó. <c>deadline</c> tính từ ĐẦU khoảng nghỉ (người gọi), không từ lúc start trả lời.
/// KHÔNG ném trừ huỷ của job.
/// </summary>
internal static class SourceImageRoundDriver
{
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);
    /// <summary>Còn ít hơn chừng này tới hạn chót thì không bắt đầu lượt (extension cũng từ chối ngân sách &lt; 5 s).</summary>
    internal const int MinStartMs = 5_000;
    /// <summary>Trần một lần hỏi riêng số ảnh còn thiếu (không chạy lượt).</summary>
    internal static readonly TimeSpan MissingTimeout = TimeSpan.FromSeconds(15);

    internal static async Task<SourceImageRound?> RunAsync(
        Func<bool, int, CancellationToken, Task<SourceImageStart?>> start,
        Func<CancellationToken, Task<SourceImageStatus?>> status,
        Func<TimeSpan, CancellationToken, Task> delay,
        Func<DateTimeOffset> now,
        DateTimeOffset deadline,
        HarvestSourceImageProgress? progress,
        string instanceKey,
        CancellationToken cancellationToken,
        TimeSpan? grace = null,
        int minStartMs = MinStartMs)
    {
        if (progress?.HubFullCode is not null) return null;   // Hub hết chỗ: thôi gọi tới khi bấm Chạy lại
        var hardStop = deadline + (grace ?? Grace);
        var budgetMs = (long)(deadline - now()).TotalMilliseconds;
        if (budgetMs < minStartMs) return null;
        SourceImageRound? last = null;
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(hardStop - now());
            var clearGeneration = progress?.HubFullClearPending(instanceKey);
            var started = await start(clearGeneration is not null, (int)Math.Min(budgetMs, int.MaxValue), limit.Token)
                .WaitAsync(limit.Token).ConfigureAwait(false);
            // Thẻ "bỏ mốc Hub hết chỗ" chỉ coi là đã dùng khi extension đã nhận (bắt đầu lượt / báo đã bỏ mốc); RPC hỏng → gửi lại lần sau.
            if (clearGeneration is { } g && started is { } s0 && (s0.Started || s0.HubFullCleared))
                progress!.MarkHubFullCleared(instanceKey, g);
            if (started is null || started.Unsupported) return null;
            if (started.RoundId is null)
            {
                if (started.Reason is { } reason) progress?.NoteError($"Ảnh gốc: {reason}");
                return null;
            }

            while (true)
            {
                await delay(PollInterval, limit.Token).WaitAsync(limit.Token).ConfigureAwait(false);
                var st = await status(limit.Token).WaitAsync(limit.Token).ConfigureAwait(false);
                if (st?.Round is { } round && round.RoundId == started.RoundId)
                {
                    last = round;
                    progress?.Apply(instanceKey, round);   // ghi dần: UI thấy số giữa lượt; cùng lượt chỉ cộng phần chênh
                }
                if (st is { Running: false } || now() >= hardStop) break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            // Quá mốc / RPC hỏng: thôi chờ, giữ số đã thấy.
        }
        return last;
    }

    /// <summary>Hỏi riêng số ảnh còn thiếu của profile (không chạy lượt) → ghi vào <paramref name="progress"/>. null = không hỏi được.
    /// KHÔNG ném trừ huỷ của job.</summary>
    internal static async Task<int?> RefreshMissingAsync(
        Func<CancellationToken, Task<SourceImageStatus?>> status,
        HarvestSourceImageProgress? progress,
        string instanceKey,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(timeout ?? MissingTimeout);
            var st = await status(limit.Token).WaitAsync(limit.Token).ConfigureAwait(false);
            if (st is null) return null;
            progress?.SetMissing(instanceKey, st.QueueIds);
            return st.QueueIds;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            return null;
        }
    }
}
