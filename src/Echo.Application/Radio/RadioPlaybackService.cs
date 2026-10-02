using Echo.Contracts.Radio;
using Echo.Playback;

namespace Echo.Application.Radio;

// Owns one cancellable address resolution, never playback facts or decoded audio.
public sealed class RadioPlaybackService(PlaybackSessionCoordinator session, IRadioStreamResolver resolver) : IDisposable
{
    private readonly object gate = new();
    private CancellationTokenSource? probe;
    private string? connectingId;
    private bool opening;
    private long generation;
    private bool disposed;
    public event Action? Changed;
    public bool IsConnecting(string stationId) { lock (gate) return connectingId == stationId; }

    public async Task StartAsync(RadioStation station, CancellationToken token)
    {
        CancellationTokenSource request; long version;
        var initial = session.Snapshot;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            probe?.Cancel(); request = CancellationTokenSource.CreateLinkedTokenSource(token);
            probe = request; connectingId = station.Id; opening = false; version = ++generation;
        }
        Notify();
        try
        {
            var info = await resolver.ResolveAsync(station.Url, request.Token).ConfigureAwait(false);
            request.Token.ThrowIfCancellationRequested();
            // A local playback/seek command issued during the probe takes priority over this late result.
            var now = session.Snapshot;
            if (now.Current?.ItemId != initial.Current?.ItemId || now.OperationId != initial.OperationId || now.Status != initial.Status) return;
            var metadata = new Dictionary<string, string?>
            {
                ["title"] = station.Name, ["artist"] = info.Description ?? station.Description,
                ["codec"] = info.Codec == "—" ? station.Codec == "—" ? null : station.Codec : info.Codec,
                ["isLiveStream"] = "true", ["radioOriginalUrl"] = station.Url,
                ["radioStationName"] = info.Name ?? station.Name,
                ["bitrate"] = info.BitrateKbps?.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            var track = new QueueTrack("radio:" + station.Id, "radio:" + station.Id, info.Url,
                new("http", info.Url),
                metadata, Origin: new("radio", station.Name, station.Id));
            Task play;
            lock (gate)
            {
                if (disposed || version != generation || request.IsCancellationRequested) return;
                probe = null; opening = true;
                play = session.PlayAsync(track, token); // Queue admission happens before a concurrent Stop.
            }
            await play.ConfigureAwait(false);
        }
        finally
        {
            lock (gate)
            {
                if (version == generation) { probe = null; connectingId = null; opening = false; }
            }
            request.Dispose(); Notify();
        }
    }
    public Task StopAsync(string stationId, CancellationToken token)
    {
        bool stopOpening;
        lock (gate)
        {
            if (disposed) return Task.CompletedTask;
            stopOpening = connectingId == stationId && opening;
            if (connectingId == stationId)
            { ++generation; probe?.Cancel(); probe = null; connectingId = null; opening = false; }
        }
        Notify();
        return stopOpening || IsCurrent(session.Snapshot, stationId) ? session.StopAsync(token) : Task.CompletedTask;
    }
    public static bool IsCurrent(PlaybackSnapshot snapshot, string stationId)
        => snapshot.Current?.Origin is { Kind: "radio", Id: { } id } && id == stationId
            || snapshot.Current?.ItemId == "radio:" + stationId;
    private void Notify()
    {
        if (Changed is not { } handlers) return;
        foreach (Action handler in handlers.GetInvocationList()) try { handler(); } catch (Exception) { }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return; disposed = true; ++generation;
            probe?.Cancel(); probe = null; connectingId = null;
        }
        Changed = null;
    }
}
