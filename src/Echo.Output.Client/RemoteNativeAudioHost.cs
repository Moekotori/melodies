using System.Text.Json;
using Echo.Core.Contracts;
using Echo.Core.Transport;
using Echo.Playback;

namespace Echo.Output.Client;

/// <summary>Core-side native audio port. The peer owns the native process; this object never handles PCM.</summary>
public sealed class RemoteNativeAudioHost : INativeAudio, INativeOutputControl
{
    private readonly RemoteCoreProfile profile;
    private readonly OutputMediaServer media;
    private readonly OutputSourceProjection projection;
    private readonly SemaphoreSlim starting = new(1, 1), commands = new(1, 1);
    private CoreWireClient? wire;
    private OutputReady? ready;
    private int closed;
    public int OutputChannels { get; private set; } = 2;
    public bool ProDspAllowed => false; // V1 advertises PCM playback, not DSD or file-based DSP parity.
    public JsonElement? Capabilities => Volatile.Read(ref ready)?.NativeCapabilities;
    public event Action<string, JsonElement>? Notification;
    public event Action<Exception>? Failed;
    public RemoteNativeAudioHost(RemoteCoreProfile profile, OutputMediaServer media)
    {
        if (profile.Role != "output-controller") throw new ArgumentException("需要 Output 控制配对文件。");
        this.profile = profile; this.media = media; projection = new(media.Tickets);
    }
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(closed != 0, this);
        await starting.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (wire?.IsConnected == true && ready is not null) return;
            await media.StartAsync(cancellationToken).ConfigureAwait(false);
            if (wire?.IsConnected != true)
            {
                if (wire is not null) await wire.DisposeAsync().ConfigureAwait(false);
                wire = await CoreWireClient.ConnectRemoteAsync(profile, cancellationToken).ConfigureAwait(false);
                wire.Received += Receive; wire.Disconnected += Disconnect; wire.Start();
            }
            var value = OutputJson.Read<OutputReady>(await wire.CallAsync("output.start", OutputJson.Value(new OutputStart(OutputProtocol.Version, OutputChannels)), cancellationToken).ConfigureAwait(false));
            if (value.Version != OutputProtocol.Version || value.Generation == Guid.Empty || value.MediaIdentity != media.Identity || value.MediaClientId != media.ClientId
                || !Uri.TryCreate(value.MediaBaseAddress, UriKind.Absolute, out var url) || url.Scheme != "http" || url.Host != "127.0.0.1" || !url.AbsolutePath.EndsWith("/media/", StringComparison.Ordinal))
                throw new IOException("Output 协议、媒体配对或回环入口不匹配。");
            if (Volatile.Read(ref ready)?.Generation != value.Generation) media.Tickets.Clear();
            projection.BaseAddress = value.MediaBaseAddress; Volatile.Write(ref ready, value);
        }
        finally { starting.Release(); }
    }
    public async Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
    {
        if (wire?.IsConnected != true || ready is null) throw new IOException("Output 未连接，请显式恢复播放。");
        await commands.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = Volatile.Read(ref ready) ?? throw new IOException("Output 会话已失效。");
            var command = projection.Rewrite(method, parameters ?? CoreJson.Empty);
            var tickets = projection.TakeTickets();
            if (tickets.Length > 0) await wire.CallAsync("output.publish", OutputJson.Value(tickets), cancellationToken).ConfigureAwait(false);
            var result = await wire.CallAsync("output.command", OutputJson.Value(new OutputCommand(current.Generation, command.Method, command.Parameters)), cancellationToken).ConfigureAwait(false);
            return projection.RestorePaths(result);
        }
        finally { commands.Release(); }
    }
    private void Receive(CoreMessage message)
    {
        if (message.Method is not ("output.native" or "output.failed")) return;
        var value = OutputJson.Read<OutputEvent>(message.Payload);
        if (Volatile.Read(ref ready) is not { } current || value.Generation != current.Generation) return;
        if (message.Method == "output.failed") { Disconnect(new IOException("Output 原生宿主已断开。")); return; }
        Notification?.Invoke(value.Method, projection.RestorePaths(value.Parameters));
    }
    private void Disconnect(Exception error)
    {
        var previous = Interlocked.Exchange(ref ready, null); media.Tickets.Clear();
        if (closed == 0 && previous is not null) Failed?.Invoke(error);
    }
    public async Task SetOutputChannelsAsync(int channels, CancellationToken token = default, bool forceRestart = false)
    {
        if (channels is < 2 or > 8) throw new ArgumentOutOfRangeException(nameof(channels));
        if (channels == OutputChannels && !forceRestart) return;
        if (wire?.IsConnected == true)
        {
            var accepted = OutputJson.Read<OutputChannels>(await wire.CallAsync("output.channels", OutputJson.Value(new OutputChannels(channels, forceRestart)), token).ConfigureAwait(false));
            if (accepted.Channels != channels) throw new IOException("Output 未接受声道初始化。");
        }
        OutputChannels = channels; Volatile.Write(ref ready, null); media.Tickets.Clear();
    }
    public async Task<IReadOnlyList<AudioOutputDevice>> EnumerateOutputDevicesAsync(string mode, CancellationToken token = default)
    {
        await StartAsync(token).ConfigureAwait(false);
        return OutputJson.Read<AudioOutputDevice[]>(await wire!.CallAsync("output.devices", OutputJson.Value(new OutputDevices(mode)), token).ConfigureAwait(false));
    }
    public Task<JsonElement> ProbeAsioDsdAsync(AudioOutputDevice device, CancellationToken token = default)
        => Task.FromException<JsonElement>(new NotSupportedException("网络 Output V1 尚不提供 DSD 探测。"));
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref closed, 1) != 0) return;
        media.Tickets.Clear();
        if (wire is not null) { wire.Received -= Receive; wire.Disconnected -= Disconnect; await wire.DisposeAsync().ConfigureAwait(false); }
        await media.DisposeAsync().ConfigureAwait(false); Notification = null; Failed = null;
    }
}
