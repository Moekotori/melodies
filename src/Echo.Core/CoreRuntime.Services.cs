using Echo.Application.Podcast;
using Echo.Application.Radio;
using Echo.Application.Remote;
using Echo.Contracts.Library;
using Echo.Contracts.Lyrics;
using Echo.Contracts.Podcast;
using Echo.Contracts.Radio;
using Echo.Core.Contracts;
using Echo.Infrastructure.Artwork;
using Echo.Infrastructure.Lyrics;
using Echo.Infrastructure.Podcast;
using Echo.Infrastructure.Preferences;
using Echo.Infrastructure.Radio;
using Echo.Infrastructure.Remote;

namespace Echo.Core;

public sealed partial class CoreRuntime
{
    private Echo.Playback.IPlaybackWake? wake;
    private PodcastProgressWriter? podcastProgress;
    private RadioNowPlaying? radioNowPlaying;
    private readonly SemaphoreSlim radioWrites = new(1, 1);
    private long radioRevision;
    private CoreRadioState radioState = new(0, []);
    public string Identity { get; private set; } = "";
    public RemoteSourceCatalog RemoteSources { get; private set; } = null!;
    public RemoteFileClients RemoteFiles { get; private set; } = null!;
    public IPodcastLibrary? Podcasts { get; private set; }
    public RadioCatalog Radio { get; private set; } = null!;
    public ILyricsSource Lyrics { get; private set; } = null!;
    public IAlbumArtwork Artwork { get; private set; } = null!;
    public IDetailResources Details { get; private set; } = null!;
    public IRadioMetadataMonitor RadioMetadata { get; private set; } = null!;
    public IRadioDirectory RadioDirectory { get; private set; } = null!;
    public Echo.Infrastructure.Broadcast.LocalBroadcastService Broadcast { get; private set; } = null!;
    public IRadioStreamResolver RadioStreams { get; private set; } = null!;
    public CoreRadioState RadioState => Volatile.Read(ref radioState);
    public event Action? RadioChanged;

    private void InitializeServices(string libraryPath, CoreRuntimeOptions? options)
    {
        var directory = options?.Directory ?? Path.GetDirectoryName(Path.GetFullPath(libraryPath))!;
        InitializeBackup(libraryPath, options);
        wake = options?.Wake;
        if (wake is not null) { wake.DiagnosticChanged += Playback.ReportPlatformDiagnostic; Playback.LocalZone.Session.Changed += UpdateWake; }
        Identity = new Transport.CoreEndpoint(directory, libraryPath).Identity;
        RemoteFiles = new(); RemoteSources = new(options?.RemoteStore, RemoteFiles);
        RemoteFiles.BindTokenWriter(RemoteSources);
        if (ownsPlayback) ((Echo.Playback.PlaybackSessionCoordinator)Playback.LocalZone.Session).AttachRemoteResolver(new RemotePlaybackResolver(RemoteSources, RemoteFiles));
        Lyrics = new RemoteSidecarLyrics(new LocalLyricsSource(), RemoteSources.Find, RemoteFiles);
        Artwork = new RemoteSidecarArtwork(new LocalAlbumArtwork(), RemoteSources.Find, RemoteFiles);
        Details = new LocalDetailResources();
        Broadcast = new(() =>
        {
            var state = Playback.LocalZone.Session.Snapshot;
            string Read(string key) => state.Current?.Metadata?.GetValueOrDefault(key) is { } value ? value[..Math.Min(value.Length, 512)] : "";
            return new(Read("title"), Read("artist"), Read("album"), state.Status.ToString(), state.PositionSeconds, state.DurationSeconds, state.Volume, state.PlaybackRate);
        });
        Radio = new(options?.Persistent == true ? new JsonRadioStationStore(Path.Combine(directory, "radio-stations.json")) : null);
        RadioMetadata = new IcyMetadataMonitor(); RadioDirectory = new RadioDirectoryClient(); RadioStreams = new HttpRadioStreamResolver();
        radioNowPlaying = new(Playback.LocalZone.Session, RadioMetadata);
        if (options?.Persistent == true)
        {
            Podcasts = new PodcastLibrary(Path.Combine(directory, "podcasts.sqlite"));
            podcastProgress = new(Playback.LocalZone.Session, Podcasts);
        }
    }

    public async Task StartAsync(CancellationToken token = default)
    {
        await Playback.StartAsync(token).ConfigureAwait(false);
        try { await RemoteSources.LoadAsync(token).ConfigureAwait(false); }
        catch (Exception) when (!token.IsCancellationRequested) { Playback.ReportPlatformDiagnostic("网盘配置暂时不可读取；本地曲库仍可使用。"); }
        await Radio.LoadAsync(token).ConfigureAwait(false);
        Volatile.Write(ref radioState, new(radioRevision, Radio.Stations));
        await RestoreCoreSettingsAsync(token).ConfigureAwait(false);
        RefreshWake();
    }

    public async Task WriteRadioAsync(CoreRadioWrite request, CancellationToken token)
    {
        await radioWrites.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (request.ExpectedRevision != radioRevision) throw new InvalidOperationException("电台列表已在另一控制端更新，请刷新后重试。");
            await Radio.UpdateAsync(_ => request.Stations, token).ConfigureAwait(false);
            Interlocked.Increment(ref radioRevision);
            Volatile.Write(ref radioState, new(radioRevision, Radio.Stations));
        }
        finally { radioWrites.Release(); }
        RadioChanged?.Invoke();
    }

    private void UpdateWake(Echo.Playback.PlaybackSnapshot _) => RefreshWake();
    internal void RefreshWake() => wake?.Update(Settings.Read("prevent-sleep-while-playing", new()).Enabled && Playback.LocalZone.Session.Snapshot.Status == Echo.Playback.PlaybackStatus.Playing);

    private async Task ReleaseServicesAsync()
    {
        if (wake is not null)
        {
            Playback.LocalZone.Session.Changed -= UpdateWake; wake.DiagnosticChanged -= Playback.ReportPlatformDiagnostic;
            await wake.DisposeAsync().ConfigureAwait(false);
        }
        try { await AutomaticBackup.DisposeAsync().ConfigureAwait(false); if (podcastProgress is not null) await podcastProgress.DisposeAsync().ConfigureAwait(false); }
        finally
        {
            Podcasts?.Dispose(); radioNowPlaying?.Dispose(); RadioDirectory.Dispose();
            if (RadioStreams is IDisposable disposable) disposable.Dispose();
            RemoteFiles.Dispose();
            await Broadcast.DisposeAsync().ConfigureAwait(false);
            if (Lyrics is IAsyncDisposable lyrics) await lyrics.DisposeAsync().ConfigureAwait(false);
        }
    }
}
