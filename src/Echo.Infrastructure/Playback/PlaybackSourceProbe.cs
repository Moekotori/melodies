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
    private readonly Dictionary<string, (long Size, long Ticks, int Rate)> cache = new(StringComparer.Ordinal);
    public PlaybackSourceProbe(string? scanner = null) => reader = new(scanner);
    public async ValueTask<int?> ReadSampleRateAsync(AudioSource source, CancellationToken token)
    {
        if (source.Kind != "local") return null;
        var info = new FileInfo(source.Uri);
        if (!info.Exists) return null;
        var key = LibraryPath.Key(source.Uri);
        if (cache.TryGetValue(key, out var found) && found.Size == info.Length && found.Ticks == info.LastWriteTimeUtc.Ticks) return found.Rate;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(2));
            var metadata = await reader.ReadAsync(new FileSnapshot(info.FullName, key, info.Length, info.LastWriteTimeUtc.Ticks), deadline.Token).ConfigureAwait(false);
            if (metadata.SampleRate <= 0) return null;
            if (cache.Count >= 256) cache.Clear();
            cache[key] = (info.Length, info.LastWriteTimeUtc.Ticks, metadata.SampleRate);
            return metadata.SampleRate;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        catch (MetadataReadException) { return null; }
        catch (IOException) { return null; }
    }
    public ValueTask DisposeAsync() => reader.DisposeAsync();
}
