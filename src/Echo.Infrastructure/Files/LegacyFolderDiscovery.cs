using System.IO.Enumeration;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;

namespace Echo.Infrastructure.Files;

// Legacy CUE/osu discovery rules, with bounded per-folder facts rather than a whole-library list.
public sealed class LegacyFolderDiscovery(ILibraryStore? store = null) : IFileDiscovery
{
    public IEnumerable<FileSnapshot> Enumerate(string root, bool requireFullCoverage,
        CancellationToken cancellation, bool force = false)
    {
        root = LibraryPath.Canonical(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Scan root is unavailable.");
        return Walk(root, root, requireFullCoverage, force, 0, cancellation);
    }

    private IEnumerable<FileSnapshot> Walk(string directory, string root, bool full, bool force,
        int depth, CancellationToken cancellation)
    {
        if (depth > 64) throw new MetadataReadException("scan_directory_depth_limit");
        foreach (var file in FolderFiles(directory, root, full, force, cancellation)) yield return file;
        // Do not retain a list of tens of thousands of child directories.
        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            cancellation.ThrowIfCancellationRequested();
            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
            { if (full) throw new IOException("Missing detection cannot exclude linked directories."); continue; }
            foreach (var file in Walk(child, root, full, force, depth + 1, cancellation)) yield return file;
        }
    }

    private IEnumerable<FileSnapshot> FolderFiles(string directory, string root, bool full, bool force,
        CancellationToken cancellation)
    {
        var cues = new List<FileSnapshot>(); var beatmaps = new List<FileSnapshot>();
        foreach (var file in TopFiles(directory, full, cancellation))
        {
            var extension = Path.GetExtension(file.Path);
            if (!extension.Equals(".cue", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".osu", StringComparison.OrdinalIgnoreCase)) continue;
            if (cues.Count + beatmaps.Count >= 4096) throw new MetadataReadException("folder_definition_limit");
            (extension.Equals(".cue", StringComparison.OrdinalIgnoreCase) ? cues : beatmaps).Add(file);
        }
        beatmaps.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        var facts = new FolderFileFacts(); var suppressed = new HashSet<string>(StringComparer.Ordinal);
        var deferred = false;
        foreach (var cue in cues)
        {
            FileSnapshot[] tracks;
            try
            {
                if (cue.Unavailable) throw new MetadataReadException("cloud_file_unavailable");
                tracks = CueFolderExpansion.Expand(cue, root, facts, suppressed, false, cancellation).ToArray();
            }
            catch (Exception error) when (error is MetadataReadException or IOException or UnauthorizedAccessException)
            { tracks = [cue with { Issue = Code(error) }]; deferred = true; }
            foreach (var track in tracks) yield return track;
        }
        var issues = new List<FileSnapshot>();
        var overlays = beatmaps.Count == 0 ? new Dictionary<string, FileSnapshot>(StringComparer.Ordinal) :
            OsuFolderExpansion.Collect(directory, beatmaps, store, facts, force, cancellation, issues);
        foreach (var issue in issues) yield return issue;
        if (deferred) yield break;
        foreach (var block in TopFiles(directory, full, cancellation)
            .Where(file => DirectoryDiscovery.Supports(Path.GetExtension(file.Path)) &&
                !Path.GetExtension(file.Path).Equals(".cue", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetExtension(file.Path).Equals(".osu", StringComparison.OrdinalIgnoreCase)).Chunk(128))
        {
            var flac = block.Where(file => Path.GetExtension(file.Path).Equals(".flac", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(file.Path).Equals(".fla", StringComparison.OrdinalIgnoreCase)).ToArray();
            var known = flac.Length == 0 ? null : store?.FindKnownAsync(flac, cancellation).GetAwaiter().GetResult();
            foreach (var file in block)
            {
                cancellation.ThrowIfCancellationRequested();
                if (suppressed.Contains(file.PathKey)) continue;
                if (overlays.TryGetValue(file.PathKey, out var overlay)) { yield return overlay; continue; }
                var extension = Path.GetExtension(file.Path);
                if (file.Unavailable || !extension.Equals(".flac", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".fla", StringComparison.OrdinalIgnoreCase)) { yield return file; continue; }
                var stamp = SourceStamp.Compute(file, file);
                var reusable = !force && known is not null && known.TryGetValue(file.PathKey, out var old) &&
                    !old.Missing && old.ParserVersion == TrackMetadata.ParserVersion &&
                    old.SizeBytes == file.SizeBytes && old.ModifiedTicks == file.ModifiedTicks && old.DependencyStamp == stamp;
                FileSnapshot[] embedded;
                try { embedded = reusable ? [] : CueFolderExpansion.Expand(file, root, facts, suppressed, true, cancellation).ToArray(); }
                catch (Exception error) when (error is MetadataReadException or IOException or UnauthorizedAccessException)
                { embedded = [file with { Issue = Code(error) }]; }
                if (embedded.Length > 0) foreach (var track in embedded) yield return track;
                else yield return file with { Source = new("local", file.Path, file.Path, stamp,
                    file.SizeBytes, file.ModifiedTicks, file.SizeBytes, file.ModifiedTicks) };
            }
        }
    }

    private static IEnumerable<FileSnapshot> TopFiles(string directory, bool full, CancellationToken cancellation)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = false, AttributesToSkip = 0 };
        var files = new FileSystemEnumerable<FileSnapshot>(directory, (ref FileSystemEntry entry) =>
        {
            var path = LibraryPath.Canonical(entry.ToFullPath());
            return new(path, LibraryPath.Key(path), entry.Length, entry.LastWriteTimeUtc.UtcTicks,
                ((int)entry.Attributes & ((int)FileAttributes.Offline | 0x40000 | 0x400000)) != 0);
        }, options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
            {
                cancellation.ThrowIfCancellationRequested();
                if (entry.IsDirectory) return false;
                if ((entry.Attributes & FileAttributes.ReparsePoint) == 0) return true;
                if (full) throw new IOException("Missing detection cannot exclude linked files.");
                return false;
            }
        };
        foreach (var file in files) yield return file;
    }
    private static string Code(Exception error) => error is MetadataReadException metadata ? metadata.Code : "source_unreadable";
}
