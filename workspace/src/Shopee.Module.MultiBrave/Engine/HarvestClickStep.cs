using System.Text.Json;
using Shopee.Core.Cdp;
using Shopee.Core.Harvest;

namespace OpenMultiBraveLauncherV3;

/// <summary>Lời đáp <c>prepareHarvestClick</c> / <c>panelButtonRect</c> của extension: hộp nổi sẵn sàng + toạ độ nút "Lấy dữ
/// liệu" (CSS px theo viewport), hoặc <see cref="Result"/> = kết quả bước khi KHÔNG có hộp để bấm (extension đã tự chạy bước
/// lấy tại chỗ: trang không phải SP / đăng nhập / xác minh…).
/// <para><see cref="Zoom"/> = zoom trình duyệt của tab (<c>chrome.tabs.getZoom</c>, 1 = 100%), <see cref="PageScale"/> = thu phóng
/// trong trang (<c>visualViewport.scale</c>), <see cref="DevicePixelRatio"/> chỉ để ghi nhận — null = extension không báo.</para></summary>
internal sealed record HarvestClickPrepare(
    bool Ready, int? TabId, double X, double Y, double Width, double Height, double ViewportWidth, double ViewportHeight,
    string? PageUrl, string? ItemId, JsonElement? Result, string? Reason,
    double? Zoom = null, double? PageScale = null, double? DevicePixelRatio = null)
{
    public static HarvestClickPrepare NotReady(string reason) => new(false, null, 0, 0, 0, 0, 0, 0, null, null, null, reason);

    public static HarvestClickPrepare Parse(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Object) return NotReady("Extension trả lời không phải object.");
        static double Num(JsonElement o, string obj, string name) =>
            o.TryGetProperty(obj, out var x) && x.ValueKind == JsonValueKind.Object && x.TryGetProperty(name, out var n)
            && n.ValueKind == JsonValueKind.Number ? n.GetDouble() : 0;
        static string? Str(JsonElement o, string name) =>
            o.TryGetProperty(name, out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        static double? Opt(JsonElement o, string name) =>
            o.TryGetProperty(name, out var n) && n.ValueKind == JsonValueKind.Number ? n.GetDouble() : null;
        return new HarvestClickPrepare(
            v.TryGetProperty("ready", out var r) && r.ValueKind == JsonValueKind.True,
            v.TryGetProperty("tabId", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out var tab) ? tab : null,
            Num(v, "rect", "x"), Num(v, "rect", "y"), Num(v, "rect", "width"), Num(v, "rect", "height"),
            Num(v, "viewport", "width"), Num(v, "viewport", "height"),
            Str(v, "pageUrl"), Str(v, "itemId"),
            v.TryGetProperty("result", out var res) && res.ValueKind == JsonValueKind.Object ? res.Clone() : null,
            Str(v, "reason"),
            Opt(v, "zoom"), Opt(v, "pageScale"), Opt(v, "devicePixelRatio"));
    }
}

/// <summary>Trạng thái lượt bấm (<c>getHarvestClickResult</c>): <c>pending</c> (extension chưa nhận cú bấm) | <c>running</c> |
/// <c>done</c> (kèm <see cref="Result"/>) | <c>unknown</c> (service worker đã khởi động lại / mã lượt lạ).</summary>
internal readonly record struct HarvestClickPoll(string State, JsonElement? Result);

/// <summary>Cửa ra extension / CDP của <see cref="HarvestClickStep"/> (thật = <see cref="CdpHarvestClickIo"/>, test dùng giả).</summary>
internal interface IHarvestClickIo
{
    DateTimeOffset Now { get; }
    /// <summary>Mở link + gắn ngữ cảnh + chờ hộp. Không tới được extension → <see cref="HarvestClickPrepare.NotReady"/> (lý do có
    /// "No SW" để phiên mở lại profile).</summary>
    Task<HarvestClickPrepare> PrepareAsync(string payloadJson, CancellationToken cancellationToken);
    /// <summary>Bấm chuột THẬT (Input.dispatchMouseEvent qua CDP) tại (x, y) CSS px của tab trang SP.</summary>
    Task ClickAsync(string? pageUrl, double x, double y, CancellationToken cancellationToken);
    Task<HarvestClickPoll> PollAsync(string token, CancellationToken cancellationToken);
    Task<HarvestClickPrepare> ButtonRectAsync(int tabId, string token, CancellationToken cancellationToken);
    Task DelayAsync(int ms, CancellationToken cancellationToken);
}

/// <summary>Một dòng ở chế độ "extension tự đẩy Hub": link + nguồn (để extension gửi Hub kèm account / sheet / dòng) + cấu
/// hình Hub của app.</summary>
internal sealed record HarvestClickRequest(
    string Link, int RowNumber, string StatusText, string InstanceName, int? TabId, IReadOnlyList<long>? BlockErrorCodes,
    string AccountId, string Sheet, string? SourceLink, string? RunId, HarvestHubAccess Hub);

/// <summary>
/// GĐ7 — app điều khiển extension kiểu BigSeller: (1) <c>prepareHarvestClick</c>: extension mở link, nhận ngữ cảnh, chờ hộp nổi
/// sẵn sàng, trả toạ độ nút (đã cuộn vào tầm nhìn); (2) app BẤM CHUỘT THẬT qua CDP vào giữa nút (event <c>isTrusted</c> — hộp bỏ
/// qua cú bấm không thật); (3) hỏi <c>getHarvestClickResult</c> tới khi xong. Cú bấm không tới (sau
/// <see cref="ClickAckTimeout"/> extension vẫn "pending") → hỏi lại toạ độ và bấm lại, tối đa <see cref="MaxClicks"/> lần.
/// Kết quả là <see cref="HarvestStepResult"/> như bước cũ (+ <see cref="HarvestStepResult.HubPush"/>) — mọi luật GĐ5–GĐ6 của
/// <see cref="HarvestRowProcessor"/> giữ nguyên. KHÔNG ném trừ khi huỷ.
/// </summary>
internal static class HarvestClickStep
{
    /// <summary>Bấm rồi mà extension vẫn chưa nhận sau chừng này → coi cú bấm trượt.</summary>
    internal static readonly TimeSpan ClickAckTimeout = TimeSpan.FromSeconds(8);
    internal const int MaxClicks = 2;
    /// <summary>Trần chờ kết quả sau cú bấm (khớp trần 600 s của bước harvest phía C#; extension tự dừng ở 540 s).</summary>
    internal static readonly TimeSpan ResultTimeout = TimeSpan.FromSeconds(600);
    internal const int PollIntervalMs = 1000;
    /// <summary>Số lần hỏi kết quả lỗi LIÊN TIẾP thì coi extension mất kết nối (No SW → mở lại profile).</summary>
    internal const int MaxPollErrors = 5;

    /// <summary>Payload <c>prepareHarvestClick</c>. Có token Hub — KHÔNG ghi payload ra log.</summary>
    internal static string BuildPayload(HarvestClickRequest r, string token)
    {
        var payload = new Dictionary<string, object?>
        {
            ["token"] = token,
            ["link"] = r.Link,
            ["rowNumber"] = r.RowNumber,
            ["statusText"] = r.StatusText,
            ["instanceName"] = r.InstanceName,
            ["tabId"] = r.TabId,
            ["context"] = new Dictionary<string, object?>
            {
                ["accountId"] = r.AccountId,
                ["sheet"] = r.Sheet,
                ["rowNo"] = r.RowNumber,
                ["url"] = r.Link,
                ["sourceUrl"] = r.SourceLink,
                ["runId"] = r.RunId,
                ["hub"] = new Dictionary<string, object?>
                {
                    ["url"] = r.Hub.BaseUrl,
                    ["token"] = r.Hub.ApiToken,
                    ["machineId"] = r.Hub.MachineId,
                },
            },
        };
        if (r.BlockErrorCodes is { Count: > 0 }) payload["blockErrorCodes"] = r.BlockErrorCodes;
        return JsonSerializer.Serialize(payload);
    }

    /// <summary>Lệch tối đa cho phép so với 100% của zoom tab / thu phóng trong trang.</summary>
    internal const double ZoomTolerance = 0.01;

    /// <summary>Trang đang phóng to / thu nhỏ → toạ độ nút (CSS px) và toạ độ chuột CDP có thể lệch nhau theo hệ số không suy ra
    /// chắc chắn được → KHÔNG bấm, báo rõ. null = 100%.</summary>
    internal static string? ZoomProblem(HarvestClickPrepare p)
    {
        if (p.Zoom is { } z && Math.Abs(z - 1) > ZoomTolerance)
            return $"tab Shopee đang zoom {z * 100:0}% — app không quy đổi chắc được toạ độ nút, đặt zoom 100% (Ctrl+0)";
        if (p.PageScale is { } s && Math.Abs(s - 1) > ZoomTolerance)
            return $"trang đang thu phóng {s * 100:0}% (chụm / Ctrl+cuộn) — đưa về 100% rồi chạy lại";
        return null;
    }

    /// <summary>Kết quả "bấm trượt" (đếm vào cầu dao <see cref="HarvestPanelClickFailure"/> của bộ xử lý dòng).</summary>
    private static HarvestStepResult Missed(string message, int? tabId, string link) =>
        HarvestStepResult.Failure(message, tabId, link) with { ClickMissed = true };

    /// <summary>Điểm bấm: ngẫu nhiên trong 40% giữa nút (không bấm đúng tâm mọi lần). null = nút không nằm trọn trong viewport /
    /// kích thước 0 (bấm vào đó là bấm trượt ra trang).</summary>
    internal static (double X, double Y)? ClickPoint(HarvestClickPrepare p, Random rng)
    {
        if (p.Width <= 0 || p.Height <= 0) return null;
        if (p.X < 0 || p.Y < 0) return null;
        if (p.ViewportWidth > 0 && p.X + p.Width > p.ViewportWidth + 0.5) return null;
        if (p.ViewportHeight > 0 && p.Y + p.Height > p.ViewportHeight + 0.5) return null;
        var fx = 0.3 + rng.NextDouble() * 0.4;
        var fy = 0.3 + rng.NextDouble() * 0.4;
        return (p.X + p.Width * fx, p.Y + p.Height * fy);
    }

    public static async Task<HarvestStepResult> RunAsync(
        IHarvestClickIo io, HarvestClickRequest request, Random rng, CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid().ToString("N");
        var prep = await io.PrepareAsync(BuildPayload(request, token), cancellationToken).ConfigureAwait(false);
        if (!prep.Ready)
        {
            return prep.Result is { } r
                ? HarvestStepResult.Parse(r, prep.TabId ?? request.TabId, request.Link)
                : HarvestStepResult.Failure(prep.Reason ?? "Extension không chuẩn bị được lượt bấm.", prep.TabId ?? request.TabId, request.Link);
        }
        var tabId = prep.TabId ?? request.TabId;

        var clicks = 0;
        var clickedAt = DateTimeOffset.MinValue;
        var pollErrors = 0;
        string? lastPollError = null;
        var target = prep;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (clicks == 0 || (io.Now - clickedAt >= ClickAckTimeout && clicks < MaxClicks && await StillPendingAsync()))
            {
                if (clicks > 0)
                {
                    target = await io.ButtonRectAsync(tabId ?? 0, token, cancellationToken).ConfigureAwait(false);
                    if (!target.Ready)
                        return Missed(
                            $"Đã bấm \"Lấy dữ liệu\" mà extension không nhận, hộp nổi cũng không còn sẵn sàng ({target.Reason}) — lượt sau lấy lại dòng {request.RowNumber}.",
                            tabId, request.Link);
                }
                if (ZoomProblem(target) is { } zoom)
                    return Missed($"Không bấm nút \"Lấy dữ liệu\": {zoom} — dòng {request.RowNumber}.", tabId, request.Link);
                if (ClickPoint(target, rng) is not { } point)
                    return Missed(
                        $"Nút \"Lấy dữ liệu\" nằm ngoài khung nhìn ({target.X:0},{target.Y:0} {target.Width:0}×{target.Height:0} / {target.ViewportWidth:0}×{target.ViewportHeight:0}) — dòng {request.RowNumber}.",
                        tabId, request.Link);
                try
                {
                    await io.ClickAsync(target.PageUrl ?? prep.PageUrl, point.X, point.Y, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    // CDP tới tab trang hỏng: lỗi dòng tạm (bộ xử lý dòng tự hỏi CDP còn sống không → mở lại profile nếu đứt).
                    return Missed(
                        $"Không bấm được nút \"Lấy dữ liệu\" ({HarvestStepPolicy.SafeForClassify(ex.Message)}) — dòng {request.RowNumber}, lượt sau lấy lại.",
                        tabId, request.Link);
                }
                clicks++;
                clickedAt = io.Now;
            }

            HarvestClickPoll poll;
            try
            {
                poll = await io.PollAsync(token, cancellationToken).ConfigureAwait(false);
                pollErrors = 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                lastPollError = ex.Message;
                if (++pollErrors >= MaxPollErrors)
                    return HarvestStepResult.Failure($"No SW: không đọc được kết quả lượt bấm từ extension ({lastPollError}).", tabId, request.Link);
                await io.DelayAsync(PollIntervalMs, cancellationToken).ConfigureAwait(false);
                continue;
            }

            switch (poll.State)
            {
                case "done" when poll.Result is { } result:
                    return HarvestStepResult.Parse(result, tabId, request.Link);
                case "unknown":
                    return HarvestStepResult.Failure(
                        $"Extension không còn giữ lượt bấm (service worker khởi động lại giữa chừng?) — lượt sau lấy lại dòng {request.RowNumber}.",
                        tabId, request.Link);
                case "pending" when clicks >= MaxClicks && io.Now - clickedAt >= ClickAckTimeout:
                    return Missed(
                        $"Bấm nút \"Lấy dữ liệu\" {clicks} lần mà extension không nhận — dòng {request.RowNumber}, lượt sau lấy lại.",
                        tabId, request.Link);
            }
            if (io.Now - clickedAt >= ResultTimeout)
                return HarvestStepResult.Failure(
                    $"Quá {(int)ResultTimeout.TotalSeconds}s chưa có kết quả lượt bấm (dòng {request.RowNumber}) — lượt sau lấy lại.",
                    tabId, request.Link);
            await io.DelayAsync(PollIntervalMs, cancellationToken).ConfigureAwait(false);
        }

        // Trạng thái mới nhất có còn "pending" không (chỉ gọi khi đã quá hạn chờ nhận cú bấm).
        async Task<bool> StillPendingAsync()
        {
            try { return (await io.PollAsync(token, cancellationToken).ConfigureAwait(false)).State == "pending"; }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { return false; }
        }
    }
}

/// <summary>IO thật: RPC vào service worker của extension + chuột thật qua CDP trên tab trang SP.</summary>
internal sealed class CdpHarvestClickIo(int cdpPort, DirectoryInfo profileRoot) : IHarvestClickIo
{
    public DateTimeOffset Now => DateTimeOffset.Now;

    public Task<HarvestClickPrepare> PrepareAsync(string payloadJson, CancellationToken cancellationToken) =>
        RunnerExtensionRpc.PrepareHarvestClickAsync(cdpPort, profileRoot, payloadJson, cancellationToken);

    public async Task ClickAsync(string? pageUrl, double x, double y, CancellationToken cancellationToken)
    {
        var targetId = await PageCdpHelper.FindWorkPageTargetIdAsync(cdpPort, pageUrl ?? "", cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("không thấy tab trang SP trên CDP");
        var hub = PortCdpHub.For(cdpPort);
        string? sess = null;
        try
        {
            sess = await hub.AttachAsync(targetId, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(sess)) throw new InvalidOperationException("không attach được tab trang SP");
            var session = sess;
            var sender = new CdpSender(
                (method, @params, ct) => hub.SendAsync(method, @params, session, ct),
                (method, @params, ct) => hub.SendNoReplyAsync(method, @params, session, ct));
            await new CdpHumanInput(HumanInputProfile.HarvestPanel).ClickAsync(sender, x, y, 1, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(sess))
                try { await hub.SendAsync("Target.detachFromTarget", new { sessionId = sess }, null, CancellationToken.None).ConfigureAwait(false); } catch { }
        }
    }

    public Task<HarvestClickPoll> PollAsync(string token, CancellationToken cancellationToken) =>
        RunnerExtensionRpc.GetHarvestClickResultAsync(cdpPort, profileRoot, token, cancellationToken);

    public Task<HarvestClickPrepare> ButtonRectAsync(int tabId, string token, CancellationToken cancellationToken) =>
        RunnerExtensionRpc.PanelButtonRectAsync(cdpPort, profileRoot, tabId, cancellationToken);

    public Task DelayAsync(int ms, CancellationToken cancellationToken) => Task.Delay(ms, cancellationToken);
}
