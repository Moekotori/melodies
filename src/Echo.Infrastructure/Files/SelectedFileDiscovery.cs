using Echo.Contracts.Library;
using Echo.Contracts.Scanning;

namespace Echo.Infrastructure.Files;

// Explicit file import never enumerates siblings or performs missing detection.
public sealed class SelectedFileDiscovery(string path, ILibraryStore? store = null) : IFileDiscovery
{
    public IEnumerable<FileSnapshot> Enumerate(string root, bool requireFullCoverage,
        CancellationToken cancellation, bool force = false)
    {
        if (requireFullCoverage) throw new ArgumentException("A selected file cannot provide full root coverage.");
        cancellation.ThrowIfCancellationRequested();
        var filePath = LibraryPath.Canonical(path);
        var info = new FileInfo(filePath);
        if (!info.Exists) throw new FileNotFoundException("Import file is unavailable.");
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked media cannot be imported.");
        var extension = info.Extension;
        if (!ScanFormats.Supports(extension)) throw new ArgumentException("Unsupported import format.");
        var file = new FileSnapshot(filePath, LibraryPath.Key(filePath), info.Length, info.LastWriteTimeUtc.Ticks,
            ((int)info.Attributes & ((int)FileAttributes.Offline | 0x40000 | 0x400000)) != 0);
        if (file.Unavailable) { yield return file; yield break; }
        var facts = new FolderFileFacts();
        using var suppressed = new FolderSourceSet();
        if (extension.Equals(".osu", StringComparison.OrdinalIgnoreCase))
        {
            var issues = new List<FileSnapshot>();
            var tracks = OsuFolderExpansion.Collect(info.DirectoryName!, [file], store, facts, force, cancellation, issues, suppressed);
            foreach (var issue in issues) yield return issue;
            foreach (var track in tracks.Values) yield return track;
            yield break;
        }
        var cue = extension.Equals(".cue", StringComparison.OrdinalIgnoreCase);
        var flac = extension.Equals(".flac", StringComparison.OrdinalIgnoreCase) || extension.Equals(".fla", StringComparison.OrdinalIgnoreCase);
        if (cue || flac)
        {
            var tracks = CueFolderExpansion.Expand(file, root, facts, suppressed, !cue, cancellation, out _);
            var expanded = false;
            foreach (var track in tracks) { expanded = true; yield return track; }
            if (expanded) yield break;
        }
        yield return flac ? file with { Source = new("local", file.Path, file.Path, SourceStamp.Compute(file, file, CueFolderExpansion.DiscoveryVersion),
            file.SizeBytes, file.ModifiedTicks, file.SizeBytes, file.ModifiedTicks) } : file;
    }
}
