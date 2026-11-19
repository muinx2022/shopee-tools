using System.Text.Encodings.Web;

namespace Shopee.Core.MassUpload.Seller;

/// <summary>
/// Extension MV3 tối thiểu trả lời xác thực proxy có user/pass (plan GĐ4 bước 2). Chrome/Brave không nhận <c>user:pass</c> trong
/// <c>--proxy-server</c>, mà đưa mật khẩu lên dòng lệnh thì tiến trình nào trên máy cũng đọc được (WMI CommandLine) ⇒ mật khẩu
/// nằm trong file <c>background.js</c> của một thư mục RIÊNG cho từng lần mở, xoá khi đóng Brave.
/// <para><b>Chưa xoá hết dấu vết:</b> Brave chép mã service worker vào ScriptCache của profile
/// (<c>&lt;profile&gt;\Default\Service Worker\ScriptCache</c>), nên mật khẩu proxy VẪN nằm trên đĩa trong profile sau khi xoá thư
/// mục extension. Mức lộ ngang <c>seller-accounts.json</c> (vốn lưu mật khẩu proxy dạng chữ thường trong cùng hồ sơ người dùng) —
/// chấp nhận; đừng coi việc xoá thư mục là đã xoá mật khẩu khỏi máy.</para>
/// <list type="bullet">
/// <item>Quyền: <c>webRequest</c> + <c>webRequestAuthProvider</c> (MV3 chỉ cho <c>onAuthRequired</c> chặn-đồng-bộ khi có quyền
/// này), <c>host_permissions: &lt;all_urls&gt;</c>.</item>
/// <item>Chỉ trả lời challenge <c>isProxy</c> đến ĐÚNG host:port của proxy; challenge của site (401) không bao giờ nhận mật khẩu
/// proxy.</item>
/// <item>Mỗi request trả lời tối đa 3 lần rồi huỷ — sai mật khẩu thì request hỏng, không lặp vô hạn, không hiện hộp thoại.</item>
/// </list>
/// </summary>
public static class ProxyAuthExtension
{
    public const string ManifestFile = "manifest.json";
    public const string BackgroundFile = "background.js";

    /// <summary>Số lần trả lời tối đa cho một request.</summary>
    public const int MaxAnswersPerRequest = 3;

    private static readonly JsonSerializerOptions JsOpts = new()
    {
        // Mặc định của System.Text.Json: thoát < > & ' " → chuỗi JSON nhúng vào JS an toàn.
        Encoder = JavaScriptEncoder.Default,
    };

    /// <summary>Nội dung các file (hàm thuần). Ném nếu proxy không có user/pass.</summary>
    public static IReadOnlyDictionary<string, string> BuildFiles(ProxyLease lease)
    {
        if (!lease.HasCredentials) throw new ArgumentException("Proxy không có user/pass — không cần extension xác thực.", nameof(lease));

        var manifest = new
        {
            manifest_version = 3,
            name = "Shopee Upload proxy auth",
            version = "1.0.0",
            permissions = new[] { "webRequest", "webRequestAuthProvider" },
            host_permissions = new[] { "<all_urls>" },
            background = new { service_worker = BackgroundFile },
        };
        var cfg = JsonSerializer.Serialize(new
        {
            host = lease.Host.ToLowerInvariant(),
            port = lease.Port,
            username = lease.User,
            password = lease.Pass,
        }, JsOpts);

        var js = $$"""
            // Sinh tự động bởi Shopee Upload — trả lời xác thực proxy. Xoá khi đóng trình duyệt.
            const PROXY = {{cfg}};
            const MAX = {{MaxAnswersPerRequest}};
            const answered = new Map();
            const norm = (h) => String(h || "").toLowerCase().replace(/^\[|\]$/g, "");
            chrome.webRequest.onAuthRequired.addListener((details) => {
              if (!details.isProxy) return {};
              const c = details.challenger || {};
              if (norm(c.host) !== norm(PROXY.host) || Number(c.port) !== PROXY.port) return {};
              const n = (answered.get(details.requestId) || 0) + 1;
              answered.set(details.requestId, n);
              if (n > MAX) return { cancel: true };
              return { authCredentials: { username: PROXY.username, password: PROXY.password } };
            }, { urls: ["<all_urls>"] }, ["blocking"]);
            const forget = (d) => answered.delete(d.requestId);
            chrome.webRequest.onCompleted.addListener(forget, { urls: ["<all_urls>"] });
            chrome.webRequest.onErrorOccurred.addListener(forget, { urls: ["<all_urls>"] });
            """;

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // manifest không nhúng vào JS, chỉ có hằng của app → không cần thoát < > (giữ "<all_urls>" dễ đọc).
            [ManifestFile] = JsonSerializer.Serialize(manifest,
                new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
            [BackgroundFile] = js + "\n",
        };
    }

    /// <summary>Ghi extension vào thư mục mới <c>&lt;parentDir&gt;\&lt;prefix&gt;-&lt;guid&gt;</c>, trả đường dẫn. Đường dẫn mới mỗi lần
    /// ⇒ ID extension mới ⇒ không dính service worker cũ cache trong profile (bài học <c>OrdersBridgeLauncher</c>).</summary>
    public static string Write(string parentDir, string prefix, ProxyLease lease)
    {
        var dir = Path.Combine(parentDir, $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        foreach (var (name, content) in BuildFiles(lease))
            File.WriteAllText(Path.Combine(dir, name), content, new UTF8Encoding(false));
        return dir;
    }
}
