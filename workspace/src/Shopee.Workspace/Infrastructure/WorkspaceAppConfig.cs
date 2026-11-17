namespace Shopee.Workspace.Infrastructure;

public enum WorkspaceRole
{
    Full,
    Scrape,
    ImportUpdate,
    /// <summary>App Shopee Harvest (exe <c>ShopeeHarvest</c>): lấy JSON SP Shopee lưu Hub, không BigSeller. Dùng lại màn
    /// Workspace chế độ scrape-only (<see cref="WorkspaceAppConfig.IsScrapeOnly"/> = true) với tiến độ/profile RIÊNG.</summary>
    Harvest
}

public static class WorkspaceAppConfig
{
    public static WorkspaceRole Role { get; private set; } = WorkspaceRole.Full;

    public static void Initialize(WorkspaceRole role) => Role = role;

    public static void InitializeFromArgs(string[]? args)
    {
        if (args is null || args.Length == 0)
        {
            Role = WorkspaceRole.Full;
            return;
        }

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--scrape", StringComparison.OrdinalIgnoreCase))
            {
                Role = WorkspaceRole.Scrape;
                return;
            }
            if (string.Equals(arg, "--update", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--import-update", StringComparison.OrdinalIgnoreCase))
            {
                Role = WorkspaceRole.ImportUpdate;
                return;
            }
            // CỐ Ý không có "--harvest" / "--role harvest": khác --scrape (ShopeeWorkspace.exe gói sẵn extension
            // shopee-scrape nên chạy được), ShopeeWorkspace.exe KHÔNG gói extensions\shopee-harvest và không có trong
            // danh sách process của harvest-profiles (BraveFleet) → vai trò Harvest CHỈ vào qua ShopeeHarvest.exe.
            if (string.Equals(arg, "--role", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                var roleVal = args[i + 1];
                if (string.Equals(roleVal, "scrape", StringComparison.OrdinalIgnoreCase))
                {
                    Role = WorkspaceRole.Scrape;
                    return;
                }
                if (string.Equals(roleVal, "update", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(roleVal, "import-update", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(roleVal, "importupdate", StringComparison.OrdinalIgnoreCase))
                {
                    Role = WorkspaceRole.ImportUpdate;
                    return;
                }
            }
        }

        Role = WorkspaceRole.Full;
    }

    public static bool ShowScrape => Role is WorkspaceRole.Full or WorkspaceRole.Scrape or WorkspaceRole.Harvest;
    public static bool ShowImportUpdate => Role is WorkspaceRole.Full or WorkspaceRole.ImportUpdate;
    /// <summary>App chỉ một việc kiểu scrape (màn Workspace rút gọn): Scrape, và Harvest dùng lại đúng màn đó. Chỗ nào
    /// Harvest phải khác Scrape thì hỏi thêm <see cref="IsHarvest"/>.</summary>
    public static bool IsScrapeOnly => Role is WorkspaceRole.Scrape or WorkspaceRole.Harvest;
    public static bool IsImportUpdateOnly => Role == WorkspaceRole.ImportUpdate;
    /// <summary>Process là app Shopee Harvest: không BigSeller (cookie/đăng nhập/Product Copy), không video, tiến độ +
    /// cấu hình đích + profile Brave RIÊNG (xem <see cref="RoleStores"/>).</summary>
    public static bool IsHarvest => Role == WorkspaceRole.Harvest;
    public static string WindowIcon => Role switch
    {
        WorkspaceRole.Scrape => "pack://application:,,,/ShopeeWorkspace;component/Assets/scrape-icon.ico",
        WorkspaceRole.ImportUpdate => "pack://application:,,,/ShopeeWorkspace;component/Assets/update-icon.ico",
        WorkspaceRole.Harvest => "pack://application:,,,/ShopeeWorkspace;component/Assets/harvest-icon.ico",
        _ => "pack://application:,,,/ShopeeWorkspace;component/Assets/app-icon.ico"
    };
    public static string WindowTitle => Role switch
    {
        WorkspaceRole.Scrape => "Shopee Scrape",
        WorkspaceRole.ImportUpdate => "Shopee Import & Update",
        WorkspaceRole.Harvest => "Shopee Harvest",
        _ => "Shopee Workspace"
    };
}
