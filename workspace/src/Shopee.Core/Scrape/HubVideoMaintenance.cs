using Shopee.Core.Coordination;

namespace Shopee.Core.Scrape;

/// <summary>
/// Quét xóa video trên hub. Phần test được nhận sẵn manifest + delegate xóa — không tự mở mạng.
/// <see cref="RunExpiredSweepAsync"/> / <see cref="RunDeleteAllAsync"/> là lớp gọi
/// <see cref="CoordinationRuntime.Client"/> lúc app chạy.
/// </summary>
public static class HubVideoMaintenance
{
    /// <summary>Một khóa cho quét hạn, xóa sạch lúc bấm scrape, và PUT video.
    /// Manifest phải đọc SAU khi đã giữ khóa: đọc trước rồi mới chờ thì PUT kịp đổi mtime,
    /// lượt quét xóa đúng file vừa đẩy.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<int> SweepExpiredAsync(
        IReadOnlyList<FileManifestEntry> entries,
        DateTimeOffset now,
        Func<string, CancellationToken, Task<bool>> delete,
        CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            return await DeleteListedAsync(HubVideoRetention.ExpiredNames(entries, now), delete, ct);
        }
        finally { Gate.Release(); }
    }

    public static async Task<int> DeleteAllVideosAsync(
        IReadOnlyList<FileManifestEntry> entries,
        Func<string, CancellationToken, Task<bool>> delete,
        CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            return await DeleteListedAsync(HubVideoRetention.AllVideoNames(entries), delete, ct);
        }
        finally { Gate.Release(); }
    }

    /// <summary>Giữ khóa suốt <paramref name="action"/> (PUT video). Chờ khóa dùng đúng token của caller —
    /// không gắn timeout HTTP. Hủy thì ném tiếp, khóa được nhả.</summary>
    public static async Task RunExclusiveAsync(Func<CancellationToken, Task> action, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            await action(ct);
        }
        finally { Gate.Release(); }
    }

    /// <summary>Client null → 0, không log. Lỗi manifest → một dòng, không ném. Hủy thì ném tiếp.
    /// Không gọi <see cref="SweepExpiredAsync"/> khi đang giữ khóa: SemaphoreSlim không reentrant.</summary>
    public static async Task<int> RunExpiredSweepAsync(Action<string> log, CancellationToken ct)
    {
        var client = CoordinationRuntime.Client;
        if (client is null) return 0;

        await Gate.WaitAsync(ct);
        try
        {
            List<FileManifestEntry> entries;
            try { entries = await client.ManifestAsync(ct); }
            // Timeout của HttpClient cũng là OperationCanceledException nhưng token không hủy — đó là lỗi mạng,
            // phải log rồi thôi. Chỉ hủy chủ động của caller mới ném tiếp.
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch
            {
                log("Không quét được video cũ trên hub");
                return 0;
            }

            var now = DateTimeOffset.UtcNow;
            var n = await DeleteListedAsync(
                HubVideoRetention.ExpiredNames(entries, now),
                (name, token) => client.DeleteFileAsync(name, token),
                ct);
            if (n > 0) log($"Đã xóa {n} video trên hub cũ hơn 30 ngày");
            return n;
        }
        finally { Gate.Release(); }
    }

    /// <summary>Cùng đường với <see cref="RunExpiredSweepAsync"/>, nhưng log cả khi xóa 0 file (người vừa bấm Có).</summary>
    public static async Task<int> RunDeleteAllAsync(Action<string> log, CancellationToken ct)
    {
        var client = CoordinationRuntime.Client;
        if (client is null) return 0;

        await Gate.WaitAsync(ct);
        try
        {
            List<FileManifestEntry> entries;
            try { entries = await client.ManifestAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch
            {
                log("Không xóa được video trên hub");
                return 0;
            }

            var n = await DeleteListedAsync(
                HubVideoRetention.AllVideoNames(entries),
                (name, token) => client.DeleteFileAsync(name, token),
                ct);
            log($"Đã xóa {n} video trên hub");
            return n;
        }
        finally { Gate.Release(); }
    }

    private static async Task<int> DeleteListedAsync(
        IReadOnlyList<string> names,
        Func<string, CancellationToken, Task<bool>> delete,
        CancellationToken ct)
    {
        var deleted = 0;
        foreach (var name in names)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await delete(name, ct)) deleted++;
            }
            catch (OperationCanceledException) { throw; }
            catch { /* một file lỗi không chặn file sau */ }
        }
        return deleted;
    }
}
