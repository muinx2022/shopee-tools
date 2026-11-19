using System.Security.Cryptography;
using System.Text;
using Shopee.Core.Infrastructure;

namespace Shopee.Upload;

/// <summary>
/// Chặn chạy 2 bản <c>ShopeeUpload.exe</c> trên cùng kho dữ liệu (cùng khuôn <c>HarvestSingleInstance</c> của app Harvest, mutex tên
/// RIÊNG nên không đụng Harvest/Scrape/Update). Gắn với thư mục dữ liệu nên user Windows khác (AppData khác) không bị chặn oan.
/// </summary>
public static class UploadSingleInstance
{
    /// <summary>Tên mutex: <c>Global\ShopeeUpload-{hash thư mục dữ liệu}</c> — Global để cả phiên Remote Desktop khác của cùng user
    /// (chung AppData) cũng thấy.</summary>
    public static string MutexName(string dataRoot)
    {
        var norm = Path.GetFullPath(dataRoot).TrimEnd('\\', '/').ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(norm)))[..16];
        return @"Global\ShopeeUpload-" + hash;
    }

    public static string DefaultMutexName() => MutexName(SuitePaths.Root);

    /// <summary>Giữ mutex suốt đời process. Trả null nếu đã có bản khác giữ (hoặc không mở được mutex — coi như đang chạy cho an
    /// toàn). Bản giữ chết thì OS tự huỷ mutex → bản sau mở được.</summary>
    public static Mutex? TryAcquire(string name)
    {
        Mutex? m = null;
        try
        {
            m = new Mutex(initiallyOwned: false, name, out var createdNew);
            if (createdNew) return m;
        }
        catch (UnauthorizedAccessException) { }
        catch (WaitHandleCannotBeOpenedException) { }
        m?.Dispose();
        return null;
    }
}
