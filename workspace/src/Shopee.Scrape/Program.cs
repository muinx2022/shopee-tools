using Shopee.Workspace.Infrastructure;

namespace Shopee.Scrape;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Shopee.Workspace.Program.Run(WorkspaceRole.Scrape, args);
    }
}
