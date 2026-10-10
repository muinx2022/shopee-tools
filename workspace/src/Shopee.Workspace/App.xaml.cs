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
    private bool _showWorkspace;

    /// <summary>Hook đóng app chạy ĐÚNG MỘT LẦN (Closing của cửa sổ chính, Exit và SessionEnding đều gọi).</summary>
    private int _shutdownHooksRan;

    /// <summary>
    /// Bọc lỗi gọi khởi tạo module Workspace — LÝ DO TỒN TẠI DUY NHẤT là để tránh lỗi nạp assembly.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void KhoiTaoUpdateProduct() => Shopee.Modules.UpdateProduct.UpdateProductRuntime.Initialize();

    /// <summary>Bọc khởi tạo MultiBrave — cùng lý do với KhoiTaoUpdateProduct.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void KhoiTaoMultiBrave() => MultiBraveRuntime.Initialize();

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
        if (Environment.GetEnvironmentVariable("SHOPEESUITE_SOFTWARE_RENDER") == "1")
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        HookBindingTrace();

        try { UiZoom.Install(); } catch (Exception ex) { TryLog("UiZoom.Install", ex); }

        UiThread.OnError = HandleUiCallbackException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => TryLog("AppDomain", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => { TryLog("UnobservedTask", args.Exception); args.SetObserved(); };

        _showWorkspace = true;  // App này luôn có Workspace

        // Khởi tạo engine workspace
        try { KhoiTaoMultiBrave(); } catch (Exception ex) { TryLog("MultiBraveRuntime.Init", ex); }
        try { KhoiTaoUpdateProduct(); } catch (Exception ex) { TryLog("UpdateProductRuntime.Init", ex); }

        try
        {
            var perf = Shopee.Core.Infrastructure.PerformanceSettingsStore.Shared.Current;
            Shopee.Core.Browser.BraveFleet.MaxConcurrentWindows =
                Shopee.Core.Browser.BraveFleet.WindowsForBudget(perf.UsableCpuCores, perf.UsableRamGb);
            Shopee.Core.Browser.BraveFleet.ConfigureJobLimits();
            var swept = Shopee.Core.Browser.BraveFleet.StartupSweep();
            if (swept > 0) TryLog("BraveFleet.StartupSweep", new Exception($"Đã dọn {swept} Brave mở cũ lúc khởi động."));
            Shopee.Core.Browser.BraveFleet.StartMaintenance();
        }
        catch (Exception ex) { TryLog("BraveFleet.Init", ex); }

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

        try { Shopee.Core.BigSeller.BigSellerStore.Shared.Save(); } catch { }
        if (_showWorkspace) { try { MultiBraveRuntime.Cleanup(); } catch { } }
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
        HandleUiCallbackException(e.Exception);
        e.Handled = true;
    }

    private static void HandleUiCallbackException(Exception ex)
    {
        TryLog("UiThread", ex);
        if (IsBenignTeardown(ex)) return;
        Dialogs.Notify(ex.Message, "Lỗi", DialogIcon.Error);
    }

    private static bool IsBenignTeardown(Exception? ex)
    {
        for (var cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur is ObjectDisposedException or OperationCanceledException)
                return true;
            if ((cur.Message ?? "").Contains("CancellationTokenSource has been disposed", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void TryLog(string source, Exception? ex)
    {
        try { File.AppendAllText(CrashLog, $"=== {source} @ {DateTimeOffset.Now:o} ===\n{ex}\n\n"); } catch { }
    }
}
