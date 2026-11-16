using Shopee.Modules.MultiBrave;

namespace OpenMultiBraveLauncherV3;

internal static class RunnerExtensionPaths
{
    /// <summary>Tên extension (manifest) của chế độ process: "Shopee Data Runner" (Scrape) / "Shopee Harvest Runner".</summary>
    public static string ExtensionDisplayName => RunnerModeProfile.Current.ExtensionDisplayName;

    /// <summary>Thư mục unpacked extension đủ file (background.js) để --load-extension.</summary>
    public static string? ResolveLoadDirectory() => ResolveLoadDirectory(MultiBraveRuntime.Mode);

    public static string? ResolveLoadDirectory(RunnerMode mode)
    {
        foreach (var dir in CandidateDirectories(mode))
        {
            if (File.Exists(Path.Combine(dir, "background.js")))
                return dir;
        }

        return null;
    }

    public static string? TryGetLoadedExtensionId() => TryGetLoadedExtensionId(MultiBraveRuntime.Mode);

    public static string? TryGetLoadedExtensionId(RunnerMode mode)
    {
        var dir = ResolveLoadDirectory(mode);
        return dir is null ? null : UnpackedExtensionId.FromPath(dir);
    }

    internal static IEnumerable<string> CandidateDirectories(RunnerMode mode)
    {
        var folder = RunnerModeProfile.For(mode).ExtensionFolder;
        yield return Path.Combine(AppContext.BaseDirectory, "extensions", folder);

        var repo = FindRepoRoot();
        if (repo is not null)
            yield return Path.Combine(repo, "extensions", folder);
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            // Mốc repo root = file solution (Python đã bỏ nên không dùng api/main.py nữa).
            if (File.Exists(Path.Combine(dir.FullName, "ShopeeWorkspace.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }
}
