using Echo.Contracts.Library;
using Echo.Contracts.Scanning;

namespace Echo.Infrastructure.Files;

// Port of OsuFolderFileFacts: only the active folder, capped at 256 observations.
internal sealed class FolderFileFacts
{
    private readonly Dictionary<string, FileSnapshot> _facts = new(StringComparer.Ordinal);
    public FileSnapshot Read(string path)
    {
        path = LibraryPath.Canonical(path);
        var key = LibraryPath.Key(path);
        if (_facts.TryGetValue(key, out var previous)) return previous;
        var info = new FileInfo(path);
        if (!info.Exists || (info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new MetadataReadException("source_audio_missing");
        var unavailable = ((int)info.Attributes & ((int)FileAttributes.Offline | 0x40000 | 0x400000)) != 0;
        if (unavailable) throw new MetadataReadException("cloud_file_unavailable");
        var value = new FileSnapshot(path, key, info.Length, info.LastWriteTimeUtc.Ticks);
        if (_facts.Count < 256) _facts.Add(key, value);
        return value;
    }
    public static string? ResolveSibling(string directory, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var path = Path.GetFullPath(name.Replace('\\', Path.DirectorySeparatorChar), directory);
        var relative = Path.GetRelativePath(directory, path);
        return relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathFullyQualified(relative) ? null : path;
    }
}
