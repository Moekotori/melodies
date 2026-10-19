using Echo.Application.Playback;
using Echo.Application.Remote;
using Echo.Application.Radio;
using Echo.Contracts.Library;
using Echo.Contracts.Lyrics;
using Echo.Contracts.Podcast;
using Echo.Contracts.Remote;
using Echo.Core.Contracts;
using Echo.Core.Transport;

namespace Echo.Core.Client;

/// <summary>A Control attachment. Disposing it does not send shutdown, stop audio, or cancel accepted tasks.</summary>
public sealed class CoreConnection : ICoreServices
{
    private readonly CoreWireClient wire;
    private readonly CoreEndpoint? endpoint;
    public bool IsRemote => endpoint is null;
    private readonly string role;
    private readonly CorePlaybackRuntime playback;
    private readonly CoreRadioClient radioClient;
    public Echo.Bridge.IBridgeRuntime Bridge { get; }
    public ICoreSettings Settings { get; }
    public Echo.Contracts.Backup.IDataBackup Backup { get; }
    public IPlaybackRuntime Playback => playback;
    public CoreLibraryClient Library { get; }
    ICoreLibrary ICoreServices.Library => Library;
    public CoreRemoteClient RemoteSources { get; }
    IRemoteSourceCatalog ICoreServices.RemoteSources => RemoteSources;
    public IRemoteFileClient RemoteFiles => RemoteSources;
    public IPodcastLibrary? Podcasts { get; }
    public Echo.Contracts.Radio.IRadioCatalog Radio { get; }
    public Echo.Contracts.Radio.IRadioMetadataMonitor RadioMetadata => radioClient;
    public Echo.Contracts.Radio.IRadioDirectory RadioDirectory => radioClient;
    public Echo.Contracts.Radio.IRadioStreamResolver RadioStreams => radioClient;
    public Echo.Contracts.Settings.ILocalBroadcast Broadcast { get; }
    public ILyricsSource Lyrics { get; }
    public IAlbumArtwork Artwork { get; }
    public IDetailResources Details { get; }
    public CoreWelcome Welcome => wire.Welcome;
    public event Action<CoreDiagnostic>? Diagnostic;
    public event Action? ListeningChanged;
    public event Action<string>? ListeningDiagnostic;

    private CoreConnection(CoreWireClient wire, CoreEndpoint? endpoint, string role)
    {
        this.wire = wire; this.endpoint = endpoint; this.role = role; Settings = new CoreSettingsClient(wire); Backup = new CoreBackupClient(wire, endpoint); Bridge = new BridgeClientRuntime(endpoint); playback = new(wire, () => ReconnectAsync(), endpoint is null); Library = new(wire); RemoteSources = new(wire); Podcasts = new CorePodcastClient(wire);
        Radio = new RadioCatalog(new CoreRadioStore(wire)); radioClient = new(wire); Broadcast = new CoreBroadcastClient(wire);
        var resources = new CoreResourceClient(wire, endpoint is not null); Lyrics = resources; Artwork = resources; Details = resources;
        wire.Received += Receive;
    }

    public static async Task<CoreConnection> ConnectAsync(CoreEndpoint endpoint, string role = "control", CancellationToken token = default)
    {
        var wire = await CoreWireClient.ConnectAsync(endpoint, role, token).ConfigureAwait(false);
        var connection = new CoreConnection(wire, endpoint, role); wire.Start();
        return connection;
    }
    public static async Task<CoreConnection> ConnectRemoteAsync(RemoteCoreProfile profile, CancellationToken token = default)
    {
        var wire = await CoreWireClient.ConnectRemoteAsync(profile, token).ConfigureAwait(false);
        var connection = new CoreConnection(wire, null, "remote-control"); wire.Start(); return connection;
    }
    private void Receive(CoreMessage message)
    {
        if (message.Method == "diagnostic") Diagnostic?.Invoke(CoreJson.Read<CoreDiagnostic>(message.Payload));
        if (role == "control" && message.Method is "radio.changed" or "core.reconnected") _ = RefreshRadioAsync();
        if (message.Method == "library.listening") ListeningChanged?.Invoke();
        if (message.Method == "diagnostic" && CoreJson.Read<CoreDiagnostic>(message.Payload) is { Kind: "listening", Message: { } messageText })
            ListeningDiagnostic?.Invoke(messageText);
    }
    private async Task RefreshRadioAsync()
    { try { await Radio.RefreshAsync(CancellationToken.None).ConfigureAwait(false); } catch (Exception) { } }
    public async Task ReconnectAsync(CancellationToken token = default)
    {
        if (endpoint is not null)
        {
            await using var probe = await CoreLauncher.ConnectAsync(endpoint, role: role, token: token).ConfigureAwait(false);
        }
        await wire.ReconnectAsync(token).ConfigureAwait(false);
        if (role == "control") await StartAsync(token).ConfigureAwait(false);
    }
    public Task<CoreEndpointInfo> InfoAsync(CancellationToken token = default) => wire.CallAsync<CoreEndpointInfo>("core.info", CoreJson.Empty, token);
    public Task ShutdownAsync(CancellationToken token = default) => wire.CallAsync("core.shutdown", CoreJson.Empty, token);
    public async Task StartAsync(CancellationToken token = default)
    {
        if (IsRemote) return;
        try { await RemoteSources.LoadAsync(token).ConfigureAwait(false); }
        catch (CoreRemoteException error) when (error.Code == "failed") { /* Source editing exposes its own failure; local playback remains usable. */ }
        await radioClient.InitializeAsync(token).ConfigureAwait(false);
    }
    public async ValueTask DisposeAsync()
    {
        Bridge.Dispose(); radioClient.Detach();
        wire.Received -= Receive;
        await playback.DisposeAsync().ConfigureAwait(false); await wire.DisposeAsync().ConfigureAwait(false);
        ListeningChanged = null; ListeningDiagnostic = null; Diagnostic = null;
    }
}
