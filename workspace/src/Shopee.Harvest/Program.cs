using Shopee.Workspace.Infrastructure;

namespace Shopee.Harvest;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Shopee.Workspace.Program.Run(WorkspaceRole.Harvest, args);
    }
}
