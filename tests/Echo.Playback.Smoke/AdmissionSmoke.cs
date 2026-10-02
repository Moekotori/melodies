using System.Diagnostics;
using Echo.Application.Scanning;
using Echo.Application.Scheduling;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Contracts.Tasks;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Playback;

internal static class AdmissionSmoke
{
    public static async Task RunAsync(string path, string root)
    {
        var host = new FakeHost();
        await using var session = new PlaybackSessionCoordinator(host);
        PressurePolicySmoke.Run(session.State);
        var clock = new TestClock();
        await using var admission = new PlaybackWorkAdmission(session, clock);
        await session.PlayAsync(new("admission", "admission", path));
        host.Emit("audio.started", "{\"operationId\":1}");
        await session.SetVolumeAsync(0);
        Check(admission.Snapshot.MetadataConcurrency == 1, "unknown playback telemetry must remain bounded and make progress");
        clock.Advance(600);
        await Buffer(480);
        _ = admission.Snapshot;
        clock.Advance(150);
        await Buffer(480);
        Check(admission.Snapshot.Pressure == PlaybackWorkPressure.Protected, "sustained low PCM buffer did not protect playback");
        using (var canceled = new CancellationTokenSource(20))
        {
            try { using var ignored = await admission.EnterMetadataAsync(canceled.Token); throw new Exception("protected metadata was admitted"); }
            catch (OperationCanceledException) { }
        }
        var blocked = admission.EnterMetadataAsync(CancellationToken.None).AsTask();
        Check(!blocked.IsCompleted, "metadata bypassed protection");
        await session.PauseAsync();
        using (await blocked.WaitAsync(TimeSpan.FromSeconds(1))) { }
        Check(admission.Snapshot.ActiveMetadata == 0 && admission.Snapshot.WaitingRequests == 0,
            "cancellation or resumed work leaked admission leases");
        var leases = new List<IDisposable>();
        for (var i = 0; i < 4; i++) leases.Add(await admission.EnterMetadataAsync(CancellationToken.None));
        using (var canceled = new CancellationTokenSource(20))
        {
            try { using var ignored = await admission.EnterMetadataAsync(canceled.Token); throw new Exception("metadata concurrency was unbounded"); }
            catch (OperationCanceledException) { }
        }
        foreach (var lease in leases) { lease.Dispose(); lease.Dispose(); }
        Check(admission.Snapshot.ActiveMetadata == 0, "lease disposal was not idempotent");

        var data = Path.Combine(root, "admission-" + Guid.NewGuid().ToString("N"));
        var media = Path.Combine(data, "media"); Directory.CreateDirectory(media);
        for (var i = 0; i < 80; i++) File.WriteAllBytes(Path.Combine(media, i + ".wav"), new byte[16]);
        using var limit = new CancellationTokenSource(6000);
        await using var database = await LibraryDatabase.OpenAsync(Path.Combine(data, "library.sqlite"), limit.Token);
        var readerState = new ReaderState();
        await session.ResumeAsync(); host.Emit("audio.started", "{\"operationId\":1}"); await session.SetVolumeAsync(0);
        _ = admission.Snapshot;
        clock.Advance(1000);
        await Buffer(480); _ = admission.Snapshot; clock.Advance(150); await Buffer(480); _ = admission.Snapshot;
        var scanner = new ScanCoordinator(database, new DirectoryDiscovery(), () => new Reader(readerState), admission);
        using (var canceledScan = new CancellationTokenSource())
        {
            var canceledTask = scanner.ScanAsync(new(media, Workers: 4, DetectMissing: true), cancellation: canceledScan.Token);
            await Until(() => admission.Snapshot.WaitingRequests > 0, limit.Token);
            canceledScan.Cancel();
            var report = await canceledTask.WaitAsync(limit.Token);
            Check(report.Status == ScanStatus.Cancelled && report.MarkedMissing == 0 && (await database.GetStatsAsync(limit.Token)).Tracks == 0,
                "protected scan cancellation wrote data or performed missing cleanup");
        }
        // Missing/stale samples fall back to one worker, so a lost telemetry stream cannot freeze import forever.
        clock.Advance(5000);
        Check(admission.Snapshot.MetadataConcurrency == 1, "stale telemetry kept background work permanently suspended");
        var imported = await scanner.ScanAsync(new(media, Workers: 4, BatchSize: 128), cancellation: limit.Token);
        Check(imported.Status == ScanStatus.Completed && imported.Inserted == 80 && readerState.Peak == 1,
            "real scan pipeline did not obey reduced metadata concurrency");
        Check((await database.GetStatsAsync(limit.Token)).Tracks == 80, "bounded scan commits lost records");
        var oldOperation = session.Snapshot.OperationId;
        await session.SeekAsync(0.5);
        host.Emit("audio.position", $"{{\"operationId\":{oldOperation},\"framesPlayed\":5000,\"bufferedFrames\":0}}");
        await session.SetVolumeAsync(0);
        Check(session.State.Performance.BufferedFrames is null, "old operation telemetry leaked across seek");
        var current = session.Snapshot.OperationId;
        host.Emit("audio.position", $"{{\"operationId\":{current},\"framesPlayed\":0,\"bufferedFrames\":960," +
            "\"inputEnded\":false,\"processing\":{\"outputFormat\":\"dsd-native-raw\",\"echoSrc\":{\"active\":true,\"processedBlocks\":1,\"lastInputFrames\":4800,\"sourceSampleRate\":48000,\"lastProcessMilliseconds\":50,\"peakProcessMilliseconds\":80}}}");
        await session.SetVolumeAsync(0);
        Check(session.State.Performance.BufferedMilliseconds is null && session.State.Performance.Src?.Speed == 2,
            "transport buffer units or single-stage performance were invented");
        host.Emit("host.performance", "{\"underrunCallbacks\":3,\"underrunFrames\":480}");
        await session.SetVolumeAsync(0);
        Check(session.State.Performance.HostUnderrunCallbacks == 3, "host-scoped telemetry was not exposed");
        host.Emit("host.performance", "{\"underrunCallbacks\":null,\"underrunFrames\":\"unknown\"}");
        host.Emit("audio.position", $"{{\"operationId\":{current},\"framesPlayed\":0,\"bufferedFrames\":null}}");
        await session.PauseAsync();
        Check(session.Snapshot.Status == PlaybackStatus.Paused && session.State.Performance.HostUnderrunCallbacks is null,
            "malformed diagnostics stopped the command lane or became a fake zero");
        Console.WriteLine("PASS low-buffer protection, canceled/FIFO admission, pause recovery, stale telemetry, real SQLite scan cancellation, one-worker import, receipt preservation and old-operation rejection");

        async Task Buffer(int frames)
        {
            host.Emit("audio.position", $"{{\"operationId\":1,\"framesPlayed\":4800,\"bufferedFrames\":{frames},\"inputEnded\":false}}");
            await session.SetVolumeAsync(0);
        }
    }
    private static async Task Until(Func<bool> condition, CancellationToken token)
    { while (!condition()) await Task.Delay(5, token); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class TestClock : TimeProvider
    {
        private long timestamp = Stopwatch.GetTimestamp();
        public override long GetTimestamp() => Interlocked.Read(ref timestamp);
        public void Advance(int milliseconds) => Interlocked.Add(ref timestamp, Stopwatch.Frequency * milliseconds / 1000);
    }
    private sealed class ReaderState { public int Active, Peak; }
    private sealed class Reader(ReaderState state) : IMetadataReader
    {
        public async ValueTask<TrackMetadata> ReadAsync(FileSnapshot file, CancellationToken cancellation)
        {
            var active = Interlocked.Increment(ref state.Active); state.Peak = Math.Max(state.Peak, active);
            try { await Task.Delay(2, cancellation); return new(Path.GetFileName(file.Path), "Admission", "Pressure", "Admission", 1, "wav", 48000, 16); }
            finally { Interlocked.Decrement(ref state.Active); }
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
