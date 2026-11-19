using System.Text.Json.Nodes;

namespace Shopee.Core.MassUpload.Seller;

/// <summary>
/// Lớp 2 chống rò IP, ghi thẳng vào file cấu hình của profile TRƯỚC khi mở Brave (lớp 1 là cờ dòng lệnh ở
/// <see cref="SellerBrowserArgs"/>):
/// <list type="bullet">
/// <item><c>&lt;profile&gt;\Default\Preferences</c>: <c>webrtc.ip_handling_policy = "disable_non_proxied_udp"</c>,
/// <c>webrtc.multiple_routes_enabled = false</c>, <c>webrtc.nonproxied_udp_enabled = false</c>.</item>
/// <item><c>&lt;profile&gt;\Local State</c>: <c>dns_over_https.mode = "off"</c> — đo được Brave tự gọi DoH thẳng tới dns.google
/// (không qua proxy).</item>
/// </list>
/// Gộp vào JSON có sẵn (giữ nguyên mọi khoá khác — cookie/đăng nhập không nằm ở đây nhưng cài đặt của user thì có), chưa có file
/// thì tạo. Ghi nguyên tử: file tạm cùng thư mục → <c>File.Move</c> đè; UTF-8 KHÔNG BOM như Chromium tự ghi. File hỏng (không
/// phải JSON object) → đổi tên <c>.corrupt-&lt;giờ&gt;</c> rồi ghi mới (Chromium cũng bỏ file hỏng). Lỗi IO → ném (launcher không mở).
/// </summary>
public static class SellerProfilePrefs
{
    public const string PreferencesRelPath = @"Default\Preferences";
    public const string LocalStateFile = "Local State";

    public static void Apply(string profileDir)
    {
        Merge(Path.Combine(profileDir, PreferencesRelPath), root =>
        {
            var webrtc = Obj(root, "webrtc");
            webrtc["ip_handling_policy"] = SellerBrowserArgs.WebRtcPolicy;
            webrtc["multiple_routes_enabled"] = false;
            webrtc["nonproxied_udp_enabled"] = false;
        });
        Merge(Path.Combine(profileDir, LocalStateFile), root => Obj(root, "dns_over_https")["mode"] = "off");
    }

    /// <summary>Đọc (hoặc tạo) JSON object ở <paramref name="path"/>, cho <paramref name="edit"/> sửa, ghi nguyên tử.</summary>
    internal static void Merge(string path, Action<JsonObject> edit)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        JsonObject? root = null;
        if (File.Exists(path))
        {
            var text = File.ReadAllText(path);   // lỗi IO → ném
            try { root = JsonNode.Parse(text) as JsonObject; } catch (JsonException) { }
            if (root is null) File.Move(path, $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}");
        }
        root ??= new JsonObject();
        edit(root);

        var tmp = $"{path}.{Environment.ProcessId}-{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tmp, root.ToJsonString(), new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
    }

    /// <summary>Lấy object con <paramref name="name"/>; chưa có hoặc sai kiểu thì thay bằng object mới.</summary>
    private static JsonObject Obj(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject o) return o;
        var n = new JsonObject();
        parent[name] = n;
        return n;
    }
}
