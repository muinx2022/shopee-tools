using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shopee.Workspace.Modules.Settings;
using Shopee.Workspace.Services;

namespace Shopee.Workspace.Modules.Harvest;

/// <summary>
/// Màn "Cài đặt" DUY NHẤT của app Harvest, chia nhóm: Kết nối Hub (đầu tiên) · Hiệu năng · Thu phóng giao diện · Kho tài khoản
/// Shopee &amp; Proxy · Extension. KHÔNG có AI (Harvest không dùng). App Scrape / Update vẫn dùng màn dùng chung
/// <see cref="LocalSettingsViewModel"/> nguyên vẹn (có AI) — màn này chỉ MƯỢN phần hiệu năng / thu phóng / kho tài khoản của nó.
/// <para>Hiệu năng giữ vì CÓ tác dụng ở Harvest: CPU / RAM → <c>BraveFleet.MaxConcurrentWindows</c> = trần số cửa sổ Brave mở cùng
/// lúc của cả app (<c>BraveFleet</c> chờ slot khi đạt trần) — lượt Harvest mở Brave qua đúng đường đó.</para>
/// </summary>
public sealed partial class HarvestSettingsViewModel : ObservableObject
{
    public HarvestHubViewModel Hub { get; }

    /// <summary>Hiệu năng · thu phóng · kho tài khoản (dùng lại VM của màn chung; phần AI của nó không hiện ở Harvest).</summary>
    public LocalSettingsViewModel General { get; }

    public HarvestSettingsViewModel(HarvestHubViewModel? hub = null, LocalSettingsViewModel? general = null, string? extensionDir = null)
    {
        Hub = hub ?? new HarvestHubViewModel();
        General = general ?? new LocalSettingsViewModel();
        ExtensionDir = extensionDir ?? DefaultExtensionDir();
    }

    /// <summary><c>extensions\shopee-harvest</c> cạnh exe — bản extension app nạp vào Brave; chép thư mục này sang máy khác để
    /// "Load unpacked".</summary>
    public static string DefaultExtensionDir() => Path.Combine(AppContext.BaseDirectory, "extensions", "shopee-harvest");

    public string ExtensionDir { get; }
    public bool ExtensionDirExists => Directory.Exists(ExtensionDir);

    public string ExtensionHint =>
        "Cài lên Brave / Chrome của máy khác: chép thư mục này sang máy đó → mở brave://extensions, bật Chế độ nhà phát triển → "
        + "\"Tải tiện ích đã giải nén\" (Load unpacked) chọn thư mục → mở Cài đặt (Tùy chọn) của extension, nhập URL + token Hub rồi Lưu.";

    [RelayCommand]
    private void OpenExtensionFolder()
    {
        if (ExtensionDirExists) ShellOpener.OpenFolder(ExtensionDir);
    }
}
