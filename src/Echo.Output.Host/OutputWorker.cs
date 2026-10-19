using System.Diagnostics;
using System.Text.Json;
using Echo.Core.Contracts;
using Echo.Core.Transport;
using Echo.Playback.NativeAudio;

namespace Echo.Output.Host;

internal sealed partial class OutputWorker(string nativePath, RemoteCoreProfile mediaProfile, string identity) : ICoreRequestHandler, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly SemaphoreSlim nativeCalls = new(1, 1);
    private readonly SemaphoreSlim telemetrySignal = new(0, 1);
    private Guid owner, generation;
    private NativeAudioHost? native;
    private LoopbackMediaProxy? media;
    private long contact;
    private bool started, releasing;
    public Guid Instance { get; } = Guid.NewGuid();
    public CorePlaybackFrame? Capture() => null;
    public event Action? Changed { add { } remove { } }
    public event Action<CoreMessage>? Notice;
    public ValueTask ConnectedAsync(CorePeer peer, CancellationToken token)
    {
        lock (gate)
        {
            if (owner != Guid.Empty) throw new InvalidOperationException("Output 已由一个 Core 持有。");
            owner = peer.ConnectionId; contact = Stopwatch.GetTimestamp();
            media = new(mediaProfile); native = new(nativePath, allowProDsp: false);
            native.Notification += NativeEvent; native.Failed += NativeFailed;
        }
        return ValueTask.CompletedTask;
    }
    public async ValueTask DisconnectedAsync(CorePeer peer)
    {
        NativeAudioHost? host; LoopbackMediaProxy? proxy;
        lock (gate)
        {
            if (owner != peer.ConnectionId) return;
            releasing = true; host = native; proxy = media;
        }
        try
        {
            if (host is not null) { host.Notification -= NativeEvent; host.Failed -= NativeFailed; await host.DisposeAsync().ConfigureAwait(false); }
            if (proxy is not null) await proxy.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            lock (gate) { owner = Guid.Empty; generation = Guid.Empty; started = false; native = null; media = null; releasing = false; telemetry.Clear(); }
        }
    }
    public Task<JsonElement> DispatchAsync(string method, JsonElement payload, string role, CancellationToken token)
        => Task.FromException<JsonElement>(new UnauthorizedAccessException());
    public async Task<JsonElement> DispatchForPeerAsync(string method, JsonElement payload, CorePeer peer, CancellationToken token)
    {
        lock (gate)
        {
            if (owner != peer.ConnectionId || releasing || peer.Role != "output-controller") throw new UnauthorizedAccessException();
            contact = Stopwatch.GetTimestamp();
        }
        if (method == "core.info") return JsonSerializer.SerializeToElement(new CoreEndpointInfo(Instance, Environment.ProcessId, identity, OutputProtocol.Version), CoreWireJson.Default.CoreEndpointInfo);
        await nativeCalls.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var host = native ?? throw new IOException("Output 未持有原生宿主。");
            var proxy = media ?? throw new IOException("Output 媒体代理不可用。");
            switch (method)
            {
                case "output.start":
                    var request = OutputJson.Read<OutputStart>(payload);
                    if (request.Version != OutputProtocol.Version || request.Channels is < 2 or > 8) throw new ArgumentException("Output 协议或声道不匹配。");
                    await host.SetOutputChannelsAsync(request.Channels, token).ConfigureAwait(false);
                    await host.StartAsync(token).ConfigureAwait(false);
                    lock (gate)
                    {
                        if (!started) { generation = Guid.NewGuid(); proxy.Clear(); telemetry.Clear(); }
                        started = true;
                        return OutputJson.Value(new OutputReady(OutputProtocol.Version, generation, mediaProfile.Identity, mediaProfile.ClientId, proxy.BaseAddress, host.OutputChannels, host.Capabilities));
                    }
                case "output.publish": proxy.Publish(OutputJson.Read<OutputMediaTicket[]>(payload)); return CoreJson.Empty;
                case "output.command":
                    var command = OutputJson.Read<OutputCommand>(payload);
                    lock (gate) if (!started || command.Generation != generation) throw new InvalidOperationException("Output 会话已变更，旧命令未执行。");
                    OutputCommandPolicy.Validate(command.Method, command.Parameters, proxy);
                    return await host.CallAsync(command.Method, command.Parameters, token).ConfigureAwait(false);
                case "output.channels":
                    var channels = OutputJson.Read<OutputChannels>(payload);
                    await host.SetOutputChannelsAsync(channels.Channels, token, channels.ForceRestart).ConfigureAwait(false);
                    lock (gate) { started = false; generation = Guid.Empty; proxy.Clear(); telemetry.Clear(); }
                    return OutputJson.Value(new OutputChannels(host.OutputChannels, channels.ForceRestart));
                case "output.devices":
                    var devices = await host.EnumerateOutputDevicesAsync(OutputJson.Read<OutputDevices>(payload).Mode, token).ConfigureAwait(false);
                    return OutputJson.Value(devices.Select(device => device with { NativeDsdSampleRates = [] }).ToArray());
                default: throw new UnauthorizedAccessException();
            }
        }
        finally { nativeCalls.Release(); }
    }
    public async Task WatchAsync(CoreServer server, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var signaled = await telemetrySignal.WaitAsync(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                if (signaled) await Task.Delay(100, token).ConfigureAwait(false);
                Guid expired; OutputEvent[] latest;
                lock (gate)
                {
                    expired = owner != Guid.Empty && Stopwatch.GetElapsedTime(contact) >= TimeSpan.FromSeconds(15) ? owner : Guid.Empty;
                    latest = telemetry.Values.ToArray(); telemetry.Clear();
                }
                if (expired != Guid.Empty) server.Disconnect(expired);
                foreach (var value in latest) Notice?.Invoke(new("event", Method: "output.native", Payload: OutputJson.Value(value)));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync()
    {
        Guid current; lock (gate) current = owner;
        if (current != Guid.Empty) await DisconnectedAsync(new(current, Guid.Empty, "output-controller")).ConfigureAwait(false);
        nativeCalls.Dispose(); telemetrySignal.Dispose(); Notice = null;
    }
}
