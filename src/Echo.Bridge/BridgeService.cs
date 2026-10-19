using Echo.Playback;

namespace Echo.Bridge;

/// <summary>Protocol adapters own their listeners and transfers. Core remains the only playback/queue owner.</summary>
public sealed class BridgeService : IBridgeService
{
    private readonly ConnectRuntime runtime;
    private readonly IBridgePreferences preferences;
    public event Action? Changed;
    public bool Active => runtime.Receiver.Running || runtime.Link.Running || runtime.Mqtt.Connected || runtime.Cast.Active || runtime.HqPlayer.Target.Length > 0;
    public BridgeState State => new(new(runtime.Cast.Active, runtime.Cast.Paused, runtime.Cast.DeviceName, runtime.Cast.TargetKey),
        new(runtime.Receiver.Running, runtime.Receiver.Port, runtime.Receiver.Addresses, runtime.Receiver.Source, runtime.Receiver.Error, runtime.Receiver.Advertising, runtime.Receiver.Name),
        new(runtime.Link.Running, runtime.Link.Port, runtime.Link.Code, runtime.Link.Token, runtime.Link.Addresses, runtime.Link.Error, runtime.Link.PageUrl),
        new(runtime.Mqtt.Connected, runtime.Mqtt.Error, runtime.Mqtt.LastCommand), new(runtime.HqPlayer.Target), runtime.TrackCaption());
    public BridgeService(IPlaybackSession session, IBridgePreferences preferences)
    {
        this.preferences = preferences;
        runtime = new(() => session); runtime.Use(preferences); runtime.Changed += OnChanged;
    }
    private void OnChanged() => Changed?.Invoke();
    public Task<IReadOnlyList<DlnaRenderer>> DiscoverAsync(CancellationToken token) => DlnaDiscovery.SearchAsync(token);
    public Task<IReadOnlyList<string>> AirPlayAsync(CancellationToken token) => MdnsBrowse.AirPlayNamesAsync(token);
    public async Task ExecuteAsync(BridgeCommand command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        switch (command.Action)
        {
            case "restore": runtime.Restore(); break;
            case "settings":
                if (command.Settings is null || command.Settings.Count > 64) throw new ArgumentException("Invalid bridge settings.");
                foreach (var item in command.Settings) preferences.Confirm(item.Key, item.Value);
                break;
            case "cast.play": await runtime.Cast.CastAsync(command.Renderer ?? throw new ArgumentException("Missing renderer."), token).ConfigureAwait(false); break;
            case "cast.toggle": await runtime.Cast.ToggleAsync(token).ConfigureAwait(false); break;
            case "cast.stop": await runtime.Cast.StopAsync(token).ConfigureAwait(false); break;
            case "cast.volume": await runtime.Cast.SetVolumeAsync(command.Port, token).ConfigureAwait(false); break;
            case "receiver.start": await runtime.Receiver.StartAsync(preferences).ConfigureAwait(false); break;
            case "receiver.stop": await runtime.Receiver.StopAsync().ConfigureAwait(false); break;
            case "receiver.stopPlayback": await runtime.Receiver.StopPlaybackAsync(token).ConfigureAwait(false); break;
            case "link.start": await runtime.Link.StartAsync().ConfigureAwait(false); break;
            case "link.stop": await runtime.Link.StopAsync().ConfigureAwait(false); break;
            case "link.rotate": runtime.Link.Rotate(); break;
            case "mqtt.connect": await runtime.Mqtt.ConnectAsync(command.Host, command.Port, command.Topic, command.Username, command.Password, token).ConfigureAwait(false); break;
            case "mqtt.disconnect": await runtime.Mqtt.DisconnectAsync().ConfigureAwait(false); break;
            case "hq.probe": await runtime.HqPlayer.ProbeAsync(command.Host, command.Port, token).ConfigureAwait(false); break;
            case "hq.play": await runtime.HqPlayer.HandOffAsync(command.Host, command.Port, token).ConfigureAwait(false); break;
            case "hq.pause": await runtime.HqPlayer.PauseAsync(command.Host, command.Port, token).ConfigureAwait(false); break;
            case "hq.stop": await runtime.HqPlayer.StopAsync(command.Host, command.Port, token).ConfigureAwait(false); break;
            default: throw new ArgumentException("Unknown bridge operation.");
        }
        Changed?.Invoke();
    }
    public void Dispose() { runtime.Changed -= OnChanged; runtime.Dispose(); }
}
