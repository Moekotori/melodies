using System.Text.Json;
using Echo.Playback;

internal static class SeamAndShuffleChecks
{
    public static async Task RunAsync(string path)
    {
        foreach (var count in new[] { 0, 25, 100 }) await AvoidanceAsync(path, count);
        await GaplessEditsAsync(path);
        Console.WriteLine("PASS shuffle 0/25/100, track identities, queue replacement, persistence, pinned priority; gapless edit, seek, cancel and committed-chain handling");
    }

    private static async Task AvoidanceAsync(string path, int count)
    {
        var host = new FakeHost();
        await using var session = new PlaybackSessionCoordinator(host);
        var tracks = Enumerable.Range(0, 31).Select(i => new QueueTrack($"i{i}", $"t{i}", path)).ToArray();
        await session.SetQueueAsync(tracks, "i0");
        for (var i = 0; i < 30; i++) await session.NextAsync();
        var checkpoint = await session.ExportCheckpointAsync();
        await using var restored = new PlaybackSessionCoordinator(new FakeHost());
        await restored.RestoreCheckpointAsync(checkpoint);
        await restored.SetShuffleAvoidRecentCountAsync(count);
        await restored.SetQueueAsync([tracks[30], tracks[0], tracks[5], tracks[29], new("fresh", "fresh", path), new("dup", "t30", path)], "i30");
        await restored.SetQueueModeAsync(QueuePlaybackMode.Shuffle);
        var reasons = restored.QueueState.Plan.ToDictionary(x => x.ItemId, x => x.Reason);
        Check(reasons["fresh"] == QueuePlanReason.ShuffleFresh && reasons["dup"] == QueuePlanReason.ShuffleRecent, "fresh/active identity priority");
        Check((reasons["i0"] == QueuePlanReason.ShuffleRecent) == (count == 100), "100-song window includes older history");
        Check((reasons["i5"] == QueuePlanReason.ShuffleRecent) == (count > 0)
            && (reasons["i29"] == QueuePlanReason.ShuffleRecent) == (count > 0), "25-song window survives restart and queue replacement");
        var plan = restored.QueueState.Plan;
        Check(!plan.SkipWhile(x => x.Reason == QueuePlanReason.ShuffleFresh).Any(x => x.Reason == QueuePlanReason.ShuffleFresh), "recent songs follow fresh ones");
        await restored.EnqueueAsync([new("pin", "t30", path)], nextUp: true);
        Check(restored.QueueState.Upcoming?.ItemId == "pin", "explicit next-up overrides avoidance");

        await session.SetShuffleAvoidRecentCountAsync(count);
        await session.SetQueueModeAsync(QueuePlaybackMode.Shuffle);
        await session.EnqueueAsync([new("duplicate", "fresh-track", path), new("original", "fresh-track", path), new("other", "other", path)]);
        await session.SelectQueueItemAsync("original");
        Check(session.QueueState.Upcoming?.TrackId != "fresh-track", "duplicate active track does not immediately repeat");
        await session.SetQueueModeAsync(QueuePlaybackMode.Sequential);
        await session.SetQueueAsync([new("x1", "x", path), new("y1", "y", path), new("x2", "x", path)], "y1");
        // Start X explicitly after Y so all candidates are recent; active duplicates must still be last.
        await session.SelectQueueItemAsync("x1");
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await session.SetQueueModeAsync(QueuePlaybackMode.Shuffle);
            Check(session.QueueState.Upcoming?.TrackId == "y", "small queue never repeats active identity while another song exists");
            await session.SetQueueModeAsync(QueuePlaybackMode.Sequential);
        }
        await session.SetQueueModeAsync(QueuePlaybackMode.Shuffle);
        var before = session.QueueState;
        host.RejectQueueOnce = true;
        try { await session.SetShuffleAvoidRecentCountAsync(count == 100 ? 25 : 100); throw new Exception("queue rejection accepted"); }
        catch (IOException) { }
        Check(ReferenceEquals(before, session.QueueState), "failed native acknowledgement preserves published plan");
        try { await session.SetShuffleAvoidRecentCountAsync(99); throw new Exception("invalid avoidance accepted"); }
        catch (ArgumentOutOfRangeException) { }
    }

    private static async Task GaplessEditsAsync(string path)
    {
        var host = new FakeHost();
        await using var session = new PlaybackSessionCoordinator(host);
        await session.SetAlbumGaplessEnabledAsync(true);
        await session.SetQueueAsync([new("a", "a", path), new("b", "b", path), new("c", "c", path)], "a");
        await session.EnqueueAsync([new("pin", "pin", path)], nextUp: true);
        Check(host.GaplessRequest!.Value[0].GetProperty("itemId").GetString() == "pin", "queue edit reprimes exact next item");
        await session.RemoveQueueItemsAsync(["pin"]);
        Check(host.GaplessRequest!.Value[0].GetProperty("itemId").GetString() == "b", "removal reprimes successor");
        var primes = host.Calls.Count(x => x == "audio.gaplessPrepare");
        await session.SeekAsync(2);
        Check(host.Calls.Count(x => x == "audio.gaplessPrepare") == primes + 1, "seek rearms gapless");
        await session.SetAlbumGaplessEnabledAsync(false);
        Check(host.Calls[^1] == "queue.set", "disable invalidates prebuffered chain");
        await session.SetAlbumGaplessEnabledAsync(true);
        await session.SetSegmentLoopAsync("a", 1, 2);
        primes = host.Calls.Count(x => x == "audio.gaplessPrepare");
        await session.ClearSegmentLoopAsync();
        Check(host.Calls.Count(x => x == "audio.gaplessPrepare") == primes + 1, "clearing A-B loop restores gapless");
        var op = session.Snapshot.OperationId;
        var revision = host.LastQueue.GetProperty("revision").GetInt64();
        host.Emit("audio.ended", $$"""{"queueAdvance":true,"gaplessAdvance":true,"fromOperationId":{{op}},"operationId":100,"queueRevision":{{revision}},"nextItemId":"b","nextSampleRate":48000,"nextDurationSeconds":10} """);
        await session.PauseAsync(); // The lane is a barrier for the host event.
        var opens = host.Calls.Count(x => x == "audio.openFile");
        await session.RemoveQueueItemsAsync(["a"]);
        Check(host.Calls.Count(x => x == "audio.openFile") == opens, "consumed-head removal preserves audible continuation");
        await session.SetAlbumGaplessEnabledAsync(false);
        Check(host.Calls.Count(x => x == "audio.openFile") == opens + 1 && session.Snapshot.Current?.ItemId == "b"
            && session.Snapshot.Status == PlaybackStatus.Paused, "disable rebuilds committed FIFO without losing current item/pause");
        await session.SetQueueModeAsync(QueuePlaybackMode.Shuffle);
        primes = host.Calls.Count(x => x == "audio.gaplessPrepare");
        await session.SetAlbumGaplessEnabledAsync(true);
        Check(host.Calls.Count(x => x == "audio.gaplessPrepare") == primes, "shuffle leaves album seam disabled");
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
