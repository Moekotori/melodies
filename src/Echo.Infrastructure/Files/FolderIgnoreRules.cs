using System.IO.Enumeration;

namespace Echo.Infrastructure.Files;

public sealed class FolderIgnoreRules(string root, IReadOnlyList<string> patterns)
{
    public bool Matches(string path)
    {
        if (patterns.Count == 0) return false;
        var marker = path.IndexOf("#cueTrack=", StringComparison.Ordinal); var clean = marker < 0 ? path : path[..marker];
        var relative = Path.GetRelativePath(root, clean).Replace('\\', '/');
        var name = Path.GetFileName(clean); var comparison = OperatingSystem.IsWindows();
        return patterns.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern.Replace('\\', '/'), relative, comparison)
            || FileSystemName.MatchesSimpleExpression(pattern.TrimEnd('/'), name, comparison));
    }
}
