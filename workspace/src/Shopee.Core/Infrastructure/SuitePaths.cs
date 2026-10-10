namespace Shopee.Core.Infrastructure;

/// <summary>
/// Vị trí dữ liệu dùng chung của Shopee Workspace. Mọi dữ liệu người dùng (settings, profile, file
/// kết quả) nằm dưới <see cref="Root"/> = %AppData%\ShopeeBackend\Workspace\&lt;module&gt; để không bị xoá khi
/// build lại, và để các module chia sẻ một gốc thống nhất.
/// </summary>
public static class SuitePaths
{
    /// <summary>
    /// %AppData%\ShopeeBackend\Workspace — gốc kho dữ liệu của app này. Nếu có file <c>data-dir.txt</c> đặt CẠNH .exe
    /// và chứa một đường dẫn, dùng <c>&lt;đường dẫn đó&gt;\ShopeeBackend\Workspace</c> làm gốc — cho phép một bản app
    /// khác (vd bản "sync") giữ kho dữ liệu RIÊNG, KHÔNG đụng dữ liệu của bản gốc. Đường dẫn tương đối
    /// được tính theo thư mục .exe; marker lỗi/trống → quay về %AppData%.
    /// </summary>
    public static string Root { get; } = Path.Combine(ResolveAppDataBase(), "ShopeeBackend", "Workspace");

    /// <summary>
    /// %AppData%\ShopeeBackend\Data\shopee_data.db — DB chung với ShopeeBackend.
    /// KHÔNG theo data-dir.txt vì đây là DB của app khác; đường dẫn này luôn cố định.
    /// </summary>
    public static string ShopeeBackendDbPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ShopeeBackend", "Data", "shopee_data.db");

    /// <summary>Đọc gốc dữ liệu từ <c>data-dir.txt</c> cạnh .exe; không có/hỏng → %AppData%.</summary>
    private static string ResolveAppDataBase()
    {
        try
        {
            var marker = Path.Combine(AppContext.BaseDirectory, "data-dir.txt");
            if (File.Exists(marker))
            {
                var custom = File.ReadAllText(marker).Trim();
                if (!string.IsNullOrWhiteSpace(custom))
                {
                    if (!Path.IsPathRooted(custom))
                        custom = Path.Combine(AppContext.BaseDirectory, custom);
                    return Path.GetFullPath(custom);
                }
            }
        }
        catch { /* marker hỏng → dùng mặc định */ }
        return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }

    /// <summary>Thư mục dữ liệu riêng của một module (tự tạo nếu chưa có).</summary>
    public static string ModuleDir(string module)
    {
        var dir = Path.Combine(Root, module);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Đường dẫn một file cục bộ-theo-máy NGAY DƯỚI Root (KHÔNG đồng bộ): machine.json, hub-client.json.
    /// Khác ModuleDir ở chỗ không tạo thư mục con; người gọi tự bảo đảm Root tồn tại khi ghi.
    /// </summary>
    public static string RootFile(string name) => Path.Combine(Root, name);

    /// <summary>Thư mục cache các file dùng chung tải từ Hub về máy này (workbook, cookie…).</summary>
    public static string HubCacheDir => ModuleDir("hub-cache");

    /// <summary>%AppData%\ShopeeStatApp — dữ liệu của app shopee-stat cũ (dùng để copy account).</summary>
    public static string ShopeeStatDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShopeeStatApp");
}
