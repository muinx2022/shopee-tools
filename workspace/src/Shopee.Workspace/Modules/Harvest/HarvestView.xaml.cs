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
}
