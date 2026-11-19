using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace Shopee.Upload.Import;

/// <summary>Hộp thoại WPF thật của hai nút nhập (GĐ5c). Dựng bằng code — chỉ là bảng + vài nút, theo theme của app.</summary>
public sealed class WpfImportPrompts : IImportPrompts
{
    private const string Title = "Shopee Upload";

    private static Window? Owner => Application.Current?.MainWindow is { IsVisible: true } w ? w : null;

    public bool ConfirmShops(ShopImportPlan plan)
    {
        var win = NewDialog("Nhập shop từ BigSeller", 860, 600);
        var root = new DockPanel { Margin = new Thickness(16) };

        var head = new TextBlock
        {
            Text = $"Sẽ thêm {plan.ToAdd.Count} shop (mã tự sinh) · đã có {plan.Existing.Count}" +
                   (plan.Possible.Count > 0 ? $" · có thể đã có (trùng tên, KHÔNG thêm) {plan.Possible.Count}" : "") +
                   (plan.Skipped.Count > 0 ? $" · bỏ qua {plan.Skipped.Count}" : "") +
                   (plan.WorkbookFills.Count > 0 ? $" · điền file Excel nguồn cho {plan.WorkbookFills.Count} shop đã có (ô đang trống)" : "") +
                   ". Bấm Thêm chỉ thêm vào danh sách — CHƯA lưu, kiểm rồi bấm Lưu như thường.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button
        {
            Content = plan.ToAdd.Count > 0 ? $"Thêm {plan.ToAdd.Count} shop"
                : plan.WorkbookFills.Count > 0 ? $"Điền file Excel cho {plan.WorkbookFills.Count} shop" : "Không có shop mới",
            IsDefault = true, IsEnabled = plan.HasChanges, MinWidth = 120, Margin = new Thickness(0, 0, 8, 0),
        };
        ok.Click += (_, _) => win.DialogResult = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Huỷ", IsCancel = true, MinWidth = 90 });
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var lists = new Grid();
        lists.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        lists.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
        lists.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        lists.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        AddSection(lists, 0, $"Sẽ thêm ({plan.ToAdd.Count})", "Sẽ thêm", plan.ToAdd, existing: false);
        AddSection(lists, 2, $"Đã có, có thể đã có hoặc bỏ qua ({plan.Existing.Count + plan.Possible.Count + plan.Skipped.Count})", "Đã có",
            [.. plan.Possible, .. plan.Existing, .. plan.Skipped], existing: true);
        root.Children.Add(lists);

        win.Content = root;
        return win.ShowDialog() == true;
    }

    private static void AddSection(Grid grid, int row, string header, string automationName, IReadOnlyList<ShopImportItem> items, bool existing)
    {
        var h = new TextBlock { Text = header, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, row == 0 ? 0 : 10, 0, 4) };
        Grid.SetRow(h, row);
        grid.Children.Add(h);

        // Cột co giãn theo bề rộng bảng (trọng số), không có thanh cuộn ngang ở cỡ cửa sổ nào.
        var view = new GridView();
        var weights = new List<double>();
        void Add(string h, string path, double w) { view.Columns.Add(Col(h, path, w * 100)); weights.Add(w); }
        Add("Tài khoản BigSeller", nameof(ShopImportItem.AccountDisplay), 2.2);
        Add("Shop (nhãn)", nameof(ShopImportItem.ShopName), 2.2);
        Add("Sheet", nameof(ShopImportItem.Sheet), 1.6);
        if (existing)
        {
            Add("Đã có ở tài khoản", nameof(ShopImportItem.ExistingName), 1.8);
            Add("Bỏ qua vì", nameof(ShopImportItem.Note), 2.2);
        }
        var list = new ListView { View = view, ItemsSource = items };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.SizeChanged += (_, _) =>
        {
            // Trừ viền + thanh cuộn dọc (luôn chừa chỗ để bảng có thêm dòng không bị tràn).
            var avail = list.ActualWidth - SystemParameters.VerticalScrollBarWidth - 12;
            if (avail <= 0) return;
            var total = weights.Sum();
            for (var i = 0; i < view.Columns.Count; i++) view.Columns[i].Width = Math.Floor(avail * weights[i] / total);
        };
        System.Windows.Automation.AutomationProperties.SetName(list, automationName);
        Grid.SetRow(list, row + 1);
        grid.Children.Add(list);
    }

    /// <summary>Cột chữ xuống dòng khi hẹp (tên shop / lý do bỏ qua dài vẫn đọc đủ, không bị cắt).</summary>
    private static GridViewColumn Col(string header, string path, double width)
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(path));
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        return new GridViewColumn { Header = header, Width = width, CellTemplate = new DataTemplate { VisualTree = text } };
    }

    public string? PickPasswordFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Chọn file Excel mật khẩu shop (.xlsx)",
            Filter = "Excel (*.xlsx)|*.xlsx",
            CheckFileExists = true,
        };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    }

    public OverwriteChoice AskOverwrite(int accounts, int fields)
    {
        var win = NewDialog("Ô đã có giá trị khác", 520, 230);
        var choice = OverwriteChoice.Cancel;
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = $"{accounts} tài khoản có {fields} ô (tài khoản / mật khẩu / email) đang có giá trị KHÁC giá trị trong file.\n" +
                   "Chọn một cách cho cả lượt nhập:",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        void Add(string text, OverwriteChoice c, bool isDefault = false, bool isCancel = false)
        {
            var b = new Button { Content = text, MinWidth = 120, Margin = new Thickness(8, 0, 0, 0), IsDefault = isDefault, IsCancel = isCancel };
            b.Click += (_, _) => { choice = c; win.DialogResult = c != OverwriteChoice.Cancel; };
            buttons.Children.Add(b);
        }
        Add("Ghi đè tất cả", OverwriteChoice.OverwriteAll);
        Add("Chỉ điền ô trống", OverwriteChoice.FillEmptyOnly, isDefault: true);
        Add("Huỷ", OverwriteChoice.Cancel, isCancel: true);
        root.Children.Add(buttons);
        win.Content = root;
        win.ShowDialog();
        return choice;
    }

    public void ShowSummary(string title, string text, bool isError = false)
    {
        var icon = isError ? MessageBoxImage.Warning : MessageBoxImage.Information;
        if (Owner is { } o) MessageBox.Show(o, text, $"{Title} — {title}", MessageBoxButton.OK, icon);
        else MessageBox.Show(text, $"{Title} — {title}", MessageBoxButton.OK, icon);
    }

    private static Window NewDialog(string title, double width, double height)
    {
        var w = new Window
        {
            Title = $"{Title} — {title}",
            Width = width,
            Height = height,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.CanResize,
            WindowStartupLocation = Owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = Owner,
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBackgroundBrush");
        w.SetResourceReference(Control.FontFamilyProperty, "UiFont");
        w.FontSize = 12.5;
        return w;
    }
}
