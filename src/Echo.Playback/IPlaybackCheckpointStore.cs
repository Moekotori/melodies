namespace Echo.Playback;

/// <summary>The application owns this store. Implementations serialize access to the independent playback database.</summary>
public interface IPlaybackCheckpointStore : IAsyncDisposable
{
    Task<string?> ReadAsync(CancellationToken token = default);
    Task SaveAsync(string json, CancellationToken token = default);
}
