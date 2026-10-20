using System.Text.Json;
using Echo.Application.Radio;
using Echo.Contracts.Radio;
using Echo.Playback;

internal static class RadioServiceSmoke
{
    public static async Task RunAsync(CancellationToken token)
    {
        var native = new FixtureNative();
        await using var session = new PlaybackSessionCoordinator(native);
        var resolver = new WaitingResolver();
        using var radio = new RadioPlaybackService(session, resolver);
        var station = new RadioStation("station", "Fixture", "https://radio.test/list.m3u");
        var pending = radio.StartAsync(station, token);
        Check(radio.IsConnecting(station.Id), "Resolve state not exposed");
        await radio.StopAsync(station.Id, token);
        try { await pending; } catch (OperationCanceledException) { }
        Check(native.OpenCount == 0 && !radio.IsConnecting(station.Id), "Cancelled probe started audio");
        resolver.Wait = false;
        await radio.StartAsync(station, token);
        Check(session.Snapshot.Current?.Source?.Uri == "https://radio.test/audio" && session.Snapshot.Current.Origin?.Id == station.Id, "Resolved identity");
        Check(session.Snapshot.Status == PlaybackStatus.Loading && session.Snapshot.DurationSeconds == 0, "Live state before confirmation");
        try { await session.SeekAsync(1, token); throw new InvalidOperationException("Live seek accepted"); } catch (InvalidOperationException error) when (error.Message.Contains("直播")) { }
        Check(native.SeekCount == 0 && session.Snapshot.Status == PlaybackStatus.Loading, "Live seek poisoned playback state");
        native.Emit("audio.started", $"{{\"operationId\":{session.Snapshot.OperationId}}}"); await session.SetVolumeAsync(0, token);
        Check(session.Snapshot.Status == PlaybackStatus.Playing, "Native receipt not applied");
        await session.PauseAsync(token); await session.ResumeAsync(token);
        Check(native.OpenCount == 2 && session.Snapshot.PositionSeconds == 0, "Live resume must reconnect at the live edge");
        var checkpoint = await session.ExportCheckpointAsync();
        await using var restored = new PlaybackSessionCoordinator(new FixtureNative()); await restored.RestoreCheckpointAsync(checkpoint);
        Check(restored.Snapshot.Current?.Origin?.Id == station.Id && restored.Snapshot.Status == PlaybackStatus.Paused, "Live checkpoint lost or autoplayed");
        await radio.StopAsync(station.Id, token); Check(session.Snapshot.Status == PlaybackStatus.Stopped, "Radio stop");
        resolver.Wait = true; pending = radio.StartAsync(station, token);
        await session.PlayAsync(new("local-next", "local-next", "unused", new("http", "https://radio.test/finite")), token);
        resolver.Finish(); await pending;
        Check(session.Snapshot.Current?.ItemId == "local-next", "Late radio probe overwrote a newer playback command");
        Console.WriteLine("PASS cancelled and stale probes, live source identity, confirmation, seek guard, reconnect, checkpoint and stop");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class WaitingResolver : IRadioStreamResolver
    {
        private TaskCompletionSource? completion;
        public bool Wait { get; set; } = true;
        public async Task<RadioStreamInfo> ResolveAsync(string url, CancellationToken token)
        {
            if (Wait) { completion = new(TaskCreationOptions.RunContinuationsAsynchronously); await completion.Task.WaitAsync(token); }
            return new("https://radio.test/audio", "AAC");
        }
        public void Finish() => completion?.TrySetResult();
    }
    private sealed class FixtureNative : INativeAudio
    {
        private int operation;
        public int OpenCount { get; private set; }
        public int SeekCount { get; private set; }
        public event Action<string, JsonElement>? Notification;
        public event Action<Exception>? Failed { add { } remove { } }
        public void Emit(string method, string json) { using var doc = JsonDocument.Parse(json); Notification?.Invoke(method, doc.RootElement.Clone()); }
        public Task StartAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken token = default)
        {
            if (method == "audio.openSource") OpenCount++; if (method == "audio.seek") SeekCount++;
            var text = method switch
            {
                "queue.set" => $"{{\"queueRevision\":{parameters!.Value.GetProperty("revision").GetInt64()}}}",
                "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000,\"channels\":2}}",
                "audio.openSource" => $"{{\"operationId\":{++operation},\"durationSeconds\":100}}",
                "audio.stop" => $"{{\"operationId\":{++operation},\"drained\":true,\"drainWaitMs\":0}}", _ => "true"
            };
            using var doc = JsonDocument.Parse(text); return Task.FromResult(doc.RootElement.Clone());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
