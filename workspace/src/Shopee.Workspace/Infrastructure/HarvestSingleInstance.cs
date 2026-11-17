using System.Security.Cryptography;
using Shopee.Core.Infrastructure;

namespace Shopee.Workspace.Infrastructure;

/// <summary>
/// Chặn chạy 2 bản <c>ShopeeHarvest.exe</c> trên cùng kho dữ liệu: hai bản dùng chung <c>harvest-outbox.db</c> → hai worker
/// đẩy trùng, và chung thư mục profile <c>harvest-profiles</c>. Mutex có tên RIÊNG của Harvest (không đụng Scrape/Update —
/// các app đó chạy song song bình thường), gắn với thư mục dữ liệu nên user Windows khác (AppData khác) không bị chặn oan.
/// </summary>
public static class HarvestSingleInstance
{
    /// <summary>Tên mutex: <c>Global\ShopeeHarvest-{hash thư mục dữ liệu}</c> — Global để cả phiên Remote Desktop khác của
    /// cùng user (chung AppData) cũng thấy.</summary>
    public static string MutexName(string dataRoot)
    {
        var norm = Path.GetFullPath(dataRoot).TrimEnd('\\', '/').ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(norm)))[..16];
        return @"Global\ShopeeHarvest-" + hash;
    }

    public static string DefaultMutexName() => MutexName(SuitePaths.Root);

    /// <summary>Giữ mutex suốt đời process. Trả null nếu đã có bản khác giữ (hoặc không mở được mutex — coi như đang
    /// chạy cho an toàn). Bản giữ chết thì OS tự huỷ mutex → bản sau mở được.</summary>
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
