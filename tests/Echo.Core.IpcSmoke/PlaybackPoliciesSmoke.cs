using Echo.Core.Client;
using Echo.Contracts.Settings;
using Echo.Playback;

internal static class PlaybackPoliciesSmoke
{
    public static async Task BeforeRestartAsync(CoreConnection a, CoreConnection b, CancellationToken token)
    {
        await a.Playback.LocalZone.Session.SetShuffleAvoidRecentCountAsync(100, token);
        await a.Settings.ConfirmAsync("shuffle-credibility", new(Selection: "避开最近 25 首"), token);
        await a.Settings.ConfirmAsync("gapless-playback", new(true), token);
        var saved = await b.Settings.ReadAsync(token);
        Check(saved["shuffle-credibility"].Selection == "避开最近 25 首" && saved["gapless-playback"].Enabled,
            "Controls did not share Core-owned playback policies");
        var session = a.Playback.LocalZone.Session;
        await session.NextAsync(token);
        await session.SetQueueModeAsync(QueuePlaybackMode.Shuffle, token);
        await UntilAsync(() => b.Playback.LocalZone.Session.QueueMode == QueuePlaybackMode.Shuffle
            && b.Playback.LocalZone.Session.QueueState.Plan is [{ Reason: QueuePlanReason.ShuffleRecent }], token);
        await session.PauseAsync(token);
        Console.WriteLine("PASS IPC typed shuffle command, shared avoidance plan and Core-owned settings");
    }

    public static async Task AfterRestartAsync(CoreConnection connection, IReadOnlyList<QueueTrack> tracks, CancellationToken token)
    {
        Check((await connection.Settings.ReadAsync(token))["shuffle-credibility"].Selection == "避开最近 25 首", "Core lost playback policy");
        var session = connection.Playback.LocalZone.Session;
        await session.SetQueueModeAsync(QueuePlaybackMode.Sequential, token);
        await session.SetQueueAsync(tracks, tracks[0].ItemId, token);
        await session.SetQueueModeAsync(QueuePlaybackMode.Shuffle, token);
        await UntilAsync(() => session.QueueState.Plan is [{ Reason: QueuePlanReason.ShuffleRecent }], token);
        Console.WriteLine("PASS IPC restart restored avoidance behavior and recent song identities");
    }

    private static async Task UntilAsync(Func<bool> condition, CancellationToken token)
    { while (!condition()) await Task.Delay(10, token); }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
