using System.Text.Json;

namespace Echo.Infrastructure.Media;

public sealed record WallpaperEngineLibraryResult(IReadOnlyList<WallpaperEngineProject> Projects, int Skipped, bool Truncated);

public static class WallpaperEngineLibrary
{
    public const int MaxProjects = 2000;
    public static WallpaperEngineLibraryResult Scan(IEnumerable<string> roots, CancellationToken token)
    {
        var projects = new List<WallpaperEngineProject>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var scanned = 0; var skipped = 0;
        foreach (var root in roots)
        foreach (var parent in ProjectLocations(root))
        {
            token.ThrowIfCancellationRequested();
            if (!Directory.Exists(parent)) continue;
            if (File.Exists(Path.Combine(parent, "project.json")))
            { if (!Read(parent)) return new(projects, skipped, true); continue; }
            try
            {
                foreach (var folder in Directory.EnumerateDirectories(parent))
                {
                    token.ThrowIfCancellationRequested();
                    if (!Read(folder)) return new(projects, skipped, true);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { skipped++; }
        }
        return new(projects, skipped, false);

        bool Read(string folder)
        {
            token.ThrowIfCancellationRequested();
            if (!seen.Add(Path.GetFullPath(folder))) return true;
            if (scanned == MaxProjects) return false;
            scanned++;
            try { projects.Add(WallpaperEngineProject.Read(folder)); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException) { skipped++; }
            return true;
        }
    }

    private static IEnumerable<string> ProjectLocations(string root)
    {
        yield return Path.Combine(root, "steamapps", "workshop", "content", "431960");
        foreach (var prefix in new[] { "steamapps/common/wallpaper_engine/projects", "projects", "" })
        foreach (var kind in new[] { "myprojects", "defaultprojects" })
            yield return Path.Combine(root, prefix, kind);
        yield return Path.Combine(root, "workshop", "content", "431960");
        yield return Path.Combine(root, "431960");
        yield return root;
    }
}
