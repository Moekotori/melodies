using System.Diagnostics;
using Echo.Bridge;
using Echo.Contracts.Settings;
using Echo.Core.Contracts;
using Echo.Core.Transport;

namespace Echo.Core.Client;

public sealed partial class BridgeClientRuntime : IBridgeRuntime
{
    private readonly CoreEndpoint? endpoint;
    private readonly SemaphoreSlim connecting = new(1, 1);
    private readonly CancellationTokenSource stop = new();
    private CoreWireClient? wire;
    private IBridgePreferences? preferences;
    private BridgeState state = new(new(), new(), new(), new(), new(), "当前没有可送出的本地歌曲或网络音频。");
    public event Action? Changed;
    public IBridgeCast Cast { get; }
    public IBridgeReceiver Receiver { get; }
    public IBridgeLink Link { get; }
    public IBridgeMqtt Mqtt { get; }
    public IBridgeHqPlayer HqPlayer { get; }
    public string TrackCaption() => Volatile.Read(ref state).TrackCaption;
    public BridgeClientRuntime(CoreEndpoint? endpoint)
    { this.endpoint = endpoint is null ? null : endpoint with { Service = "bridge" }; Cast = new CastClient(this); Receiver = new ReceiverClient(this); Link = new LinkClient(this); Mqtt = new MqttClient(this); HqPlayer = new HqClient(this); }
    public void Use(IBridgePreferences store)
    {
        if (preferences is not null) preferences.Changed -= PreferencesChanged;
        preferences = store; preferences.Changed += PreferencesChanged;
    }
    private void PreferencesChanged(string id)
    {
        if (id.Length == 0 || id.StartsWith("connect.", StringComparison.Ordinal)) Restore();
    }
    public void Restore() => _ = RestoreAsync();
    private async Task RestoreAsync()
    {
        if (endpoint is null) return;
        try
        {
            var settings = preferences?.Values.Where(pair => pair.Key.StartsWith("connect.", StringComparison.Ordinal)).Take(64).ToDictionary(pair => pair.Key, pair => pair.Value);
            // No default Bridge process: launch only for enabled saved services or an explicit Bridge action.
            if (wire is null && settings?.Values.Any(value => value.Enabled) != true) return;
            if (settings is { Count: > 0 }) await ExecuteAsync(new("settings", Settings: settings), stop.Token).ConfigureAwait(false);
            await ExecuteAsync(new("restore"), stop.Token).ConfigureAwait(false);
        }
        catch (Exception) { /* Actual connect actions report failures to the control. */ }
    }
    private async Task<CoreWireClient> WireAsync(CancellationToken token)
    {
        if (endpoint is null) throw new NotSupportedException("远程控制端暂不提供连接服务管理。");
        await connecting.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (wire is not null) return wire;
            try
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token); attempt.CancelAfter(250);
                wire = await CoreWireClient.ConnectAsync(endpoint, "control", attempt.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException)
            {
                var host = ServiceExecutable.Resolve(Path.Combine(AppContext.BaseDirectory, "bridge"), "Echo.Bridge.Host", "ECHO_BRIDGE_HOST");
                if (!File.Exists(host)) throw new FileNotFoundException("找不到 Bridge 程序，请重新构建桌面应用。");
                var start = new ProcessStartInfo(Path.GetExtension(host) == ".dll" ? Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet" : host)
                { UseShellExecute = false, CreateNoWindow = true };
                if (Path.GetExtension(host) == ".dll") start.ArgumentList.Add(Path.GetFullPath(host));
                foreach (var item in new[] { "--data", endpoint.Directory, "--db", endpoint.LibraryPath }) start.ArgumentList.Add(item);
                using var process = Process.Start(start) ?? throw new IOException("Bridge 启动失败。");
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(10000);
                while (wire is null)
                {
                    try
                    {
                        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token); attempt.CancelAfter(500);
                        wire = await CoreWireClient.ConnectAsync(endpoint, "control", attempt.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!deadline.IsCancellationRequested) { }
                    if (wire is null) await Task.Delay(100, deadline.Token).ConfigureAwait(false);
                }
            }
            wire.Received += Receive; wire.Start(); return wire;
        }
        finally { connecting.Release(); }
    }
    private void Receive(CoreMessage message)
    { if (message.Method == "bridge.state") { Volatile.Write(ref state, CoreJson.Read<BridgeState>(message.Payload)); Changed?.Invoke(); } }
    internal async Task ExecuteAsync(BridgeCommand command, CancellationToken token = default)
    {
        var connection = await WireAsync(token).ConfigureAwait(false);
        var next = await connection.CallAsync<BridgeState>("bridge.execute", CoreJson.Value(command), token).ConfigureAwait(false);
        Volatile.Write(ref state, next); Changed?.Invoke();
    }
    public async Task RefreshAsync(CancellationToken token = default)
    {
        var connection = await WireAsync(token).ConfigureAwait(false);
        Volatile.Write(ref state, await connection.CallAsync<BridgeState>("bridge.read", CoreJson.Empty, token).ConfigureAwait(false)); Changed?.Invoke();
    }
    public async Task<IReadOnlyList<DlnaRenderer>> DiscoverAsync(CancellationToken token)
        => await (await WireAsync(token).ConfigureAwait(false)).CallAsync<DlnaRenderer[]>("bridge.discover", CoreJson.Empty, token).ConfigureAwait(false);
    public async Task<IReadOnlyList<string>> AirPlayAsync(CancellationToken token)
        => await (await WireAsync(token).ConfigureAwait(false)).CallAsync<string[]>("bridge.airplay", CoreJson.Empty, token).ConfigureAwait(false);
    public void Dispose()
    {
        if (preferences is not null) preferences.Changed -= PreferencesChanged;
        stop.Cancel();
        if (wire is { } connection) { connection.Received -= Receive; _ = connection.DisposeAsync(); }
    }
}
