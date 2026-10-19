namespace Echo.Wallpapers;

public static class WallpaperPaths
{
    public const string ManifestName = "__echo_wallpaper__.json";
    public static bool IsProject(string path) => Path.GetFileName(path) == ManifestName;

    // All resources are relative capabilities rooted in one explicitly selected project.
    public static string? Inside(string root, string name, bool exists = true)
    {
        name = name.Replace('\\', '/');
        if (name.Length == 0 || name.Length > 4096 || name.Contains(':') || name.StartsWith('/')) return null;
        var parts = name.Split('/');
        if (parts.Any(p => p is ".." or "." or "")) return null;
        var current = Path.GetFullPath(root);
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return null;
        }
        return !exists || File.Exists(current) ? current : null;
    }
}
