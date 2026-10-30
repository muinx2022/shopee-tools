namespace Shopee.Workspace.Infrastructure;

public enum WorkspaceRole
{
    Full,
    Scrape,
    ImportUpdate
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

    public static bool ShowScrape => Role is WorkspaceRole.Full or WorkspaceRole.Scrape;
    public static bool ShowImportUpdate => Role is WorkspaceRole.Full or WorkspaceRole.ImportUpdate;
    public static bool IsScrapeOnly => Role == WorkspaceRole.Scrape;
    public static bool IsImportUpdateOnly => Role == WorkspaceRole.ImportUpdate;
    public static string WindowIcon => Role switch
    {
        WorkspaceRole.Scrape => "pack://application:,,,/ShopeeWorkspace;component/Assets/scrape-icon.ico",
        WorkspaceRole.ImportUpdate => "pack://application:,,,/ShopeeWorkspace;component/Assets/update-icon.ico",
        _ => "pack://application:,,,/ShopeeWorkspace;component/Assets/app-icon.ico"
    };
    public static string WindowTitle => Role switch
    {
        WorkspaceRole.Scrape => "Shopee Scrape",
        WorkspaceRole.ImportUpdate => "Shopee Import & Update",
        _ => "Shopee Workspace"
    };
}
