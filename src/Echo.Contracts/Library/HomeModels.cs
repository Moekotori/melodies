namespace Echo.Contracts.Library;

public sealed record HomeLibrarySummary(long Artists, long Albums, long Tracks, long Folders);
public sealed record ListeningEntry(string Id, string TrackId, string Path, long StartedUnix,
    long UpdatedUnix, double PlayedSeconds);
public sealed record ListeningDay(string Date, long Plays, double Seconds);
public sealed record ListeningArtist(string Name, long Plays, double Seconds);
public sealed record ListeningTrack(TrackRow Track, long Plays, double Seconds, long LastPlayedUnix);
public sealed record ListeningAlbum(AlbumRow Album, long Plays, double Seconds, long LastPlayedUnix);
public sealed record ListeningQuality(string Codec, double Seconds);
public sealed record HomeListeningSummary(long WeekPlays, double WeekSeconds, int ActiveDays,
    double AverageSeconds, double RepeatRate, IReadOnlyList<ListeningDay> Days,
    IReadOnlyList<ListeningArtist> Artists, IReadOnlyList<ListeningTrack> Tracks,
    IReadOnlyList<ListeningAlbum> Albums, IReadOnlyList<ListeningQuality> Quality);
public sealed record HomeDashboard(HomeLibrarySummary Library, IReadOnlyList<AlbumRow> RecentAdded,
    IReadOnlyList<AlbumRow> RecentPlayed, IReadOnlyList<AlbumRow> Recommended,
    TrackRow? LastTrack, HomeListeningSummary Listening);

// Home reads are bounded snapshots; listening writes use the same library write owner as scans.
public interface IHomeLibrary
{
    Task<HomeDashboard> GetHomeAsync(DateOnly today, CancellationToken cancellation = default);
    Task<IReadOnlyList<TrackRow>> GetRandomTracksAsync(CancellationToken cancellation = default);
    Task<AlbumRow?> GetTrackAlbumAsync(string path, CancellationToken cancellation = default);
    Task RecordListeningAsync(ListeningEntry entry, CancellationToken cancellation = default);
}
