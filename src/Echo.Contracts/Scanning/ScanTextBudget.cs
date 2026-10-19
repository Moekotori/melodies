using Echo.Contracts.Library;

namespace Echo.Contracts.Scanning;

/// <summary>Conservative UTF-16 payload accounting, including JSON escaping of virtual source facts.</summary>
public static class ScanTextBudget
{
    public static int Estimate(FileSnapshot file, TrackMetadata? metadata = null)
    {
        var characters = file.Path.Length + file.PathKey.Length;
        if (!file.PreserveExisting && file.Issue is null && file.Source is { } source)
            characters += SourceUpperBound(source);
        if (metadata is { } m)
            characters += m.Title.Length + m.Artist.Length + m.Album.Length + m.AlbumArtist.Length + m.Genre.Length + m.Codec.Length;
        return characters;
    }

    public static int SourceUpperBound(TrackSource source)
    {
        var text = source.Kind.Length + source.AudioPath.Length + source.DefinitionPath.Length + source.Stamp.Length
            + (source.Title?.Length ?? 0) + (source.Artist?.Length ?? 0) + (source.Album?.Length ?? 0)
            + (source.AlbumArtist?.Length ?? 0) + (source.BeatmapId?.Length ?? 0) + (source.BeatmapSetId?.Length ?? 0)
            + (source.CoverPath?.Length ?? 0);
        return 512 + text * 6;
    }
}
