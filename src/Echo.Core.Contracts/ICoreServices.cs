using Echo.Application.Playback;
using Echo.Contracts.Library;
using Echo.Contracts.Remote;
using Echo.Contracts.Podcast;
using Echo.Contracts.Lyrics;
using Echo.Contracts.Scanning;

namespace Echo.Core.Contracts;

public interface ICoreLibrary : ITrackLibrary, IAlbumLibrary, IArtistLibrary, IGenreLibrary,
    IFolderLibrary, IAlbumPersonalState, IPlayerTrackLibrary, IHomeLibrary, IFolderOperations,
    IFolderWorkbench, IPlaylistLibrary, IRemoteTrackIndex, IHistoryLibrary, ILibraryMaintenance, ILibraryReview, IAlbumMetadata
{
    Task<LibraryStats> GetStatsAsync(CancellationToken cancellation = default);
    Task<IReadOnlyList<ScanIssue>> GetScanIssuesAsync(string jobId, int limit = 16, CancellationToken cancellation = default);
    Task<ScanReport> ImportAsync(string folder, IProgress<ScanProgress> progress, CancellationToken cancellation);
    Task SetScanProfileAsync(string value, CancellationToken cancellation = default);
}

public interface ICoreServices : IAsyncDisposable
{
    bool IsRemote { get; }
    Echo.Bridge.IBridgeRuntime Bridge { get; }
    ICoreSettings Settings { get; }
    Echo.Contracts.Backup.IDataBackup Backup { get; }
    IPlaybackRuntime Playback { get; }
    ICoreLibrary Library { get; }
    IRemoteSourceCatalog RemoteSources { get; }
    IRemoteFileClient RemoteFiles { get; }
    IPodcastLibrary? Podcasts { get; }
    Echo.Contracts.Radio.IRadioCatalog Radio { get; }
    Echo.Contracts.Radio.IRadioMetadataMonitor RadioMetadata { get; }
    Echo.Contracts.Radio.IRadioDirectory RadioDirectory { get; }
    Echo.Contracts.Radio.IRadioStreamResolver RadioStreams { get; }
    Echo.Contracts.Settings.ILocalBroadcast Broadcast { get; }
    ILyricsSource Lyrics { get; }
    IAlbumArtwork Artwork { get; }
    IDetailResources Details { get; }
    event Action<CoreDiagnostic>? Diagnostic;
    event Action? ListeningChanged;
    event Action<string>? ListeningDiagnostic;
}
public interface ICoreTaskControl
{
    Task CancelActiveTaskAsync(CancellationToken token = default);
    Task RunMaintenanceAsync(string action, IProgress<CoreTaskSnapshot> progress, CancellationToken token);
    Task<int> SetAllFolderWatchAsync(bool enabled, CancellationToken token);
}
public interface ICoreBatchImport
{
    Task<ScanReport> ImportBatchAsync(IReadOnlyList<string> paths, IProgress<CoreTaskSnapshot> progress, CancellationToken token);
}
