using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace Shopee.Upload.Services;

/// <summary>Hộp thoại của panel xuất (GĐ8). App thật: <see cref="WpfExportPrompts"/>; test tiêm bản giả.</summary>
public interface IExportPrompts
{
    /// <summary>Chọn file Excel nguồn. null = huỷ.</summary>
    string? PickWorkbook(string? current);

    /// <summary>Chọn một hay nhiều file mẫu Đăng Hàng Loạt. Rỗng = huỷ.</summary>
    IReadOnlyList<string> PickTemplates();

    /// <summary>Chọn ảnh bảng size (JPG/PNG). null = huỷ.</summary>
    string? PickImage(string? current);

    /// <summary>Chọn thư mục ghi file xuất. null = huỷ.</summary>
    string? PickFolder(string? current);

    /// <summary>Hỏi Có/Không, kèm cảnh báo.</summary>
    bool Confirm(string title, string message);

    /// <summary>Xác nhận việc không hoàn tác được: chỉ đồng ý khi người dùng gõ đúng <paramref name="requiredText"/>.</summary>
    bool ConfirmTyped(string title, string message, string requiredText);

    /// <summary>Mở thư mục trong Explorer.</summary>
    void OpenFolder(string path);
}

/// <summary>Hộp thoại WPF thật của panel xuất.</summary>
public sealed class WpfExportPrompts : IExportPrompts
{
    private const string Title = "Shopee Upload";

    private static Window? Owner => Application.Current?.MainWindow is { IsVisible: true } w ? w : null;

    public string? PickWorkbook(string? current)
    {
        var d = new OpenFileDialog
        {
            Title = "Chọn file Excel nguồn (link SP, như app Harvest)",
            Filter = "Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|Tất cả|*.*",
            InitialDirectory = ExistingDir(current),
        };
        return Show(d) ? d.FileName : null;
    }

    public IReadOnlyList<string> PickTemplates()
    {
        var d = new OpenFileDialog
        {
            Title = "Chọn file mẫu Đăng Hàng Loạt (bản nâng cao theo ngành, hoặc basic) — chọn được nhiều file",
            Filter = "File mẫu Shopee (*.xlsx)|*.xlsx",
            Multiselect = true,
        };
        return Show(d) ? d.FileNames : [];
    }

    public string? PickImage(string? current)
    {
        var d = new OpenFileDialog
        {
            Title = "Chọn ảnh bảng size dùng chung",
            Filter = "Ảnh (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
            InitialDirectory = ExistingDir(current),
        };
        return Show(d) ? d.FileName : null;
    }

    public string? PickFolder(string? current)
    {
        var d = new OpenFolderDialog { Title = "Chọn thư mục ghi file xuất" };
        if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current)) d.InitialDirectory = current;
        var owner = Owner;
        return (owner is null ? d.ShowDialog() : d.ShowDialog(owner)) == true ? d.FolderName : null;
    }

    public bool Confirm(string title, string message) =>
        (Owner is { } o
            ? MessageBox.Show(o, message, $"{Title} — {title}", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            : MessageBox.Show(message, $"{Title} — {title}", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No))
        == MessageBoxResult.Yes;

    public bool ConfirmTyped(string title, string message, string requiredText)
    {
        var win = new Window
        {
            Title = $"{Title} — {title}", Width = 560, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = Owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = Owner, ShowInTaskbar = false,
        };
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = $"Gõ {requiredText} để xác nhận:", Margin = new Thickness(0, 0, 0, 4) });
        var box = new System.Windows.Controls.TextBox();
        System.Windows.Automation.AutomationProperties.SetName(box, "Ô xác nhận");
        panel.Children.Add(box);
        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var ok = new System.Windows.Controls.Button { Content = "Xoá", IsEnabled = false, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        box.TextChanged += (_, _) => ok.IsEnabled = box.Text.Trim() == requiredText;
        ok.Click += (_, _) => win.DialogResult = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(new System.Windows.Controls.Button { Content = "Huỷ", IsCancel = true, IsDefault = true, MinWidth = 90 });
        panel.Children.Add(buttons);
        win.Content = panel;
        box.Focus();
        return win.ShowDialog() == true && box.Text.Trim() == requiredText;
    }

    public void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch { /* không mở được Explorer — đường dẫn vẫn có trong log */ }
    }

    private static bool Show(CommonDialog d) => (Owner is { } o ? d.ShowDialog(o) : d.ShowDialog()) == true;

    private static string ExistingDir(string? path)
    {
        try
        {
            var dir = string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path.Trim());
            return dir is not null && Directory.Exists(dir) ? dir : "";
        }
        catch { return ""; }
    }
}
