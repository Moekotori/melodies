using System.Text.Json;
using Echo.Playback;

internal static class AudioChainRegressionChecks
{
    public static async Task RunAsync()
    {
        var path = Path.GetFullPath(Path.Combine("misc", "playback-smoke", "a.wav"));
        // Tests below only require an existing local path; no fake host decodes this file.
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path)) await File.WriteAllBytesAsync(path, [0]);
        var failures = new List<Exception>();
        foreach (var check in new Func<Task>[] { () => OutputFailureAsync(path, transport: false),
            () => OutputFailureAsync(path, transport: true), () => OutputFailureAsync(path, transport: false, early: true),
            () => PositionFailureAsync(path), () => StaleAutomixAsync(path), () => DurationBudgetAsync(path) })
            try { await check(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count > 0) throw new AggregateException(failures);
    }

    private static async Task OutputFailureAsync(string path, bool transport, bool early = false)
    {
        var host = new AuditHost { FailOnPlay = !transport && !early, DisconnectOnPlay = transport, FailBeforeOpenReceipt = early };
        await using var session = new PlaybackSessionCoordinator(host);
        await session.ConfigureOutputAsync(new AudioOutputConfiguration(Mode: "asio"));
        var command = session.PlayAsync(new("a", "a", path));
        try { await command.WaitAsync(TimeSpan.FromSeconds(1)); throw new Exception("Native output failure was accepted"); }
        catch (IOException error) when (error.Message.Contains("audit output failure", StringComparison.Ordinal)) { }
        catch (TimeoutException)
        {
            // Drain the issued command before disposal even on the old implementation.
            try { await command; } catch (IOException) { }
            throw new Exception("Professional output confirmation hid the native failure until timeout");
        }
        Console.WriteLine($"PASS professional output reports {(early ? "early decoder" : transport ? "transport" : "audio")} failure immediately");
    }

    private static async Task PositionFailureAsync(string path)
    {
        var host = new AuditHost();
        var probe = new AuditProbe();
        var session = new PlaybackSessionCoordinator(host, probe);
        await session.ConfigureOutputAsync(new AudioOutputConfiguration(Mode: "asio"));
        await session.SetAutomixEnabledAsync(true);
        await session.SetQueueAsync([new("a", "a", path), new("b", "b", path)], "a");
        await session.SetVolumeAsync(0); // Barrier behind audio.started.
        probe.Fail = true;
        host.Emit("audio.position", $$"""{"operationId":{{session.Snapshot.OperationId}},"framesPlayed":4800,"outputFrame":4800} """);
        await session.SetVolumeAsync(0).WaitAsync(TimeSpan.FromSeconds(1));
        if (session.Snapshot.Status != PlaybackStatus.Faulted || session.Snapshot.Error != "audit missing source")
            throw new Exception("Position-driven source failure did not become an observable fault");
        await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(1));
        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        Console.WriteLine("PASS position-driven source failure preserves the command lane and resource release");
    }

    private sealed class AuditProbe : IPlaybackSourceProbe
    {
        public bool Fail { get; set; }
        public ValueTask<int?> ReadSampleRateAsync(AudioSource source, CancellationToken token)
            => Fail ? throw new IOException("audit missing source") : new((int?)null);
        public ValueTask<double?> ReadDurationAsync(AudioSource source, CancellationToken token) => new(10);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static async Task StaleAutomixAsync(string path)
    {
        var host = new FakeHost();
        await using var session = new PlaybackSessionCoordinator(host);
        await session.SetQueueAsync([new("a", "a", path), new("b", "b", path)], "a");
        await session.SeekAsync(1);
        await session.SeekAsync(2);
        var operation = session.Snapshot.OperationId;
        var revision = host.LastQueue.GetProperty("revision").GetInt64();
        host.Emit("audio.transitionCommitted", $$"""
            {"queueRevision":{{revision}},"operationId":{{operation - 1}},"fromItemId":"a","fromTrackId":"a","toItemId":"b","toTrackId":"b","outputFrame":48000,"sourcePositionSeconds":0.25}
            """);
        await session.SetVolumeAsync(0);
        if (session.Snapshot.OperationId != operation || session.Snapshot.Current?.ItemId != "a" || session.Snapshot.PositionSeconds != 2)
            throw new Exception("A delayed automix commit replaced the newer seek operation");
        Console.WriteLine("PASS delayed automix commit cannot replace a newer seek operation");
    }

    private static async Task DurationBudgetAsync(string path)
    {
        var host = new FakeHost();
        await using var session = new PlaybackSessionCoordinator(host);
        await session.SetAutomixEnabledAsync(true);
        for (var index = 0; index < PlaybackSessionCoordinator.QueueCapacity + 10; index++)
        {
            await session.SetQueueAsync([new($"a{index}", "a", path), new($"b{index}", "b", path)], $"a{index}");
            var operation = session.Snapshot.OperationId;
            host.Emit("audio.started", $$"""{"operationId":{{operation}}} """);
            host.Emit("audio.position", $$"""{"operationId":{{operation}},"framesPlayed":48000,"outputFrame":48000} """);
            await session.SetVolumeAsync(0);
        }
        var cache = (System.Collections.IDictionary)typeof(PlaybackSessionCoordinator)
            .GetField("durations", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(session)!;
        if (cache.Count > session.Queue.Count + 1) throw new Exception("Automix duration cache grew beyond the queue budget");
        Console.WriteLine("PASS automix duration residency remains bounded across queue replacements");
    }

    private sealed class AuditHost : INativeAudio, INativeOutputControl
    {
        private long operation;
        public bool FailOnPlay { get; init; }
        public bool DisconnectOnPlay { get; init; }
        public bool FailBeforeOpenReceipt { get; init; }
        public int OutputChannels => 2;
        public bool ProDspAllowed => true;
        public JsonElement? Capabilities => null;
        public event Action<string, JsonElement>? Notification;
        public event Action<Exception>? Failed;
        public void Emit(string method, string json) => Notification?.Invoke(method, JsonDocument.Parse(json).RootElement.Clone());
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetOutputChannelsAsync(int channels, CancellationToken token = default, bool forceRestart = false) => Task.CompletedTask;
        public Task<IReadOnlyList<AudioOutputDevice>> EnumerateOutputDevicesAsync(string mode, CancellationToken token = default)
            => Task.FromResult<IReadOnlyList<AudioOutputDevice>>([]);
        public Task<JsonElement> ProbeAsioDsdAsync(AudioOutputDevice device, CancellationToken token = default) => throw new NotSupportedException();
        public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
        {
            if (method == "audio.openFile")
            {
                operation++;
                if (FailBeforeOpenReceipt) Emit("audio.error", $$"""{"operationId":{{operation}},"message":"audit output failure"} """);
            }
            if (method == "audio.play")
            {
                if (DisconnectOnPlay) Failed?.Invoke(new IOException("audit output failure"));
                else Emit(FailOnPlay ? "audio.error" : "audio.started",
                    $$"""{"operationId":{{operation}},"message":"audit output failure"} """);
            }
            var json = method switch
            {
                "device.configure" => "{\"accepted\":true,\"sampleRate\":48000}",
                "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000,\"channels\":2}}",
                "audio.openFile" => $$"""{"operationId":{{operation}},"sourceSampleRate":48000,"durationSeconds":10} """,
                "queue.set" => $$"""{"queueRevision":{{parameters!.Value.GetProperty("revision").GetInt64()}}} """,
                _ => "true"
            };
            return Task.FromResult(JsonDocument.Parse(json).RootElement.Clone());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
