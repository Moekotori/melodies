using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Metadata;
using Echo.Playback;

namespace Echo.Infrastructure.Playback;

/// <summary>The playback lane owns this metadata reader and a 256-entry source-facts cache.</summary>
public sealed class PlaybackSourceProbe : IPlaybackSourceProbe
{
    private readonly LocalMetadataReader reader;
    private readonly Dictionary<string, (long Size, long Ticks, int Rate, double Duration)> cache = new(StringComparer.Ordinal);
    public PlaybackSourceProbe(string? scanner = null) => reader = new(scanner);
    public async ValueTask<int?> ReadSampleRateAsync(AudioSource source, CancellationToken token)
        => (await ReadFactsAsync(source, token).ConfigureAwait(false))?.Rate;
    public async ValueTask<double?> ReadDurationAsync(AudioSource source, CancellationToken token)
    {
        var facts = await ReadFactsAsync(source, token).ConfigureAwait(false);
        return facts is { Duration: > 0 } ? facts.Value.Duration : null;
    }
    private async ValueTask<(int Rate, double Duration)?> ReadFactsAsync(AudioSource source, CancellationToken token)
    {
        if (source.Kind != "local") return null;
        var info = new FileInfo(source.Uri);
        if (!info.Exists) return null;
        var key = LibraryPath.Key(source.Uri);
        if (cache.TryGetValue(key, out var found) && found.Size == info.Length && found.Ticks == info.LastWriteTimeUtc.Ticks)
            return (found.Rate, found.Duration);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(2));
            var metadata = await reader.ReadAsync(new FileSnapshot(info.FullName, key, info.Length, info.LastWriteTimeUtc.Ticks), deadline.Token).ConfigureAwait(false);
            if (metadata.SampleRate <= 0) return null;
            if (cache.Count >= 256) cache.Clear();
            cache[key] = (info.Length, info.LastWriteTimeUtc.Ticks, metadata.SampleRate, metadata.DurationSeconds);
            return (metadata.SampleRate, metadata.DurationSeconds);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        catch (MetadataReadException) { return null; }
        catch (IOException) { return null; }
    }
    public ValueTask DisposeAsync() => reader.DisposeAsync();
}
