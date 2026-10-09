using Echo.Desktop.Features.Player;

namespace Echo.Desktop.Features.Connect.Runtime;

public sealed class DlnaCastSession : IDisposable
{
    private readonly Func<PlayerViewModel?> player;
    private readonly SemaphoreSlim gate = new(1, 1);
    private CastStreamServer? server;
    private DlnaRenderer? renderer;
    private int disposed;

    public DlnaCastSession(Func<PlayerViewModel?> player) => this.player = player;

    public bool Active { get; private set; }
    public bool Paused { get; private set; }
    public string DeviceName { get; private set; } = "";
    public string TargetKey { get; private set; } = "";
    public event Action? Changed;

    public async Task CastAsync(DlnaRenderer target, CancellationToken token)
    {
        var track = ConnectTrack.Current(player());
        if (track is null) throw new InvalidOperationException("先播放一首本地歌曲，或一个不带账号的网络音频。网盘曲目不会被转发出去。");
        await gate.WaitAsync(token).ConfigureAwait(false);
        CastStreamServer? opened = null;
        try
        {
            await StopCoreAsync(token).ConfigureAwait(false);
            var location = track.Value.Location;
            if (track.Value.LocalFile)
            {
                opened = CastStreamServer.Start(track.Value.Location, track.Value.Mime);
                location = opened.Url(target.Address);
            }
            await DlnaControl.SetUriAsync(target, location, ConnectTrack.Didl(location, track.Value.Mime, track.Value.Title, track.Value.Artist), token).ConfigureAwait(false);
            await DlnaControl.PlayAsync(target, token).ConfigureAwait(false);
            server = opened;
            opened = null;
            renderer = target;
            Active = true;
            Paused = false;
            DeviceName = target.Name;
            TargetKey = target.AvTransport.ControlUrl;
            Changed?.Invoke();
            await ConnectPlayback.PauseLocalAsync(player(), token).ConfigureAwait(false);
        }
        finally
        {
            opened?.Dispose();
            gate.Release();
        }
    }

    public async Task ToggleAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (renderer is null || !Active) throw new InvalidOperationException("还没有投送到设备。");
            if (Paused) await DlnaControl.PlayAsync(renderer, token).ConfigureAwait(false);
            else await DlnaControl.PauseAsync(renderer, token).ConfigureAwait(false);
            Paused = !Paused;
            Changed?.Invoke();
        }
        finally { gate.Release(); }
    }

    public async Task StopAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { await StopCoreAsync(token).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    public async Task SetVolumeAsync(int volume, CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (renderer is null || !Active) throw new InvalidOperationException("还没有投送到设备。");
            await DlnaControl.SetVolumeAsync(renderer, volume, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        server?.Dispose();
        server = null;
        Active = false;
    }

    private async Task StopCoreAsync(CancellationToken token)
    {
        var current = renderer;
        renderer = null;
        server?.Dispose();
        server = null;
        var wasActive = Active;
        Active = false;
        Paused = false;
        DeviceName = "";
        TargetKey = "";
        if (wasActive) Changed?.Invoke();
        if (current is null) return;
        try { await DlnaControl.StopAsync(current, token).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { }
    }
}
