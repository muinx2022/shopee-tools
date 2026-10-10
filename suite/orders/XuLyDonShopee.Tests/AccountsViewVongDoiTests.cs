using System;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.App.Views;

namespace XuLyDonShopee.Tests;

/// <summary>
/// Vòng đời đăng ký của <see cref="AccountsView"/> vào <see cref="AccountsViewModel"/>.
/// <para>
/// <b>Bối cảnh (phản biện 18/08/2026).</b> <c>MainView</c> đặt màn con bằng <c>ContentControl</c> +
/// <c>DataTemplate</c>, nên mỗi lượt vào màn Tài khoản là một <c>AccountsView</c> MỚI, còn VM thì sống suốt đời
/// app. View cũ bị vứt mà KHÔNG hề đổi <c>DataContext</c> ⇒ nếu chỉ gỡ đăng ký trong <c>DataContextChanged</c>
/// thì nó không bao giờ chạy, VM giữ view chết sống mãi, và cứ mỗi nhịp gom log (~250ms) cả đám view chết lại
/// cùng nhau đi cuộn một <c>LogBox</c> đã tháo khỏi cây.
/// </para>
/// <para>
/// Đúng lỗi này từng được vá cho <c>OrdersView</c> (commit 2957563) nhưng bỏ sót <c>AccountsView</c>. Test này
/// là cái lưới còn thiếu.
/// </para>
/// <para>
/// <b>Vì sao bắn tay <c>LoadedEvent</c>/<c>UnloadedEvent</c> thay vì dựng <c>Window</c> thật:</b> cái cần khoá ở
/// đây là TÍNH ĐỐI XỨNG đăng ký ↔ gỡ đăng ký của chính view, không phải việc WPF có bắn hai sự kiện đó đúng lúc
/// hay không (điều đó do khung lo, và <c>OrdersView</c> đã dựa vào y hệt). Bắn tay thì test tất định, không cần
/// hiện cửa sổ, không treo.
/// </para>
/// </summary>
public class AccountsViewVongDoiTests
{
    /// <summary>
    /// Đếm số người đang nghe <c>PropertyChanged</c> của VM. Sự kiện field-like của <c>ObservableObject</c> nên
    /// đọc thẳng backing field — không có đường công khai nào đếm được, mà chính CON SỐ này mới là thứ phân biệt
    /// "đã gỡ" với "tưởng đã gỡ".
    /// </summary>
    private static int SoNguoiNghe(INotifyPropertyChanged vm)
    {
        for (var t = vm.GetType(); t is not null; t = t.BaseType)
        {
            var f = t.GetField("PropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            if (f is null)
            {
                continue;
            }

            return f.GetValue(vm) is Delegate d ? d.GetInvocationList().Length : 0;
        }

        throw new InvalidOperationException("Không tìm được backing field PropertyChanged — đổi base class của VM?");
    }

    private static (TempDatabase Temp, AccountsViewModel Vm) DungVm()
    {
        var temp = new TempDatabase();
        return (temp, new AccountsViewModel(new AppServices(temp.Path)));
    }

    private static void VaoCay(FrameworkElement v) => v.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

    private static void RoiCay(FrameworkElement v) => v.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

    /// <summary>Vào cây thì bám VM, rời cây thì NHẢ ra — về đúng mức nền.</summary>
    [StaFact]
    public void VaoCayThiBamVm_RoiCayThiNha()
    {
        var (temp, vm) = DungVm();
        using var db = temp;
        var nen = SoNguoiNghe(vm);

        var view = new AccountsView { DataContext = vm };
        VaoCay(view);
        Assert.Equal(nen + 1, SoNguoiNghe(vm));

        RoiCay(view);
        Assert.Equal(nen, SoNguoiNghe(vm));
    }

    /// <summary>
    /// CA CHÍNH — đi qua đi lại nhiều lượt (Tài khoản → Thống kê → Tài khoản → …), mỗi lượt một view MỚI đúng
    /// như <c>DataTemplate</c> làm. Số người nghe phải về đúng mức nền, KHÔNG leo theo số lượt.
    /// </summary>
    [StaFact]
    public void DiQuaDiLaiNhieuLuot_SoNguoiNgheKhongLeo()
    {
        var (temp, vm) = DungVm();
        using var db = temp;
        var nen = SoNguoiNghe(vm);

        for (var luot = 0; luot < 5; luot++)
        {
            var view = new AccountsView { DataContext = vm };
            VaoCay(view);
            RoiCay(view);
        }

        Assert.Equal(nen, SoNguoiNghe(vm));
    }

    /// <summary>
    /// Gắn <c>DataContext</c> SAU khi đã vào cây (thứ tự thật của <c>DataTemplate</c> đôi khi là vậy) vẫn phải
    /// bám đúng MỘT lần — không đăng ký đôi, vì cả Loaded lẫn DataContextChanged đều dẫn vào cùng một chỗ.
    /// </summary>
    [StaFact]
    public void GanDataContextSauKhiVaoCay_ChiDangKyMotLan()
    {
        var (temp, vm) = DungVm();
        using var db = temp;
        var nen = SoNguoiNghe(vm);

        var view = new AccountsView();
        VaoCay(view);
        view.DataContext = vm;

        Assert.Equal(nen + 1, SoNguoiNghe(vm));

        RoiCay(view);
        Assert.Equal(nen, SoNguoiNghe(vm));
    }

    /// <summary>
    /// View bị vứt TRƯỚC khi kịp vào cây thì không được bám vào VM chút nào — không có <c>Unloaded</c> nào tới
    /// gỡ hộ nó.
    /// </summary>
    [StaFact]
    public void ChuaVaoCay_ThiKhongBamVm()
    {
        var (temp, vm) = DungVm();
        using var db = temp;
        var nen = SoNguoiNghe(vm);

        _ = new AccountsView { DataContext = vm };

        Assert.Equal(nen, SoNguoiNghe(vm));
    }

    /// <summary>Đổi sang VM khác lúc đang ở trong cây: nhả VM cũ, bám VM mới.</summary>
    [StaFact]
    public void DoiSangVmKhac_ThiNhaVmCu()
    {
        var (temp1, vm1) = DungVm();
        using var db1 = temp1;
        var (temp2, vm2) = DungVm();
        using var db2 = temp2;
        var nen1 = SoNguoiNghe(vm1);
        var nen2 = SoNguoiNghe(vm2);

        var view = new AccountsView { DataContext = vm1 };
        VaoCay(view);
        view.DataContext = vm2;

        Assert.Equal(nen1, SoNguoiNghe(vm1));
        Assert.Equal(nen2 + 1, SoNguoiNghe(vm2));
    }
}
