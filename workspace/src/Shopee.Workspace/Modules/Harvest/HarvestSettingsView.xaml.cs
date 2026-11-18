using System.Windows.Controls;

namespace Shopee.Workspace.Modules.Harvest;

public partial class HarvestSettingsView : UserControl
{
    public HarvestSettingsView()
    {
        InitializeComponent();
        // Mở màn (kể cả từ nút "Cài đặt Hub" ở màn Lấy dữ liệu) → đứng ở đầu màn = nhóm Kết nối Hub.
        Loaded += (_, _) => Scroll.ScrollToTop();
    }
}
