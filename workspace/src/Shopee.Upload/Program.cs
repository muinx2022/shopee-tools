using System.Windows;

namespace Shopee.Upload;

public static class Program
{
    /// <summary>Mutex chặn bản thứ hai — giữ tham chiếu suốt đời process (GC thu là mất chặn).</summary>
    private static Mutex? _instance;

    // WPF yêu cầu [STAThread] cho luồng UI.
    [STAThread]
    public static void Main()
    {
        // Chặn bản thứ hai TRƯỚC mọi thứ: 2 bản = 2 cổng mở Brave không biết IP của nhau + ghi đè seller-accounts.json của nhau.
        _instance = UploadSingleInstance.TryAcquire(UploadSingleInstance.DefaultMutexName());
        if (_instance is null)
        {
            MessageBox.Show("Shopee Upload đang chạy trên máy này. Dùng cửa sổ đang mở (xem thanh tác vụ).",
                "Shopee Upload", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var app = new App();
        app.InitializeComponent();   // nạp App.xaml (theme)
        app.Run();                   // MainWindow dựng trong App.OnStartup (không dùng StartupUri)
        GC.KeepAlive(_instance);
    }
}
