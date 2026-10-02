using System.Diagnostics;
using System.Text.Json;

namespace Echo.Playback;

public sealed partial class PlaybackSessionCoordinator
{
    private sealed record LevelEvent(PlaybackLevel Level, long Epoch);
    private LevelEvent? pendingLevel;
    private PlaybackLevel? currentLevel;
    private int levelQueued;

    // Keep one aggregate frame, never PCM/spectrum or an unbounded channel history.
    private void QueueLevel(JsonElement value, long epoch)
    {
        if (MaximumDb(value, "peakDb") is not { } peak) return;
        var level = new PlaybackLevel(value.Long("operationId"), peak, MaximumDb(value, "rmsDb"), Stopwatch.GetTimestamp(), ReadSpectrum(value));
        Interlocked.Exchange(ref pendingLevel, new(level, epoch));
        if (Interlocked.Exchange(ref levelQueued, 1) != 0) return;
        if (!lane.Post(() =>
        {
            Interlocked.Exchange(ref levelQueued, 0);
            var latest = Interlocked.Exchange(ref pendingLevel, null);
            if (latest is not null && latest.Epoch == transportEpoch && latest.Level.OperationId == snapshot.OperationId
                && snapshot.Status == PlaybackStatus.Playing)
            { currentLevel = latest.Level; PublishState(); }
            return Task.CompletedTask;
        })) Interlocked.Exchange(ref levelQueued, 0);
    }

    private static double? MaximumDb(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var array)
            || array.ValueKind != JsonValueKind.Array) return null;
        double? maximum = null;
        var count = Math.Min(8, array.GetArrayLength());
        for (var i = 0; i < count; i++)
            if (array[i].ValueKind == JsonValueKind.Number && array[i].TryGetDouble(out var db) && double.IsFinite(db))
                maximum = Math.Max(maximum ?? -160, Math.Clamp(db, -160, 24));
        return maximum;
    }
    private static IReadOnlyList<float>? ReadSpectrum(JsonElement value)
    {
        if (!value.TryGetProperty("visualSpectrum", out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() is 0 or > 128) return null;
        var buckets = new float[array.GetArrayLength()];
        for (var i = 0; i < buckets.Length; i++)
        {
            if (array[i].ValueKind != JsonValueKind.Number || !array[i].TryGetSingle(out var number) || !float.IsFinite(number)) return null;
            buckets[i] = Math.Clamp(number, 0, 1);
        }
        return Array.AsReadOnly(buckets);
    }
}
