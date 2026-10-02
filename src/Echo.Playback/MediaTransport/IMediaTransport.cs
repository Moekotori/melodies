namespace Echo.Playback.MediaTransport;

public sealed record MediaTransportState(string? ItemId, string Title, string Artist, string Album,
    string AlbumArtist, string? CoverPath, string Status, double PositionSeconds, double DurationSeconds,
    bool Play, bool Pause, bool Previous, bool Next, bool Seek, string Repeat);

public sealed record MediaTransportCommand(string Name, double PositionSeconds = 0, string? Mode = null);

/// <summary>Platform boundary; publishes small confirmed state, never owns playback.</summary>
public interface IMediaTransport : IAsyncDisposable
{
    event Action<MediaTransportCommand>? Command;
    event Action<string?>? DiagnosticChanged;
    void Publish(MediaTransportState state);
    void Retry();
}
