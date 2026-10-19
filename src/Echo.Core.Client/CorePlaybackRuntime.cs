using Echo.Application.Playback;
using Echo.Contracts.Tasks;
using Echo.Core.Contracts;
using Echo.Core.Transport;
using Echo.Playback;
using Echo.Playback.MediaTransport;

namespace Echo.Core.Client;

internal sealed class CorePlaybackRuntime : IPlaybackRuntime
{
    private readonly CoreWireClient wire;
    private readonly Func<Task> reconnect;
    private readonly CorePlaybackClient session;
    private readonly CoreSleepTimer timer;
    private readonly CoreMediaAccessory accessory;
    private readonly CoreBackgroundAdmission admission;
    private string? persistenceError, mediaError;
    public PlaybackZone LocalZone { get; }
    public IBackgroundWorkAdmission BackgroundWork { get; }
    public ISleepTimer SleepTimer => timer;
    public string? PersistenceError => persistenceError;
    public string? MediaError => mediaError;
    public IMediaAccessory? Accessories => accessory;
    public event Action<string?>? PersistenceDiagnosticChanged;
    public event Action<string?>? MediaDiagnosticChanged;
    public event Action<IReadOnlyList<string>>? FilesOpened;

    internal CorePlaybackRuntime(CoreWireClient wire, Func<Task> reconnect)
    {
        this.wire = wire; this.reconnect = reconnect; session = new(wire); timer = new(wire); accessory = new(wire);
        LocalZone = new(PlaybackZone.LocalId, "本机", session);
        admission = new(wire); BackgroundWork = admission;
        wire.Received += Receive; wire.Disconnected += OnDisconnected;
    }
    private void Receive(CoreMessage message)
    {
        if (message.Playback is { } frame)
        {
            if (frame.PersistenceError != persistenceError) { persistenceError = frame.PersistenceError; PersistenceDiagnosticChanged?.Invoke(persistenceError); }
            if (frame.MediaError != mediaError) { mediaError = frame.MediaError; MediaDiagnosticChanged?.Invoke(mediaError); }
        }
        if (message.Method == "media.files") FilesOpened?.Invoke(CoreJson.Read<string[]>(message.Payload));
    }
    private void OnDisconnected(Exception _) { mediaError = "与 Core 的连接已断开，请重试连接。"; MediaDiagnosticChanged?.Invoke(mediaError); }
    public Task StartAsync(CancellationToken token = default) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public void RetryMediaTransport() => _ = RetryAsync();
    private async Task RetryAsync()
    {
        try { if (!wire.IsConnected) await reconnect().ConfigureAwait(false); else accessory.Send("retry", "", false); }
        catch (Exception) { mediaError = "Core 重连失败，请检查服务后重试。"; MediaDiagnosticChanged?.Invoke(mediaError); }
    }
    public async ValueTask DisposeAsync()
    { wire.Received -= Receive; wire.Disconnected -= OnDisconnected; timer.Detach(); admission.Detach(); await session.DisposeAsync().ConfigureAwait(false); }
}

internal sealed class CoreSleepTimer : ISleepTimer
{
    private readonly CoreWireClient wire;
    private Func<Task>? quit;
    private SleepTimerState state;
    public SleepTimerState State => Volatile.Read(ref state);
    public event Action<SleepTimerState>? Changed;
    public CoreSleepTimer(CoreWireClient wire)
    { this.wire = wire; state = wire.InitialPlayback!.Sleep; wire.Received += Receive; }
    private void Receive(CoreMessage message)
    {
        if (message.Playback is { } frame && frame.Sleep != state) { Volatile.Write(ref state, frame.Sleep); Changed?.Invoke(frame.Sleep); }
        if (message.Method == "core.quit" && quit is { } handler) _ = RunQuit(handler);
    }
    private static async Task RunQuit(Func<Task> handler) { try { await handler().ConfigureAwait(false); } catch (Exception) { } }
    public void SetQuitHandler(Func<Task>? handler) => quit = handler;
    public Task StartAsync(double minutes, SleepTimerAction action, CancellationToken token = default)
        => wire.CallAsync("sleep.start", CoreJson.Value(new CoreSleepRequest(minutes, action)), token);
    public Task CancelAsync(CancellationToken token = default) => wire.CallAsync("sleep.cancel", CoreJson.Empty, token);
    public void Detach() { wire.Received -= Receive; quit = null; }
}

internal sealed class CoreBackgroundAdmission : IBackgroundWorkAdmission
{
    // Metadata production is entirely Core-owned. Controls can only wait for visible rendering admission.
    private readonly CoreWireClient wire;
    private BackgroundAdmissionSnapshot snapshot = new(PlaybackWorkPressure.Normal, PlaybackWorkReason.TelemetryUnknown, 0, 0, 0, 0);
    public BackgroundAdmissionSnapshot Snapshot => Volatile.Read(ref snapshot);
    public CoreBackgroundAdmission(CoreWireClient wire)
    { this.wire = wire; if (wire.InitialPlayback?.Admission is { } initial) snapshot = initial; wire.Received += Receive; }
    private void Receive(CoreMessage message)
    { if (message.Playback?.Admission is { } value) Volatile.Write(ref snapshot, value); }
    public void Detach() => wire.Received -= Receive;
    public ValueTask<IDisposable> EnterMetadataAsync(CancellationToken cancellation)
        => ValueTask.FromException<IDisposable>(new InvalidOperationException("Metadata work must be submitted to Core."));
    public async ValueTask WaitAsync(BackgroundWorkKind kind, CancellationToken cancellation)
    {
        if (kind != BackgroundWorkKind.VisibleArtwork) throw new InvalidOperationException("Background production belongs to Core.");
        await wire.CallAsync("background.wait", CoreJson.Value(kind), cancellation).ConfigureAwait(false);
    }
}

internal sealed class CoreMediaAccessory(CoreWireClient wire) : IMediaAccessory
{
    private bool pauseOnOutputDisconnect;
    public bool PauseOnOutputDisconnect { get => pauseOnOutputDisconnect; set { pauseOnOutputDisconnect = value; Send("pauseOnDisconnect", "", value); } }
    public void SetVisible(string surface, bool visible) => Send("visible", surface, visible);
    public void Invoke(string action) => Send("invoke", action, false);
    public void SetLanguage(bool chinese) => Send("language", "", chinese);
    public void Send(string action, string value, bool enabled) => _ = SendAsync(new CoreAccessoryRequest(action, value, enabled));
    private async Task SendAsync(CoreAccessoryRequest request)
    { try { await wire.CallAsync("media.accessory", CoreJson.Value(request)).ConfigureAwait(false); } catch (Exception) { } }
}
