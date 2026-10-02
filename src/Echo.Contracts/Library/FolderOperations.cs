using Echo.Contracts.Scanning;

namespace Echo.Contracts.Library;

public enum FolderScanMode { Incremental, Full, EmbeddedTags }
public sealed record FolderScanOptions(string Path, FolderScanMode Mode = FolderScanMode.Incremental, bool Recursive = true);
public sealed record FolderIgnorePreview(long Examined, long Ignored, IReadOnlyList<string> Samples, bool Limited);
public sealed record FolderResourceProgress(long Checked, long Total, long Covers, long Lyrics);
public interface IFolderOperations
{
    Task<ScanReport> ScanFolderAsync(FolderScanOptions options, IProgress<ScanProgress> progress, CancellationToken cancellation);
    Task<FolderIgnorePreview> PreviewFolderIgnoresAsync(string path, CancellationToken cancellation);
    Task CheckFolderResourcesAsync(FolderTrackQuery query, IProgress<FolderResourceProgress> progress, CancellationToken cancellation);
    Task SetFolderWatchAsync(string path, bool enabled, CancellationToken cancellation);
    event Action<string>? FolderChanged;
}
