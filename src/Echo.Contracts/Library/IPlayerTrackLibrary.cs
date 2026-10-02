namespace Echo.Contracts.Library;

// A single indexed lookup for the active track, using the existing database owner.
public interface IPlayerTrackLibrary
{
    Task<TrackRow?> FindTrackByPathAsync(string path, CancellationToken cancellation);
    Task SetTrackFavoriteAsync(string trackId, bool favorite, CancellationToken cancellation);
}
