using System.Security.Cryptography;
using Shopee.Core.Coordination;

namespace Shopee.Core.Scrape;

/// <summary>Một file video trong thư mục video của máy, đã qua bộ lọc 30 ngày và trần kích thước.</summary>
public sealed record LocalVideo(string Path, string RemoteName, long Size, DateTime LastWriteUtc);

/// <summary>Quét hub không xong (không nối hub, manifest lỗi, không đọc được thư mục). Thông điệp đưa thẳng cho người dùng.</summary>
public sealed class HubVideoScanException : Exception
{
    public HubVideoScanException(string message) : base(message) { }
}

/// <summary>
/// Đẩy lô video trong thư mục video lên hub — nút "Đẩy video" trên ribbon Workspace. Người dùng chủ động bấm,
/// nên lớp này KHÔNG hỏi gì: ViewModel gọi <see cref="ScanAsync"/> để biết X và Y, hỏi người dùng, rồi gọi
/// <see cref="PushToHubAsync"/>. Mỗi file giữ <see cref="HubVideoMaintenance.RunExclusiveAsync"/> riêng; không giữ khóa cả lô.
/// Luật đẩy (trần 80MB, timeout 90s) giống <see cref="SkuVideoSync"/>, nhưng tên đích lấy từ file trên đĩa.
/// </summary>
public static class HubVideoBulkPush
{
    private static readonly string BadSizeReason =
        $"file rỗng hoặc quá {SkuVideoSync.MaxPushBytes / (1024 * 1024)} MB";

    /// <summary>
    /// File <c>.mp4</c> thường trực tiếp trong <paramref name="folder"/> (không đệ quy), sửa trong
    /// <see cref="HubVideoRetention.MaxAge"/> tính tới <paramref name="nowUtc"/>. Đuôi so đúng hoa-thường: <c>.MP4</c> bị bỏ
    /// vì hub không nhận tên đó. Thư mục không có → rỗng, không ném.
    /// </summary>
    public static IReadOnlyList<LocalVideo> ListLocal(string folder, DateTime nowUtc)
    {
        var list = new List<LocalVideo>();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return list;

        foreach (var path in Directory.EnumerateFiles(folder, "*.mp4", SearchOption.TopDirectoryOnly))
        {
            if (!string.Equals(Path.GetExtension(path), ".mp4", StringComparison.Ordinal)) continue;

            var remoteName = "videos/" + Path.GetFileName(path);
            if (!HubVideoRetention.IsVideoName(remoteName)) continue;

            try
            {
                var info = new FileInfo(path);
                var lastWriteUtc = info.LastWriteTimeUtc;
                if (nowUtc - lastWriteUtc > HubVideoRetention.MaxAge) continue;

                var length = info.Length;
                if (!IsPushableSize(length)) continue;

                list.Add(new LocalVideo(path, remoteName, length, lastWriteUtc));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // File vừa bị xóa hoặc đang bị khóa giữa lúc quét: bỏ qua file này, không làm hỏng cả lượt.
            }
        }

        list.Sort((a, b) => string.CompareOrdinal(a.RemoteName, b.RemoteName));
        return list;
    }

    /// <summary>
    /// File cần đẩy: hub không có key; hoặc <c>Size</c> khác (không gọi <paramref name="hashOf"/>); hoặc cùng <c>Size</c>
    /// mà hash khác (so không phân biệt hoa-thường). <paramref name="hashOf"/> ném (trừ hủy) → coi là cần đẩy.
    /// </summary>
    public static IReadOnlyList<LocalVideo> SelectMissing(
        IReadOnlyList<LocalVideo> locals,
        IReadOnlyList<FileManifestEntry> manifest,
        Func<string, string> hashOf)
    {
        var byName = new Dictionary<string, FileManifestEntry>(StringComparer.Ordinal);
        foreach (var entry in manifest) byName[entry.Name] = entry;

        var missing = new List<LocalVideo>();
        foreach (var video in locals)
        {
            if (!byName.TryGetValue(video.RemoteName, out var entry))
            {
                missing.Add(video);
                continue;
            }
            if (entry.Size != video.Size)
            {
                missing.Add(video);
                continue;
            }

            string localHash;
            try
            {
                localHash = hashOf(video.Path);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                missing.Add(video);
                continue;
            }

            if (!string.Equals(localHash, entry.Hash, StringComparison.OrdinalIgnoreCase)) missing.Add(video);
        }
        return missing;
    }

    /// <summary>
    /// SHA-256 (hex hoa) của một file. Cùng cách <c>HubConfigSync.LocalSha256</c>, nhưng mở với chia sẻ ghi/xóa
    /// để tiến trình tải video không bị chặn khi đang đọc.
    /// </summary>
    public static string Sha256Hex(string path)
    {
        using var fs = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, useAsync: false);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(fs));
    }

    /// <summary>
    /// Quét: liệt kê file local, tải manifest một lần, rồi so hash. Phần hash chạy trên thread pool.
    /// Không nối hub, manifest lỗi, hoặc không đọc được thư mục → <see cref="HubVideoScanException"/>.
    /// Hủy của <paramref name="ct"/> → ném <see cref="OperationCanceledException"/>.
    /// </summary>
    public static async Task<(IReadOnlyList<LocalVideo> All, IReadOnlyList<LocalVideo> Missing)> ScanAsync(
        string folder, CancellationToken ct)
    {
        var client = CoordinationRuntime.Client
            ?? throw new HubVideoScanException("Chưa nối hub, không đẩy được video.");

        IReadOnlyList<LocalVideo> all;
        try
        {
            // Quét thư mục (có thể chậm với ổ mạng) chạy ngoài UI thread.
            all = await Task.Run(() => ListLocal(folder, DateTime.UtcNow), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new HubVideoScanException("Không đọc được thư mục video.");
        }

        List<FileManifestEntry> manifest;
        try
        {
            manifest = await client.ManifestAsync(ct).ConfigureAwait(false);
        }
        // Timeout của HttpClient cũng là OperationCanceledException nhưng token không hủy — đó là lỗi mạng.
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw new HubVideoScanException("Không lấy được danh sách video trên hub.");
        }

        var missing = await Task.Run(() => SelectMissing(all, manifest, path =>
        {
            ct.ThrowIfCancellationRequested();
            return Sha256Hex(path);
        }), ct).ConfigureAwait(false);

        return (all, missing);
    }

    /// <summary>
    /// Đẩy lô qua hub thật. Hub chưa nối → mọi file đều lỗi «chưa nối hub». Mỗi file gọi <paramref name="onEach"/> đúng
    /// một lần sau khi xong. Hủy → ném tiếp, <paramref name="onEach"/> đã gọi cho các file xong trước đó.
    /// </summary>
    public static Task<(int Ok, int Fail)> PushToHubAsync(
        IReadOnlyList<LocalVideo> items,
        Action<LocalVideo, bool, string?> onEach,
        CancellationToken ct)
    {
        var client = CoordinationRuntime.Client;
        return PushAllAsync(items, Push, onEach, ct);

        Task<string?> Push(string name, byte[] bytes, CancellationToken token) =>
            client is null
                ? Task.FromResult<string?>("chưa nối hub")
                : UploadOneAsync(client, name, bytes, token);
    }

    /// <summary>
    /// Lõi lô, không gọi mạng: lần lượt từng item, mỗi item một <see cref="HubVideoMaintenance.RunExclusiveAsync"/>.
    /// <paramref name="push"/> trả <c>null</c> = đã nhận, chuỗi = lý do lỗi. Push ném (trừ hủy) → lỗi, đi tiếp.
    /// Hủy (của <paramref name="ct"/> hoặc OperationCanceledException từ push) → ném tiếp, các file sau không được gọi.
    /// </summary>
    public static async Task<(int Ok, int Fail)> PushAllAsync(
        IReadOnlyList<LocalVideo> items,
        Func<string, byte[], CancellationToken, Task<string?>> push,
        Action<LocalVideo, bool, string?> onEach,
        CancellationToken ct)
    {
        var ok = 0;
        var fail = 0;
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();

            // null = đã nhận. Chỉ có một đường gán: lambda (trả null/lý do) hoặc catch (gán lý do khác null).
            string? reason = null;
            try
            {
                await HubVideoMaintenance.RunExclusiveAsync(async token =>
                {
                    reason = await PushOneAsync(item, push, token).ConfigureAwait(false);
                }, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                reason = ShortReason(ex.Message) ?? "lỗi không rõ";
            }

            if (reason is null) ok++;
            else fail++;
            onEach(item, reason is null, reason);
        }
        return (ok, fail);
    }

    /// <summary>Đọc byte đúng một lần, kiểm trần, rồi gọi push. Lỗi đọc file → lý do, không gọi push.</summary>
    private static async Task<string?> PushOneAsync(
        LocalVideo item,
        Func<string, byte[], CancellationToken, Task<string?>> push,
        CancellationToken ct)
    {
        byte[] bytes;
        try
        {
            // Chia sẻ ghi/xóa: scrape đang tải có thể ghi hoặc xóa file này, không được làm hỏng lượt đọc.
            await using var stream = new FileStream(
                item.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, useAsync: true);
            if (!IsPushableSize(stream.Length)) return BadSizeReason;

            bytes = new byte[stream.Length];
            await stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "không đọc được file";
        }

        if (!IsPushableSize(bytes.Length)) return BadSizeReason;
        return await push(item.RemoteName, bytes, ct).ConfigureAwait(false);
    }

    /// <summary>Một PUT, có timeout 90s riêng cho file này. Hết giờ hoặc hub từ chối → lý do; hủy của người dùng → ném tiếp.</summary>
    private static async Task<string?> UploadOneAsync(HubClient client, string name, byte[] bytes, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(SkuVideoSync.TransferTimeout);
        try
        {
            var resp = await client.UploadAsync(name, bytes, null, timeoutCts.Token).ConfigureAwait(false);
            return resp.Ok ? null : (ShortReason(resp.Conflict) ?? "hub từ chối");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return $"quá {(int)SkuVideoSync.TransferTimeout.TotalSeconds} giây";
        }
    }

    private static bool IsPushableSize(long length) => length > 0 && length <= SkuVideoSync.MaxPushBytes;

    /// <summary>Cùng luật <c>LauncherRunnerLoop.ShortHubReason</c>: một dòng, tối đa 120 ký tự. Rỗng → null.</summary>
    private static string? ShortReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return null;
        var oneLine = reason.Trim().Replace('\r', ' ').Replace('\n', ' ');
        return oneLine.Length <= 120 ? oneLine : oneLine[..120];
    }
}
