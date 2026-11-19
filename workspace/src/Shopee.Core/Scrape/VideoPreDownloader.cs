using Shopee.Core.Coordination;

namespace Shopee.Core.Scrape;

/// <summary>
/// Tải video từ Hub về thư mục local TRƯỚC khi Update chạy.
/// Bước này chạy từ nền — caller hiện progress ra UI; lỗi mạng bị bắt per-file, không dừng file khác.
/// </summary>
public static class VideoPreDownloader
{
    /// <summary>
    /// Từ danh sách SKU + thư mục video, tìm SKU nào CHƯA có file local và CHƯA bị xác nhận là 404 trên Hub.
    /// SKU null/rỗng được bỏ qua. Dedup cùng SKU nhiều dòng.
    /// </summary>
    public static IReadOnlyList<(string RemoteName, string LocalPath)> FindMissing(
        string folder, IEnumerable<string?> skus)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(string, string)>();
        foreach (var sku in skus)
        {
            if (string.IsNullOrWhiteSpace(sku)) continue;
            var local = SkuVideoSync.LocalPath(folder, sku);
            var remote = SkuVideoSync.RemoteName(sku);
            if (!seen.Add(remote)) continue; // dedup
            if (!File.Exists(local) && !VideoNotFoundStore.Shared.Contains(remote))
                result.Add((remote, local));
        }
        return result;
    }

    /// <param name="missing">Danh sách (remoteName, localPath) cần tải.</param>
    /// <param name="onProgress">Callback: (đã xong, tổng, SKU hiện tại).</param>
    public static async Task<DownloadResult> DownloadMissingAsync(
        HubClient client,
        IReadOnlyList<(string RemoteName, string LocalPath)> missing,
        Action<int, int, string>? onProgress,
        CancellationToken ct)
    {
        if (missing.Count == 0) return new DownloadResult(0, 0, 0);

        var ok = 0;
        var fail = 0;
        var notFound = 0;
        var done = 0;

        // Tải song song 12 luồng để quét nhanh danh sách video
        // Tải song song tối đa 5 luồng: vừa đủ nhanh, vừa tránh nghẽn socket/tunnel và giật đĩa
        using var sem = new SemaphoreSlim(5);
        var tasks = missing.Select(async item =>
        {
            await sem.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();
                var (remote, local) = item;
                try
                {
                    var got = await client.DownloadToFileAsync(remote, local, ct).ConfigureAwait(false);
                    if (got)
                    {
                        Interlocked.Increment(ref ok);
                    }
                    else
                    {
                        // 404 = không có trên Hub → ghi nhận để không hỏi lại
                        Interlocked.Increment(ref notFound);
                        VideoNotFoundStore.Shared.Add(remote);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    Interlocked.Increment(ref fail);
                }
                finally
                {
                    var current = Interlocked.Increment(ref done);
                    try { onProgress?.Invoke(current, missing.Count, Path.GetFileNameWithoutExtension(item.LocalPath)); } catch { }
                }
            }
            finally
            {
                sem.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        VideoNotFoundStore.Shared.Save();
        return new DownloadResult(ok, fail, notFound);
    }
}

public record DownloadResult(int Downloaded, int Failed, int NotOnHub);
