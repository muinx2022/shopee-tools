namespace Shopee.Workspace.Infrastructure;

/// <summary>
/// Gộp các yêu cầu làm mới dồn dập (vd mỗi SP lưu xong / mỗi lượt đẩy Hub) thành TỐI ĐA một lượt chạy tại một thời điểm,
/// mà KHÔNG mất yêu cầu cuối: yêu cầu đến trong lúc đang chạy được chạy bù ngay sau đó, kể cả khi nó đến đúng lúc lượt
/// trước vừa thoát vòng mà chưa nhả cờ. Lượt chạy lỗi → giữ cờ "cần làm mới" và tự thử lại sau <c>retryDelay</c>
/// (không lặp nóng).
/// </summary>
public sealed class CoalescingRefresher
{
    private readonly Func<Task> _work;
    private readonly TimeSpan _retryDelay;
    private int _busy;
    private int _dirty;

    /// <summary>Test: chạy đúng khe "vòng trong đã thấy hết việc nhưng chưa nhả cờ bận" — nơi yêu cầu dễ bị mất.</summary>
    internal Action? AfterDrainForTest { get; set; }

    public CoalescingRefresher(Func<Task> work, TimeSpan? retryDelay = null)
    {
        _work = work ?? throw new ArgumentNullException(nameof(work));
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(5);
    }

    /// <summary>Xin một lượt làm mới. Không ném. Task hoàn tất khi lượt do lời gọi này gánh (nếu có) chạy xong.</summary>
    public async Task RequestAsync()
    {
        Interlocked.Exchange(ref _dirty, 1);
        while (Interlocked.CompareExchange(ref _busy, 1, 0) == 0)
        {
            var failed = false;
            try
            {
                while (Interlocked.Exchange(ref _dirty, 0) == 1)
                {
                    try { await _work().ConfigureAwait(false); }
                    catch
                    {
                        Interlocked.Exchange(ref _dirty, 1);   // lượt này hỏng → vẫn còn nợ một lần làm mới
                        failed = true;
                        break;
                    }
                }
                AfterDrainForTest?.Invoke();
            }
            finally { Interlocked.Exchange(ref _busy, 0); }

            if (failed)
            {
                _ = RetryLaterAsync();
                return;
            }
            // Yêu cầu đến SAU khi vòng trong thấy dirty=0 nhưng TRƯỚC khi nhả busy đã bỏ về (thấy busy=1) → chạy bù ở đây.
            if (Volatile.Read(ref _dirty) == 0) return;
        }
    }

    private async Task RetryLaterAsync()
    {
        try { await Task.Delay(_retryDelay).ConfigureAwait(false); }
        catch { return; }
        await RequestAsync().ConfigureAwait(false);
    }
}
