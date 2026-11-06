using System.IO;
using System.Windows;
using Shopee.Core.Coordination;
using Shopee.Core.Scrape;
using UpdateProduct;

namespace Shopee.Workspace.Modules.UpdateProduct;

public partial class VideoPreDownloadDialog : Window
{
    private readonly HubClient? _client;
    private readonly BigSellerWorkflowSettings _wf;
    private readonly string _videoFolder;
    private readonly bool _autoStartDownload;
    private readonly CancellationToken _externalCt;
    private CancellationTokenRegistration? _externalCtReg;
    private CancellationTokenSource? _cts;
    private IReadOnlyList<(string RemoteName, string LocalPath)>? _missing;

    public bool ShouldProceed { get; private set; }

    internal VideoPreDownloadDialog(
        HubClient? client, BigSellerWorkflowSettings wf, string videoFolder,
        bool autoStartDownload = false, CancellationToken externalCt = default,
        IReadOnlyList<(string RemoteName, string LocalPath)>? missing = null)
    {
        InitializeComponent();
        _client = client;
        _wf = wf;
        _videoFolder = videoFolder;
        _autoStartDownload = autoStartDownload;
        _externalCt = externalCt;
        _missing = missing;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_externalCt.IsCancellationRequested)
            {
                ShouldProceed = false;
                Close();
                return;
            }

            _cts = new CancellationTokenSource();
            if (_externalCt.CanBeCanceled)
            {
                _externalCtReg = _externalCt.Register(() =>
                {
                    try { _cts?.Cancel(); } catch { }
                    try
                    {
                        if (!Dispatcher.HasShutdownStarted)
                            Dispatcher.BeginInvoke(() => { try { Close(); } catch { } });
                    }
                    catch { }
                });
            }

            if (_client == null)
            {
                StatusText.Text = "⚠ Hub chưa kết nối hoặc chưa cấu hình. Tiếp tục Update sau 1.5s...";
                ShouldProceed = true;
                try { await Task.Delay(1500, _cts.Token); } catch (OperationCanceledException) { }
                try { Close(); } catch { }
                return;
            }

            await CheckAsync();
        }
        catch (OperationCanceledException)
        {
            try { Close(); } catch { }
        }
        catch (Exception ex)
        {
            StatusText.Text = "⚠ Lỗi kiểm tra video: " + ex.Message;
            ShouldProceed = true; // Không chặn Update
            try { await Task.Delay(1500); } catch { }
            try { Close(); } catch { }
        }
    }

    private async Task CheckAsync()
    {
        if (_missing == null)
        {
            StatusText.Text = "Đang kiểm tra danh sách video...";
            try
            {
                var skus = await Task.Run(() => WorkbookRecordCache.GetSkusAsync(_wf, _cts!.Token), _cts!.Token);
                _missing = await Task.Run(() => VideoPreDownloader.FindMissing(_videoFolder, skus), _cts!.Token);
            }
            catch (OperationCanceledException)
            {
                Close();
                return;
            }
        }

        if (_missing.Count == 0)
        {
            StatusText.Text = "✓ Tất cả video đã có sẵn trên máy/cache. Bắt đầu Update...";
            BtnOk.Visibility = Visibility.Visible;
            ShouldProceed = true;
            try { await Task.Delay(1000, _cts!.Token); } catch (OperationCanceledException) { }
            Close();
        }
        else
        {
            StatusText.Text = $"Thiếu {_missing.Count} video — Đang tự động tải từ Hub...";
            await StartDownloadAsync();
        }
    }

    private async void BtnDownload_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await StartDownloadAsync();
        }
        catch (OperationCanceledException)
        {
            try { Close(); } catch { }
        }
        catch (Exception ex)
        {
            StatusText.Text = "⚠ Lỗi tải video: " + ex.Message;
            try { Close(); } catch { }
        }
    }

    private async Task StartDownloadAsync()
    {
        if (_missing == null || _client == null) return;
        BtnDownload.Visibility = Visibility.Collapsed;
        BtnSkip.Visibility = Visibility.Visible;
        BtnCancel.Visibility = Visibility.Visible;
        Progress.Visibility = Visibility.Visible;
        DetailText.Visibility = Visibility.Visible;
        Progress.Maximum = _missing.Count;
        Progress.Value = 0;

        DownloadResult result;
        try
        {
            result = await VideoPreDownloader.DownloadMissingAsync(
                _client, _missing,
                (done, total, name) =>
                {
                    try
                    {
                        if (Dispatcher.HasShutdownStarted) return;
                        Dispatcher.BeginInvoke(() =>
                        {
                            try
                            {
                                Progress.Value = done;
                                StatusText.Text = $"Đang tải {done}/{total} video...";
                                if (!string.IsNullOrEmpty(name))
                                    DetailText.Text = name + ".mp4";
                            }
                            catch { }
                        });
                    }
                    catch { }
                },
                _cts!.Token);
        }
        catch (OperationCanceledException)
        {
            try { Close(); } catch { }
            return;
        }
        catch (Exception ex)
        {
            StatusText.Text = "⚠ Lỗi tải video: " + ex.Message;
            ShouldProceed = true;
            try { await Task.Delay(1500); } catch { }
            try { Close(); } catch { }
            return;
        }

        BtnCancel.Visibility = Visibility.Collapsed;
        Progress.Visibility = Visibility.Collapsed;
        DetailText.Visibility = Visibility.Collapsed;

        if (result.Failed > 0)
        {
            StatusText.Text = $"⚠ Tải lỗi {result.Failed}/{_missing.Count} video. Tự động tiếp tục Update sau 1.5s...";
            ShouldProceed = true;
            try { await Task.Delay(1500, _cts!.Token); } catch (OperationCanceledException) { }
            Close();
            return;
        }

        if (result.NotOnHub > 0)
            StatusText.Text = $"✓ Đã tải {result.Downloaded}/{_missing.Count} video ({result.NotOnHub} chưa có trên Hub). Bắt đầu Update...";
        else
            StatusText.Text = $"✓ Đã tải {result.Downloaded} video. Bắt đầu Update...";

        BtnOk.Visibility = Visibility.Visible;
        ShouldProceed = true;
        try { await Task.Delay(1200, _cts!.Token); } catch (OperationCanceledException) { }
        Close();
    }

    private void BtnSkip_Click(object sender, RoutedEventArgs e)
    {
        ShouldProceed = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        ShouldProceed = false;
        try { _cts?.Cancel(); } catch { }
        try { Close(); } catch { }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        // Nếu đóng hộp thoại bằng nút [X] khi chưa xong → hủy và không chạy Update
        if (!ShouldProceed)
        {
            try { _cts?.Cancel(); } catch { }
        }
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        try { _externalCtReg?.Dispose(); } catch { }
        try { _cts?.Cancel(); } catch { }
    }
}
