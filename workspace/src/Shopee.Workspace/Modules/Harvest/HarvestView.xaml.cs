using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Shopee.Workspace.Infrastructure;

namespace Shopee.Workspace.Modules.Harvest;

public partial class HarvestView : UserControl
{
    public HarvestView()
    {
        InitializeComponent();
        // Bấm 1 dòng tiến trình → đưa cửa sổ Brave của process đó lên trước (như màn Workspace của app Scrape).
        InstancesGrid.AddHandler(PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(OnInstanceClick), handledEventsToo: true);
    }

    private void OnInstanceClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not HarvestViewModel vm) return;
        var row = VisualTreeSearch.FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.DataContext is Scrape.ScrapeInstanceViewModel inst)
            vm.Scrape.BringInstanceToFront(inst);
    }

    // ── Ô "Từ dòng / Đến dòng" trong bảng sheet ──────────────────────────────────────────────────────────────────────

    /// <summary>Áp số đang gõ trong ô vào VM (luật kẹp / báo lỗi của <see cref="HarvestSheetRowViewModel"/>) rồi cho ô hiện lại đúng giá
    /// trị VM giữ (đã kẹp, hoặc dải cũ khi nhập sai). Ô trống → trả về giá trị đang áp.</summary>
    internal static void CommitRowBox(TextBox box)
    {
        var binding = box.GetBindingExpression(TextBox.TextProperty);
        if (binding is null) return;
        if (string.IsNullOrWhiteSpace(box.Text)) binding.UpdateTarget();
        else binding.UpdateSource();
        binding.UpdateTarget();
    }

    /// <summary>Enter = áp ngay, ở lại ô (DataGrid KHÔNG được nhảy xuống dòng dưới); Tab / Shift+Tab = áp rồi sang ô nhập kế / trước
    /// (ô DataGrid không nhận focus — <c>rowBoxCell</c>), không để DataGrid tự đổi ô hiện hành.</summary>
    private void RowBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        if (e.Key is Key.Enter or Key.Return)
        {
            CommitRowBox(box);
            box.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            CommitRowBox(box);
            var back = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            box.MoveFocus(new TraversalRequest(back ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }

    /// <summary>Ô dòng chỉ nhận chữ số (dán / gõ chữ khác bị bỏ, khỏi lỗi chuyển kiểu viền đỏ).</summary>
    private void RowBox_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsAsciiDigit);

    /// <summary>Vào ô của một sheet → dòng đó thành dòng đang chọn (nút "Áp dải cho sheet đã tick" lấy dải của đúng sheet đang sửa) và
    /// bôi sẵn số để gõ đè.</summary>
    private void RowBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox box) return;
        if (VisualTreeSearch.FindAncestor<DataGridRow>(box) is { IsSelected: false } row) row.IsSelected = true;
        box.SelectAll();
    }
}
