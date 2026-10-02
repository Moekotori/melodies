using System.Text.Json;
using Echo.Playback;

internal static class QueueScenarios
{
    public static async Task RunAsync(string a, string b)
    {
        var native = new FakeHost();
        await using var session = new PlaybackSessionCoordinator(native);
        var album = new QueueOrigin("album", "Album A", "album-a");
        var first = new QueueTrack("a", "a", a, Origin: album);
        var last = new QueueTrack("c", "c", a, Origin: album);
        await session.SetQueueModeAsync(QueuePlaybackMode.Shuffle);
        await session.EnqueueAsync([first, last]);
        Check(native.Calls.Count == 0, "editing/mode selection initialized audio before playback");
        var planned = session.QueueState.Upcoming!.ItemId;
        await session.ResumeAsync();
        Check(session.Snapshot.Current?.ItemId == planned, "play from an idle queue ignored the displayed upcoming order");
        await session.SetQueueModeAsync(QueuePlaybackMode.Sequential);
        await session.SetQueueAsync([first, last], "a");
        await session.EnqueueAsync([new("b1", "b", b)], true);
        await session.EnqueueAsync([new("b2", "b", b)], true);
        Check(session.Queue.Select(x => x.ItemId).SequenceEqual(new[] { "a", "b1", "b2", "c" }), "FIFO next-up / duplicate track occurrences");
        await session.SetQueueModeAsync(QueuePlaybackMode.Shuffle);
        Check(session.QueueState.Upcoming?.ItemId == "b1", "next-up priority in shuffle");
        await session.NextAsync(); Check(session.Snapshot.Current?.ItemId == "b1", "first next-up");
        await session.NextAsync(); Check(session.Snapshot.Current?.ItemId == "b2" && !session.Queue.Any(x => x.ItemId == "b1"), "next-up consumption");
        await session.NextAsync(); Check(session.Snapshot.Current?.ItemId == "c" && session.Queue.Count == 2, "return to base deck");
        await session.PreviousAsync(); Check(session.Snapshot.Current?.ItemId == "b2", "shuffle previous uses history");
        await session.NextAsync();
        await session.SetQueueModeAsync(QueuePlaybackMode.Sequential);
        await session.PreviousAsync(); Check(session.Snapshot.Current?.ItemId == "a", "album previous skips consumed next-up");
        await session.SetQueueModeAsync(QueuePlaybackMode.RepeatOne);
        Check(native.LastQueue.GetProperty("repeatMode").GetString() == "one" && session.QueueState.Upcoming is null, "repeat one native policy");
        await session.NextAsync(); Check(session.Snapshot.Current?.ItemId == "c", "manual next exits repeat-one occurrence");
        await session.SetQueueModeAsync(QueuePlaybackMode.RepeatAll);
        await session.NextAsync(); Check(session.Snapshot.Current?.ItemId == "a", "repeat all wrap");
        var operation = session.Snapshot.OperationId;
        await session.ClearQueueAsync();
        Check(session.Queue.Count == 0 && session.Snapshot.OperationId == operation && native.LastQueue.GetProperty("items").GetArrayLength() == 1
            && native.LastQueue.GetProperty("repeatMode").GetString() == "off", "clear preserves current audio, disables detached repeat");
        var media = Echo.Playback.MediaTransport.MediaTransportMapping.Create(session.Snapshot, session.Queue, session.Repeat, session.QueueState);
        Check(media.Play && media.Pause && media.ItemId == "a" && !media.Next, "clear disabled system pause/current metadata");
        var detachedState = await session.ExportCheckpointAsync();
        await using (var detached = new PlaybackSessionCoordinator(new FakeHost()))
        {
            await detached.RestoreCheckpointAsync(detachedState);
            Check(detached.Queue.Count == 0 && detached.Snapshot.Current?.ItemId == "a" && detached.Snapshot.Status == PlaybackStatus.Paused, "detached restore without autoplay");
        }
        await session.UndoQueueEditAsync(); Check(session.Queue.Count == 2, "undo clear");
        await session.EnqueueAsync([new("d", "d", b), new("e", "e", b)]);
        await session.MoveQueueItemsAsync(["d", "e"], 1);
        Check(session.Queue.Select(x => x.ItemId).SequenceEqual(new[] { "a", "d", "e", "c" }), "batch move keeps selected order");
        await session.SetRemoveAfterPlayAsync(["a"], true);
        await session.NextAsync(); Check(!session.Queue.Any(x => x.ItemId == "a"), "remove after play");
        var queueBefore = session.Queue;
        native.RejectQueueOnce = true;
        try { await session.EnqueueAsync([new("rejected", "z", b)]); throw new Exception("unconfirmed queue accepted"); }
        catch (IOException) { }
        Check(ReferenceEquals(queueBefore, session.Queue), "unconfirmed mutation must not publish queue");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await session.ClearQueueAsync(cancelled.Token); throw new Exception("cancelled queue admitted"); }
            catch (OperationCanceledException) { }
            Check(ReferenceEquals(queueBefore, session.Queue), "cancelled edit changed queue");
        }
        try { await session.SetQueueAsync(Enumerable.Range(0, 1001).Select(i => new QueueTrack($"q{i}", "a", a))); throw new Exception("unbounded queue"); }
        catch (ArgumentException) { }
        await session.SetQueueAsync([first, last], "a");
        var events = 0; session.QueueChanged += _ => events++;
        var beforeCalls = native.Calls.Count(x => x == "queue.set");
        for (var i = 0; i < 30; ++i) native.Emit("audio.position", $"{{\"operationId\":{session.Snapshot.OperationId},\"framesPlayed\":{i * 48000}}}");
        await session.SetVolumeAsync(0.5);
        Check(events == 0 && beforeCalls == native.Calls.Count(x => x == "queue.set"), "position-only updates rebuilt/synced queue");
        var from = session.Snapshot.OperationId;
        var revision = native.LastQueue.GetProperty("revision").GetInt64();
        var advance = $"{{\"queueAdvance\":true,\"fromOperationId\":{from},\"operationId\":{from + 100},\"queueRevision\":{revision},\"nextItemId\":\"c\",\"nextDurationSeconds\":10}}";
        native.Emit("audio.ended", advance); native.Emit("audio.ended", advance);
        await session.SetVolumeAsync(0.6);
        Check(session.Snapshot.Current?.ItemId == "c" && session.QueueState.History.Count == 1, "duplicate EOF advanced or recorded twice");
        native.Emit("audio.ended", $"{{\"operationId\":{from}}}");
        await session.SetVolumeAsync(0.6);
        Check(session.Snapshot.Status == PlaybackStatus.Playing, "stale EOF replaced confirmed current");
        await session.SetQueueModeAsync(QueuePlaybackMode.Shuffle);
        var checkpoint = await session.ExportCheckpointAsync();
        await using var restored = new PlaybackSessionCoordinator(new FakeHost());
        await restored.RestoreCheckpointAsync(checkpoint);
        Check(restored.QueueMode == QueuePlaybackMode.Shuffle && restored.QueueState.History.Count == 1
            && restored.Snapshot.Status == PlaybackStatus.Paused, "queue policy/history checkpoint restore");
        Console.WriteLine("PASS queue FIFO, occurrences, modes, history, edit/undo, clear-current, receipts, cancellation, bounds, duplicate/stale EOF and 30 position-only updates");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static async Task NativeAsync(PlaybackSessionCoordinator session, string a, string b)
    {
        var played = new List<string>();
        void Observe(PlaybackSnapshot state)
        {
            if (state.Status == PlaybackStatus.Playing && state.Current is { } track && played.LastOrDefault() != track.ItemId) played.Add(track.ItemId);
        }
        session.Changed += Observe;
        try
        {
            await session.SetQueueModeAsync(QueuePlaybackMode.Sequential);
            await session.SetQueueAsync([new("fifo-a", "a", a), new("fifo-tail", "tail", b)], "fifo-a");
            await session.EnqueueAsync([new("fifo-b1", "b", b)], true);
            await session.EnqueueAsync([new("fifo-b2", "b", b)], true);
            await session.SeekAsync(2.0);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (session.Snapshot.Status != PlaybackStatus.Ended)
            { if (clock.Elapsed > TimeSpan.FromSeconds(4)) throw new TimeoutException("native FIFO advance"); await Task.Delay(10); }
            Check(played.TakeLast(3).SequenceEqual(new[] { "fifo-b1", "fifo-b2", "fifo-tail" }) && session.Queue.Count == 2, "native next-up FIFO/return/cleanup");
            Console.WriteLine("PASS real Native Host EOF: inserted B1 -> B2 -> original tail, consumed occurrences removed");
        }
        finally { session.Changed -= Observe; }
    }
}
