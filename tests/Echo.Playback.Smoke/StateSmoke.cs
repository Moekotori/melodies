using Echo.Playback;
using System.Threading.Channels;

internal static class StateSmoke
{
    public static async Task RunAsync(string path)
    {
        var host = new FakeHost();
        await using var session = new PlaybackSessionCoordinator(host);
        var zone = new PlaybackZone("local", "本机", session);
        using var all = zone.Subscribe();
        using var queues = zone.Subscribe(PlaybackStateChanges.Queue);
        using var limit = new CancellationTokenSource(3000);
        var first = await all.ReadAsync(limit.Token);
        Check(first.IsInitial && first.State.Version == 0, "first subscription is not a complete initial frame");
        await queues.ReadAsync(limit.Token);
        await session.SetQueueAsync([new("a", "a", path), new("b", "b", path)], "a");
        var state = (await all.ReadAsync(limit.Token)).State;
        Check(state.Playback.Current?.ItemId == state.Queue.CurrentItemId && state.Queue.Items.Count == 2,
            "playback and queue were captured from different owner revisions");
        await queues.ReadAsync(limit.Token);
        host.Emit("audio.started", "{\"operationId\":1}");
        await session.SetVolumeAsync(0.5);
        await all.ReadAsync(limit.Token);
        var rows = state.Queue.Items;
        for (var i = 1; i <= 40; i++) host.Emit("audio.position", $"{{\"operationId\":1,\"framesPlayed\":{i * 4800}}}");
        await session.SetVolumeAsync(0.5);
        var clock = await all.ReadAsync(limit.Token);
        Check(clock.Changes == PlaybackStateChanges.Clock && ReferenceEquals(rows, clock.State.Queue.Items),
            "position-only changes republished structural state");
        using (var noQueue = new CancellationTokenSource(20))
        {
            try { await queues.ReadAsync(noQueue.Token); throw new Exception("queue-only subscriber received clock changes"); }
            catch (OperationCanceledException) { }
        }
        await session.SetQueueModeAsync(QueuePlaybackMode.Shuffle);
        host.Emit("audio.position", "{\"operationId\":1,\"framesPlayed\":216000}");
        await session.SetVolumeAsync(0.5);
        var merged = await all.ReadAsync(limit.Token);
        Check((merged.Changes & PlaybackStateChanges.Queue) != 0 && merged.State.Queue.Mode == QueuePlaybackMode.Shuffle,
            "a slow control lost structural changes behind a newer clock update");
        using var reconnect = zone.Subscribe();
        Check(reconnect.Initial == zone.Snapshot.State, "reconnect did not atomically register its initial frame");
        var anchor = session.State.ClockTimestamp;
        await session.SetVolumeAsync(0.6);
        Check(session.State.ClockTimestamp == anchor, "volume edit reset the native clock anchor");
        var capacity = Enumerable.Range(0, 29).Select(_ => zone.Subscribe()).ToArray();
        try { zone.Subscribe(); throw new Exception("unbounded subscription admission"); }
        catch (InvalidOperationException) { }
        foreach (var subscription in capacity) subscription.Dispose();
        await using var other = new PlaybackSessionCoordinator(new FakeHost());
        Check(other.State.Generation != session.State.Generation, "a new session reused an old generation");
        await session.DisposeAsync();
        var final = await all.ReadAsync(limit.Token);
        Check(!final.State.IsAvailable && final.State.Playback.Status == PlaybackStatus.Stopped && !final.State.Output.HasConfirmedDevice,
            "shutdown did not publish revoked output state");
        try { await all.ReadAsync(limit.Token); throw new Exception("shutdown left a live subscription"); }
        catch (ChannelClosedException) { }
        Console.WriteLine("PASS consistent Zone snapshots, classified updates, slow control coalescing, reconnect, clock anchor, subscription cap and shutdown");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
