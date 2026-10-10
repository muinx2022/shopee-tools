using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;

namespace XuLyDonShopee.Tests;

/// <summary>
/// Cờ Cài đặt "Cho phép mọi tài khoản chạy qua IP máy này": mặc định TẮT; đổi → lưu ngay;
/// Reload / khởi tạo lại VM đọc đúng bản đã lưu (không ghi ngược lúc nạp).
/// </summary>
public class SettingsViewModelChoPhepIpMayTests
{
    [Fact]
    public void ChoPhepChayTatCaAccQuaIpMay_MacDinhTat_DoiThiLuuNgay()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);

        var vm = new SettingsViewModel(services);
        Assert.False(vm.ChoPhepChayTatCaAccQuaIpMay);
        Assert.False(services.Settings.GetChoPhepChayTatCaAccQuaIpMay());

        vm.ChoPhepChayTatCaAccQuaIpMay = true;
        Assert.True(services.Settings.GetChoPhepChayTatCaAccQuaIpMay());

        vm.ChoPhepChayTatCaAccQuaIpMay = false;
        Assert.False(services.Settings.GetChoPhepChayTatCaAccQuaIpMay());
    }

    [Fact]
    public void ChoPhepChayTatCaAccQuaIpMay_NapTuSettings_KhiKhoiTaoVaReload()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        services.Settings.SetChoPhepChayTatCaAccQuaIpMay(true);

        var vm = new SettingsViewModel(services);
        Assert.True(vm.ChoPhepChayTatCaAccQuaIpMay);

        vm.Reload();
        Assert.True(vm.ChoPhepChayTatCaAccQuaIpMay);
        Assert.True(services.Settings.GetChoPhepChayTatCaAccQuaIpMay());
    }
}
