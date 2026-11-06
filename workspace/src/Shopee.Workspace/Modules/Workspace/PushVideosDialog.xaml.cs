using System.Windows;
using Shopee.Workspace.Infrastructure;

namespace Shopee.Workspace.Modules.Workspace;

/// <summary>Lựa chọn của hộp "Đẩy video lên hub". Đóng bằng X, Esc hoặc nút Hủy đều giữ <see cref="Cancel"/>.</summary>
public enum PushVideosChoice { Cancel, Missing, All }

/// <summary>
/// Hỏi một lần: đẩy phần video hub thiếu/khác, hay đẩy lại toàn bộ. Người gọi mở qua <c>WindowHost.ShowDialogAsync</c>,
/// rồi <c>await dialog.WaitClosedAsync()</c>, rồi mới đọc <see cref="Choice"/>. Không dựa vào giá trị trả về của ShowDialog,
/// vì khi không có cửa sổ chủ thì <c>WindowHost</c> mở không modal và trả về ngay.
/// </summary>
public partial class PushVideosDialog : Window
{
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PushVideosDialog(int total, int missing)
    {
        InitializeComponent();
        this.FitOnOpen();

        SummaryText.Text = $"Thư mục có {total} video trong 30 ngày qua. Hub đang thiếu hoặc khác {missing} video.";
        MissingLabel.Text = $"Chỉ đẩy {missing} video thiếu/khác";
        AllLabel.Text = $"Đẩy lại toàn bộ {total} video";
        MissingButton.IsEnabled = missing > 0;
        AllButton.IsEnabled = total > 0;
    }

    public PushVideosChoice Choice { get; private set; } = PushVideosChoice.Cancel;

    /// <summary>Hoàn thành khi cửa sổ đã đóng (X, Esc, Hủy, hoặc nút chọn).</summary>
    public Task WaitClosedAsync() => _closed.Task;

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _closed.TrySetResult();
    }

    private void OnMissing(object sender, RoutedEventArgs e)
    {
        Choice = PushVideosChoice.Missing;
        Close();
    }

    private void OnAll(object sender, RoutedEventArgs e)
    {
        Choice = PushVideosChoice.All;
        Close();
    }
}
