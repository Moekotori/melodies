namespace Echo.Contracts.Library;

public sealed record FileSnapshot(string Path, string PathKey, long SizeBytes, long ModifiedTicks,
    bool Unavailable = false, TrackSource? Source = null, string? Issue = null, bool PreserveExisting = false);
public sealed record TrackMetadata(
    string Title, string Artist, string Album, string AlbumArtist,
    double DurationSeconds, string Codec, int SampleRate, int BitDepth,
    int? TrackNumber = null, int? DiscNumber = null, int? Year = null, string Genre = "", int? Bitrate = null, double? Bpm = null)
{
    // Version changes invalidate incremental metadata reuse.
    public const int ParserVersion = 3;

    // Upgrade only affected physical sources, including CUE/osu backed by WAV.
    public static int VersionFor(FileSnapshot file)
    {
        var extension = System.IO.Path.GetExtension((file.Source?.AudioPath ?? file.Path).AsSpan());
        return extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".wave", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bwf", StringComparison.OrdinalIgnoreCase) ? 4 : ParserVersion;
    }
}
public sealed record KnownTrack(string Id, long SizeBytes, long ModifiedTicks, int ParserVersion, bool Missing,
    string Path = "", string? DependencyStamp = null);
public enum ScanEntryKind { Metadata, Unchanged, Error, Preserved }
public sealed record ScanEntry(FileSnapshot File, ScanEntryKind Kind,
    TrackMetadata? Metadata = null, string? ExistingId = null, string? ErrorCode = null);
public sealed record BatchReceipt(int Inserted, int Updated, int Unchanged, int Failed, long Revision);
public sealed record TrackRow(string Id, string Path, string Title, string Artist,
    string Album, double DurationSeconds, string Codec, int SampleRate, int BitDepth, string SortKey,
    int? TrackNumber = null, int? DiscNumber = null, bool Favorite = false, TrackSource? Source = null,
    string Genre = "");
public sealed record TrackCursor(string SortKey, string Id, long Revision, int Offset = 0, string? QueryKey = null);
public sealed record TrackQuery(int Limit = 100, string? Search = null, TrackCursor? After = null,
    IReadOnlyList<TrackOrder>? Order = null, LibraryFilter? Filter = null, int Offset = 0, long? Revision = null, int RandomSeed = 1);
public sealed record TrackPage(IReadOnlyList<TrackRow> Items, TrackCursor? Next, long Revision, int? Total = null, double? TotalDurationSeconds = null);
public sealed record LibraryStats(long Tracks, long Missing, long Revision);
public sealed class LibraryChangedException() : Exception("Library changed; refresh the first page.");
public sealed class LibraryBusyException() : Exception("The bounded database lane is busy.");
