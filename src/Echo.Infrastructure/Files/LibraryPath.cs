namespace Echo.Infrastructure.Files;

// Only this adapter owns OS path identity rules.
public static class LibraryPath
{
    public const int MaxCharacters = 4096;
    public static string Canonical(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.Length > MaxCharacters) throw new ArgumentException("Path exceeds the bounded protocol limit.");
        return full;
    }
    public static string Key(string path) =>
        OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
    public static string RootPrefix(string root)
    {
        var full = Path.TrimEndingDirectorySeparator(Canonical(root));
        return Key(full) + (Path.EndsInDirectorySeparator(full) ? "" : Path.DirectorySeparatorChar.ToString());
    }
}
