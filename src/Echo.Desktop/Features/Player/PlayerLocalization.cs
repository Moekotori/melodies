using Echo.Playback;

namespace Echo.Desktop.Features.Player;

public sealed partial class PlayerViewModel
{
    private void RefreshLanguage()
    {
        if (Volatile.Read(ref detached) != 0) return;
        var state = session.State;
        var playback = state.Playback;
        if (playback.Current is null)
        { Title = Text.Translate("暂无播放"); ArtistAlbum = Text.Translate("选择歌曲开始播放"); }
        Subtitle = Text.Translate(playback.Status switch
        {
            PlaybackStatus.Loading => "正在准备输出…", PlaybackStatus.Playing => "播放中", PlaybackStatus.Paused => "已暂停",
            PlaybackStatus.Ended => "播放结束", PlaybackStatus.Faulted => "播放失败，可重试或选择输出设备", _ => "打开音频文件开始播放"
        });
        RepeatText = Text.Translate(state.Queue.Mode switch
        {
            QueuePlaybackMode.Shuffle => "随机播放", QueuePlaybackMode.RepeatAll => "列表循环",
            QueuePlaybackMode.RepeatOne => "单曲循环", _ => "顺序播放"
        });
        OutputName = Text.Translate(state.Output.DeviceName);
        SignalPath = state.Output.Confirmed is { } output
            ? Text.Format("player.signal", Number(output, "hardwareSampleRate", "sampleRate"), playback.Channels,
                Read(output, "backend"), Read(output, "deviceName"), Read(output, "format"), Number(output, "actualBufferFrames", "deviceBufferFrames"))
            : Text.Translate("尚未确认实际设备输出；配置会在播放时验证。");
    }
}
