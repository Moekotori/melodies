namespace Echo.Core.Protocol;

public sealed class ScanCueRequest
{
    public string FilePath { get; set; } = string.Empty;
    public string? RootPath { get; set; }
    public bool IsolateFileErrors { get; set; }
    public bool Embedded { get; set; }
    public List<ScanCueFile>? Files { get; set; }
}

public sealed record ScanCueFile(string FilePath, bool Embedded);
public sealed record ScanCueTrackDetails(string CuePath, string AudioPath, string Source, int TrackNumber,
    string? Title, string? Performer, string? Album, string? AlbumArtist,
    double? PregapStartSeconds, double StartSeconds, double? EndSeconds);
public sealed record ScanCueTrack(int TrackNumber, string AudioPath, long SizeBytes, long MtimeMs, ScanCueTrackDetails? CueTrack = null);
public sealed record ScanCueResult(List<ScanCueTrack> Tracks, string? Issue = null);
public sealed record ScanCueFileResult(string FilePath, ScanCueResult Result);
public sealed record ScanCueBatchResult(List<ScanCueFileResult> Files);
