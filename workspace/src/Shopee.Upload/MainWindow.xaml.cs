using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace Shopee.Upload;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _browserWatch;

    /// <summary>true = đã dọn xong (đóng Brave) — lần Close sau cho đóng thật.</summary>
    private bool _shutdownDone;

    /// <summary>true = đang dọn để thoát (đã hỏi lưu, đang đợi đóng Brave) — bấm X thêm lần nữa thì bỏ qua: không hỏi lưu lần hai,
    /// không gọi ShutdownAsync lần hai.</summary>
    private bool _shuttingDown;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        LogBox.TextChanged += (_, _) => LogBox.ScrollToEnd();

        // User tự đóng cửa sổ Brave → dọn phiên để mở lại được.
        _browserWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _browserWatch.Tick += async (_, _) => await _vm.CheckBrowserExitedAsync();
        _browserWatch.Start();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        // Cửa sổ chưa từng hiện (App đóng vì lỗi khởi động trước Show) → không có gì để hỏi/dọn.
        if (_shutdownDone || !IsLoaded) return;
        if (_shuttingDown) { e.Cancel = true; return; }

        if (_vm.IsDirty)
        {
            var answer = MessageBox.Show(this, "Có thay đổi chưa lưu. Lưu trước khi thoát?", "Shopee Upload",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) { e.Cancel = true; return; }
            if (answer == MessageBoxResult.Yes && !_vm.TrySave())
            {
                MessageBox.Show(this, "Chưa lưu được — còn lỗi (xem khung đỏ). Sửa rồi thoát lại.", "Shopee Upload",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                e.Cancel = true;
                return;
            }
        }

        // Tắt app thì đóng Brave: chặn đóng lần này, dọn xong mới đóng thật.
        _shuttingDown = true;
        e.Cancel = true;
        IsEnabled = false;
        _browserWatch.Stop();
        _vm.BrowserStatus = "Đang đóng Brave trước khi thoát…";
        try { await _vm.ShutdownAsync(); }
        catch { }
        _shutdownDone = true;
        // KHÔNG gọi Close() thẳng ở đây: không có Brave thì ShutdownAsync xong đồng bộ, dòng này chạy khi OnClosing của lần đóng này
        // còn trên stack → WPF ném "Cannot … Close … while a Window is closing". Xếp hàng cho dispatcher chạy SAU khi lượt đóng hiện tại
        // kết thúc (đúng cả khi ShutdownAsync xong bất đồng bộ).
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Close));
    }
}
