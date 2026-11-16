using OpenMultiBraveLauncherV3;

namespace Shopee.Modules.MultiBrave;

/// <summary>Khởi tạo/dọn dẹp runtime của engine v31 (cấp port block, thư mục session). Gọi 1 lần
/// lúc app khởi động và lúc thoát.</summary>
public static class MultiBraveRuntime
{
    public static void Initialize() => ScrapeSession.Current.Initialize();
    public static void Cleanup() => ScrapeSession.Current.Cleanup();
    public static void Shutdown() => Cleanup();

    /// <summary>Chế độ của CẢ process (extension nạp vào Brave, probe, đọc tiến độ extension). Mặc định
    /// <see cref="RunnerMode.Scrape"/> = hành vi cũ. Các tra cứu extension tĩnh của engine (tìm thư mục/ID extension,
    /// biểu thức probe) đọc giá trị này vì không có cấu hình phiên đi kèm.</summary>
    public static RunnerMode Mode { get; private set; } = RunnerMode.Scrape;

    /// <summary>Đặt chế độ process — gọi MỘT lần lúc khởi động (exe Harvest), trước khi dựng <see cref="ScrapeRunner"/>
    /// và mở Brave. Đổi giữa chừng khi phiên đang chạy là sai cách dùng (phiên cũ đang giữ extension của chế độ cũ).</summary>
    public static void SetMode(RunnerMode mode) => Mode = mode;
}
