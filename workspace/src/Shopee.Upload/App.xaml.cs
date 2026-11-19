using System.Windows;
using System.Windows.Threading;
using Shopee.Core.Coordination;
using Shopee.Core.MassUpload.Seller;
using Shopee.Upload.Services;

namespace Shopee.Upload;

public partial class App : Application
{
    private MainViewModel? _vm;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        try
        {
            var store = new SellerAccountStore(log: msg => Dispatcher.BeginInvoke(() => _vm?.Log(msg)));
            // GĐ8: nối Hub như app Harvest (chỉ client, cấu hình Hub riêng của Harvest). Lỗi init không chặn cửa sổ — panel xuất tự báo
            // "chưa nối Hub".
            ExportService.UseHarvestHubConfig();
            try { CoordinationRuntime.InitForCurrentMode(); }
            catch (Exception ex) { ExportService.HubInitError = ex.Message; }   // HubProblem báo kèm lý do khi bấm Xuất
            var export = new ExportService(log: msg => Dispatcher.BeginInvoke(() => _vm?.Log(msg)));
            _vm = new MainViewModel(
                store,
                ui: action => Dispatcher.BeginInvoke(action),
                confirm: message => MessageBox.Show(message, "Shopee Upload", MessageBoxButton.YesNo, MessageBoxImage.Question)
                                    == MessageBoxResult.Yes,
                export: export,
                showBrowserFeatures: UploadFeatures.ShowBrowserFeatures);
            if (ExportService.HubInitError is { } hubError) _vm.Log($"Không nối được Hub lúc mở app: {hubError}");
            var window = new MainWindow(_vm);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            // Không dựng được cửa sổ: báo rồi THOÁT HẲN. Không thoát thì tiến trình sống không cửa sổ, giữ Mutex — mở lại app chỉ thấy
            // "đang chạy". (DispatcherUnhandledException bên dưới cũng nuốt lỗi nên phải bắt ngay ở đây.) Chưa có key nào lên form nên
            // không cần che.
            MessageBox.Show($"Không mở được Shopee Upload: {ex.Message}", "Shopee Upload — lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>Lỗi lọt ra UI: báo (đã che key/mật khẩu) và giữ app sống (Brave đang mở vẫn được đóng khi tắt app).</summary>
    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        var message = e.Exception.Message;
        try { if (_vm is not null) message = _vm.Redact(message); }
        catch { message = "(lỗi không hiện được nội dung)"; }
        MessageBox.Show(message, "Shopee Upload — lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        // Lỗi lọt ra khi chưa có cửa sổ nào hiện → thoát, không để tiến trình mồ côi giữ Mutex.
        if (MainWindow is not { IsVisible: true }) Shutdown(1);
    }
}
