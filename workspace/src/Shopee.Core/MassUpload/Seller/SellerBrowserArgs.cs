using BraveArgs = Shopee.Toolkit.Browser.BraveArgs;

namespace Shopee.Core.MassUpload.Seller;

/// <summary>
/// Tham số dòng lệnh mở Brave cho một shop (hàm thuần, plan GĐ4 bước 2). Hướng "mở sạch" của app check đơn
/// (<c>suite/orders/…/BraveLaunchArgs.BuildCleanPocArgs</c>): KHÔNG <c>--remote-debugging-port</c>, KHÔNG CDP — extension tự lái
/// trang. Ngoại lệ duy nhất: chế độ <see cref="SellerLaunchMode.Login"/> (GĐ5b) thêm <see cref="CdpFlag"/>. Dựng bằng <see cref="BraveArgs"/> chế độ DANH SÁCH (gộp <c>--disable-features</c> + tự chặn tải model AI on-device).
/// <para>Cờ RIÊNG của luồng này, so với bản check đơn:</para>
/// <list type="bullet">
/// <item><c>--proxy-server</c> BẮT BUỘC (thiếu proxy là ném, không bao giờ để trống — plan: không đi IP máy). Không mang user/pass:
/// Chrome bỏ qua user:pass trong cờ này; xác thực đi qua extension proxy-auth (<see cref="ProxyAuthExtension"/>).</item>
/// <item><c>--proxy-bypass-list</c> chỉ loopback (<c>localhost;127.0.0.1;[::1]</c>, như <c>BrowserLauncher</c>) — đúng các địa
/// chỉ Chromium vốn tự bỏ qua proxy; ghi rõ để không ai thêm gì khác. Không dùng <c>&lt;-loopback&gt;</c>: nó đẩy cả loopback qua
/// proxy, làm hỏng kênh WebSocket localhost của extension nghiệp vụ (GĐ3) mà không che thêm được gì (loopback không ra mạng).</item>
/// <item>Chặn rò IP qua WebRTC: <c>--webrtc-ip-handling-policy=disable_non_proxied_udp</c> (UDP không đi được qua proxy thì không
/// gửi). Đo trên Brave 157.1.99.57: cờ này → 0 ứng viên ICE, STUN không nhận gói; còn <c>--force-webrtc-ip-handling-policy</c>
/// KHÔNG có trong chrome.dll của Brave (bị bỏ qua, lộ srflx IP thật) — đừng đổi sang cờ đó. Lớp 2: cùng chính sách ghi vào
/// <c>Default\Preferences</c> (<see cref="SellerProfilePrefs"/>).</item>
/// <item>URL mở đầu mặc định <c>about:blank</c>, KHÔNG phải Shopee: kiểm IP hai lớp (plan "Chốt thiết kế sau GĐ0" mục 6) — lớp
/// .NET ở <see cref="ExitIpVerifier"/> trước khi mở, lớp trong trình duyệt do extension GĐ3 làm rồi MỚI điều hướng sang
/// <see cref="SellerHomeUrl"/>. Mở thẳng Shopee thì request đầu tới banhang.shopee.vn đi trước lớp kiểm thứ hai.</item>
/// <item><c>--disable-quic</c>: không mở QUIC/UDP nào tới site; mọi kết nối là TCP qua proxy.</item>
/// <item>KHÔNG dùng <c>--disable-background-mode</c> / <c>--no-restore-last-session</c>: hai cờ này không tồn tại trong Brave 157
/// (đo trên chrome.dll). Không khôi phục tab cũ là nhờ dọn session ở bước chuẩn bị profile.</item>
/// </list>
/// Giữ của bản check đơn: chống bóp tài nguyên nền, <c>DisableLoadExtensionCommandLineSwitch</c> (Brave 137+ mới nạp
/// <c>--load-extension</c>), <c>--disable-gpu-process-crash-limit</c>, <c>--lang=vi-VN</c>; thêm <see cref="BraveArgs.DiskCacheLimit"/>
/// (có <c>--disable-component-update</c>) như mọi đường phóng phía workspace.
/// </summary>
/// <summary>Chế độ mở Brave của một shop.</summary>
public enum SellerLaunchMode
{
    /// <summary>Mặc định: KHÔNG CDP — <see cref="SellerBraveProcesses.CheckMain"/> từ chối mọi cờ CDP.</summary>
    Normal,

    /// <summary>GĐ5b: thêm <see cref="SellerBrowserArgs.CdpFlag"/> để Playwright nối vào CHỈ trong lúc đăng nhập (như app check đơn).
    /// Mọi luật khác của <see cref="Normal"/> giữ nguyên.</summary>
    Login,
}

public static class SellerBrowserArgs
{
    /// <summary>URL mở đầu mặc định — trang trống, chưa chạm Shopee.</summary>
    public const string StartUrl = "about:blank";

    /// <summary>Trang Seller Centre — extension GĐ3 điều hướng tới đây sau khi kiểm IP trong trình duyệt.</summary>
    public const string SellerHomeUrl = "https://banhang.shopee.vn/";

    public const string WebRtcPolicy = "disable_non_proxied_udp";

    /// <summary>Cờ WebRTC đúng của Brave (xem ghi chú đầu lớp). Launcher kiểm lại cờ này trên tiến trình chính sau khi mở.</summary>
    public const string WebRtcFlag = "--webrtc-ip-handling-policy=" + WebRtcPolicy;

    public const string ProxyBypassList = "localhost;127.0.0.1;[::1]";

    /// <summary>Cờ CDP của chế độ <see cref="SellerLaunchMode.Login"/>: cổng 0 = Chromium tự chọn cổng trống, ghi vào
    /// <c>&lt;profile&gt;\DevToolsActivePort</c> (cách của <c>suite/orders/…/LoginBrowserBootstrap</c>). Không kèm
    /// <c>--remote-debugging-address</c>: Brave có giao diện chỉ mở CDP trên 127.0.0.1 (cờ address chỉ có tác dụng ở headless);
    /// launcher vẫn kiểm lại cổng chỉ nghe loopback sau khi mở.</summary>
    public const string CdpFlag = "--remote-debugging-port=0";

    private static readonly string[] KhongBopTaiNguyenNen =
    [
        "--disable-background-timer-throttling",
        "--disable-backgrounding-occluded-windows",
        "--disable-renderer-backgrounding",
    ];

    /// <summary>Dựng danh sách tham số (mỗi phần tử một tham số, KHÔNG bọc ngoặc). Ném nếu thiếu proxy / profile, proxy sai,
    /// SOCKS5 có mật khẩu (Brave không xác thực được SOCKS5), hay đường dẫn extension có dấu phẩy (phá danh sách
    /// <c>--load-extension</c>). <paramref name="mode"/> = <see cref="SellerLaunchMode.Login"/> thì thêm đúng một cờ
    /// <see cref="CdpFlag"/>; chế độ thường KHÔNG có cờ CDP nào.</summary>
    public static IReadOnlyList<string> Build(string userDataDir, ProxyLease proxy, IReadOnlyList<string>? extensionDirs,
        string startUrl = StartUrl, SellerLaunchMode mode = SellerLaunchMode.Normal)
    {
        if (string.IsNullOrWhiteSpace(userDataDir)) throw new ArgumentException("Thiếu thư mục profile.", nameof(userDataDir));
        if (proxy is null) throw new ArgumentNullException(nameof(proxy), "Không có proxy thì không mở Brave.");
        if (string.IsNullOrWhiteSpace(proxy.Host) || proxy.Port is < 1 or > 65535)
            throw new ArgumentException($"Proxy không hợp lệ: {proxy}.", nameof(proxy));
        // Chốt cuối (GĐ5d vòng 2): Brave gặp --proxy-server sai (ký tự vô hình, chữ số toàn khổ…) thì BỎ proxy, đi thẳng IP máy.
        // Câu lỗi không in host (có thể chứa ký tự lạ khó nhìn).
        if (!ProxyHostRules.IsSafeForBrowser(proxy))
            throw new ArgumentException("Proxy có ký tự lạ ở host/cổng (ngoài ASCII in được) — không mở Brave.", nameof(proxy));
        if (proxy.Scheme == ProxyScheme.Socks5 && proxy.HasCredentials)
            throw new NotSupportedException("Brave không hỗ trợ SOCKS5 có user/pass.");

        var exts = (extensionDirs ?? []).Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
        foreach (var e in exts)
            if (e.Contains(',')) throw new ArgumentException($"Đường dẫn extension không được có dấu phẩy: {e}", nameof(extensionDirs));

        var b = BraveArgs.WindowRaw(userDataDir)
            .AddRange(KhongBopTaiNguyenNen)
            .DisableFeatures("Translate,CalculateNativeWinOcclusion,IntensiveWakeUpThrottling")
            .DiskCacheLimit()
            .Add("--disable-gpu-process-crash-limit")
            .Add("--lang=vi-VN")
            .ProxyServer(proxy.ServerArg)
            .Add($"--proxy-bypass-list={ProxyBypassList}")
            .Add(WebRtcFlag)
            .Add("--disable-quic");

        if (exts.Count > 0)
        {
            b.DisableFeatures("DisableLoadExtensionCommandLineSwitch");
            b.LoadExtension(string.Join(",", exts));
        }

        if (mode == SellerLaunchMode.Login) b.Add(CdpFlag);

        return b.StartUrl(startUrl).BuildList();
    }

    /// <summary>Nối danh sách thành một dòng lệnh cho <c>BraveJobObject.Start(exe, args)</c>: tham số có dấu cách thì bọc CẢ tham số
    /// trong ngoặc kép — đúng dạng (3) mà <c>BraveProcessReaper.ExtractUserDataDir</c> đọc được. Tham số Chromium không chứa
    /// <c>"</c>; có thì ném (không có cách bọc an toàn).</summary>
    public static string ToCommandLine(IReadOnlyList<string> args)
    {
        var sb = new StringBuilder();
        foreach (var a in args)
        {
            if (a.Contains('"')) throw new ArgumentException($"Tham số có dấu ngoặc kép: {a}");
            if (sb.Length > 0) sb.Append(' ');
            if (a.Length == 0 || a.Any(char.IsWhiteSpace))
            {
                // Dấu \ ở cuối ngay trước " đóng sẽ thành \" (thoát ngoặc) → nhân đôi các \ cuối.
                var trailing = a.Length - a.TrimEnd('\\').Length;
                sb.Append('"').Append(a).Append('\\', trailing).Append('"');
            }
            else sb.Append(a);
        }
        return sb.ToString();
    }
}
