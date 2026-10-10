using CommunityToolkit.Mvvm.ComponentModel;
using Shopee.Core.Accounts;
using Shopee.Core.BigSeller;
using Shopee.Core.Proxy;
using Shopee.Workspace.Services;

namespace Shopee.Workspace.ViewModels;

/// <summary>
/// ViewModel đoạn "Workspace" của footer trạng thái cấp shell: counter chỉ ĐỌC-HIỂN THỊ từ các kho dùng
/// chung (TK BigSeller · shop · acc Shopee · proxy · trình duyệt). Không thêm nghiệp vụ; chỉ đếm.
/// </summary>
public sealed partial class WorkspaceStatusViewModel : ObservableObject
{
    [ObservableProperty] private string _bigSellerText = "";
    [ObservableProperty] private string _shopText = "";
    [ObservableProperty] private string _shopeeAccountText = "";
    [ObservableProperty] private string _proxyText = "";
    [ObservableProperty] private string _browserText = "Trình duyệt: Brave";
    public string MachineText => $"{ShopeeAccountText} · {ProxyText}";

    public WorkspaceStatusViewModel()
    {
        Refresh();
        BigSellerStore.Shared.Changed += () => UiThread.Post(Refresh);
        AccountStore.Shared.Changed += () => UiThread.Post(Refresh);
        KiotProxyPoolStore.Shared.Changed += () => UiThread.Post(Refresh);
    }

    /// <summary>Đọc lại số liệu từ các kho dùng chung. Best-effort.</summary>
    public void Refresh()
    {
        try
        {
            var bigSellers = BigSellerStore.Shared.Accounts;
            var shopCount = bigSellers.Sum(a => a.Shops.Count);
            BigSellerText = $"{bigSellers.Count} tài khoản BigSeller";
            ShopText = $"{shopCount} shop";
            ShopeeAccountText = $"{AccountStore.Shared.Accounts.Count} acc Shopee";
            ProxyText = $"{KiotProxyPoolStore.Shared.Count} proxy";
            OnPropertyChanged(nameof(MachineText));
        }
        catch { }
    }
}
