using System.Text.Json;

namespace Shopee.Core.Harvest;

/// <summary>
/// GĐ7 — extension <c>shopee-harvest</c> TỰ đẩy SP lên Hub (nút "Lấy dữ liệu" trên hộp nổi, app bấm qua CDP): kết quả gửi
/// Hub đính kèm kết quả bước (<c>result.hub</c>, xem <c>extensions/shopee-harvest/click-flow.js</c> HUB_STATUS). null = bước
/// không đi đường extension tự đẩy (bước cũ / không lấy được JSON).
/// </summary>
/// <param name="Status"><c>saved</c> | <c>queued</c> | <c>rejected</c> | <c>invalid</c> | <c>failed</c> (chữ khác → coi như failed).</param>
/// <param name="Reason">Lý do (Hub / extension báo), có thể null.</param>
/// <param name="Applied">Hub thật sự ghi bản mới (false = trùng hash / bản cũ hơn — vẫn là đã lưu).</param>
public sealed record HarvestHubPush(string Status, string? Reason, bool Applied)
{
    public const string Saved = "saved";
    public const string Queued = "queued";
    public const string Rejected = "rejected";
    public const string Invalid = "invalid";
    public const string Failed = "failed";

    /// <summary>Hub ĐÃ nhận SP (ok=true) → app được ghi <c>done</c>.</summary>
    public bool IsSaved => Status == Saved;

    /// <summary>Đọc <c>hub</c> trong object kết quả bước; vắng / không phải object → null.</summary>
    public static HarvestHubPush? Parse(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("hub", out var hub) || hub.ValueKind != JsonValueKind.Object)
            return null;
        var status = hub.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "" : "";
        var reason = hub.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
        var applied = hub.TryGetProperty("applied", out var a) && a.ValueKind == JsonValueKind.True;
        return new HarvestHubPush(status is Saved or Queued or Rejected or Invalid ? status : Failed, reason, applied);
    }
}

/// <summary>GĐ7 — app bấm nút "Lấy dữ liệu" mà extension không nhận (trượt / bị che / trang đang phóng to) nhiều dòng LIÊN TIẾP:
/// lỗi của MÁY / cửa sổ (zoom, phần tử che), không phải của tài khoản hay SP → engine dừng CẢ job (StopJob, không bỏ dòng, không
/// cho tài khoản nghỉ). Thông điệp mang <see cref="Prefix"/> để <c>ScrapeFailurePolicy</c> nhận ra; KHÔNG chứa chữ mà
/// <c>ScrapeRunner.Classify</c> đọc ra cách ly / lỗi mạng.</summary>
public static class HarvestPanelClickFailure
{
    public const string Prefix = "Bấm nút hộp nổi lỗi: ";

    /// <summary>Số dòng liên tiếp bấm trượt thì dừng job.</summary>
    public const int BreakerThreshold = 3;

    public static string Message(int rowNumber, int streak, string? lastReason) =>
        $"{Prefix}{streak} dòng liên tiếp (tới dòng {rowNumber}) app bấm nút \"Lấy dữ liệu\" mà extension không nhận — đặt zoom của " +
        "tab Shopee về 100% (Ctrl+0), không để cửa sổ / phần tử nào che góc dưới phải trang, rồi chạy lại (không dòng nào bị bỏ). " +
        $"Lần cuối: {HarvestStepPolicy.SafeForClassify(lastReason)}";

    public static bool Is(string? message) =>
        message is not null && message.StartsWith(Prefix, StringComparison.Ordinal);
}

/// <summary>Cấu hình Hub app truyền cho extension ở chế độ tự động (URL gốc, token <c>X-Api-Token</c>, mã máy
/// <c>X-Machine-Id</c>) — cùng bộ app đang dùng (<c>CoordinationRuntime.HarvestHubAccess</c>). KHÔNG ghi ra log / file.</summary>
public sealed record HarvestHubAccess(string BaseUrl, string ApiToken, string MachineId)
{
    /// <summary>Không in token khi bị ghi log nhầm.</summary>
    public override string ToString() => $"HarvestHubAccess {{ BaseUrl = {BaseUrl}, MachineId = {MachineId} }}";
}

/// <summary>
/// Sink của chế độ "extension tự đẩy Hub" (GĐ7): engine chỉ gọi <see cref="SaveAsync"/> khi extension đã báo Hub NHẬN SP
/// (<see cref="HarvestHubPush.IsSaved"/>) → ghi khoá SP vào tập <c>done</c> + link mẫu (<see cref="HarvestOutbox.RecordPushedByExtensionAsync"/>),
/// KHÔNG đưa JSON vào outbox (outbox không nhận mục mới; worker vẫn đẩy nốt mục cũ). <see cref="EnsureReadyAsync"/> chặn từ đầu job
/// khi outbox (tập done) không ghi được hoặc app chưa nối Hub (extension không có cấu hình để gửi).
/// </summary>
public sealed class ExtensionPushedHarvestSink : IHarvestSink
{
    private readonly HarvestOutbox _outbox;
    private readonly Func<HarvestHubAccess?> _hubAccess;
    private readonly Action? _onSaved;

    public ExtensionPushedHarvestSink(HarvestOutbox outbox, Func<HarvestHubAccess?> hubAccess, Action? onSaved = null)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _hubAccess = hubAccess ?? throw new ArgumentNullException(nameof(hubAccess));
        _onSaved = onSaved;
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        await _outbox.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        if (_hubAccess() is null)
            throw new InvalidOperationException("app chưa nối Hub (Cài đặt → Hub) — extension cần URL / token Hub để tự đẩy dữ liệu");
    }

    public async Task SaveAsync(HarvestedItem item, CancellationToken cancellationToken)
    {
        await _outbox.RecordPushedByExtensionAsync(item, cancellationToken).ConfigureAwait(false);
        try { _onSaved?.Invoke(); }
        catch { /* đã ghi bền — báo hiệu hỏng không được biến thành "lưu lỗi" */ }
    }
}
