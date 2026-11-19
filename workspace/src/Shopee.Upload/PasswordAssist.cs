using System.Windows;
using System.Windows.Controls;

namespace Shopee.Upload;

/// <summary>
/// Bind được <see cref="PasswordBox.Password"/> (WPF không cho bind thẳng). Dùng cho ô key Kiot: key không bao giờ hiện nguyên trên
/// màn, chỉ <c>…abcd</c> bên cạnh. Cờ <c>_syncing</c> chặn vòng lặp ô ↔ nguồn.
/// </summary>
public static class PasswordAssist
{
    public static readonly DependencyProperty BoundPasswordProperty = DependencyProperty.RegisterAttached(
        "BoundPassword", typeof(string), typeof(PasswordAssist),
        // Mặc định null (không phải ""): nguồn là "" thì vẫn có lần đổi giá trị đầu tiên → kịp gắn PasswordChanged.
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

    private static readonly DependencyProperty SyncingProperty =
        DependencyProperty.RegisterAttached("Syncing", typeof(bool), typeof(PasswordAssist), new PropertyMetadata(false));

    private static readonly DependencyProperty HookedProperty =
        DependencyProperty.RegisterAttached("Hooked", typeof(bool), typeof(PasswordAssist), new PropertyMetadata(false));

    public static string? GetBoundPassword(DependencyObject d) => (string?)d.GetValue(BoundPasswordProperty);

    public static void SetBoundPassword(DependencyObject d, string? value) => d.SetValue(BoundPasswordProperty, value);

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;
        if (!(bool)box.GetValue(HookedProperty))
        {
            box.PasswordChanged += OnPasswordChanged;
            box.SetValue(HookedProperty, true);
        }
        if ((bool)box.GetValue(SyncingProperty)) return;
        var value = e.NewValue as string ?? "";
        if (box.Password != value)
        {
            box.SetValue(SyncingProperty, true);
            box.Password = value;
            box.SetValue(SyncingProperty, false);
        }
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        if ((bool)box.GetValue(SyncingProperty)) return;
        box.SetValue(SyncingProperty, true);
        SetBoundPassword(box, box.Password);
        box.SetValue(SyncingProperty, false);
    }
}
