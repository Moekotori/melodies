using System.Diagnostics;
using System.Text.Json;
using Echo.Playback;
using Echo.Playback.MediaTransport;
using Echo.Platform.Windows;
using Echo.Playback.NativeAudio;

var root = Path.GetFullPath("misc/media-transport-smoke");
Directory.CreateDirectory(root);
var path = Path.Combine(root, "fixture.wav");
using (var writer = new BinaryWriter(File.Create(path)))
{
    const int size = 48000 * 10 * 4;
    writer.Write("RIFF"u8); writer.Write(36 + size); writer.Write("WAVEfmt "u8); writer.Write(16);
    writer.Write((short)1); writer.Write((short)2); writer.Write(48000); writer.Write(192000);
    writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8); writer.Write(size); writer.Write(new byte[size]);
}
var native = new FakeAudio();
await using var session = new PlaybackSessionCoordinator(native);
var fake = new FakeTransport();
var bridge = new PlaybackMediaTransport(session, fake);
var tracks = new[] {
    new QueueTrack("one", "one", path, Metadata: new Dictionary<string, string?> { ["title"] = "ECHO SMTC smoke 一", ["artist"] = "Echo test", ["album"] = "Migration",
        ["coverPath"] = Path.GetFullPath("src/Echo.Desktop/Resources/Icons/player/play-base.png") }),
    new QueueTrack("two", "two", path, Metadata: new Dictionary<string, string?> { ["title"] = "ECHO SMTC smoke 二" }) };
await session.SetQueueAsync(tracks, "one");
await session.SetVolumeAsync(0); // Barrier behind confirmed started notification.
Check(fake.Latest is { Title: "ECHO SMTC smoke 一", Status: "playing", Next: true, Seek: true }, "confirmed state mapping");
fake.Send(new("pause")); await Until(() => session.Snapshot.Status == PlaybackStatus.Paused);
fake.Send(new("seek", 3)); await Until(() => session.Snapshot.PositionSeconds == 3);
fake.Send(new("setRepeat", Mode: "list")); await Until(() => session.Repeat == RepeatMode.All);
fake.Send(new("next")); await Until(() => session.Snapshot.Current?.ItemId == "two");
await session.SetVolumeAsync(0);
fake.Send(new("stop")); await Until(() => session.Snapshot.Status == PlaybackStatus.Stopped);
fake.Send(new("play")); await Until(() => session.Snapshot.Status == PlaybackStatus.Playing);
await bridge.DisposeAsync();
fake.Send(new("pause")); await session.SetVolumeAsync(0);
Check(fake.Disposed && session.Snapshot.Status == PlaybackStatus.Playing, "unsubscribe and disposal");
var loading = MediaTransportMapping.Create(session.Snapshot with { Status = PlaybackStatus.Loading, OperationId = 0 }, tracks, RepeatMode.Off);
Check(loading.PositionSeconds == 0 && loading.DurationSeconds == 0 && loading.Status == "loading", "no old timeline during switch");
var invalid = MediaTransportMapping.Create(session.Snapshot with { PositionSeconds = double.NaN, DurationSeconds = double.PositiveInfinity }, tracks, RepeatMode.Off);
Check(invalid.PositionSeconds == 0 && invalid.DurationSeconds == 0, "nonfinite timeline");
Check(MediaTransportMapping.Create(session.Snapshot, [], RepeatMode.Off).ItemId is null, "removed queue clears system session");
Console.WriteLine("PASS SMTC mapping, pause/seek/next/repeat/stop/restart routing, detach/disposal");

if (args.Length == 0) return;
await using var liveSession = args.Length > 2 ? new PlaybackSessionCoordinator(new NativeAudioHost(args[2])) : null;
var systemSession = liveSession ?? session;
var transport = new WindowsSmtcTransport(args[0]);
var diagnostics = new List<string>();
var ready = false;
transport.DiagnosticChanged += message => { if (message is null) ready = true; else { lock (diagnostics) diagnostics.Add(message); } };
await using (var live = new PlaybackMediaTransport(systemSession, transport))
{
    await systemSession.SetVolumeAsync(0);
    await systemSession.SetRepeatAsync(RepeatMode.Off);
    await systemSession.SetQueueAsync(tracks, "one");
    await Until(() => systemSession.Snapshot.Status == PlaybackStatus.Playing);
    await Until(() => ready);
    if (args.Length > 1)
    {
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", args[1] }) start.ArgumentList.Add(argument);
        using var probe = Process.Start(start)!;
        var output = probe.StandardOutput.ReadToEndAsync();
        var error = probe.StandardError.ReadToEndAsync();
        try { await probe.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
        catch { if (!probe.HasExited) probe.Kill(true); throw; }
        Console.WriteLine(await output);
        Check(probe.ExitCode == 0, "Windows session readback: " + await error);
        await Until(() => systemSession.Snapshot.Current?.ItemId == "two" && systemSession.Snapshot.Status == PlaybackStatus.Playing);
        if (liveSession is null) Check(native.PauseCount >= 2 && native.SeekCount >= 2, "Windows controls routed into coordinator");
        else Console.WriteLine("PASS Windows commands -> coordinator -> real muted native decode/output confirmations");
    }
    lock (diagnostics) Check(diagnostics.Count == 0, "native host reported error");
}
Console.WriteLine("PASS real Windows SMTC helper readiness and bounded disposal");
// Missing helper must be isolated, and manual recovery must publish the latest state.
var missingPath = Path.Combine(root, "recovery-host.exe");
if (File.Exists(missingPath)) File.Delete(missingPath);
await using (var recovering = new WindowsSmtcTransport(missingPath))
{
    var unavailable = false;
    var recovered = false;
    recovering.DiagnosticChanged += message => { if (message is null) recovered = true; else unavailable = true; };
    recovering.Publish(MediaTransportMapping.Create(session.Snapshot, session.Queue, session.Repeat));
    await Until(() => unavailable);
    File.Copy(args[0], missingPath);
    recovering.Retry();
    await Until(() => recovered);
}
Console.WriteLine("PASS missing helper isolation and explicit retry");

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static async Task Until(Func<bool> condition)
{
    var watch = Stopwatch.StartNew();
    while (!condition()) { if (watch.ElapsedMilliseconds > 4000) throw new TimeoutException("SMTC smoke"); await Task.Delay(10); }
}

sealed class FakeTransport : IMediaTransport
{
    public event Action<MediaTransportCommand>? Command;
    public event Action<string?>? DiagnosticChanged { add { } remove { } }
    public MediaTransportState? Latest { get; private set; }
    public bool Disposed { get; private set; }
    public void Publish(MediaTransportState state) => Latest = state;
    public void Send(MediaTransportCommand command) => Command?.Invoke(command);
    public void Retry() { }
    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}
sealed class FakeAudio : INativeAudio
{
    public event Action<string, JsonElement>? Notification;
    public event Action<Exception>? Failed { add { } remove { } }
    private int operation;
    public int PauseCount { get; private set; }
    public int SeekCount { get; private set; }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
    {
        if (method == "audio.pause") PauseCount++;
        if (method == "audio.seek") SeekCount++;
        var json = method switch
        {
            "queue.set" => $"{{\"queueRevision\":{parameters!.Value.GetProperty("revision").GetInt64()}}}",
            "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000}}",
            "audio.openFile" => $"{{\"operationId\":{++operation},\"durationSeconds\":10}}",
            "audio.seek" => $"{{\"operationId\":{++operation}}}",
            _ => "true"
        };
        if (method is "audio.play" or "audio.resume")
        {
            using var started = JsonDocument.Parse($"{{\"operationId\":{operation}}}");
            Notification?.Invoke("audio.started", started.RootElement.Clone());
        }
        using var document = JsonDocument.Parse(json);
        return Task.FromResult(document.RootElement.Clone());
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
