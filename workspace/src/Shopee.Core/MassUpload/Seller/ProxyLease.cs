using System.Net;
using System.Net.Sockets;

namespace Shopee.Core.MassUpload.Seller;

public enum ProxyLeaseSource { Kiot, Static }

/// <summary>
/// Proxy đã cấp cho MỘT shop sắp chạy. <see cref="Host"/> không có ngoặc vuông (IPv6 thêm ngoặc ở <see cref="HostForUri"/>).
/// <see cref="ToString"/> KHÔNG in mật khẩu — record mặc định in mọi thuộc tính, lọt vào log là lộ.
/// </summary>
public sealed record ProxyLease(ProxyScheme Scheme, string Host, int Port, string? User = null, string? Pass = null,
    string? ExpectedIp = null)
{
    public ProxyLeaseSource Source { get; init; } = ProxyLeaseSource.Static;

    /// <summary>true = IP ra phải KHÁC mọi IP đã dùng cho shop khác trong lượt (key Kiot đổi IP mỗi shop — plan GĐ4 bước 3).</summary>
    public bool MustBeUnusedIp { get; init; }

    /// <summary>4 ký tự cuối key Kiot (để log), null với proxy tĩnh.</summary>
    public string? KiotKeyTail { get; init; }

    public bool HasCredentials => !string.IsNullOrEmpty(User);

    public string SchemeText => Scheme == ProxyScheme.Socks5 ? "socks5" : "http";

    public string HostForUri => Host.Contains(':') ? $"[{Host}]" : Host;

    /// <summary>Giá trị cho <c>--proxy-server</c> / <see cref="WebProxy"/>: <c>scheme://host:port</c>, KHÔNG kèm user/pass.</summary>
    public string ServerArg => $"{SchemeText}://{HostForUri}:{Port}";

    public Uri ServerUri => new(ServerArg);

    public override string ToString() => ServerArg + (HasCredentials ? " (có user/pass)" : "");
}

/// <summary>
/// Đọc chuỗi proxy tĩnh. Nhận:
/// <list type="bullet">
/// <item><c>host:port</c>, <c>host:port:user:pass</c> (mật khẩu được chứa <c>:</c>);</item>
/// <item>có scheme: <c>http://…</c>, <c>socks5://…</c>, và dạng URL <c>scheme://user:pass@host:port</c>;</item>
/// <item>IPv6 trong ngoặc vuông: <c>[2001:db8::1]:8080[:user:pass]</c>;</item>
/// <item>(GĐ5d) cách nhau bằng khoảng trắng / tab — chép từ danh sách proxy: <c>host&lt;ws&gt;port</c>,
/// <c>host&lt;ws&gt;port&lt;ws&gt;user&lt;ws&gt;pass</c>. Đúng 2 hoặc 4 đoạn, không scheme, cổng toàn chữ số, host và user không có
/// <c>:</c>. Khoảng trắng ở chỗ khác vẫn bị từ chối. Đổi về dạng <c>host:port[:user:pass]</c> rồi đọc như thường.</item>
/// </list>
/// Không ghi scheme thì lấy <c>defaultScheme</c>. Từ chối: scheme lạ, thiếu/sai cổng, host sai, có user mà thiếu pass, SOCKS5 có
/// mật khẩu (Chrome/Brave không hỗ trợ xác thực SOCKS5 — mở ra sẽ đi không được hoặc hiện hộp thoại, không có đường nào an toàn).
/// <para>Câu báo lỗi KHÔNG chứa mẩu nào của chuỗi user nhập: nhập sai thứ tự (vd <c>user:pass:host:port</c>) thì "cổng" hay "host"
/// chính là mật khẩu — in ra là lộ lên màn/log.</para>
/// <para>Dạng URL chỉ nhận khi phần sau <c>@</c> CUỐI đúng là <c>host:port</c>; không thì đọc dạng <c>host:port:user:pass</c> — mật
/// khẩu được chứa <c>@</c>.</para>
/// </summary>
public static class StaticProxyParser
{
    /// <summary>Lỗi riêng "SOCKS5 có user/pass" — <see cref="SellerAccountStore.Validate"/> hạ thành cảnh báo cho proxy tĩnh chung khi
    /// đang có key Kiot (proxy đó bị bỏ qua).</summary>
    public const string Socks5WithCredentialsError =
        "Brave không hỗ trợ SOCKS5 có user/pass — ghi http:// ở đầu nếu proxy là http, hoặc dùng SOCKS5 không mật khẩu.";

    public static bool TryParse(string? raw, ProxyScheme defaultScheme, out ProxyLease? lease, out string? error)
    {
        lease = null;
        error = null;
        // Bỏ BOM / ký tự vô hình (định dạng Cf: LRM, ZWSP, soft hyphen…) ở HAI ĐẦU — hay dính khi chép từ Excel / web. Ở giữa thì để
        // nguyên cho kiểm host báo lỗi: tự bỏ là đoán.
        var s = ProxyHostRules.TrimInvisible(raw ?? "");
        if (s.Length == 0) { error = "Proxy tĩnh rỗng."; return false; }
        if (s.Any(char.IsWhiteSpace))
        {
            if (!TryJoinWhitespaceForm(s, out var joined))
            {
                error = "Proxy tĩnh không được có dấu cách (trừ dạng host<cách/tab>port hoặc host<cách/tab>port<cách/tab>user<cách/tab>pass).";
                return false;
            }
            s = joined;
        }

        var scheme = defaultScheme;
        var sep = s.IndexOf("://", StringComparison.Ordinal);
        if (sep >= 0)
        {
            var sch = s[..sep].ToLowerInvariant();
            if (sch == "http") scheme = ProxyScheme.Http;
            else if (sch == "socks5") scheme = ProxyScheme.Socks5;
            else { error = "Kiểu proxy không hỗ trợ (chỉ http, socks5)."; return false; }
            s = s[(sep + 3)..].TrimEnd('/');
        }

        string? user = null, pass = null;
        var at = s.LastIndexOf('@');
        // Phần trước @ đã có dạng "host:port:…" (vd 1.2.3.4:8080:me@mail.com:pass) → @ nằm trong user/pass, đọc dạng host:port:user:pass.
        if (at >= 0 && LooksLikeHostPort(s[(at + 1)..]) && !StartsWithHostPortSegment(s[..at]))
        {
            // Dạng URL user:pass@host:port.
            var cred = s[..at];
            s = s[(at + 1)..];
            var c = cred.IndexOf(':');
            if (c <= 0) { error = "Thiếu mật khẩu proxy (cần user:pass@host:port)."; return false; }
            user = Uri.UnescapeDataString(cred[..c]);
            pass = Uri.UnescapeDataString(cred[(c + 1)..]);
        }

        string host;
        string rest;
        if (s.StartsWith('['))
        {
            var close = s.IndexOf(']');
            if (close < 0) { error = "IPv6 thiếu dấu ']'."; return false; }
            host = s[1..close];
            if (!IPAddress.TryParse(host, out var v6) || v6.AddressFamily != AddressFamily.InterNetworkV6)
            { error = "IPv6 trong ngoặc vuông không hợp lệ."; return false; }
            rest = s[(close + 1)..];
            if (!rest.StartsWith(':')) { error = "Thiếu cổng proxy."; return false; }
            rest = rest[1..];
        }
        else
        {
            var c = s.IndexOf(':');
            if (c < 0) { error = "Thiếu cổng proxy (cần host:port)."; return false; }
            host = s[..c];
            rest = s[(c + 1)..];
        }

        // rest = "port" hoặc "port:user:pass" (pass được chứa ':').
        var parts = rest.Split(':', 3);
        if (!parts[0].All(char.IsAsciiDigit)
            || !int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535)
        { error = "Cổng proxy không hợp lệ (cần số 1–65535, dạng host:port[:user:pass])."; return false; }
        if (parts.Length == 2) { error = "Thiếu mật khẩu proxy (cần host:port:user:pass)."; return false; }
        if (parts.Length == 3)
        {
            if (user is not null) { error = "Proxy ghi user/pass hai lần."; return false; }
            user = parts[1];
            pass = parts[2];
        }

        if (user is not null && (user.Length == 0 || string.IsNullOrEmpty(pass)))
        { error = "User/pass proxy không được rỗng."; return false; }
        // Host phải là tên miền / IPv4 ASCII thuần hoặc IPv6: .NET tự "làm sạch" host lạ (IdnHost) nên vẫn đi đúng proxy khi kiểm IP,
        // còn Brave gặp --proxy-server sai thì BỎ proxy, đi thẳng IP máy (đo thật GĐ5d vòng 2).
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown || !ProxyHostRules.IsValidHost(host))
        { error = "Host proxy không hợp lệ (chỉ chữ/số ASCII, '.', '-', hoặc IPv6 trong [ ]; không ký tự vô hình / chữ số toàn khổ)."; return false; }
        if (user is not null && (ProxyHostRules.HasControlOrInvisible(user) || ProxyHostRules.HasControlOrInvisible(pass!)))
        { error = "User/pass proxy có ký tự điều khiển / vô hình."; return false; }
        // user là IPv4 còn host không phải IP và không có dấu chấm (vd "myuser:12345:1.2.3.4:8080") → gần như chắc là gõ ngược thứ tự:
        // đọc tiếp thì mật khẩu thật thành "cổng", "host" thật thành user — lộ khi in ra.
        if (user is not null && IsIPv4(user) && !IPAddress.TryParse(host, out _) && !host.Contains('.'))
        { error = "Proxy tĩnh có vẻ gõ ngược thứ tự — đúng là host:port:user:pass (host/IP trước, mật khẩu sau cùng)."; return false; }
        if (scheme == ProxyScheme.Socks5 && user is not null)
        { error = Socks5WithCredentialsError; return false; }

        lease = new ProxyLease(scheme, host, port, user, pass) { Source = ProxyLeaseSource.Static };
        return true;
    }

    /// <summary>Dạng cách bằng khoảng trắng (GĐ5d) → dạng <c>host:port[:user:pass]</c>. false nếu không đúng khuôn (không đoán).</summary>
    private static bool TryJoinWhitespaceForm(string s, out string joined)
    {
        joined = "";
        var t = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (t.Length is not (2 or 4)) return false;
        if (t.Any(x => x.Contains("://", StringComparison.Ordinal))) return false;
        var host = t[0];
        var hostOk = host.StartsWith('[') ? host.EndsWith(']') : !host.Contains(':') && !host.Contains('@');
        if (!hostOk || t[1].Length == 0 || !t[1].All(char.IsAsciiDigit)) return false;
        if (t.Length == 4 && t[2].Contains(':')) return false;   // user có ':' thì dạng ghép sẽ cắt sai chỗ
        joined = string.Join(':', t);
        return true;
    }

    /// <summary>IPv4 đủ 4 phần (IPAddress.TryParse nhận cả "123").</summary>
    private static bool IsIPv4(string s) =>
        s.Count(c => c == '.') == 3 && IPAddress.TryParse(s, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork;

    /// <summary>true nếu <paramref name="s"/> mở đầu bằng <c>host:port:</c> (đoạn thứ hai toàn chữ số, sau nó còn phần nữa).</summary>
    private static bool StartsWithHostPortSegment(string s)
    {
        if (s.StartsWith('['))
        {
            // [ipv6]:port:… — tách theo ':' sẽ cắt vụn IPv6.
            var close = s.IndexOf(']');
            if (close < 0 || close + 1 >= s.Length || s[close + 1] != ':') return false;
            var after = s[(close + 2)..].Split(':');
            return after.Length >= 2 && after[0].Length > 0 && after[0].All(char.IsAsciiDigit);
        }
        var parts = s.Split(':');
        return parts.Length >= 3 && parts[0].Length > 0 && parts[1].Length > 0 && parts[1].All(char.IsAsciiDigit);
    }

    /// <summary>true nếu <paramref name="s"/> có dạng <c>host:port</c> hoặc <c>[ipv6]:port</c> (cổng toàn chữ số, không thêm phần nào).</summary>
    private static bool LooksLikeHostPort(string s)
    {
        string port;
        if (s.StartsWith('['))
        {
            var close = s.IndexOf(']');
            if (close < 0 || close + 1 >= s.Length || s[close + 1] != ':') return false;
            port = s[(close + 2)..];
        }
        else
        {
            var c = s.IndexOf(':');
            if (c <= 0 || s.IndexOf(':', c + 1) >= 0) return false;
            port = s[(c + 1)..];
        }
        return port.Length > 0 && port.All(char.IsAsciiDigit);
    }
}

/// <summary>
/// Luật host / cổng proxy dùng chung cho parser proxy tĩnh, địa chỉ Kiot trả về và chốt cuối trước khi đưa cho Brave (GĐ5d vòng 2).
/// Lý do: .NET (<see cref="Uri.IdnHost"/>) "làm sạch" host có ký tự vô hình / chữ số toàn khổ nên kiểm IP vẫn qua proxy, còn Brave gặp
/// <c>--proxy-server</c> sai thì bỏ proxy và đi thẳng bằng IP máy.
/// </summary>
public static class ProxyHostRules
{
    /// <summary>Tên miền / IPv4 ASCII: các nhãn <c>[A-Za-z0-9-]</c> 1–63 ký tự, không mở/kết bằng <c>-</c>, cách bằng một dấu
    /// <c>.</c>, cả chuỗi ≤ 253 ký tự. Hoặc IPv6 (không ngoặc) chỉ gồm <c>[0-9A-Fa-f:.]</c> và đọc được là IPv6.</summary>
    public static bool IsValidHost(string? host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        if (host.Contains(':'))
            return host.All(c => char.IsAsciiHexDigit(c) || c is ':' or '.')
                   && IPAddress.TryParse(host, out var v6) && v6.AddressFamily == AddressFamily.InterNetworkV6;
        if (host.Length > 253) return false;
        foreach (var label in host.Split('.'))
        {
            if (label.Length is 0 or > 63) return false;
            if (label[0] == '-' || label[^1] == '-') return false;
            if (!label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) return false;
        }
        return true;
    }

    public static bool IsValidPort(int port) => port is >= 1 and <= 65535;

    /// <summary>Ký tự điều khiển (Cc) hoặc định dạng vô hình (Cf: BOM, LRM, ZWSP, soft hyphen…).</summary>
    public static bool HasControlOrInvisible(string s) => s.Any(IsControlOrInvisible);

    private static bool IsControlOrInvisible(char c) =>
        char.IsControl(c) || System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format;

    /// <summary>Bỏ khoảng trắng + ký tự vô hình ở HAI ĐẦU (không đụng ở giữa).</summary>
    public static string TrimInvisible(string s)
    {
        int a = 0, b = s.Length;
        while (a < b && (char.IsWhiteSpace(s[a]) || IsControlOrInvisible(s[a]))) a++;
        while (b > a && (char.IsWhiteSpace(s[b - 1]) || IsControlOrInvisible(s[b - 1]))) b--;
        return s[a..b];
    }

    /// <summary><c>scheme://host:port</c> toàn ký tự ASCII in được (0x21–0x7E) và host / cổng đúng luật — chốt cuối trước Brave.</summary>
    public static bool IsSafeForBrowser(ProxyLease lease) =>
        IsValidHost(lease.Host) && IsValidPort(lease.Port) && lease.ServerArg.All(c => c is >= '!' and <= '~');

    /// <summary>Host mà .NET sẽ thật sự nối tới (<see cref="Uri.IdnHost"/>) phải là CHÍNH host đưa cho Brave — không để .NET "làm sạch"
    /// khác đi. Tên miền so không phân biệt hoa thường; IPv6 so theo địa chỉ (cách viết khác nhau vẫn là một).</summary>
    public static bool UriHostMatches(ProxyLease lease)
    {
        string idn;
        try { idn = lease.ServerUri.IdnHost; }
        catch (UriFormatException) { return false; }
        if (lease.Host.Contains(':'))
            return IPAddress.TryParse(idn.Trim('[', ']'), out var a) && IPAddress.TryParse(lease.Host, out var b) && a.Equals(b);
        return string.Equals(idn, lease.Host, StringComparison.OrdinalIgnoreCase);
    }
}
