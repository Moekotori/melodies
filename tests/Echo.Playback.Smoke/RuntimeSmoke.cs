using System.Text.Json;
using Echo.Application.Playback;
using Echo.Infrastructure.Playback;
using Echo.Playback;
using Echo.Playback.MediaTransport;

internal static class RuntimeSmoke
{
    public static async Task RunAsync(string root, string track)
    {
        var path = Path.Combine(root, "runtime-" + Guid.NewGuid().ToString("N") + ".sqlite");
        var native = new RuntimeHost();
        var system = new RuntimeMedia();
        var opens = 0;
        var runtime = new PlaybackRuntime(native, async token =>
        { Interlocked.Increment(ref opens); return await PlaybackStateDatabase.OpenAsync(path, token); }, system);
        await Task.WhenAll(runtime.StartAsync(), runtime.StartAsync());
        Check(opens == 1, "two controls opened duplicate checkpoint owners");
        var zone = runtime.LocalZone;
        var session = zone.Session;
        await session.PlayAsync(new("runtime-track", "track", track));
        await session.PauseAsync();
        await session.SeekAsync(1);
        var originalQueue = session.Queue;
        await session.ConfigureOutputAsync(new AudioOutputConfiguration());
        Check(ReferenceEquals(originalQueue, session.Queue) && zone.Id == PlaybackZone.LocalId,
            "output change replaced the zone or queue");
        Check(zone.Output.HasConfirmedDevice && !zone.Output.IsRendering, "paused device state is confused with rendering");
        system.Send(new("seek", 2));
        await WaitAsync(() => session.Snapshot.PositionSeconds == 2);
        var stops = new[] { runtime.DisposeAsync().AsTask(), runtime.DisposeAsync().AsTask() };
        await Task.WhenAll(stops);
        Check(native.Disposals == 1 && system.Disposals == 1, "shutdown released shared resources more than once");
        Check(!zone.Output.HasConfirmedDevice && session.Snapshot.Status == PlaybackStatus.Stopped,
            "released native output still appeared connected");

        var restoredNative = new RuntimeHost();
        await using (var restored = new PlaybackRuntime(restoredNative, async token => await PlaybackStateDatabase.OpenAsync(path, token)))
        {
            await restored.StartAsync();
            Check(restored.LocalZone.Session.Queue.Count == 1 && restored.LocalZone.Session.Snapshot.PositionSeconds == 2,
                "core-owned SQLite checkpoint lost queue or position");
            Check(restored.LocalZone.Session.Snapshot.Status == PlaybackStatus.Paused && restoredNative.Starts == 0,
                "restore must not start the audio host or autoplay");
            Check(!restored.LocalZone.Output.HasConfirmedDevice, "saved output request was presented as a live device");
        }

        var delayed = new RuntimeStore();
        var failingNative = new RuntimeHost();
        var failingSystem = new RuntimeMedia();
        await using (var late = new PlaybackRuntime(failingNative, _ => Task.FromResult<IPlaybackCheckpointStore>(delayed), failingSystem))
        {
            var startup = late.StartAsync();
            await delayed.Reading.Task;
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            try { await late.StartAsync(canceled.Token); throw new Exception("caller cancellation was ignored"); }
            catch (OperationCanceledException) { }
            await late.LocalZone.Session.SetVolumeAsync(0.3);
            delayed.Read.TrySetResult("invalid old checkpoint");
            await startup;
            Check(late.PersistenceError is null && late.LocalZone.Session.Snapshot.Volume == 0.3,
                "late restore overwrote a new control action");
            delayed.FailSave = true;
            late.PersistenceDiagnosticChanged += _ => throw new Exception("broken control");
        }
        Check(failingNative.Disposals == 1 && failingSystem.Disposals == 1 && delayed.Disposals == 1,
            "checkpoint failure leaked audio, media control or store");
        Console.WriteLine("PASS runtime owner, shared startup, local zone/output identity, system command routing, SQLite restore, cancellation, shutdown failure cleanup");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task WaitAsync(Func<bool> condition)
    {
        using var limit = new CancellationTokenSource(1000);
        while (!condition()) await Task.Delay(5, limit.Token);
    }

    private sealed class RuntimeStore : IPlaybackCheckpointStore
    {
        public TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string?> Read { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FailSave { get; set; }
        public int Disposals { get; private set; }
        public Task<string?> ReadAsync(CancellationToken token = default) { Reading.TrySetResult(); return Read.Task; }
        public Task SaveAsync(string json, CancellationToken token = default)
            => FailSave ? Task.FromException(new IOException("save failed")) : Task.CompletedTask;
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    private sealed class RuntimeMedia : IMediaTransport
    {
        public event Action<MediaTransportCommand>? Command;
        public event Action<string?>? DiagnosticChanged { add { } remove { } }
        public int Disposals { get; private set; }
        public void Send(MediaTransportCommand command) => Command?.Invoke(command);
        public void Publish(MediaTransportState state) { }
        public void Retry() { }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    private sealed class RuntimeHost : INativeAudio
    {
        private int operation;
        public int Starts { get; private set; }
        public int Disposals { get; private set; }
        public event Action<string, JsonElement>? Notification { add { } remove { } }
        public event Action<Exception>? Failed { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken = default) { Starts++; return Task.CompletedTask; }
        public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
        {
            var json = method switch
            {
                "queue.set" => $"{{\"queueRevision\":{parameters!.Value.GetProperty("revision").GetInt64()}}}",
                "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000,\"deviceName\":\"runtime device\"}}",
                "device.configure" => "{\"accepted\":true,\"sampleRate\":48000,\"channels\":2,\"deviceName\":\"runtime device\"}",
                "audio.openFile" => $"{{\"operationId\":{++operation},\"sampleRate\":48000,\"durationSeconds\":10}}",
                "audio.seek" => $"{{\"operationId\":{++operation}}}",
                _ => "true"
            };
            using var document = JsonDocument.Parse(json);
            return Task.FromResult(document.RootElement.Clone());
        }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
