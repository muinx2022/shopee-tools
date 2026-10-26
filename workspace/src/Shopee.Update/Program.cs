using Shopee.Workspace.Infrastructure;

namespace Shopee.Update;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Shopee.Workspace.Program.Run(WorkspaceRole.ImportUpdate, args);
    }
}
