namespace Shopee.Core.Harvest;

/// <summary>
/// Lớp bọc mỏng quanh sink thật (outbox): lưu xong thì gọi <c>onSaved</c> — app Harvest dùng để đánh thức worker đẩy Hub
/// (<see cref="HarvestOutboxWorker.Nudge"/>) và làm mới bộ đếm trên UI. KHÔNG đổi hợp đồng <see cref="IHarvestSink"/>: ném
/// của sink thật đi thẳng ra engine (chưa lưu → không báo); lỗi của <c>onSaved</c> bị nuốt, vì dữ liệu ĐÃ lưu bền — để nó
/// ném thì engine tưởng lưu hỏng mà dừng job oan.
/// </summary>
public sealed class NudgingHarvestSink : IHarvestSink
{
    private readonly IHarvestSink _inner;
    private readonly Action _onSaved;

    public NudgingHarvestSink(IHarvestSink inner, Action onSaved)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _onSaved = onSaved ?? throw new ArgumentNullException(nameof(onSaved));
    }

    public Task EnsureReadyAsync(CancellationToken cancellationToken) => _inner.EnsureReadyAsync(cancellationToken);

    public async Task SaveAsync(HarvestedItem item, CancellationToken cancellationToken)
    {
        await _inner.SaveAsync(item, cancellationToken).ConfigureAwait(false);
        try { _onSaved(); }
        catch { /* đã lưu bền — báo hiệu hỏng không được biến thành "lưu lỗi" */ }
    }
}
