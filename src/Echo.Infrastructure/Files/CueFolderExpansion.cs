using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Core.Media;
using Echo.Core.Protocol;

namespace Echo.Infrastructure.Files;

internal static class CueFolderExpansion
{
    // Only CUE/FLAC discovery fingerprints are invalidated; ordinary audio keeps incremental reuse.
    internal const string DiscoveryVersion = "cue-discovery/v5";
    public static FileSnapshot[] Expand(FileSnapshot definition, string root,
        FolderFileFacts facts, FolderSourceSet suppressed, bool embedded, CancellationToken cancellation,
        out bool unresolvedSources)
    {
        var result = ScanCueReader.Read(new ScanCueRequest
        { FilePath = definition.Path, Embedded = embedded, RootPath = root, IsolateFileErrors = true }, cancellation);
        unresolvedSources = false;
        if (result.Issue is { } issue)
        {
            // A failed sheet must not turn its physical audio into a replacement for old segments.
            // Named sources can be protected without blocking unrelated files in the folder.
            unresolvedSources = !embedded && result.SourcePaths is null;
            foreach (var path in result.SourcePaths ?? []) suppressed.Add(LibraryPath.Key(path));
            return [definition with { Issue = issue }];
        }
        var output = new List<FileSnapshot>(result.Tracks.Count);
        // Protect every declared source before probing any one of them (cloud/link/probe failures).
        foreach (var track in result.Tracks) suppressed.Add(LibraryPath.Key(track.AudioPath));
        foreach (var track in result.Tracks)
        {
            cancellation.ThrowIfCancellationRequested();
            if (track.CueTrack is not { } cue) continue;
            // Parser validation rejects repeated numbers before producing legacy virtual identities.
            var virtualPath = definition.Path + "#cueTrack=" + track.TrackNumber;
            var audio = facts.Read(track.AudioPath);
            var source = new TrackSource("cue", audio.Path, definition.Path, SourceStamp.Compute(audio, definition, DiscoveryVersion),
                audio.SizeBytes, audio.ModifiedTicks, definition.SizeBytes, definition.ModifiedTicks,
                cue.Title, cue.Performer, cue.Album, cue.AlbumArtist, cue.StartSeconds, cue.EndSeconds, cue.TrackNumber);
            output.Add(new(virtualPath, LibraryPath.Key(virtualPath), audio.SizeBytes,
                Math.Max(audio.ModifiedTicks, definition.ModifiedTicks), Source: source));
        }
        return output.ToArray();
    }
}
