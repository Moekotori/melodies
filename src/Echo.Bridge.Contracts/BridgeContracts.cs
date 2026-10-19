using Echo.Contracts.Settings;
namespace Echo.Bridge;

public sealed record BridgeCastState(bool Active = false, bool Paused = false, string DeviceName = "", string TargetKey = "");
public sealed record BridgeReceiverState(bool Running = false, int Port = 0, string Addresses = "", string Source = "未连接", string Error = "", bool Advertising = false, string Name = "");
public sealed record BridgeLinkState(bool Running = false, int Port = 0, string Code = "", string Token = "", string Addresses = "", string Error = "", string PageUrl = "");
public sealed record BridgeMqttState(bool Connected = false, string Error = "", string LastCommand = "");
public sealed record BridgeHqPlayerState(string Target = "");
public sealed record BridgeState(BridgeCastState Cast, BridgeReceiverState Receiver, BridgeLinkState Link,
    BridgeMqttState Mqtt, BridgeHqPlayerState HqPlayer, string TrackCaption);
public sealed record BridgeCommand(string Action, DlnaRenderer? Renderer = null, string Host = "", int Port = 0,
    string Topic = "", string Username = "", string Password = "", string Value = "", bool Enabled = false,
    Dictionary<string, FeatureSetting>? Settings = null);

public interface IBridgeService : IDisposable
{
    BridgeState State { get; }
    bool Active { get; }
    event Action? Changed;
    Task ExecuteAsync(BridgeCommand command, CancellationToken token);
    Task<IReadOnlyList<DlnaRenderer>> DiscoverAsync(CancellationToken token);
    Task<IReadOnlyList<string>> AirPlayAsync(CancellationToken token);
}

public interface IBridgeRuntime : IDisposable
{
    IBridgeCast Cast { get; }
    IBridgeReceiver Receiver { get; }
    IBridgeLink Link { get; }
    IBridgeMqtt Mqtt { get; }
    IBridgeHqPlayer HqPlayer { get; }
    event Action? Changed;
    string TrackCaption();
    void Use(IBridgePreferences store);
    void Restore();
    Task RefreshAsync(CancellationToken token = default);
    Task<IReadOnlyList<DlnaRenderer>> DiscoverAsync(CancellationToken token);
    Task<IReadOnlyList<string>> AirPlayAsync(CancellationToken token);
}
public interface IBridgeCast
{
    bool Active { get; } bool Paused { get; } string DeviceName { get; } string TargetKey { get; }
    event Action? Changed;
    Task CastAsync(DlnaRenderer target, CancellationToken token);
    Task ToggleAsync(CancellationToken token = default);
    Task StopAsync(CancellationToken token = default);
    Task SetVolumeAsync(int volume, CancellationToken token = default);
}
public interface IBridgeReceiver
{
    bool Running { get; } int Port { get; } string Addresses { get; } string Source { get; }
    string Error { get; } bool Advertising { get; } string Name { get; }
    event Action? Changed;
    Task StartAsync(IBridgePreferences preferences);
    Task StopAsync();
    Task StopPlaybackAsync(CancellationToken token = default);
}
public interface IBridgeLink
{
    bool Running { get; } int Port { get; } string Code { get; } string Token { get; }
    string Addresses { get; } string Error { get; } string PageUrl { get; }
    event Action? Changed;
    Task StartAsync(); Task StopAsync(); Task RotateAsync();
}
public interface IBridgeMqtt
{
    bool Connected { get; } string Error { get; } string LastCommand { get; }
    event Action? Changed;
    Task ConnectAsync(string host, int port, string topicPrefix, string username, string password, CancellationToken token);
    Task DisconnectAsync();
}
public interface IBridgeHqPlayer
{
    string Target { get; }
    Task ProbeAsync(string host, int port, CancellationToken token);
    Task HandOffAsync(string host, int port, CancellationToken token);
    Task PauseAsync(string host, int port, CancellationToken token);
    Task StopAsync(string host, int port, CancellationToken token);
}
