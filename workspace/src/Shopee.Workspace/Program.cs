namespace Shopee.Workspace;

internal static class Program
{
    // WPF yêu cầu [STAThread] cho luồng UI.
    [STAThread]
    public static void Main()
    {
        var app = new App();
        app.InitializeComponent();   // nạp App.xaml (theme + icon + DataTemplate)
        app.Run();                   // MainWindow dựng trong App.OnStartup (không dùng StartupUri)
    }
}
