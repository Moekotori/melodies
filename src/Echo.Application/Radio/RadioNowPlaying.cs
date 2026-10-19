using Echo.Contracts.Radio;
using Echo.Playback;

namespace Echo.Application.Radio;

// Follows the playback session and keeps one ICY reader beside the native audio connection.
public sealed class RadioNowPlaying : IDisposable
{
    private readonly IPlaybackSession session;
    private readonly IRadioMetadataMonitor monitor;
    private string? url;
    private PlaybackStatus status = (PlaybackStatus)(-1);
    private bool disposed;

    public RadioNowPlaying(IPlaybackSession session, IRadioMetadataMonitor monitor)
    {
        this.session = session;
        this.monitor = monitor;
        session.Changed += OnChanged;
        OnChanged(session.Snapshot);
    }

    public IRadioMetadataMonitor Monitor => monitor;

    private void OnChanged(PlaybackSnapshot snapshot)
    {
        if (disposed) return;
        var current = snapshot.Current;
        var live = current?.Metadata?.GetValueOrDefault("isLiveStream") == "true";
        var nextUrl = live ? current?.Source?.Uri : null;
        if (nextUrl == url && snapshot.Status == status) return;
        url = nextUrl;
        status = snapshot.Status;
        if (string.IsNullOrEmpty(nextUrl)) monitor.Follow(null, false);
        else if (snapshot.Status is PlaybackStatus.Playing or PlaybackStatus.Loading) monitor.Follow(nextUrl, false);
        else if (snapshot.Status == PlaybackStatus.Paused) monitor.Follow(null, true);
        else monitor.Follow(null, false);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        session.Changed -= OnChanged;
        monitor.Dispose();
    }
}
