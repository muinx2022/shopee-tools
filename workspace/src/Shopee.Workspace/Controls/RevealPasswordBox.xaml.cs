using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Shopee.Workspace.Controls;

/// <summary>Ô mật khẩu có nút con mắt: bấm để hiện chữ, bấm lần nữa để che lại.</summary>
public partial class RevealPasswordBox : UserControl
{
    public static readonly DependencyProperty PasswordProperty = DependencyProperty.Register(
        nameof(Password), typeof(string), typeof(RevealPasswordBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public string Password
    {
        get => (string)GetValue(PasswordProperty);
        set => SetValue(PasswordProperty, value);
    }

    public RevealPasswordBox() => InitializeComponent();

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        var show = Plain.Visibility != Visibility.Visible;
        Plain.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        Masked.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        EyeIcon.Data = (Geometry)FindResource(show ? "IconEyeOff" : "IconEye");
        Eye.ToolTip = show ? "Ẩn mật khẩu" : "Hiện mật khẩu";
    }
}
