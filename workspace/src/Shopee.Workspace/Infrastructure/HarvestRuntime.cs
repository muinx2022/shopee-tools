using Shopee.Core.Coordination;
using Shopee.Core.Harvest;

namespace Shopee.Workspace.Infrastructure;

/// <summary>
/// Kho lưu cục bộ + worker đẩy Hub của app Harvest, sống suốt process (một outbox <c>harvest-outbox.db</c> / một worker —
/// chặn 2 bản exe bằng <see cref="HarvestSingleInstance"/>). <see cref="Sink"/> là thứ đưa cho engine: lưu vào outbox rồi
/// đánh thức worker + báo UI làm mới bộ đếm. Chỉ app Harvest dựng (<see cref="Start"/>); vai trò khác <see cref="Current"/> = null.
/// </summary>
public sealed class HarvestRuntime
{
    /// <summary>Thời gian tối đa đẩy nốt khi đóng app — đóng app không được treo vì Hub chậm/sập (dữ liệu còn lại vẫn nằm
    /// bền trong outbox, lần mở sau đẩy tiếp).</summary>
    public static readonly TimeSpan ShutdownFlushTimeout = TimeSpan.FromSeconds(8);

    public static HarvestRuntime? Current { get; private set; }

    /// <summary>Chỉ cho test: gắn / gỡ runtime hiện hành (app thật gắn qua <see cref="Start"/>).</summary>
    internal static void SetCurrentForTest(HarvestRuntime? runtime) => Current = runtime;

    public HarvestOutbox Outbox { get; }
    public HarvestOutboxWorker Worker { get; }

    /// <summary>Sink đưa cho engine (<c>HarvestRunOptions.Sink</c>): outbox + Nudge sau mỗi lần lưu xong.</summary>
    public IHarvestSink Sink { get; }

    /// <summary>GĐ7 — sink của luồng "extension tự đẩy Hub": chỉ ghi <c>done</c> khi extension báo Hub đã nhận; outbox không nhận
    /// mục mới (worker vẫn đẩy nốt mục cũ).</summary>
    public IHarvestSink ExtensionSink { get; }

    /// <summary>GĐ7 — URL / token / mã máy Hub app đang dùng, truyền cho extension (null = chưa nối Hub).</summary>
    public Func<HarvestHubAccess?> HubAccess { get; }

    /// <summary>Số liệu có thể đã đổi (lưu mới / đẩy xong / lỗi / requeue) — UI gọi lại <see cref="GetStatsAsync"/>. Bắn từ
    /// luồng nền.</summary>
    public event Action? StatsChanged;

    /// <summary>Kênh log của worker/outbox (UI gán vào log tab). null → bỏ.</summary>
    public Action<string>? Log { get; set; }

    /// <summary>Hỏi Hub SP nào đã có dữ liệu còn hạn (<see cref="HubClient.GetHarvestedKnownAsync"/>). Chưa nối Hub → ném (caller
    /// coi là "không hỏi được", chỉ dựa vào tập đã xong cục bộ). Test thay được.</summary>
    public HarvestKnownQuery KnownQuery { get; internal set; }

    internal HarvestRuntime(string dbPath, Func<HubClient?> client, Func<HarvestHubAccess?>? hubAccess = null)
    {
        HubAccess = hubAccess ?? CoordinationRuntime.HarvestHubAccess;
        KnownQuery = async (ids, maxAgeDays, ct) =>
            await (client() ?? throw new InvalidOperationException("chưa nối Hub")).GetHarvestedKnownAsync(ids, maxAgeDays, ct)
                .ConfigureAwait(false);
        Outbox = new HarvestOutbox(dbPath, log: m => Log?.Invoke(m));
        Worker = new HarvestOutboxWorker(Outbox, client, log: m => Log?.Invoke(m));
        Worker.Changed += RaiseStatsChanged;
        Sink = new NudgingHarvestSink(Outbox, () =>
        {
            Worker.Nudge();
            RaiseStatsChanged();
        });
        ExtensionSink = new ExtensionPushedHarvestSink(Outbox, HubAccess, RaiseStatsChanged);
    }

    /// <summary>Dựng outbox tại đường dẫn mặc định + chạy worker nền. Gọi MỘT lần lúc khởi động app Harvest, sau khi
    /// điều phối Hub đã init (worker đọc <see cref="CoordinationRuntime.Client"/> mỗi lần gửi nên Hub nối sau vẫn được).</summary>
    public static HarvestRuntime Start()
    {
        var rt = new HarvestRuntime(HarvestOutbox.DefaultDbPath(), () => CoordinationRuntime.Client);
        rt.Worker.Start();
        Current = rt;
        return rt;
    }

    public Task<HarvestOutboxStats> GetStatsAsync() => Worker.GetStatsAsync();

    public Task<int> RequeueFailedAsync() => Worker.RequeueFailedAsync();

    /// <summary>Dòng "Hub: đã nhận P · chờ đẩy Q · lỗi R" cho bảng tổng kết cuối khối (engine tự giới hạn thời gian chờ).</summary>
    public async Task<string?> HubStatusLineAsync(CancellationToken cancellationToken)
    {
        var st = await GetStatsAsync().ConfigureAwait(false);
        return HarvestOverlayText.HubLine(st.HubReceivedTotal, st.Pending, st.Failed);   // GĐ7: gồm cả SP extension tự đẩy
    }

    /// <summary>Đóng app: dừng vòng nền rồi đẩy nốt trong <paramref name="flushTimeout"/>. Trả true nếu hàng chờ đã rỗng.</summary>
    public async Task<bool> ShutdownAsync(TimeSpan flushTimeout)
    {
        await Worker.StopAsync().ConfigureAwait(false);
        return await Worker.FlushAsync(flushTimeout).ConfigureAwait(false);
    }

    private void RaiseStatsChanged()
    {
        try { StatsChanged?.Invoke(); } catch { }
    }
}
