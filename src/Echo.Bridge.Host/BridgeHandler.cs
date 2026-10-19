using System.Text.Json;
using Echo.Bridge;
using Echo.Core.Contracts;
using Echo.Core.Transport;
using Echo.Playback;

namespace Echo.Bridge.Host;

internal sealed class BridgeHandler : ICoreRequestHandler, IDisposable
{
    private readonly BridgeService service;
    private readonly IPlaybackSession playback;
    private string? currentItem;
    public Guid Instance { get; } = Guid.NewGuid();
    public event Action? Changed { add { } remove { } }
    public event Action<CoreMessage>? Notice;
    public BridgeHandler(BridgeService service, IPlaybackSession playback)
    { this.service = service; this.playback = playback; service.Changed += Publish; playback.Changed += TrackChanged; }
    public CorePlaybackFrame? Capture() => null;
    private void Publish() => Notice?.Invoke(new("event", Method: "bridge.state", Payload: CoreJson.Value(service.State)));
    private void TrackChanged(PlaybackSnapshot snapshot)
    { if (currentItem != snapshot.Current?.ItemId) { currentItem = snapshot.Current?.ItemId; Publish(); } }
    public async Task<JsonElement> DispatchAsync(string method, JsonElement payload, string role, CancellationToken token)
    {
        if (role != "control") throw new UnauthorizedAccessException();
        switch (method)
        {
            case "bridge.read": return CoreJson.Value(service.State);
            case "bridge.execute": await service.ExecuteAsync(CoreJson.Read<BridgeCommand>(payload), token).ConfigureAwait(false); return CoreJson.Value(service.State);
            case "bridge.discover": return CoreJson.Value((await service.DiscoverAsync(token).ConfigureAwait(false)).ToArray());
            case "bridge.airplay": return CoreJson.Value((await service.AirPlayAsync(token).ConfigureAwait(false)).ToArray());
            default: throw new ArgumentException("Unknown bridge operation.");
        }
    }
    public void Dispose() { service.Changed -= Publish; playback.Changed -= TrackChanged; }
}
