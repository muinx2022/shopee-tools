using System.Text.RegularExpressions;

namespace Shopee.Hub.Web.Api;

/// <summary>
/// Luật tên video mà client được phép DELETE. Chép từ <c>HubVideoRetention.IsVideoName</c> bên workspace
/// (hub không reference project đó). Lệch một bên là 403 oan hoặc xóa nhầm config/cookie/ảnh.
/// </summary>
internal static class HubVideoNames
{
    private static readonly Regex VideoKey = new(
        @"^videos/[^/\\:]+\.mp4$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsVideoName(string? name)
    {
        if (string.IsNullOrEmpty(name) || !VideoKey.IsMatch(name)) return false;
        var file = name["videos/".Length..];
        if (file is "." or "..") return false;
        return !file.Contains("..", StringComparison.Ordinal);
    }
}
