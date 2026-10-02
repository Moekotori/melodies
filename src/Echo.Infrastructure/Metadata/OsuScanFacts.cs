namespace Echo.Infrastructure.Metadata;

// Port of OsuArchiveImport/OsuScanText fields; no hit-object or animation arrays.
internal sealed record OsuScanFacts(string? AudioFilename, string? Title, string? Artist,
    string? Creator, string? Version, string? BeatmapId, string? BeatmapSetId,
    double? Bpm, string? CoverFilename, bool HasStoryboard);
