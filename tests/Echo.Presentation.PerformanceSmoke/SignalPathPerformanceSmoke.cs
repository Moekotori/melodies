using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Echo.Desktop.Features.Player;
using Echo.Playback;

internal static class SignalPathPerformanceSmoke
{
    public static void Run(bool baseline)
    {
        var session = new PlaybackSessionCoordinator(new IdleNative());
        var player = new PlayerViewModel(new PlaybackZone("fixture", "fixture", session));
        using var panel = new SignalPathViewModel(player);
        var latest = typeof(SignalPathViewModel).GetField("latest", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var present = typeof(SignalPathViewModel).GetMethod("Present", BindingFlags.NonPublic | BindingFlags.Instance)!.CreateDelegate<Action>(panel);
        using var output = JsonDocument.Parse("{\"deviceName\":\"fixture\",\"backend\":\"wasapi\",\"exclusive\":true,\"sampleRate\":48000,\"hardwareSampleRate\":48000,\"channels\":2}");
        using var processing = JsonDocument.Parse("{\"outputFormat\":\"f32\",\"bitPerfectCandidate\":true,\"limiter\":{\"active\":false}}");
        var track = new QueueTrack("item", "track", "fixture.flac", Metadata: new Dictionary<string, string?> { ["codec"] = "FLAC", ["duration"] = "300" });
        var frame = session.State with
        {
            Playback = new(1, PlaybackStatus.Playing, track, 0, 300, 1, 1, 48000, 2, null, output.RootElement.Clone(), processing.RootElement.Clone()),
            Output = new("fixture", "wasapi", true, true, null, output.RootElement.Clone()),
            Level = new(1, -20, -24, Stopwatch.GetTimestamp())
        };
        void Show(PlaybackStateSnapshot value) { latest.SetValue(panel, value); present(); }
        Show(frame);
        // Simulate separately deserialized IPC frames, including equivalent metadata dictionaries/JSON.
        var frames = Enumerable.Range(1, 200).Select(i => frame with
        {
            Version = i + 1,
            Playback = frame.Playback with { Version = i + 1, PositionSeconds = i * .1,
                Current = track with { Metadata = new Dictionary<string, string?>(track.Metadata!) },
                Processing = frame.Playback.Processing!.Value.Clone() },
            Output = frame.Output with { Confirmed = frame.Output.Confirmed!.Value.Clone() }
        }).ToArray();
        var builds = 0;
        panel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SignalPathViewModel.Nodes)) builds++; };
        var allocated = GC.GetAllocatedBytesForCurrentThread(); var started = Stopwatch.GetTimestamp();
        foreach (var value in frames) Show(value);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Console.WriteLine($"MEASURE signal path progress: samples={frames.Length}, node_rebuilds={builds}, allocated_bytes={bytes}, elapsed_ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F2}");
        if (!baseline)
        {
            Check(builds == 0, "pure position/IPC identity changes preserve the path");
            var nodes = panel.Nodes; var insights = panel.Insights;
            Show(frame with { Level = new(1, -1, -3, Stopwatch.GetTimestamp()) });
            Check(ReferenceEquals(nodes, panel.Nodes) && !ReferenceEquals(insights, panel.Insights) && panel.LevelPercent > 0, "live level updates without rebuilding nodes");
            Show(frame with { Performance = PlaybackPerformance.Unknown with { BufferedFrames = 4800, PcmSampleRate = 48000 } });
            Check(ReferenceEquals(nodes, panel.Nodes) && panel.EngagedChips.Any(c => c.Value == "100 ms"), "buffer readout updates independently");
            Show(frame with { Playback = frame.Playback with { Status = PlaybackStatus.Faulted, Error = "fixture fault" } });
            Check(panel.Diagnosis == "fixture fault" && !ReferenceEquals(nodes, panel.Nodes), "fault rebuilds authoritative path");
            Show(frame); nodes = panel.Nodes;
            player.Details.BitDepth = 16; Show(frame);
            Check(!ReferenceEquals(nodes, panel.Nodes), "late track metadata rebuilds the path");
            nodes = panel.Nodes; player.Text.ChangeLanguage();
            Check(!ReferenceEquals(nodes, panel.Nodes), "language refresh invalidates the path");
            nodes = panel.Nodes;
            using var changed = JsonDocument.Parse("{\"limiter\":{\"active\":true,\"protecting\":true}}");
            Show(frame with { Playback = frame.Playback with { Processing = changed.RootElement.Clone() } });
            Check(!ReferenceEquals(nodes, panel.Nodes), "native DSP change rebuilds the path");
            nodes = panel.Nodes; Show(frame with { Generation = Guid.NewGuid(), IsAvailable = false });
            Check(!ReferenceEquals(nodes, panel.Nodes), "reconnection/generation changes invalidate the path");
            Console.WriteLine("PASS signal path: progress deduplication, live levels/buffer, fault, metadata, language, DSP and reconnect");
        }
        panel.Dispose(); session.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class IdleNative : INativeAudio
    {
        public event Action<string, JsonElement>? Notification { add { } remove { } }
        public event Action<Exception>? Failed { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
            => Task.FromResult(JsonSerializer.SerializeToElement(true));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
