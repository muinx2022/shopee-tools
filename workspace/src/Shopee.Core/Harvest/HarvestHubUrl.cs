using Shopee.Core.Coordination;

namespace Shopee.Core.Harvest;

/// <summary>
/// Luật URL Hub CHUNG của app Harvest và extension <c>shopee-harvest</c> (<c>hub-client.js</c> → <c>normalizeHubUrl</c>): chỉ nhận
/// <c>https://…</c>, hoặc <c>http://</c> tới <c>localhost</c> / <c>127.0.0.1</c> (máy này là Hub) — không gửi token qua http trơn ra
/// ngoài máy. Chuẩn hoá về gốc <c>scheme://host[:port]</c> (bỏ path / query / "/" cuối) như extension. Hàm thuần.
/// </summary>
public static class HarvestHubUrl
{
    /// <summary>true + <paramref name="origin"/> nếu dùng được; false + <paramref name="error"/> (câu báo cho người dùng) nếu không.
    /// <paramref name="httpOutside"/> = URL http tới máy khác (riêng ca này: KHÔNG được gửi token).</summary>
    public static bool TryNormalize(string? raw, out string origin, out string error, out bool httpOutside)
    {
        origin = "";
        error = "";
        httpOutside = false;
        var t = (raw ?? "").Trim();
        if (!Uri.TryCreate(t, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https") || u.Host.Length == 0)
        {
            error = "URL Hub không hợp lệ (cần dạng https://…).";
            return false;
        }
        if (u.Scheme == "http" && !IsLocal(u.Host))
        {
            httpOutside = true;
            error = $"URL http ra ngoài máy ({u.Host}) — app và extension không gửi token qua http trơn; cần https://… hoặc http://localhost.";
            return false;
        }
        origin = u.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return true;
    }

    public static bool IsLocal(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || host == "127.0.0.1";

    /// <summary>Extension chỉ được cấp sẵn quyền tới Hub mặc định (<see cref="HubDefaults.BaseUrl"/>) và localhost (manifest); URL
    /// khác cần người dùng cấp quyền ở trang Cài đặt của extension.</summary>
    public static bool NeedsExtensionPermission(string origin)
    {
        if (string.Equals(origin, HubDefaults.BaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) return false;
        return !(Uri.TryCreate(origin, UriKind.Absolute, out var u) && IsLocal(u.Host));
    }
}
