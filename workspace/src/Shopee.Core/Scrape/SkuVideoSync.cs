using System.Collections.Concurrent;
using Shopee.Core.Infrastructure;

namespace Shopee.Core.Scrape;

/// <summary>
/// Tên file video theo SKU, và đẩy/kéo đúng bản đó qua hub — dùng chung cho máy cào và máy update.
/// Lớp này KHÔNG gọi mạng: nơi gọi đưa delegate (HubClient lúc chạy, hoặc fake trong test).
/// Hub tắt, lỗi mạng, thiếu file: trả false/null, không làm fail vòng chạy. Chỉ hủy của người gọi thì ném tiếp.
/// </summary>
public static class SkuVideoSync
{
    /// <summary>Trần một lần đẩy. Video &lt; 60s thường vài chục MB; quá mức này không đọc và không gọi push
    /// (tránh một PUT nuốt RAM và chiếm kênh hub).</summary>
    public const long MaxPushBytes = 80L * 1024 * 1024;

    /// <summary>Trần chờ MỘT lần đẩy hoặc kéo. HttpClient bulk của hub để 5 phút (cỡ workbook) —
    /// một video treo không được chiếm hết khoảng đó.</summary>
    public static readonly TimeSpan TransferTimeout = TimeSpan.FromSeconds(90);

    /// <summary>
    /// <c>{sku đã sanitize}.mp4</c>. Cùng luật <see cref="VideoDownloader"/> từng giữ riêng: tách
    /// <see cref="Path.GetInvalidFileNameChars"/>, nối <c>_</c>, Trim + Trim('.'), rỗng → <c>video</c>, rồi cộng <c>.mp4</c>.
    /// </summary>
    public static string FileName(string? sku)
    {
        var cleaned = string.Join("_", (sku ?? "video").Split(Path.GetInvalidFileNameChars())).Trim().Trim('.');
        if (string.IsNullOrEmpty(cleaned)) cleaned = "video";
        return cleaned + ".mp4";
    }

    /// <summary>Key blob trên hub: một đoạn tên file dưới <c>videos/</c>, không thêm <c>\</c> <c>/</c> <c>:</c>.</summary>
    public static string RemoteName(string? sku) => "videos/" + FileName(sku);

    /// <summary>Đường dẫn local trong thư mục video của máy đang chạy.</summary>
    public static string LocalPath(string folder, string? sku) => Path.Combine(folder, FileName(sku));

    /// <summary>Một khóa cho mỗi file đích. Update chạy tới 10 lane chung một thư mục — không khóa thì hai lane
    /// cùng SKU ghi đè một file tạm rồi xóa dest đang được lane kia upload. Giữ lại semaphore sau khi nhả:
    /// gỡ khỏi dictionary trong lúc lane khác đang Wait sẽ tạo hai khóa cho cùng một dest.</summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DestLocks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Đọc file local rồi gọi <paramref name="push"/> với key hub và đúng byte đó.
    /// Không có file, rỗng, hoặc lớn hơn <see cref="MaxPushBytes"/> → <c>false</c> và KHÔNG gọi push.
    /// Push trả false hoặc ném (trừ hủy) → <c>false</c>, không ném.
    /// </summary>
    public static async Task<bool> TryPushAsync(
        string? savedPath,
        string? sku,
        Func<string, byte[], CancellationToken, Task<bool>> push,
        CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(savedPath) || !File.Exists(savedPath)) return false;
            var length = new FileInfo(savedPath).Length;
            if (length <= 0 || length > MaxPushBytes) return false;

            var bytes = await File.ReadAllBytesAsync(savedPath, ct).ConfigureAwait(false);
            if (bytes.Length == 0 || bytes.Length > MaxPushBytes) return false;
            return await push(RemoteName(sku), bytes, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Đã có file local → trả đường đó, không gọi <paramref name="pull"/>. Thiếu file: kéo byte (giữ khóa dest
    /// suốt lúc kéo và ghi, lane sau thấy file thì không tải lại), ghi <c>{đích}.{guid}.part</c> rồi Move đè đích.
    /// Folder trống, ổ đích dưới <see cref="DiskSpaceGuard.VideoMinFreeBytes"/>, pull null/rỗng/dài hơn
    /// <see cref="MaxPushBytes"/>, hoặc pull ném (trừ hủy) → null, không gọi ghi, không để file tạm.
    /// Hủy giữa chừng thì ném tiếp và xóa đúng file tạm mình đã tạo.
    /// </summary>
    public static async Task<string?> TryEnsureLocalAsync(
        string? folder,
        string? sku,
        Func<string, CancellationToken, Task<byte[]?>> pull,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;

        var dest = LocalPath(folder, sku);
        if (File.Exists(dest)) return dest;
        if (!DiskSpaceGuard.HasFreeSpace(folder, DiskSpaceGuard.VideoMinFreeBytes)) return null;

        var gate = DestLocks.GetOrAdd(LockKey(dest), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(dest)) return dest;

            byte[]? bytes;
            try
            {
                bytes = await pull(RemoteName(sku), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }

            if (bytes is null || bytes.Length == 0 || bytes.Length > MaxPushBytes) return null;

            var part = dest + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                Directory.CreateDirectory(folder);
                await File.WriteAllBytesAsync(part, bytes, ct).ConfigureAwait(false);
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(part, dest);
                return dest;
            }
            catch (OperationCanceledException)
            {
                TryDelete(part);
                throw;
            }
            catch
            {
                TryDelete(part);
                return null;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static string LockKey(string dest)
    {
        try { return Path.GetFullPath(dest); }
        catch { return dest; }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // .part sót không được làm fail vòng update; lần sau thiếu dest sẽ kéo lại.
        }
    }
}
