using Echo.Bridge;
namespace Echo.Core.Client;

public sealed partial class BridgeClientRuntime
{
    private sealed class CastClient(BridgeClientRuntime owner) : IBridgeCast
    {
        public bool Active => owner.state.Cast.Active; public bool Paused => owner.state.Cast.Paused;
        public string DeviceName => owner.state.Cast.DeviceName; public string TargetKey => owner.state.Cast.TargetKey;
        public event Action? Changed { add => owner.Changed += value; remove => owner.Changed -= value; }
        public Task CastAsync(DlnaRenderer target, CancellationToken token) => owner.ExecuteAsync(new("cast.play", target), token);
        public Task ToggleAsync(CancellationToken token = default) => owner.ExecuteAsync(new("cast.toggle"), token);
        public Task StopAsync(CancellationToken token = default) => owner.ExecuteAsync(new("cast.stop"), token);
        public Task SetVolumeAsync(int volume, CancellationToken token = default) => owner.ExecuteAsync(new("cast.volume", Port: volume), token);
    }
    private sealed class ReceiverClient(BridgeClientRuntime owner) : IBridgeReceiver
    {
        public bool Running => owner.state.Receiver.Running; public int Port => owner.state.Receiver.Port;
        public string Addresses => owner.state.Receiver.Addresses; public string Source => owner.state.Receiver.Source;
        public string Error => owner.state.Receiver.Error; public bool Advertising => owner.state.Receiver.Advertising; public string Name => owner.state.Receiver.Name;
        public event Action? Changed { add => owner.Changed += value; remove => owner.Changed -= value; }
        public Task StartAsync(IBridgePreferences preferences) => owner.ExecuteAsync(new("receiver.start"));
        public Task StopAsync() => owner.ExecuteAsync(new("receiver.stop"));
        public Task StopPlaybackAsync(CancellationToken token = default) => owner.ExecuteAsync(new("receiver.stopPlayback"), token);
    }
    private sealed class LinkClient(BridgeClientRuntime owner) : IBridgeLink
    {
        public bool Running => owner.state.Link.Running; public int Port => owner.state.Link.Port; public string Code => owner.state.Link.Code;
        public string Token => owner.state.Link.Token; public string Addresses => owner.state.Link.Addresses; public string Error => owner.state.Link.Error;
        public string PageUrl => owner.state.Link.PageUrl;
        public event Action? Changed { add => owner.Changed += value; remove => owner.Changed -= value; }
        public Task StartAsync() => owner.ExecuteAsync(new("link.start"));
        public Task StopAsync() => owner.ExecuteAsync(new("link.stop"));
        public Task RotateAsync() => owner.ExecuteAsync(new("link.rotate"));
    }
    private sealed class MqttClient(BridgeClientRuntime owner) : IBridgeMqtt
    {
        public bool Connected => owner.state.Mqtt.Connected; public string Error => owner.state.Mqtt.Error; public string LastCommand => owner.state.Mqtt.LastCommand;
        public event Action? Changed { add => owner.Changed += value; remove => owner.Changed -= value; }
        public Task ConnectAsync(string host, int port, string topicPrefix, string username, string password, CancellationToken token)
            => owner.ExecuteAsync(new("mqtt.connect", Host: host, Port: port, Topic: topicPrefix, Username: username, Password: password), token);
        public Task DisconnectAsync() => owner.ExecuteAsync(new("mqtt.disconnect"));
    }
    private sealed class HqClient(BridgeClientRuntime owner) : IBridgeHqPlayer
    {
        public string Target => owner.state.HqPlayer.Target;
        public Task ProbeAsync(string host, int port, CancellationToken token) => owner.ExecuteAsync(new("hq.probe", Host: host, Port: port), token);
        public Task HandOffAsync(string host, int port, CancellationToken token) => owner.ExecuteAsync(new("hq.play", Host: host, Port: port), token);
        public Task PauseAsync(string host, int port, CancellationToken token) => owner.ExecuteAsync(new("hq.pause", Host: host, Port: port), token);
        public Task StopAsync(string host, int port, CancellationToken token) => owner.ExecuteAsync(new("hq.stop", Host: host, Port: port), token);
    }
}
