using Shopee.Workspace.Infrastructure;

namespace Shopee.Workspace;

public static class Program
{
    public static void Run(WorkspaceRole? role = null, string[]? args = null)
    {
        if (role.HasValue)
            WorkspaceAppConfig.Initialize(role.Value);
        else
            WorkspaceAppConfig.InitializeFromArgs(args);

        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        var app = new App();
        app.InitializeComponent();   // nạp App.xaml (theme + icon + DataTemplate)
        app.Run();                   // MainWindow dựng trong App.OnStartup (không dùng StartupUri)
    }

    // WPF yêu cầu [STAThread] cho luồng UI.
    [STAThread]
    public static void Main(string[] args)
    {
        Run(null, args);
    }
}
