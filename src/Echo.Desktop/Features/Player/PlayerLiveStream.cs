using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Playback;

namespace Echo.Desktop.Features.Player;

public sealed partial class PlayerViewModel
{
    [ObservableProperty] private bool isLiveStream;
    [ObservableProperty] private bool isPodcast;
    private string liveStatus = "";
    public bool CanSeek => CanControl && !IsLiveStream;
    public string LiveStatus => liveStatus;
    public string NowPlayingViewLabel => Text.Translate(IsLiveStream ? "打开电台界面" : IsPodcast ? "打开播客" : "打开歌词");
    partial void OnIsLiveStreamChanged(bool value) { OnPropertyChanged(nameof(CanSeek)); OnPropertyChanged(nameof(NowPlayingViewLabel)); }
    partial void OnIsPodcastChanged(bool value) => OnPropertyChanged(nameof(NowPlayingViewLabel));
    private void UpdateLiveStream(PlaybackSnapshot state)
    {
        IsLiveStream = state.Current?.Metadata?.GetValueOrDefault("isLiveStream") == "true";
        IsPodcast = !IsLiveStream && state.Current?.Origin?.Kind == "podcast";
        liveStatus = Text.Translate(state.Status switch
        {
            PlaybackStatus.Playing => "直播中", PlaybackStatus.Loading => "连接中…", PlaybackStatus.Paused => "已暂停",
            PlaybackStatus.Faulted => "连接失败", PlaybackStatus.Ended => "连接已断开", _ => "已停止"
        });
        OnPropertyChanged(nameof(LiveStatus));
        OnPropertyChanged(nameof(NowPlayingViewLabel));
    }
}
