namespace Echo.Core.Transport;

internal static class CanonicalPath
{
    public static string Resolve(string input)
    {
        var full = Path.GetFullPath(input);
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var component in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            FileSystemInfo info = System.IO.Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null) current = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? current;
        }
        return current;
    }
}
