namespace Echo.Playback.MediaTransport;

public sealed record MediaTransportState(string? ItemId, string Title, string Artist, string Album,
    string AlbumArtist, string? CoverPath, string Status, double PositionSeconds, double DurationSeconds,
    bool Play, bool Pause, bool Previous, bool Next, bool Seek, string Repeat, string? FilePath = null,
    string? OutputName = null);

/// <summary>Optional system surfaces such as a desktop card. Playback does not wait on them.</summary>
public interface IMediaAccessory
{
    void SetVisible(string surface, bool visible);
    void Invoke(string action);
    void SetLanguage(bool chinese);
    bool PauseOnOutputDisconnect { get; set; }
}

/// <summary>Finder and helper open-file events. The transport does not import them.</summary>
public interface IMediaFileActivation
{
    event Action<IReadOnlyList<string>>? FilesOpened;
}

public sealed record MediaTransportCommand(string Name, double PositionSeconds = 0, string? Mode = null);

/// <summary>Platform boundary; publishes small confirmed state, never owns playback.</summary>
public interface IMediaTransport : IAsyncDisposable
{
    event Action<MediaTransportCommand>? Command;
    event Action<string?>? DiagnosticChanged;
    void Publish(MediaTransportState state);
    void Retry();
}
