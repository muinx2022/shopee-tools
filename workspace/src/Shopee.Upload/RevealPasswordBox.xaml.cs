using System.Windows;
using System.Windows.Controls;

namespace Shopee.Upload;

/// <summary>
/// Ô mật khẩu có nút con mắt hiện/ẩn. <see cref="Password"/> bind hai chiều như một ô thường; <see cref="IsRevealed"/> chỉ là trạng
/// thái màn hình — không bind ra ViewModel, không lưu file, mặc định ẩn, đổi DataContext (đổi tài khoản đang chọn) thì tự ẩn lại.
/// Giá trị không bao giờ đi vào tooltip/automation name; ẩn lại thì TextBox bị xoá trắng.
/// </summary>
public partial class RevealPasswordBox : UserControl
{
    public static readonly DependencyProperty PasswordProperty = DependencyProperty.Register(
        nameof(Password), typeof(string), typeof(RevealPasswordBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordChanged));

    public static readonly DependencyProperty IsRevealedProperty = DependencyProperty.Register(
        nameof(IsRevealed), typeof(bool), typeof(RevealPasswordBox), new PropertyMetadata(false, OnIsRevealedChanged));

    /// <summary>true = đang tự đẩy chữ vào TextBox — TextChanged lúc đó không được ghi ngược về <see cref="Password"/>.</summary>
    private bool _syncing;

    public RevealPasswordBox()
    {
        InitializeComponent();
        Shown.TextChanged += OnShownTextChanged;
        DataContextChanged += (_, _) => SetCurrentValue(IsRevealedProperty, false);
        Unloaded += (_, _) => SetCurrentValue(IsRevealedProperty, false);
    }

    public string? Password
    {
        get => (string?)GetValue(PasswordProperty);
        set => SetValue(PasswordProperty, value);
    }

    public bool IsRevealed
    {
        get => (bool)GetValue(IsRevealedProperty);
        set => SetValue(IsRevealedProperty, value);
    }

    private static void OnPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (RevealPasswordBox)d;
        if (!box.IsRevealed) return;
        var value = e.NewValue as string ?? "";
        if (box.Shown.Text != value) box.SetShownText(value);
    }

    private static void OnIsRevealedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (RevealPasswordBox)d;
        var revealed = (bool)e.NewValue;
        var hadFocus = box.Hidden.IsKeyboardFocusWithin || box.Shown.IsKeyboardFocusWithin;

        box.SetShownText(revealed ? box.Password ?? "" : "");
        box.Shown.Visibility = revealed ? Visibility.Visible : Visibility.Collapsed;
        box.Hidden.Visibility = revealed ? Visibility.Collapsed : Visibility.Visible;

        // Đang gõ dở mà bấm mắt (phím tắt/Space) thì con trỏ đi theo sang ô đang hiện.
        if (!hadFocus) return;
        if (revealed)
        {
            box.Shown.Focus();
            box.Shown.CaretIndex = box.Shown.Text.Length;
        }
        else box.Hidden.Focus();
    }

    private void SetShownText(string value)
    {
        _syncing = true;
        Shown.Text = value;
        _syncing = false;
    }

    private void OnShownTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || !IsRevealed) return;
        SetCurrentValue(PasswordProperty, Shown.Text);
    }
}
