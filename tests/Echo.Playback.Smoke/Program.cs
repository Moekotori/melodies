using System.Diagnostics;
using System.Text.Json;
using Echo.Playback;
using Echo.Playback.NativeAudio;

var root = Path.GetFullPath(Path.Combine("misc", "playback-smoke"));
Directory.CreateDirectory(root);
var a = Path.Combine(root, "a.wav");
var b = Path.Combine(root, "b.wav");
Wave(a, 2.5); Wave(b, 0.6);
if (args.Contains("--admission-only")) { await AdmissionSmoke.RunAsync(a, root); await StateSmoke.RunAsync(a); return; }
if (args.Contains("--state-only")) { await StateSmoke.RunAsync(a); await RuntimeSmoke.RunAsync(root, a); return; }
if (args.Contains("--runtime-only")) { await RuntimeSmoke.RunAsync(root, a); return; }
await FakeAsync(a, b);
await QueueScenarios.RunAsync(a, b);
await RuntimeSmoke.RunAsync(root, a);
if (args.Length == 0) { Console.WriteLine("PASS session ownership, stale events, queue revision, pause/seek/stop"); return; }
var watch = Stopwatch.StartNew();
var host = new NativeAudioHost(args[0]);
await using (var player = new PlaybackSessionCoordinator(host))
{
    var items = new[] { new QueueTrack("a", "a", a), new QueueTrack("b", "b", b) };
    var advanced = false;
    var playing = false;
    player.Changed += state => { if (state.Current?.ItemId == "b" && state.Status == PlaybackStatus.Playing) advanced = true; if (state.Status == PlaybackStatus.Playing) playing = true; };
    var devices = await player.GetDevicesAsync();
    Console.WriteLine($"Native device enumeration: {devices.GetArrayLength()} entries");
    await player.SetVolumeAsync(0); // Real device render, silent fixture.
    using var rate = JsonDocument.Parse("[{\"rate\":1,\"mode\":\"speed\"}]");
    await player.DspAsync("playbackRate.configure", rate.RootElement, true);
    await player.SetQueueAsync(items, "a");
    await UntilAsync(() => playing, 3500);
    await player.PauseAsync();
    Check(player.Snapshot.Status == PlaybackStatus.Paused, "native pause receipt");
    await player.SeekAsync(1.6);
    await player.ResumeAsync();
    await UntilAsync(() => advanced, 3500);
    await UntilAsync(() => player.Snapshot.Status == PlaybackStatus.Ended, 3500);
    await player.StopAsync();
    Check(player.Snapshot.OperationId == 0, "native stop ownership");
    await player.ResumeAsync();
    await UntilAsync(() => player.Snapshot.Status == PlaybackStatus.Playing, 2000);
    var oldPid = host.ProcessId!.Value;
    using (var process = Process.GetProcessById(oldPid)) process.Kill();
    await UntilAsync(() => player.Snapshot.Status == PlaybackStatus.Faulted, 2000);
    await player.RecoverAsync();
    await UntilAsync(() => player.Snapshot.Status == PlaybackStatus.Playing, 2500);
    Check(host.ProcessId != oldPid, "host recovery reused dead process");
    await QueueScenarios.NativeAsync(player, a, b);
    var checkpoint = await player.ExportCheckpointAsync();
    await using var restored = new PlaybackSessionCoordinator(new FakeHost());
    await restored.RestoreCheckpointAsync(checkpoint);
    Check(restored.Queue.Count == 2 && restored.Snapshot.Status == PlaybackStatus.Paused, "checkpoint restore must not autoplay");
}
Console.WriteLine($"PASS native decode/output, DSP, pause, seek/resume, autonomous advance, drain/end, process restart, checkpoint, disposal ({watch.ElapsedMilliseconds} ms)");

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static async Task UntilAsync(Func<bool> condition, int ms)
{
    var clock = Stopwatch.StartNew();
    while (!condition()) { if (clock.ElapsedMilliseconds > ms) throw new TimeoutException("playback smoke condition"); await Task.Delay(15); }
}
static void Wave(string path, double seconds)
{
    using var file = File.Create(path); using var writer = new BinaryWriter(file);
    var size = (int)(48000 * seconds) * 4;
    writer.Write("RIFF"u8); writer.Write(36 + size); writer.Write("WAVEfmt "u8); writer.Write(16);
    writer.Write((short)1); writer.Write((short)2); writer.Write(48000); writer.Write(192000);
    writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8); writer.Write(size); writer.Write(new byte[size]);
}
static async Task FakeAsync(string a, string b)
{
    var native = new FakeHost();
    await using var player = new PlaybackSessionCoordinator(native);
    await player.SetQueueAsync([new("a", "a", a), new("b", "b", b)], "a");
    native.Emit("audio.started", "{\"operationId\":1}");
    await UntilAsync(() => player.Snapshot.Status == PlaybackStatus.Playing, 500);
    await player.NextAsync();
    native.Emit("audio.ended", "{\"operationId\":1}");
    await player.SetVolumeAsync(0.7); // Barrier behind notification.
    Check(player.Snapshot.Current?.ItemId == "b" && player.Snapshot.Status != PlaybackStatus.Ended, "stale EOF changed current item");
    await player.PauseAsync(); await player.SeekAsync(1);
    native.Emit("audio.position", "{\"operationId\":2,\"framesPlayed\":99999}");
    await player.SetVolumeAsync(0.6);
    Check(player.Snapshot.PositionSeconds == 1 && player.Snapshot.Status == PlaybackStatus.Paused, "stale seek position");
    await player.StopAsync();
    native.Emit("audio.started", "{\"operationId\":3}");
    await player.SetVolumeAsync(0.5);
    Check(player.Snapshot.Status == PlaybackStatus.Stopped, "late started after stop");
    Check(native.Calls.IndexOf("queue.set") < native.Calls.IndexOf("audio.openFile"), "queue admission ordering");
}

sealed class FakeHost : INativeAudio
{
    public JsonElement LastQueue { get; private set; }
    public bool RejectQueueOnce { get; set; }
    public List<string> Calls { get; } = [];
    public event Action<string, JsonElement>? Notification;
    public event Action<Exception>? Failed { add { } remove { } }
    private int operation;
    public void Emit(string method, string json) => Notification?.Invoke(method, JsonDocument.Parse(json).RootElement.Clone());
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
    {
        Calls.Add(method);
        if (method == "queue.set")
        {
            if (RejectQueueOnce) { RejectQueueOnce = false; return Task.FromResult(JsonDocument.Parse("{}").RootElement.Clone()); }
            LastQueue = parameters!.Value.Clone();
        }
        var json = method switch
        {
            "queue.set" => $"{{\"queueRevision\":{parameters!.Value.GetProperty("revision").GetInt64()}}}",
            "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000,\"format\":\"float32le\"}}",
            "audio.openFile" => $"{{\"operationId\":{++operation},\"sampleRate\":48000,\"durationSeconds\":10}}",
            "audio.seek" => $"{{\"operationId\":{++operation}}}",
            _ => "true"
        };
        return Task.FromResult(JsonDocument.Parse(json).RootElement.Clone());
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
