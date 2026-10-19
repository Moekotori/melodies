using System.IO.Enumeration;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;

namespace Echo.Infrastructure.Files;

// Scan-owned CUE references spill to disk; metadata facts remain bounded to the active folder.
public sealed class LegacyFolderDiscovery(ILibraryStore? store = null, bool recursive = true, Func<string, bool>? ignored = null) : IFileDiscovery
{
    public IEnumerable<FileSnapshot> Enumerate(string root, bool requireFullCoverage,
        CancellationToken cancellation, bool force = false)
    {
        root = LibraryPath.Canonical(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Scan root is unavailable.");
        using var suppressed = new FolderSourceSet();
        using var deferred = new FolderSourceSet();
        using var beatmapFolders = new FolderSourceSet();
        // CUE may reference a later sibling or a nested folder. Discover references before any
        // physical audio can reconcile away those segments; no whole-library path list is retained.
        foreach (var directory in Directories(root, requireFullCoverage, 0, cancellation))
        {
            var facts = new FolderFileFacts();
            var hasBeatmaps = false;
            foreach (var cue in TopFiles(directory, requireFullCoverage, cancellation, definitions: true))
            {
                if (ignored?.Invoke(cue.Path) == true)
                {
                    if (Path.GetExtension(cue.Path).Equals(".cue", StringComparison.OrdinalIgnoreCase))
                        foreach (var preserved in StoredCueProtection.Read(cue, store, suppressed, deferred, cancellation)) yield return preserved;
                    continue;
                }
                if (Path.GetExtension(cue.Path).Equals(".osu", StringComparison.OrdinalIgnoreCase))
                { hasBeatmaps = true; continue; }
                FileSnapshot[] tracks;
                try
                {
                    if (cue.Unavailable) throw new MetadataReadException("cloud_file_unavailable");
                    tracks = CueFolderExpansion.Expand(cue, root, facts, suppressed, false, cancellation, out var unresolved);
                    if (unresolved) deferred.Add(LibraryPath.Key(directory));
                }
                catch (Exception error) when (error is MetadataReadException or IOException or UnauthorizedAccessException)
                { tracks = [cue with { Issue = Code(error) }]; deferred.Add(LibraryPath.Key(directory)); }
                if (tracks.Any(track => track.Issue is not null))
                    foreach (var preserved in StoredCueProtection.Read(cue, store, suppressed, deferred, cancellation)) yield return preserved;
                foreach (var track in tracks)
                    yield return Included(track) ? track : track with { Source = null, PreserveExisting = true };
            }
            if (hasBeatmaps) beatmapFolders.Add(LibraryPath.Key(directory));
        }
        foreach (var directory in Directories(root, requireFullCoverage, 0, cancellation))
            foreach (var file in FolderFiles(directory, root, requireFullCoverage, force, suppressed,
                deferred.Contains(LibraryPath.Key(directory)), beatmapFolders.Contains(LibraryPath.Key(directory)), cancellation))
                if (Included(file) || file.PreserveExisting) yield return file;
    }

    private bool Included(FileSnapshot file) => ignored?.Invoke(file.Path) != true &&
        (file.Source is null || ignored?.Invoke(file.Source.AudioPath) != true);

    private IEnumerable<string> Directories(string directory, bool full, int depth, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (depth > 64) throw new MetadataReadException("scan_directory_depth_limit");
        yield return directory;
        if (!recursive) yield break;
        // Do not retain a list of tens of thousands of child directories.
        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            cancellation.ThrowIfCancellationRequested();
            if (ignored?.Invoke(child) == true) continue;
            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
            { if (full) throw new IOException("Missing detection cannot exclude linked directories."); continue; }
            foreach (var descendant in Directories(child, full, depth + 1, cancellation)) yield return descendant;
        }
    }

    private IEnumerable<FileSnapshot> FolderFiles(string directory, string root, bool full, bool force,
        FolderSourceSet suppressed, bool deferred, bool hasBeatmaps, CancellationToken cancellation)
    {
        var beatmaps = new List<FileSnapshot>();
        var facts = new FolderFileFacts();
        foreach (var cue in hasBeatmaps ? TopFiles(directory, full, cancellation, definitions: true) : [])
        {
            if (ignored?.Invoke(cue.Path) == true || !Path.GetExtension(cue.Path).Equals(".osu", StringComparison.OrdinalIgnoreCase)) continue;
            if (beatmaps.Count >= 4096) throw new MetadataReadException("osu_folder_definition_limit");
            beatmaps.Add(cue);
        }
        beatmaps.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        var issues = new List<FileSnapshot>();
        var overlays = beatmaps.Count == 0 ? new Dictionary<string, FileSnapshot>(StringComparer.Ordinal) :
            OsuFolderExpansion.Collect(directory, beatmaps, store, facts, force, cancellation, issues, suppressed);
        foreach (var issue in issues) yield return issue;
        if (deferred) yield break;
        foreach (var block in TopFiles(directory, full, cancellation, definitions: false).Chunk(128))
        {
            var flac = block.Where(file => Path.GetExtension(file.Path).Equals(".flac", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(file.Path).Equals(".fla", StringComparison.OrdinalIgnoreCase)).ToArray();
            var known = flac.Length == 0 ? null : store?.FindKnownAsync(flac, cancellation).GetAwaiter().GetResult();
            foreach (var file in block)
            {
                cancellation.ThrowIfCancellationRequested();
                if (ignored?.Invoke(file.Path) == true)
                {
                    if (Path.GetExtension(file.Path).Equals(".flac", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetExtension(file.Path).Equals(".fla", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var preserved in StoredCueProtection.Read(file, store, suppressed, null, cancellation)) yield return preserved;
                    }
                    yield return file with { PreserveExisting = true };
                    continue;
                }
                if (suppressed.Contains(file.PathKey)) continue;
                if (overlays.TryGetValue(file.PathKey, out var overlay)) { yield return overlay; continue; }
                var extension = Path.GetExtension(file.Path);
                if (file.Unavailable || !extension.Equals(".flac", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".fla", StringComparison.OrdinalIgnoreCase)) { yield return file; continue; }
                var stamp = SourceStamp.Compute(file, file, CueFolderExpansion.DiscoveryVersion);
                var reusable = !force && known is not null && known.TryGetValue(file.PathKey, out var old) &&
                    !old.Missing && old.ParserVersion == TrackMetadata.ParserVersion &&
                    old.SizeBytes == file.SizeBytes && old.ModifiedTicks == file.ModifiedTicks && old.DependencyStamp == stamp;
                FileSnapshot[] embedded;
                try { embedded = reusable ? [] : CueFolderExpansion.Expand(file, root, facts, suppressed, true, cancellation, out _); }
                catch (Exception error) when (error is MetadataReadException or IOException or UnauthorizedAccessException)
                { embedded = [file with { Issue = Code(error) }]; }
                if (embedded.Length > 0) foreach (var track in embedded) yield return track;
                else yield return file with { Source = new("local", file.Path, file.Path, stamp,
                    file.SizeBytes, file.ModifiedTicks, file.SizeBytes, file.ModifiedTicks) };
            }
        }
    }

    private static IEnumerable<FileSnapshot> TopFiles(string directory, bool full, CancellationToken cancellation, bool definitions)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = false, AttributesToSkip = 0, BufferSize = 16 * 1024 };
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
                var extension = Path.GetExtension(entry.FileName);
                var definition = extension.Equals(".cue", StringComparison.OrdinalIgnoreCase) || extension.Equals(".osu", StringComparison.OrdinalIgnoreCase);
                if (definitions ? !definition : definition || !ScanFormats.Supports(extension)) return false;
                if ((entry.Attributes & FileAttributes.ReparsePoint) == 0) return true;
                if (full) throw new IOException("Missing detection cannot exclude linked files.");
                return false;
            }
        };
        foreach (var file in files) yield return file;
    }
    private static string Code(Exception error) => error is MetadataReadException metadata ? metadata.Code : "source_unreadable";
}
