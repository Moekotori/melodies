using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Core.Media;
using Echo.Core.Protocol;

namespace Echo.Infrastructure.Files;

internal static class CueFolderExpansion
{
    public static IEnumerable<FileSnapshot> Expand(FileSnapshot definition, string root,
        FolderFileFacts facts, FolderSourceSet suppressed, bool embedded, CancellationToken cancellation)
    {
        var result = ScanCueReader.Read(new ScanCueRequest
        { FilePath = definition.Path, Embedded = embedded, RootPath = root, IsolateFileErrors = true }, cancellation);
        if (result.Issue is not null) throw new MetadataReadException(result.Issue);
        if (result.Tracks.Count <= 1) yield break; // Same legacy expansion rule.
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var track in result.Tracks)
        {
            cancellation.ThrowIfCancellationRequested();
            if (track.CueTrack is not { } cue) continue;
            // Legacy identity is cuePath#cueTrack=N. A repeated number cannot share that path.
            var virtualPath = definition.Path + "#cueTrack=" + track.TrackNumber;
            if (!identities.Add(virtualPath)) continue;
            var audio = facts.Read(track.AudioPath);
            suppressed.Add(audio.PathKey);
            var source = new TrackSource("cue", audio.Path, definition.Path, SourceStamp.Compute(audio, definition),
                audio.SizeBytes, audio.ModifiedTicks, definition.SizeBytes, definition.ModifiedTicks,
                cue.Title, cue.Performer, cue.Album, cue.AlbumArtist, cue.StartSeconds, cue.EndSeconds, cue.TrackNumber);
            yield return new(virtualPath, LibraryPath.Key(virtualPath), audio.SizeBytes,
                Math.Max(audio.ModifiedTicks, definition.ModifiedTicks), Source: source);
        }
    }
}
