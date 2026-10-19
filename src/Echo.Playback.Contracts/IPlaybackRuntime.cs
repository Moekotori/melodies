using Echo.Contracts.Tasks;
using Echo.Playback;
using Echo.Playback.MediaTransport;

namespace Echo.Application.Playback;

public interface ISleepTimer
{
    SleepTimerState State { get; }
    event Action<SleepTimerState>? Changed;
    void SetQuitHandler(Func<Task>? handler);
    Task StartAsync(double minutes, SleepTimerAction action, CancellationToken token = default);
    Task CancelAsync(CancellationToken token = default);
}

public interface IPlaybackRuntime : IAsyncDisposable
{
    PlaybackZone LocalZone { get; }
    IBackgroundWorkAdmission BackgroundWork { get; }
    ISleepTimer SleepTimer { get; }
    string? PersistenceError { get; }
    string? MediaError { get; }
    IMediaAccessory? Accessories { get; }
    event Action<string?>? PersistenceDiagnosticChanged;
    event Action<string?>? MediaDiagnosticChanged;
    event Action<IReadOnlyList<string>>? FilesOpened;
    Task StartAsync(CancellationToken token = default);
    void RetryMediaTransport();
}
