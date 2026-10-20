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
        Check(session.QueueState.Upcoming?.ItemId == "b1" && session.QueueState.Plan.Count > 0
            && session.QueueState.Plan[0].Reason == QueuePlanReason.PinnedNext, "next-up priority in shuffle");
        await session.NextAsync(); Check(session.Snapshot.Current?.ItemId == "b1", "first next-up");
        await session.NextAsync(); Check(session.Snapshot.Current?.ItemId == "b2" && !session.Queue.Any(x => x.ItemId == "b1"), "next-up consumption");
        await session.NextAsync(); Check(session.Snapshot.Current?.ItemId == "c" && session.Queue.Count == 2, "return to base deck");
        await session.PreviousAsync(); Check(session.Snapshot.Current?.ItemId == "b2", "shuffle previous uses history");
        await session.NextAsync();
        await session.SetQueueModeAsync(QueuePlaybackMode.Sequential);
        await session.PreviousAsync(); Check(session.Snapshot.Current?.ItemId == "a", "album previous skips consumed next-up");
        await session.SetQueueModeAsync(QueuePlaybackMode.RepeatOne);
        Check(native.LastQueue.GetProperty("repeatMode").GetString() == "one" && session.QueueState.Upcoming is null
            && session.QueueState.Plan.Count == 0, "repeat one native policy");
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
        await GaplessAsync(a, b);
        await AutomixAsync(a, b);
        await CheckPlanAsync(a, b);
        Console.WriteLine("PASS queue FIFO, occurrences, modes, history, edit/undo, clear-current, receipts, cancellation, bounds, duplicate/stale EOF and 30 position-only updates");
    }
    private static async Task GaplessAsync(string a, string b)
    {
        var native = new FakeHost();
        await using var session = new PlaybackSessionCoordinator(native);
        await session.SetAlbumGaplessEnabledAsync(true);
        await session.SetQueueAsync([new("a", "track-a", a), new("b", "track-b", b), new("c", "track-c", a)], "a");
        var opened = native.Calls.IndexOf("audio.openFile");
        var primed = native.Calls.IndexOf("audio.gaplessPrepare");
        var played = native.Calls.IndexOf("audio.play");
        var request = native.GaplessRequest!.Value[0];
        var following = request.GetProperty("following");
        Check(opened >= 0 && opened < primed && primed < played, "gapless prime is between open and play");
        Check(request.GetProperty("filePath").GetString() == b && request.GetProperty("itemId").GetString() == "b"
            && request.GetProperty("trackId").GetString() == "track-b" && request.GetProperty("replayGainIndex").GetInt32() == 1
            && following.GetArrayLength() == 1 && following[0].GetProperty("itemId").GetString() == "c"
            && following[0].GetProperty("replayGainIndex").GetInt32() == 2, "gapless request follows the native queue");

        var repeat = new FakeHost();
        await using var repeating = new PlaybackSessionCoordinator(repeat);
        await repeating.SetRepeatAsync(RepeatMode.One);
        await repeating.SetAlbumGaplessEnabledAsync(true);
        await repeating.SetQueueAsync([new("a", "a", a), new("b", "b", b)], "a");
        Check(!repeat.Calls.Contains("audio.gaplessPrepare"), "repeat-one does not prime the next track");

        var dsf = Path.ChangeExtension(a, ".dsf");
        await File.WriteAllBytesAsync(dsf, [0]);
        var dsd = new FakeHost();
        await using var dsdSession = new PlaybackSessionCoordinator(dsd);
        await dsdSession.SetAlbumGaplessEnabledAsync(true);
        await dsdSession.SetQueueAsync([new("a", "a", a), new("d", "d", dsf)], "a");
        Check(!dsd.Calls.Contains("audio.gaplessPrepare"), "DSD source is not primed as PCM gapless");

        var off = new FakeHost();
        await using var ordinary = new PlaybackSessionCoordinator(off);
        await ordinary.SetQueueAsync([new("a", "a", a), new("b", "b", b)], "a");
        Check(!off.Calls.Contains("audio.gaplessPrepare"), "gapless stays off until the setting is enabled");
    }

    private static async Task CheckPlanAsync(string a, string b)
    {
        var native = new FakeHost();
        await using var session = new PlaybackSessionCoordinator(native);
        await session.SetShuffleAvoidRecentCountAsync(25);
        var recentA = new QueueTrack("r1", "r1", a);
        var recentB = new QueueTrack("r2", "r2", a);
        var fresh = new QueueTrack("f1", "f1", b);
        var later = new QueueTrack("f2", "f2", b);
        await session.SetQueueAsync([recentA, recentB, fresh, later], "r1");
        await session.NextAsync();
        await session.NextAsync();
        await session.SetQueueModeAsync(QueuePlaybackMode.Shuffle);
        await session.EnqueueAsync([new QueueTrack("f3", "f3", b)]);
        var ids = NativeItemIds(native);
        Check(ids is ["f1", "f2", "f3", _, _], "fresh tracks stay ahead of songs just heard");
        Check(ids[3] != ids[4] && ids[3] is "r1" or "r2" && ids[4] is "r1" or "r2", "recent tracks follow as one group");
        var plan = session.QueueState.Plan;
        Check(plan.Count == 4 && plan[0].ItemId == "f2" && plan[0].Reason == QueuePlanReason.ShuffleFresh
            && plan[1].ItemId == "f3" && plan[1].Reason == QueuePlanReason.ShuffleFresh
            && plan[2].Reason == QueuePlanReason.ShuffleRecent && plan[3].Reason == QueuePlanReason.ShuffleRecent,
            "play order names fresh songs before recently heard ones");

        await using var repeating = new PlaybackSessionCoordinator(new FakeHost());
        await repeating.SetQueueAsync([new("a", "a", a), new("c", "c", a)], "c");
        await repeating.SetQueueModeAsync(QueuePlaybackMode.RepeatAll);
        Check(repeating.QueueState.Upcoming?.ItemId == "a" && repeating.QueueState.Plan is [{ Reason: QueuePlanReason.RepeatWrap }, ..],
            "repeat-all names the wrap back to the start");

        var album = new QueueOrigin("album", "Album", "album-plan");
        await using var backward = new PlaybackSessionCoordinator(new FakeHost());
        await backward.SetQueueAsync([new("old", "old", b), new("only", "only", a, Origin: album)], "old");
        await backward.NextAsync();
        await backward.PreviousAsync();
        Check(backward.Snapshot.Current?.ItemId == "old", "album previous falls back to history at the album start");
    }

    private static string[] NativeItemIds(FakeHost native)
    {
        var items = native.LastQueue.GetProperty("items");
        var ids = new string[items.GetArrayLength()];
        var index = 0;
        foreach (var item in items.EnumerateArray()) ids[index++] = item.GetProperty("itemId").GetString()!;
        return ids;
    }

    private static async Task AutomixAsync(string a, string b)
    {
        var native = new FakeHost();
        await using var session = new PlaybackSessionCoordinator(native, new FixedDurationProbe(30));
        await session.SetAutomixEnabledAsync(true);
        await session.SetAlbumGaplessEnabledAsync(true);
        await session.SetQueueAsync([new("a", "track-a", a), new("b", "track-b", b)], "a");
        Check(!native.Calls.Contains("audio.gaplessPrepare"), "automix owns the seam instead of gapless");
        native.Emit("audio.started", "{\"operationId\":1}");
        native.Emit("audio.position", "{\"operationId\":1,\"framesPlayed\":48000,\"outputFrame\":48000}");
        await session.SetVolumeAsync(0.4);
        var plan = native.AutomixRequest!.Value[0].GetProperty("plan");
        Check(plan.GetProperty("mode").GetString() == "short_crossfade"
            && plan.GetProperty("overlapFrames").GetInt64() == 86400
            && plan.GetProperty("fadeStartOutputFrame").GetInt64() == 393600
            && plan.GetProperty("fadeEndOutputFrame").GetInt64() == plan.GetProperty("fadeStartOutputFrame").GetInt64() + 86400
            && plan.GetProperty("commitOutputFrame").GetInt64() == plan.GetProperty("fadeEndOutputFrame").GetInt64()
            && plan.GetProperty("fallbackReason").GetString() == "analysis_unavailable"
            && plan.GetProperty("toItemId").GetString() == "b"
            && native.AutomixRequest.Value[0].GetProperty("nextSource").GetProperty("uri").GetString() == b,
            "automix uses the analysis-unavailable short crossfade");
        var queueRevision = native.LastQueue.GetProperty("revision").GetInt64();
        native.Emit("audio.transitionCommitted", $$"""
            {"queueRevision":{{queueRevision}},"operationId":9,"fromItemId":"a","fromTrackId":"track-a","toItemId":"b","toTrackId":"track-b","outputFrame":480000,"sourcePositionSeconds":0.25}
            """);
        await session.SetVolumeAsync(0.3);
        Check(session.Snapshot.Current?.ItemId == "b" && session.Snapshot.OperationId == 9
            && Math.Abs(session.Snapshot.PositionSeconds - 0.25) < 0.001, "automix commit adopts the incoming track");

        var repeat = new FakeHost();
        await using var repeating = new PlaybackSessionCoordinator(repeat, new FixedDurationProbe(30));
        await repeating.SetRepeatAsync(RepeatMode.One);
        await repeating.SetAutomixEnabledAsync(true);
        await repeating.SetQueueAsync([new("a", "a", a), new("b", "b", b)], "a");
        repeat.Emit("audio.started", "{\"operationId\":1}");
        repeat.Emit("audio.position", "{\"operationId\":1,\"framesPlayed\":4800,\"outputFrame\":4800}");
        await repeating.SetVolumeAsync(0.2);
        Check(!repeat.Calls.Contains("automix.prepare"), "repeat-one does not arm automix");
    }

    private sealed class FixedDurationProbe(double duration) : IPlaybackSourceProbe
    {
        public ValueTask<int?> ReadSampleRateAsync(AudioSource source, CancellationToken token) => new(48000);
        public ValueTask<double?> ReadDurationAsync(AudioSource source, CancellationToken token) => new(duration);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
            await session.SetQueueAsync([new("duplicate-first", "a", a), new("duplicate-second", "a", a)], "duplicate-first");
            await session.SelectQueueItemAsync("duplicate-second");
            clock.Restart();
            while (session.Snapshot.Status != PlaybackStatus.Playing)
            { if (clock.Elapsed > TimeSpan.FromSeconds(2)) throw new TimeoutException("duplicate occurrence start"); await Task.Delay(10); }
            await session.SeekAsync(2.15);
            clock.Restart();
            while (session.Snapshot.Status != PlaybackStatus.Ended)
            { if (clock.Elapsed > TimeSpan.FromSeconds(2)) throw new TimeoutException("manual duplicate cursor replayed an earlier occurrence"); await Task.Delay(10); }
            Check(session.Snapshot.Current?.ItemId == "duplicate-second", "manual same-source occurrence remains authoritative");
            Console.WriteLine("PASS real Native Host cursorOperationId: manual same-source occurrence ends without replaying itself");
        }
        finally { session.Changed -= Observe; }
    }
}
