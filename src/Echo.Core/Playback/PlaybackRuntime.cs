using Echo.Playback;
using Echo.Playback.MediaTransport;
using System.Threading.Channels;
using Echo.Application.Scheduling;

namespace Echo.Application.Playback;

/// <summary>UI-independent owner of the local zone, checkpoint store and system media control.</summary>
public sealed class PlaybackRuntime : IPlaybackRuntime
{
    private readonly object gate = new();
    private readonly Func<CancellationToken, Task<IPlaybackCheckpointStore>>? openStore;
    private readonly IMediaTransport? transport;
    private readonly PlaybackMediaTransport? media;
    private IPlaybackCheckpointStore? store;
    private readonly Channel<bool> saves = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private Task? saving;
    private Task? startup, shutdown;
    private string? persistenceError, mediaError;

    public PlaybackZone LocalZone { get; }
    public PlaybackWorkAdmission BackgroundWork { get; }
    public SleepTimerService SleepTimer { get; }
    ISleepTimer IPlaybackRuntime.SleepTimer => SleepTimer;
    Echo.Contracts.Tasks.IBackgroundWorkAdmission IPlaybackRuntime.BackgroundWork => BackgroundWork;
    public string? PersistenceError => Volatile.Read(ref persistenceError);
    public string? MediaError => Volatile.Read(ref mediaError);
    public IMediaAccessory? Accessories => transport as IMediaAccessory;
    public event Action<string?>? PersistenceDiagnosticChanged;
    public event Action<string?>? MediaDiagnosticChanged;
    public event Action<IReadOnlyList<string>>? FilesOpened;

    public PlaybackRuntime(INativeAudio native,
        Func<CancellationToken, Task<IPlaybackCheckpointStore>>? openStore = null, IMediaTransport? transport = null,
        IPlaybackSourceProbe? sourceProbe = null, IPlaybackFileResolver? fileResolver = null)
    {
        this.openStore = openStore;
        this.transport = transport;
        LocalZone = new(PlaybackZone.LocalId, "本机", new PlaybackSessionCoordinator(native, sourceProbe, fileResolver));
        SleepTimer = new(LocalZone.Session);
        BackgroundWork = new(LocalZone.Session);
        if (transport is not null)
        {
            transport.DiagnosticChanged += OnMediaDiagnostic;
            if (transport is IMediaFileActivation files) files.FilesOpened += OnFilesOpened;
            media = new(LocalZone.Session, transport);
        }
    }

    // Cancellation cancels this caller's wait, not the shared initialization or another control's playback.
    public Task StartAsync(CancellationToken token = default)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(shutdown is not null, this);
            return (startup ??= Task.Run(InitializeAsync)).WaitAsync(token);
        }
    }

    private async Task InitializeAsync()
    {
        if (openStore is null) return;
        try
        {
            store = await openStore(CancellationToken.None).ConfigureAwait(false);
            if (await store.ReadAsync().ConfigureAwait(false) is { } json)
                await LocalZone.Session.RestoreCheckpointAsync(json).ConfigureAwait(false);
            LocalZone.Session.QueueChanged += OnQueueChanged;
            saves.Writer.TryWrite(true);
            saving = RunSavesAsync();
        }
        catch (Exception)
        {
            Volatile.Write(ref persistenceError, "播放状态无法恢复；仍可打开文件播放。");
            Notify(PersistenceDiagnosticChanged, PersistenceError);
        }
    }

    private void OnQueueChanged(QueueSnapshot _) => saves.Writer.TryWrite(true);
    private void OnFilesOpened(IReadOnlyList<string> paths) => FilesOpened?.Invoke(paths);
    private async Task RunSavesAsync()
    {
        await foreach (var signal in saves.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            while (saves.Reader.TryRead(out _)) { }
            await SaveCheckpointAsync().ConfigureAwait(false);
        }
    }

    private async Task SaveCheckpointAsync()
    {
        try { await store!.SaveAsync(await LocalZone.Session.ExportCheckpointAsync().ConfigureAwait(false)).ConfigureAwait(false); }
        catch (Exception)
        {
            Volatile.Write(ref persistenceError, "播放状态保存失败；下次启动可能恢复较早的状态。");
            Notify(PersistenceDiagnosticChanged, PersistenceError);
        }
    }

    public void RetryMediaTransport() => media?.Retry();
    internal void ReportPlatformDiagnostic(string? message) => OnMediaDiagnostic(message);
    private void OnMediaDiagnostic(string? message)
    {
        Volatile.Write(ref mediaError, message);
        Notify(MediaDiagnosticChanged, message);
    }

    private static void Notify(Action<string?>? handlers, string? message)
    {
        if (handlers is null) return;
        foreach (Action<string?> handler in handlers.GetInvocationList())
            try { handler(message); } catch (Exception) { /* A disconnected control cannot interrupt the owner. */ }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate) return new(shutdown ??= Task.Run(ShutdownAsync));
    }

    private async Task ShutdownAsync()
    {
        if (startup is not null) await startup.ConfigureAwait(false);
        await SleepTimer.DisposeAsync().ConfigureAwait(false);
        await BackgroundWork.DisposeAsync().ConfigureAwait(false);
        try
        {
            // Stop accepting system commands before capturing the session checkpoint.
            if (media is not null) await media.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            if (transport is not null)
            {
                transport.DiagnosticChanged -= OnMediaDiagnostic;
                if (transport is IMediaFileActivation files) files.FilesOpened -= OnFilesOpened;
            }
            LocalZone.Session.QueueChanged -= OnQueueChanged;
            saves.Writer.TryComplete();
            try
            {
                if (store is not null)
                {
                    if (saving is not null) await saving.ConfigureAwait(false);
                    await SaveCheckpointAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                try { await LocalZone.Session.DisposeAsync().ConfigureAwait(false); }
                finally
                {
                    if (store is not null) await store.DisposeAsync().ConfigureAwait(false);
                    PersistenceDiagnosticChanged = null;
                    MediaDiagnosticChanged = null;
                    FilesOpened = null;
                }
            }
        }
    }
}
