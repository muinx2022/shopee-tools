using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Shopee.Modules.MultiBrave;
using Shopee.Workspace.Infrastructure;
using Shopee.Workspace.Services;
using Shopee.Workspace.ViewModels;

namespace Shopee.Workspace;

public partial class App : Application
{
    private static readonly string CrashLog =
        Path.Combine(Path.GetTempPath(), "shopeeworkspace-crash.log");

    /// <summary>Chế độ hiện tại có nhóm Workspace không (quyết định có cleanup MultiBrave lúc đóng).</summary>

    /// <summary>Hook đóng app chạy ĐÚNG MỘT LẦN (Closing của cửa sổ chính, Exit và SessionEnding đều gọi).</summary>
    private int _shutdownHooksRan;

    /// <summary>Đang trong <see cref="HandleUiCallbackException"/> phía Notify. Lỗi của chính hộp thoại không mở hộp thoại thứ hai.</summary>
    private static int _uiErrorDepth;

    /// <summary>Shutdown do luồng vẽ chết — đúng một lần. Lần sau bỏ qua, không popup.</summary>
    private static int _uiShutdownOnce;

    /// <summary>Quét video hub quá 30 ngày: một lần ngay sau init, rồi mỗi 6 giờ khi app còn mở.</summary>
    private System.Threading.Timer? _hubVideoSweep;

    /// <summary>
    /// Bọc lỗi gọi khởi tạo module Workspace — LÝ DO TỒN TẠI DUY NHẤT là để tránh lỗi nạp assembly.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void KhoiTaoUpdateProduct() => Shopee.Modules.UpdateProduct.UpdateProductRuntime.Initialize();

    /// <summary>Bọc khởi tạo MultiBrave — cùng lý do với KhoiTaoUpdateProduct.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void KhoiTaoMultiBrave() => MultiBraveRuntime.Initialize();
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DongUpdateProduct() => Shopee.Modules.UpdateProduct.UpdateProductRuntime.Shutdown();
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DongMultiBrave() => MultiBraveRuntime.Shutdown();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Kiểm tra Brave trước khi làm gì khác
        if (Shopee.Toolkit.Browser.BraveRequirement.LyDoChanKhoiDong(
                Shopee.Toolkit.Browser.BrowserLocator.FindBraveExecutable()) is { } lyDoChan)
        {
            MessageBox.Show(lyDoChan, "Thiếu trình duyệt Brave", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // Software rendering (debug)
        // Mặc định bật Software rendering để tránh UCEERR_RENDERTHREADFAILURE (0x88980406) khi GPU/DWM reset
        if (Environment.GetEnvironmentVariable("SHOPEESUITE_SOFTWARE_RENDER") != "0")
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        HookBindingTrace();

        try { UiZoom.Install(); } catch (Exception ex) { TryLog("UiZoom.Install", ex); }

        UiThread.OnError = HandleUiCallbackException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            TryLog("AppDomain", ex);
            if (UiErrorPolicy.Decide(ex, inHandler: true) == UiErrorAction.Shutdown)
            {
                try { (Current as App)?.RunShutdownHooks(); } catch { }
                Environment.Exit(0);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) => { TryLog("UnobservedTask", args.Exception); args.SetObserved(); };


        // Khởi tạo engine workspace
        try { KhoiTaoMultiBrave(); } catch (Exception ex) { TryLog("MultiBraveRuntime.Init", ex); }
        if (WorkspaceAppConfig.ShowImportUpdate)
        {
            try { KhoiTaoUpdateProduct(); } catch (Exception ex) { TryLog("UpdateProductRuntime.Init", ex); }
        }

        try
        {
            var perf = Shopee.Core.Infrastructure.PerformanceSettingsStore.Shared.Current;
            Shopee.Core.Browser.BraveFleet.MaxConcurrentWindows =
                Shopee.Core.Browser.BraveFleet.WindowsForBudget(perf.UsableCpuCores, perf.UsableRamGb);
            Shopee.Core.Browser.BraveFleet.ConfigureJobLimits();
            if (WorkspaceAppConfig.IsScrapeOnly) Shopee.Core.Browser.BraveFleet.SetManagedSubRoot("profiles");
            else if (WorkspaceAppConfig.IsImportUpdateOnly) Shopee.Core.Browser.BraveFleet.SetManagedSubRoot("bigseller-profiles");
            var swept = Shopee.Core.Browser.BraveFleet.StartupSweep();
            if (swept > 0) TryLog("BraveFleet.StartupSweep", new Exception($"Đã dọn {swept} Brave mở cũ lúc khởi động."));
            Shopee.Core.Browser.BraveFleet.StartMaintenance();
        }
        catch (Exception ex) { TryLog("BraveFleet.Init", ex); }

        // App suite gọi init sau BraveFleet. Workspace trước đây không gọi → CoordinationRuntime.Client luôn
        // null: đẩy/kéo video và xóa video quá hạn đều không chạy. Lỗi init không được chặn cửa sổ.
        try { Shopee.Core.Coordination.CoordinationRuntime.InitForCurrentMode(); }
        catch (Exception ex) { TryLog("Coordination.Init", ex); }
        try
        {
            // Chờ 10 phút sau khi mở app mới quét video quá hạn để app khởi động nhẹ và không tranh mạng
            _hubVideoSweep = new System.Threading.Timer(
                _ => SweepHubVideos(), null, TimeSpan.FromMinutes(10), TimeSpan.FromHours(6));
        }
        catch (Exception ex) { TryLog("HubVideoSweep", ex); }

        try
        {
            Shopee.Core.Infrastructure.StartupJanitor.Notice = msg => TryLog("StartupJanitor", new Exception(msg));
            Shopee.Core.Infrastructure.StartupJanitor.RunInBackground();
        }
        catch (Exception ex) { TryLog("StartupJanitor.Init", ex); }

        var window = new MainWindow { DataContext = new ShellViewModel() };
        MainWindow = window;

        window.Closing += (_, _) => RunShutdownHooks();
        Exit += (_, _) => RunShutdownHooks();
        SessionEnding += (_, _) => RunShutdownHooks();

        window.Show();
    }

    private void RunShutdownHooks()
    {
        if (System.Threading.Interlocked.Exchange(ref _shutdownHooksRan, 1) != 0) return;

        try { _hubVideoSweep?.Dispose(); } catch { }
        _hubVideoSweep = null;

        try { Shopee.Core.BigSeller.BigSellerStore.Shared.Save(); } catch { }
        try { Shopee.Core.Scrape.VideoNotFoundStore.Shared.Save(); } catch { }
        if (WorkspaceAppConfig.ShowImportUpdate)
        {
            try { DongUpdateProduct(); } catch { }
        }
        try { DongMultiBrave(); } catch { }
    }

    private static void HookBindingTrace()
    {
        try
        {
            var path = Environment.GetEnvironmentVariable("SHOPEESUITE_BINDING_LOG");
            if (string.IsNullOrWhiteSpace(path)) return;

            System.Diagnostics.PresentationTraceSources.Refresh();
            var source = System.Diagnostics.PresentationTraceSources.DataBindingSource;
            source.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(path) { TraceOutputOptions = System.Diagnostics.TraceOptions.None });
            source.Switch.Level = System.Diagnostics.SourceLevels.Warning;
            System.Diagnostics.Trace.AutoFlush = true;
        }
        catch (Exception ex) { TryLog("BindingTrace.Hook", ex); }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Handled trước khi quyết định: WPF không tự bật hộp thoại mặc định trong lúc ta đóng.
        e.Handled = true;
        HandleUiCallbackException(e.Exception);
    }

    private static void HandleUiCallbackException(Exception ex)
    {
        var inHandler = System.Threading.Volatile.Read(ref _uiErrorDepth) != 0
            || System.Threading.Volatile.Read(ref _uiShutdownOnce) != 0;
        switch (UiErrorPolicy.Decide(ex, inHandler))
        {
            case UiErrorAction.Shutdown:
                // Đúng một lần. Lần 0x88980406 sau (đang đóng) rơi vào đây rồi bỏ qua: không log thêm, không Shutdown, không popup.
                if (System.Threading.Interlocked.Exchange(ref _uiShutdownOnce, 1) != 0) return;
                TryLog("UiThread", ex);
                try { (Current as App)?.RunShutdownHooks(); } catch { }
                Environment.Exit(0);
                return;
            case UiErrorAction.Ignore:
                TryLog("UiThread", ex);
                return;
            default:
                TryLog("UiThread", ex);
                // Bật TRƯỚC Notify, tắt trong finally — ShowDialog ném Win32 thì không mở hộp thoại thứ hai.
                System.Threading.Interlocked.Increment(ref _uiErrorDepth);
                try { Dialogs.Notify(ex.Message, "Lỗi", DialogIcon.Error); }
                finally { System.Threading.Interlocked.Decrement(ref _uiErrorDepth); }
                return;
        }
    }

    private static void SweepHubVideos()
    {
        _ = SweepHubVideosAsync();
    }

    private static async Task SweepHubVideosAsync()
    {
        try
        {
            await Shopee.Core.Scrape.HubVideoMaintenance.RunExpiredSweepAsync(
                msg => TryLog("HubVideo", new Exception(msg)),
                CancellationToken.None);
        }
        catch (Exception ex) { TryLog("HubVideo", ex); }
    }

    private static void TryLog(string source, Exception? ex)
    {
        try { File.AppendAllText(CrashLog, $"=== {source} @ {DateTimeOffset.Now:o} ===\n{ex}\n\n"); } catch { }
    }
}
