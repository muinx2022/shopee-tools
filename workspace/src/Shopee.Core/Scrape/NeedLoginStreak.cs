namespace Shopee.Core.Scrape;

/// <summary>
/// Chuỗi "BigSeller báo login-first" LIÊN TIẾP của một job Scrape (Lead). Luật thuần, thread-safe (Interlocked):
/// CHỈ một dòng thật sự cào xong mới reset chuỗi (chunk lỗi proxy / không cào được gì KHÔNG tính là bằng chứng
/// phiên còn sống); đủ <see cref="Threshold"/> lần login-first liên tiếp → dừng job.
/// </summary>
public sealed class NeedLoginStreak(int threshold)
{
    private int _count;

    /// <summary>Ngưỡng login-first liên tiếp để dừng job.</summary>
    public int Threshold { get; } = threshold;

    /// <summary>Số lần login-first liên tiếp hiện tại.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>Một dòng cào xong thật → phiên BigSeller còn sống → reset chuỗi.</summary>
    public void RecordRowCompleted() => Interlocked.Exchange(ref _count, 0);

    /// <summary>Ghi nhận 1 lần login-first. <paramref name="streak"/> = số lần liên tiếp sau lần này;
    /// trả true khi đạt ngưỡng (phải dừng job).</summary>
    public bool RecordNeedLogin(out int streak)
    {
        streak = Interlocked.Increment(ref _count);
        return streak >= Threshold;
    }
}
