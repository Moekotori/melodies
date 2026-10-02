namespace Echo.Playback;

public sealed partial class PlaybackSessionCoordinator
{
    private readonly Dictionary<string, int> sourceRates = new(StringComparer.Ordinal);
    private int? KnownSourceRate(QueueTrack track)
        => sourceRates.TryGetValue(track.ItemId, out var rate) ? rate : track.SampleRate is > 0 ? track.SampleRate : null;

    private async Task<(QueueTrack[] Items, string Repeat)> PrepareNativeQueueAsync(QueueTrack[] projected, string? current, RepeatMode mode)
    {
        if (deviceConfiguration?.Text("outputMode") != "asio"
            || (deviceConfiguration?.Child("processing")?.Text("outputFormat") ?? "pcm") != "pcm")
            return (projected.Select(x => x with { SampleRate = KnownSourceRate(x) }).ToArray(), mode.ToString().ToLowerInvariant());

        var index = Array.FindIndex(projected, x => x.ItemId == current);
        if (index < 0) return ([], "off");
        if (sourceProbe is not null)
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            foreach (var track in projected.Skip(index).Take(3))
            {
                if (KnownSourceRate(track) is not null) continue;
                try
                {
                    var rate = await sourceProbe.ReadSampleRateAsync(track.Source ?? AudioSource.Local(track.FilePath), budget.Token).ConfigureAwait(false);
                    if (rate is > 0) RememberSourceRate(track, rate.Value);
                }
                catch (OperationCanceledException) when (budget.IsCancellationRequested) { break; }
            }
        }
        var rated = projected.Select(x => x with { SampleRate = KnownSourceRate(x) }).ToArray();
        if (rated.All(x => x.SampleRate is > 0)) return (rated, mode.ToString().ToLowerInvariant());
        // An unknown tail ends normally; the managed owner opens it once rather than inventing a source rate.
        var window = rated.Skip(index).TakeWhile(x => x.SampleRate is > 0).Take(3).ToArray();
        if (window.Length == 0) window = [rated[index]];
        return (window, mode == RepeatMode.One && window[0].SampleRate is > 0 ? "one" : "off");
    }
    private void RememberSourceRate(QueueTrack track, int rate)
    {
        if (rate <= 0) return;
        if (sourceRates.Count >= QueueCapacity + 1 && !sourceRates.ContainsKey(track.ItemId))
        {
            var active = queue.Select(x => x.ItemId).ToHashSet(StringComparer.Ordinal);
            foreach (var key in sourceRates.Keys.Where(x => !active.Contains(x)).ToArray()) sourceRates.Remove(key);
        }
        sourceRates[track.ItemId] = rate;
    }
}
