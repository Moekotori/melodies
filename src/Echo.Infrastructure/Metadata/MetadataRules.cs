using Echo.Contracts.Library;
using Echo.Contracts.Scanning;

namespace Echo.Infrastructure.Metadata;

internal static class MetadataRules
{
    public static TrackMetadata Normalize(FileSnapshot file, TrackMetadata value)
    {
        if (!double.IsFinite(value.DurationSeconds) || value.DurationSeconds < 0 ||
            value.SampleRate <= 0 || value.BitDepth < 0)
            throw new MetadataReadException("invalid_audio_properties");
        return value with
        {
            Title = Limit(string.IsNullOrWhiteSpace(value.Title) ? Path.GetFileNameWithoutExtension(file.Path) : value.Title, 1024),
            Artist = Limit(value.Artist, 512), Album = Limit(value.Album, 512),
            AlbumArtist = Limit(value.AlbumArtist, 512), Genre = Limit(value.Genre, 256),
            Codec = Limit(value.Codec, 64),
            Bitrate = value.Bitrate is > 0 ? value.Bitrate : null,
            Bpm = value.Bpm is > 0 and <= 10000 && double.IsFinite(value.Bpm.Value) ? value.Bpm : null
        };
    }

    public static string Limit(string value, int length)
    {
        value = value.Trim().Trim('\0');
        if (value.Length <= length) return value;
        throw new MetadataReadException("metadata_text_limit");
    }
}
