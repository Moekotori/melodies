using System.Text.Json;
using Echo.Core.Contracts;
using Echo.Contracts.Library;
using Echo.Core.Transport;
using Echo.Playback;
using Echo.Contracts.Remote;
using Echo.Contracts.Radio;
using Echo.Contracts.Tasks;

namespace Echo.Core.Hosting;

internal sealed partial class CoreDispatch : ICoreRequestHandler, IAsyncDisposable
{
    private readonly CoreRuntime runtime;
    private readonly CoreTaskRegistry tasks;
    private readonly PlaybackStateSubscription subscription;
    private readonly CancellationTokenSource stop = new();
    private readonly Task updates;
    private readonly object frameGate = new();
    private readonly SemaphoreSlim resources = new(2, 2);
    private readonly SemaphoreSlim lyricsResources = new(4, 4);
    private long serial;
    public Guid Instance { get; } = Guid.NewGuid();
    public event Action? Changed;
    public event Action<CoreMessage>? Notice;
    public event Action? ShutdownRequested;
    public void RequestQuit() => Notice?.Invoke(new("event", Method: "core.quit", Payload: CoreJson.Empty));
    public bool HasActiveTasks => tasks.HasActive;

    public CoreDispatch(CoreRuntime runtime)
    {
        this.runtime = runtime; tasks = new(runtime);
        subscription = runtime.Playback.LocalZone.Subscribe();
        updates = Task.Run(async () =>
        {
            try { while (true) { await subscription.ReadAsync(stop.Token).ConfigureAwait(false); Changed?.Invoke(); } }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (System.Threading.Channels.ChannelClosedException) { }
        });
        runtime.ListeningChanged += OnListeningChanged;
        runtime.ListeningDiagnostic += OnListeningDiagnostic;
        runtime.Library.FolderChanged += OnFolderChanged;
        runtime.Library.ArtistImportChanged += OnArtistImportChanged;
        runtime.Playback.SleepTimer.Changed += OnSleep;
        runtime.Playback.FilesOpened += OnFiles;
        runtime.RadioMetadata.Changed += OnRadioMetadata; runtime.RadioChanged += OnRadioChanged;
    }

    public CorePlaybackFrame Capture()
    {
        lock (frameGate)
        {
            var playback = runtime.Playback;
            var session = playback.LocalZone.Session;
            return new(++serial, session.State, session.VolumeLocked, session.PlaybackRateMode, session.CanUndoQueueEdit,
                session.GeneratedDsdAllowed, session.SegmentLoop, playback.SleepTimer.State, playback.PersistenceError, playback.MediaError, Admission: playback.BackgroundWork.Snapshot, ZoneName: playback.LocalZone.Name);
        }
    }

    private void OnListeningChanged() => Notice?.Invoke(new("event", Method: "library.listening", Payload: CoreJson.Empty));
    private void OnListeningDiagnostic(string message) => Notice?.Invoke(new("event", Method: "diagnostic", Payload: CoreJson.Value(new CoreDiagnostic("listening", message))));
    private void OnFolderChanged(string path) => Notice?.Invoke(new("event", Method: "library.folder", Payload: CoreJson.Value(path)));
    private void OnArtistImportChanged(ArtistImportSettings settings) => Notice?.Invoke(new("event", Method: "library.artistImport", Payload: CoreJson.Value(settings)));
    private void OnRadioChanged() => Notice?.Invoke(new("event", Method: "radio.changed", Payload: CoreJson.Empty));
    private void OnRadioMetadata() => Notice?.Invoke(new("event", Method: "radio.metadata", Payload: CoreJson.Value(new CoreRadioMetadata(runtime.RadioMetadata.StreamUrl, runtime.RadioMetadata.StreamTitle))));
    private void OnFiles(IReadOnlyList<string> paths)
    {
        try { tasks.Start(new("", Paths: paths.Take(256).ToArray())); }
        catch (Exception) { Notice?.Invoke(new("event", Method: "diagnostic", Payload: CoreJson.Value(new CoreDiagnostic("import", "导入任务尚未接受，请等待当前任务完成后重试。")))); }
        Notice?.Invoke(new("event", Method: "media.files", Payload: CoreJson.Value(paths.Take(256).ToArray())));
    }
    private void OnSleep(Echo.Application.Playback.SleepTimerState _) => Changed?.Invoke();

    public async Task<JsonElement> DispatchAsync(string method, JsonElement payload, string role, CancellationToken token)
    {
        if (role == "remote-control" && await DispatchRemoteControlAsync(method, payload, token).ConfigureAwait(false) is { } remoteResult) return remoteResult;
        // The LAN bridge gets transport and bounded read capabilities, never credentials, SQL, maintenance or library mutations.
        if (role == "bridge" && !BridgeAllowed(method)) throw new UnauthorizedAccessException();
        if (method.StartsWith("playback.", StringComparison.Ordinal))
        {
            var result = await DispatchPlaybackAsync(method, payload, token).ConfigureAwait(false) ?? throw new ArgumentException("Unknown playback operation.");
            Changed?.Invoke(); return result;
        }
        if (method.StartsWith("library.", StringComparison.Ordinal))
            return await DispatchLibraryAsync(method, payload, token).ConfigureAwait(false) ?? throw new ArgumentException("Unknown library operation.");
        if (method.StartsWith("podcasts.", StringComparison.Ordinal))
            return await DispatchPodcastsAsync(method, payload, token).ConfigureAwait(false) ?? throw new ArgumentException("Unknown podcast operation.");
        switch (method)
        {
            case "core.info": return CoreJson.Value(new CoreEndpointInfo(Instance, Environment.ProcessId, runtime.Identity, CoreProtocol.Version));
            case "core.shutdown":
                _ = Task.Run(async () => { await Task.Delay(200).ConfigureAwait(false); ShutdownRequested?.Invoke(); }); return CoreJson.Empty;
            case "media.accessory":
                var accessoryRequest = CoreJson.Read<CoreAccessoryRequest>(payload);
                if (accessoryRequest.Action == "retry") runtime.Playback.RetryMediaTransport();
                else if (runtime.Playback.Accessories is { } accessory)
                {
                    switch (accessoryRequest.Action)
                    {
                        case "visible": accessory.SetVisible(accessoryRequest.Value, accessoryRequest.Enabled); break;
                        case "invoke": accessory.Invoke(accessoryRequest.Value); break;
                        case "language": accessory.SetLanguage(accessoryRequest.Enabled); break;
                        case "pauseOnDisconnect": accessory.PauseOnOutputDisconnect = accessoryRequest.Enabled; break;
                        default: throw new ArgumentException("Unknown media accessory action.");
                    }
                }
                return CoreJson.Empty;
            case "settings.features.read": return CoreJson.Value(runtime.Settings.Snapshot());
            case "settings.features.write":
                var setting = CoreJson.Read<CoreSettingsRequest>(payload);
                if (!CoreFeatureKeys.Contains(setting.Key)) throw new ArgumentException("Unsupported Core setting.");
                if (runtime.Settings.Read(setting.Key, new()) != setting.Value)
                {
                    var session = runtime.Playback.LocalZone.Session;
                    switch (setting.Key)
                    {
                        case "fixed-volume": await session.SetVolumeLockedAsync(setting.Value.Enabled, token).ConfigureAwait(false); break;
                        case "gapless-playback": await session.SetAlbumGaplessEnabledAsync(setting.Value.Enabled, token).ConfigureAwait(false); break;
                        case "shuffle-credibility": await session.SetShuffleAvoidRecentCountAsync(ShuffleAvoidance.FromSelection(setting.Value.Selection), token).ConfigureAwait(false); break;
                        case "automix": await session.SetAutomixEnabledAsync(setting.Value.Enabled, token).ConfigureAwait(false); break;
                        case "speed-mode": await session.SetPlaybackRateModeAsync(setting.Value.Selection == "Nightcore" ? "nightcore" : "speed", token).ConfigureAwait(false); break;
                        case "scan-performance": await runtime.Library.SetScanProfileAsync(setting.Value.Selection, token).ConfigureAwait(false); break;
                    }
                    runtime.Settings.Confirm(setting.Key, setting.Value); runtime.RefreshWake();
                    if (setting.Key == "network-lyrics") runtime.RefreshNetworkLyrics();
                    Changed?.Invoke();
                }
                return CoreJson.Empty;
            case "backup.validate": return CoreJson.Value(await runtime.Backup.ValidateAsync(CoreJson.Read<string>(payload), token).ConfigureAwait(false));
            case "backup.configure": await runtime.ConfigureBackupAsync(CoreJson.Read<Echo.Contracts.Backup.BackupSchedule>(payload), token).ConfigureAwait(false); return CoreJson.Empty;
            case "core.detach": return CoreJson.Empty;
            case "tasks.start": return CoreJson.Value(tasks.Start(CoreJson.Read<CoreTaskRequest>(payload)));
            case "tasks.get": return CoreJson.Value(tasks.Get(CoreJson.Read<CoreTaskCancel>(payload).Id));
            case "tasks.list": return CoreJson.Value(tasks.List());
            case "tasks.cancel": tasks.Cancel(CoreJson.Read<CoreTaskCancel>(payload).Id); return CoreJson.Empty;
            case "sleep.start":
                var sleep = CoreJson.Read<CoreSleepRequest>(payload);
                await runtime.Playback.SleepTimer.StartAsync(sleep.Minutes, sleep.Action, token).ConfigureAwait(false); return CoreJson.Empty;
            case "sleep.cancel": await runtime.Playback.SleepTimer.CancelAsync(token).ConfigureAwait(false); return CoreJson.Empty;
            case "settings.watchAll":
                var folders = await runtime.Library.GetFoldersAsync(new(Limit: 16), token).ConfigureAwait(false);
                if (folders.HasMore) throw new InvalidOperationException("最多同时监听 16 个根目录。");
                foreach (var folder in folders.Items) await runtime.Library.SetFolderWatchAsync(folder.Path, CoreJson.Read<bool>(payload), token).ConfigureAwait(false);
                return CoreJson.Value(folders.Items.Count);
            case "settings.scan": await runtime.Library.SetScanProfileAsync(CoreJson.Read<string>(payload), token).ConfigureAwait(false); return CoreJson.Empty;
            case "radio.metadata": return CoreJson.Value(new CoreRadioMetadata(runtime.RadioMetadata.StreamUrl, runtime.RadioMetadata.StreamTitle));
            case "radio.search": return CoreJson.Value(await runtime.RadioDirectory.SearchAsync(CoreJson.Read<CoreRadioSearch>(payload).Query, token).ConfigureAwait(false));
            case "radio.resolve": return CoreJson.Value(await runtime.RadioStreams.ResolveAsync(CoreJson.Read<string>(payload), token).ConfigureAwait(false));
            case "broadcast.configure": await runtime.Broadcast.SetEnabledAsync(CoreJson.Read<CoreBroadcastRequest>(payload).Enabled, token).ConfigureAwait(false); return CoreJson.Value(new CoreBroadcastState(runtime.Broadcast.IsRunning, runtime.Broadcast.Address));
            case "radio.read": return CoreJson.Value(runtime.RadioState);
            case "radio.write": await runtime.WriteRadioAsync(CoreJson.Read<CoreRadioWrite>(payload), token).ConfigureAwait(false); return CoreJson.Empty;
            case "remote.call": return await DispatchRemoteAsync(CoreJson.Read<CoreRemoteRequest>(payload), token).ConfigureAwait(false);
            case "resource.read": return await ReadResourceAsync(CoreJson.Read<CoreReadResource>(payload), token).ConfigureAwait(false);
            case "background.wait": await runtime.Playback.BackgroundWork.WaitAsync(CoreJson.Read<BackgroundWorkKind>(payload), token).ConfigureAwait(false); return CoreJson.Empty;
            case "background.snapshot": return CoreJson.Value(runtime.Playback.BackgroundWork.Snapshot);
            default: throw new ArgumentException("Unknown Core operation.");
        }
    }

    private static bool BridgeAllowed(string method) => method is
        "core.info" or "core.detach" or "playback.PlayAsync" or "playback.PauseAsync" or "playback.ResumeAsync" or "playback.StopAsync"
        or "playback.SetVolumeAsync" or "playback.NextAsync" or "playback.PreviousAsync" or "playback.SeekAsync"
        or "library.GetTracksAsync" or "library.GetAlbumsAsync" or "library.GetAlbumTracksAsync" or "library.GetArtistsAsync"
        or "library.GetArtistAlbumsAsync" or "library.GetArtistTracksAsync";

    private async Task<JsonElement> ReadResourceAsync(CoreReadResource request, CancellationToken token)
    {
        var resourceLane = request.Kind == "lyrics" ? lyricsResources : resources;
        if (!resourceLane.Wait(0)) throw new InvalidOperationException("Core 资源读取容量已满。");
        try
        {
            return request.Kind switch
            {
                "artwork" => CoreJson.Value(new CoreBytes(await runtime.Artwork.ReadAsync(request.Path, token).ConfigureAwait(false))),
                "artist-artwork" => CoreJson.Value(new CoreBytes(request.Path.StartsWith("online:", StringComparison.Ordinal)
                    ? await runtime.Library.ReadArtistImageAsync(request.Path[7..], token).ConfigureAwait(false)
                    : runtime.Artwork is IArtistArtwork artists ? await artists.ReadArtistAsync(request.Path, token).ConfigureAwait(false) : null)),
                "lyrics" => CoreJson.Value(await runtime.Lyrics.ReadAsync(request.Path, request.ExplicitPath, token).ConfigureAwait(false)),
                "attachments" => CoreJson.Value(await runtime.Details.ReadAttachmentsAsync(request.Path, token).ConfigureAwait(false)),
                "description" => CoreJson.Value(await runtime.Details.ReadDescriptionAsync(request.Path, token).ConfigureAwait(false)),
                _ => throw new ArgumentException("Unknown resource kind.")
            };
        }
        finally { resourceLane.Release(); }
    }

    private async Task<JsonElement> DispatchRemoteAsync(CoreRemoteRequest request, CancellationToken token)
    {
        var catalog = runtime.RemoteSources;
        await catalog.LoadAsync(token).ConfigureAwait(false);
        switch (request.Action)
        {
            case "connections": return CoreJson.Value(catalog.Sources.Select(info => catalog.GetConnection(info.Id)).ToArray());
            case "save": await catalog.SaveAsync(request.Draft?.Connection ?? throw new ArgumentException("Missing source draft."), token).ConfigureAwait(false); return CoreJson.Empty;
            case "remove": await catalog.RemoveAsync(request.Id, token).ConfigureAwait(false); return CoreJson.Empty;
            case "browse": return CoreJson.Value(await catalog.BrowseAsync(request.Id, request.Path, request.Page, request.Search, request.AudioOnly, token).ConfigureAwait(false));
            case "test": return CoreJson.Value(await runtime.RemoteFiles.TestConnectionAsync(request.Draft?.Connection ?? throw new ArgumentException("Missing source draft."), token).ConfigureAwait(false));
        }
        var source = catalog.GetConnection(request.Id);
        return request.Action switch
        {
            "resolve" => CoreJson.Value(await runtime.RemoteFiles.ResolveMediaAsync(source, request.Entry ?? throw new ArgumentException("Missing entry."), token).ConfigureAwait(false)),
            "file" when request.MaxBytes is > 0 and <= 4 * 1024 * 1024 => CoreJson.Value(new CoreBytes(await runtime.RemoteFiles.ReadFileAsync(source, request.Path, request.MaxBytes, token).ConfigureAwait(false))),
            "artwork" => CoreJson.Value(new CoreBytes(await runtime.RemoteFiles.ReadArtworkAsync(source, request.Path, token).ConfigureAwait(false))),
            "lyrics" => CoreJson.Value(await runtime.RemoteFiles.ReadServerLyricsAsync(source, request.Path, token).ConfigureAwait(false)),
            _ => throw new ArgumentException("Unknown source operation.")
        };
    }

    public async ValueTask DisposeAsync()
    {
        runtime.ListeningChanged -= OnListeningChanged; runtime.ListeningDiagnostic -= OnListeningDiagnostic;
        runtime.Library.ArtistImportChanged -= OnArtistImportChanged;
        runtime.Library.FolderChanged -= OnFolderChanged; runtime.Playback.SleepTimer.Changed -= OnSleep; runtime.Playback.FilesOpened -= OnFiles; runtime.RadioMetadata.Changed -= OnRadioMetadata; runtime.RadioChanged -= OnRadioChanged;
        await stop.CancelAsync().ConfigureAwait(false); subscription.Dispose();
        await updates.ConfigureAwait(false); await tasks.DisposeAsync().ConfigureAwait(false);
        resources.Dispose(); lyricsResources.Dispose(); stop.Dispose();
    }
}
