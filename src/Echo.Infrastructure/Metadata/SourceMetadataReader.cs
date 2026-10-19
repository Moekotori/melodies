using Echo.Contracts.Library;
using Echo.Contracts.Scanning;

namespace Echo.Infrastructure.Metadata;

internal sealed class SourceMetadataReader
{
    private readonly Dictionary<string, (long Size, long Ticks, TrackMetadata Value)> _audio = new(StringComparer.Ordinal);
    public async ValueTask<TrackMetadata> ReadAsync(FileSnapshot file, TrackSource source,
        Func<FileSnapshot, bool, CancellationToken, ValueTask<TrackMetadata>> read, CancellationToken cancellation)
    {
        var key = source.AudioPath;
        if (!_audio.TryGetValue(key, out var cached) || cached.Size != source.AudioSize || cached.Ticks != source.AudioTicks)
        {
            var physical = new FileSnapshot(source.AudioPath, Files.LibraryPath.Key(source.AudioPath), source.AudioSize, source.AudioTicks);
            var value = await read(physical, source.Kind == "osu", cancellation).ConfigureAwait(false);
            cached = (source.AudioSize, source.AudioTicks, value);
            if (_audio.Count >= 128) _audio.Remove(_audio.Keys.First());
            _audio[key] = cached;
        }
        var metadata = cached.Value;
        if (source.Kind == "local") return metadata;
        var end = source.EndSeconds ?? metadata.DurationSeconds;
        if (source.Kind == "cue" && (source.StartSeconds < 0 || end <= source.StartSeconds ||
            end > metadata.DurationSeconds + 0.05)) throw new MetadataReadException("cue_range_invalid");
        return MetadataRules.Normalize(file, metadata with
        {
            Title = source.Title ?? metadata.Title, Artist = source.Artist ?? metadata.Artist,
            Album = string.IsNullOrWhiteSpace(source.Album) ? metadata.Album : source.Album,
            AlbumArtist = string.IsNullOrWhiteSpace(source.AlbumArtist) ? metadata.AlbumArtist : source.AlbumArtist,
            TrackNumber = source.TrackNumber ?? metadata.TrackNumber,
            Bpm = source.Bpm ?? metadata.Bpm,
            DurationSeconds = source.Kind == "cue" ? end - source.StartSeconds : metadata.DurationSeconds
        });
    }
}
