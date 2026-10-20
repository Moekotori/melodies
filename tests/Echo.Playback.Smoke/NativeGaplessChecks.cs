using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Echo.Playback;
using Echo.Playback.NativeAudio;

internal static class NativeGaplessChecks
{
    public static async Task RunAsync(string executable, string a, string b)
    {
        await using var host = new NativeAudioHost(executable);
        await using var session = new PlaybackSessionCoordinator(host);
        var advances = new ConcurrentQueue<JsonElement>();
        host.Notification += (method, value) =>
        {
            if (method == "audio.ended" && value.TryGetProperty("queueAdvance", out var advance) && advance.ValueKind == JsonValueKind.True)
                advances.Enqueue(value.Clone());
        };
        await session.SetVolumeAsync(0); // Exercise the real output callback with a silent fixture.
        await session.SetAlbumGaplessEnabledAsync(true);
        await session.SetQueueAsync([new("a", "a", a), new("b", "b", b), new("c", "c", b), new("d", "d", b)], "a");
        await UntilAsync(() => session.Snapshot.Status == PlaybackStatus.Playing);
        await session.SeekAsync(2);
        await UntilAsync(() => session.Snapshot.Status == PlaybackStatus.Ended);
        CheckAdvances(advances, ["b", "c", "d"]);
        Console.WriteLine("PASS real native gapless: seek rearmed, three confirmed PCM seams, full album drained");

        advances.Clear();
        await session.SetQueueAsync([new("a", "a", a), new("b", "b", a), new("c", "c", b), new("d", "d", b)], "a");
        await UntilAsync(() => session.Snapshot.Status == PlaybackStatus.Playing);
        await session.SeekAsync(2);
        await UntilAsync(() => session.Snapshot.Current?.ItemId == "b");
        await session.PauseAsync();
        await session.MoveQueueItemsAsync(["d"], 2);
        if (session.Snapshot.Current?.ItemId != "b" || session.Snapshot.Status != PlaybackStatus.Paused)
            throw new InvalidOperationException("committed chain edit lost current song or pause");
        await session.ResumeAsync();
        await UntilAsync(() => session.Snapshot.Status == PlaybackStatus.Ended);
        CheckAdvances(advances, ["b", "d", "c"]);
        Console.WriteLine("PASS real native gapless: committed-chain reorder preserved pause/current and followed the edited sequence");
    }

    private static void CheckAdvances(ConcurrentQueue<JsonElement> advances, string[] expected)
    {
        var events = advances.ToArray();
        if (!events.Select(x => x.GetProperty("nextItemId").GetString()).SequenceEqual(expected)
            || events.Any(x => !x.TryGetProperty("gaplessAdvance", out var gapless) || gapless.ValueKind != JsonValueKind.True))
            throw new InvalidOperationException("Native did not confirm the complete gapless sequence: " + string.Join(";", events.Select(x => x.GetRawText())));
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(8)) throw new TimeoutException("native gapless state");
            await Task.Delay(10);
        }
    }
}
