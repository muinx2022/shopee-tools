using Shopee.Core.Browser;
using Shopee.Core.Platform;

namespace Shopee.Core.MassUpload.Seller;

/// <summary>Một tiến trình brave.exe của profile.</summary>
public readonly record struct SellerProcInfo(int Pid, string CommandLine)
{
    /// <summary>Tiến trình browser chính: không có <c>--type=</c> (renderer/gpu/utility đều có).</summary>
    public bool IsMain => !SellerBraveProcesses.Tokens(CommandLine).Any(t => t.StartsWith("--type=", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Khớp tiến trình Brave theo profile và kiểm tiến trình vừa mở (hàm thuần, test được). Tự làm ở đây thay vì dùng phép so của
/// <c>BraveProcessReaper</c> (chỉ cắt ngoặc + dấu gạch cuối): user mở tay profile bằng <c>C:/…</c>, khác hoa/thường hay tên ngắn
/// 8.3 (<c>NGXUAN~1</c>) thì phép so đó trượt ⇒ Brave cũ không bị dọn ⇒ lệnh mở mới chuyển sang tiến trình cũ, không proxy.
/// Chỉ dùng lại bộ tách tham số <c>BraveProcessReaper.TachThamSo</c> / <c>ExtractUserDataDir</c> (đọc, không sửa).
/// </summary>
public static class SellerBraveProcesses
{
    /// <summary>Tên giá trị chính sách máy làm cờ proxy/WebRTC của app mất tác dụng (chính sách đè cờ dòng lệnh).</summary>
    private static readonly string[] BlockingPolicyPrefixes = ["ProxyMode", "ProxyServer", "ProxySettings", "ProxyPacUrl", "WebRtcIPHandling"];

    internal static IEnumerable<string> Tokens(string commandLine) => BraveProcessReaper.TachThamSo(commandLine ?? "");

    /// <summary>Chuẩn hoá đường dẫn profile để so: bỏ ngoặc/khoảng trắng, <c>/</c>→<c>\</c>, đường dẫn đầy đủ, mở tên ngắn 8.3 (nếu
    /// thư mục tồn tại), bỏ <c>\</c> cuối. So bằng <see cref="StringComparison.OrdinalIgnoreCase"/>.</summary>
    public static string NormalizeProfilePath(string path)
    {
        var p = path.Trim().Trim('"').Trim().Replace('/', '\\');
        if (p.Length == 0) return "";
        try { p = Path.GetFullPath(p); } catch { return p.TrimEnd('\\'); }
        if (p.Contains('~')) p = ExpandShortNames(p);
        return p.Length > 3 ? p.TrimEnd('\\') : p;
    }

    public static bool SameProfile(string a, string b) =>
        string.Equals(NormalizeProfilePath(a), NormalizeProfilePath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Lọc các brave.exe có <c>--user-data-dir</c> trỏ tới <paramref name="profileDir"/> (so chuẩn hoá).</summary>
    public static List<SellerProcInfo> FilterByProfile(IEnumerable<ProcessCommandLine> all, string profileDir)
    {
        var want = NormalizeProfilePath(profileDir);
        var list = new List<SellerProcInfo>();
        if (want.Length == 0) return list;
        foreach (var p in all)
        {
            var dir = BraveProcessReaper.ExtractUserDataDir(p.CommandLine);
            if (p.Pid > 0 && dir is not null && string.Equals(NormalizeProfilePath(dir), want, StringComparison.OrdinalIgnoreCase))
                list.Add(new SellerProcInfo(p.Pid, p.CommandLine));
        }
        return list;
    }

    /// <summary>Kiểm tiến trình chính vừa mở: phải có, và MỌI tiến trình chính của profile đều mang đúng
    /// <c>--proxy-server=&lt;ServerArg&gt;</c> (một cờ duy nhất) và <see cref="SellerBrowserArgs.WebRtcFlag"/>. null = đạt; khác null =
    /// lý do. Tiến trình chính khác lệnh = lệnh mở bị chuyển sang Brave khác, hoặc cờ bị đổi giữa đường.
    /// <para>Cờ CDP (<see cref="IsCdpToken"/>): chế độ <see cref="SellerLaunchMode.Normal"/> có bất kỳ cờ nào là KHÔNG đạt; chế độ
    /// <see cref="SellerLaunchMode.Login"/> phải có đúng một cờ <see cref="SellerBrowserArgs.CdpFlag"/> và không cờ CDP nào khác
    /// (vd <c>--remote-debugging-address=0.0.0.0</c> mở cổng ra mạng).</para></summary>
    public static string? CheckMain(IReadOnlyList<SellerProcInfo> procs, ProxyLease lease, SellerLaunchMode mode = SellerLaunchMode.Normal)
    {
        var mains = procs.Where(p => p.IsMain).ToList();
        if (mains.Count == 0) return "không thấy tiến trình Brave chính của profile sau khi mở";
        var wantProxy = "--proxy-server=" + lease.ServerArg;
        foreach (var m in mains)
        {
            var toks = Tokens(m.CommandLine).ToList();
            var proxies = toks.Where(t => t.StartsWith("--proxy-server=", StringComparison.OrdinalIgnoreCase)).ToList();
            if (proxies.Count != 1 || !string.Equals(proxies[0], wantProxy, StringComparison.Ordinal))
                return $"tiến trình Brave chính (PID {m.Pid}) không chạy đúng {wantProxy}";
            if (!toks.Contains(SellerBrowserArgs.WebRtcFlag, StringComparer.Ordinal))
                return $"tiến trình Brave chính (PID {m.Pid}) thiếu {SellerBrowserArgs.WebRtcFlag}";
            var cdp = toks.Where(IsCdpToken).ToList();
            if (mode == SellerLaunchMode.Normal)
            {
                if (cdp.Count > 0) return $"tiến trình Brave chính (PID {m.Pid}) có cờ CDP {cdp[0]} — chế độ thường cấm CDP";
            }
            else if (cdp.Count != 1 || !string.Equals(cdp[0], SellerBrowserArgs.CdpFlag, StringComparison.Ordinal))
            {
                return $"tiến trình Brave chính (PID {m.Pid}) không chạy đúng một cờ {SellerBrowserArgs.CdpFlag} " +
                       $"(thấy: {(cdp.Count == 0 ? "không có" : string.Join(" ", cdp))})";
            }
        }
        return null;
    }

    /// <summary>Cờ mở/nới cổng điều khiển trình duyệt: <c>--remote-debugging-*</c> (port / address / pipe…) và
    /// <c>--remote-allow-origins</c>.</summary>
    internal static bool IsCdpToken(string token) =>
        token.StartsWith("--remote-debugging", StringComparison.OrdinalIgnoreCase)
        || token.StartsWith("--remote-allow-origins", StringComparison.OrdinalIgnoreCase);

    /// <summary>Giá trị chính sách (dạng <c>HKLM\Tên</c>) nào làm hỏng proxy/WebRTC của app.</summary>
    public static List<string> BlockingPolicies(IEnumerable<string> policyValues) =>
        policyValues.Where(v =>
        {
            var name = v[(v.LastIndexOf('\\') + 1)..];
            return BlockingPolicyPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }).ToList();

    /// <summary>Đổi từng đoạn tên ngắn 8.3 (có <c>~</c>) thành tên dài bằng cách hỏi hệ thống file (tìm theo tên ngắn trả tên thật).
    /// Đoạn nào không tồn tại thì giữ nguyên phần còn lại.</summary>
    private static string ExpandShortNames(string full)
    {
        try
        {
            var root = Path.GetPathRoot(full) ?? "";
            var cur = root;
            foreach (var part in full[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                var name = part;
                if (part.Contains('~') && Directory.Exists(cur))
                {
                    var hit = Directory.GetFileSystemEntries(cur, part).FirstOrDefault();
                    if (hit is not null) name = Path.GetFileName(hit);
                }
                cur = Path.Combine(cur, name);
            }
            return cur;
        }
        catch { return full; }
    }
}
