namespace Echo.Contracts.Library;

// Physical/source facts are immutable and bounded; virtual identity remains separate from playback path.
public sealed record TrackSource(
    string Kind, string AudioPath, string DefinitionPath, string Stamp,
    long AudioSize, long AudioTicks, long DefinitionSize, long DefinitionTicks,
    string? Title = null, string? Artist = null, string? Album = null, string? AlbumArtist = null,
    double StartSeconds = 0, double? EndSeconds = null, int? TrackNumber = null,
    double? Bpm = null, string? BeatmapId = null, string? BeatmapSetId = null,
    string? CoverPath = null, bool HasStoryboard = false);
public sealed record SourceCacheItem(string PathKey, long SizeBytes, long ModifiedTicks, string Json);
