using System.Text.RegularExpressions;
using Shopee.Core.Coordination;

namespace Shopee.Core.Scrape;

/// <summary>
/// Video trên hub sống 30 ngày theo <see cref="FileManifestEntry.Mtime"/> (UTC lúc PUT). Cào lại cùng SKU
/// thì PUT đè và mtime tính lại từ đầu — không có một lịch "xóa sạch vào ngày X".
/// Luật tên chép y hệt <c>HubVideoNames</c> trên hub (hub không reference project này): lệch một bên là
/// client xin xóa video mà hub trả 403, hoặc hub nhận một key không phải video.
/// </summary>
public static class HubVideoRetention
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    /// <summary>Một đoạn tên file dưới <c>videos/</c>, đuôi <c>.mp4</c> đúng hoa-thường. Không <c>.</c>/<c>..</c>, không <c>..</c> lồng trong tên.</summary>
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

    /// <summary><c>default</c> = hub không có ngày → không xóa (xóa mò dễ mất file vừa đẩy mà mtime chưa đọc được).</summary>
    public static bool IsExpired(DateTimeOffset mtime, DateTimeOffset now)
        => mtime != default && now - mtime >= MaxAge;

    public static IReadOnlyList<string> ExpiredNames(IReadOnlyList<FileManifestEntry> entries, DateTimeOffset now)
    {
        var names = new List<string>();
        foreach (var entry in entries)
        {
            if (!IsVideoName(entry.Name) || !IsExpired(entry.Mtime, now)) continue;
            names.Add(entry.Name);
        }
        return names;
    }

    public static IReadOnlyList<string> AllVideoNames(IReadOnlyList<FileManifestEntry> entries)
    {
        var names = new List<string>();
        foreach (var entry in entries)
        {
            if (IsVideoName(entry.Name)) names.Add(entry.Name);
        }
        return names;
    }
}
