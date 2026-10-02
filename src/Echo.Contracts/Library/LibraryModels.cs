namespace Echo.Contracts.Library;

public sealed record FileSnapshot(string Path, string PathKey, long SizeBytes, long ModifiedTicks, bool Unavailable = false);
public sealed record TrackMetadata(
    string Title, string Artist, string Album, string AlbumArtist,
    double DurationSeconds, string Codec, int SampleRate, int BitDepth,
    int? TrackNumber = null, int? DiscNumber = null, int? Year = null, string Genre = "")
{
    // Version changes invalidate incremental metadata reuse.
    public const int ParserVersion = 1;
}
public sealed record KnownTrack(string Id, long SizeBytes, long ModifiedTicks, int ParserVersion, bool Missing, string Path = "");
public enum ScanEntryKind { Metadata, Unchanged, Error }
public sealed record ScanEntry(FileSnapshot File, ScanEntryKind Kind,
    TrackMetadata? Metadata = null, string? ExistingId = null, string? ErrorCode = null);
public sealed record BatchReceipt(int Inserted, int Updated, int Unchanged, int Failed, long Revision);
public sealed record TrackRow(string Id, string Path, string Title, string Artist,
    string Album, double DurationSeconds, string Codec, int SampleRate, int BitDepth, string SortKey,
    int? TrackNumber = null, int? DiscNumber = null, bool Favorite = false);
public sealed record TrackCursor(string SortKey, string Id, long Revision);
public sealed record TrackQuery(int Limit = 100, string? Search = null, TrackCursor? After = null);
public sealed record TrackPage(IReadOnlyList<TrackRow> Items, TrackCursor? Next, long Revision);
public sealed record LibraryStats(long Tracks, long Missing, long Revision);
public sealed class LibraryChangedException() : Exception("Library changed; refresh the first page.");
public sealed class LibraryBusyException() : Exception("The bounded database lane is busy.");
