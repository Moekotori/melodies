using Echo.Desktop.Features.Player;
using Echo.Playback;

namespace Echo.Desktop.Features.Connect.Runtime;

internal static class ConnectPlayback
{
    public static async Task PlayHttpAsync(PlayerViewModel? player, string url, string? title, CancellationToken token)
    {
        if (player is null) throw new InvalidOperationException("播放服务尚未连接。");
        if (!ConnectAddress.IsHttp(url)) throw new ArgumentException("只接受不带账号的 http 或 https 地址。");
        var source = new AudioSource("http", url);
        source.Validate();
        var id = "connect-" + Guid.NewGuid().ToString("N");
        Dictionary<string, string?>? metadata = null;
        if (!string.IsNullOrWhiteSpace(title))
            metadata = new() { ["title"] = title.Length > 200 ? title[..200] : title };
        await player.Session.PlayAsync(new QueueTrack(id, id, url, source, metadata), token).ConfigureAwait(false);
    }

    public static async Task TransportAsync(PlayerViewModel? player, string action, CancellationToken token)
    {
        if (player is null) throw new InvalidOperationException("播放服务尚未连接。");
        var session = player.Session;
        switch (action)
        {
            case "play":
            case "resume":
                await session.ResumeAsync(token).ConfigureAwait(false);
                break;
            case "pause":
                await session.PauseAsync(token).ConfigureAwait(false);
                break;
            case "toggle":
                if (session.Snapshot.Status == PlaybackStatus.Playing) await session.PauseAsync(token).ConfigureAwait(false);
                else await session.ResumeAsync(token).ConfigureAwait(false);
                break;
            case "next":
                await session.NextAsync(token).ConfigureAwait(false);
                break;
            case "previous":
                await session.PreviousAsync(token).ConfigureAwait(false);
                break;
            case "stop":
                await session.StopAsync(token).ConfigureAwait(false);
                break;
            default:
                if (action.StartsWith("volume:", StringComparison.Ordinal) && int.TryParse(action.AsSpan(7), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var volume))
                    await session.SetVolumeAsync(Math.Clamp(volume, 0, 100) / 100d, token).ConfigureAwait(false);
                else
                    throw new ArgumentException("不认识的播放命令。");
                break;
        }
    }

    public static string Transport(PlayerViewModel? player) => player?.Session.Snapshot.Status switch
    {
        PlaybackStatus.Playing or PlaybackStatus.Loading => "PLAYING",
        PlaybackStatus.Paused => "PAUSED_PLAYBACK",
        _ => "STOPPED"
    };

    public static string Clock(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) seconds = 0;
        var whole = (int)seconds;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{whole / 3600}:{(whole / 60) % 60:00}:{whole % 60:00}");
    }

    public static async Task PauseLocalAsync(PlayerViewModel? player, CancellationToken token)
    {
        if (player?.Session.Snapshot.Status is PlaybackStatus.Playing or PlaybackStatus.Loading)
            await player.Session.PauseAsync(token).ConfigureAwait(false);
    }
}
