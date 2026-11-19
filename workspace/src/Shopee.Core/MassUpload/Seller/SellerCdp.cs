using System.Net;
using System.Net.NetworkInformation;

namespace Shopee.Core.MassUpload.Seller;

/// <summary>
/// Phần CDP của chế độ <see cref="SellerLaunchMode.Login"/> (plan GĐ5b mục 2), hàm thuần trừ <see cref="SystemListeners"/>.
/// Cổng đọc từ <c>&lt;profile&gt;\DevToolsActivePort</c> như <c>suite/orders/…/LoginBrowserBootstrap.WaitForDevToolsPortAsync</c>
/// (dòng 1 = cổng, dòng 2 = đường dẫn WebSocket của browser). Thêm so với bản check đơn: kiểm cổng CHỈ nghe loopback.
/// </summary>
public static class SellerCdp
{
    public const string DevToolsActivePortFile = "DevToolsActivePort";

    /// <summary>Cổng ở dòng đầu của nội dung <c>DevToolsActivePort</c>; null = chưa ghi xong / sai dạng.</summary>
    public static int? ParseDevToolsActivePort(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var first = content.Split('\n')[0].Trim();
        return int.TryParse(first, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var port)
               && port is >= 1 and <= 65535
            ? port
            : null;
    }

    /// <summary>Các điểm đang nghe TCP trên <paramref name="port"/> phải có ít nhất một, và TẤT CẢ là loopback (127.x / ::1).
    /// null = đạt; khác null = lý do. Nghe <c>0.0.0.0</c> / IP LAN là máy khác trong mạng điều khiển được Brave đang đăng nhập.</summary>
    public static string? CheckLoopbackOnly(IEnumerable<IPEndPoint> listeners, int port)
    {
        var mine = listeners.Where(l => l.Port == port).ToList();
        if (mine.Count == 0) return $"không thấy cổng CDP {port} đang nghe";
        var open = mine.FirstOrDefault(l => !IPAddress.IsLoopback(l.Address));
        return open is null ? null : $"cổng CDP {port} nghe cả {open.Address} (không chỉ loopback)";
    }

    /// <summary>Endpoint để Playwright nối (<c>ConnectOverCDPAsync</c>) — luôn loopback.</summary>
    public static string Endpoint(int port) => $"http://127.0.0.1:{port}";

    /// <summary>Điểm đang nghe TCP của máy. Lỗi → rỗng (bên gọi coi là "không thấy cổng" ⇒ không đạt).</summary>
    public static IReadOnlyList<IPEndPoint> SystemListeners()
    {
        try { return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners(); }
        catch { return []; }
    }
}
