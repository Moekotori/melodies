using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Desktop.Features.Player;

namespace Echo.Desktop.Features.Connect;

// A small mirror of the current player. It does not open a second playback subscription.
public sealed partial class ConnectNowPlaying : ObservableObject, IDisposable
{
    private PlayerViewModel? player;
    [ObservableProperty] private string title = "暂无播放";
    [ObservableProperty] private string artist = "选择曲目后，这里显示正在播放的内容";
    [ObservableProperty] private string status = "播放服务尚未连接";
    [ObservableProperty] private string outputName = "当前由本机播放";
    [ObservableProperty] private string signal = "尚未确认实际设备输出";
    [ObservableProperty] private string positionText = "0:00";
    [ObservableProperty] private string durationText = "0:00";
    [ObservableProperty] private double progress;
    [ObservableProperty] private bool canTransport;
    [ObservableProperty] private bool hasTrack;
    [ObservableProperty] private string playLabel = "播放";
    [ObservableProperty] private double volumePercent;

    public void Attach(PlayerViewModel? source)
    {
        if (player is not null) player.PropertyChanged -= OnPlayer;
        player = source;
        if (player is not null) player.PropertyChanged += OnPlayer;
        Pull();
    }

    public void Dispose()
    {
        if (player is not null) player.PropertyChanged -= OnPlayer;
        player = null;
    }

    private void OnPlayer(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.Title) or nameof(PlayerViewModel.ArtistAlbum) or nameof(PlayerViewModel.Subtitle)
            or nameof(PlayerViewModel.OutputName) or nameof(PlayerViewModel.SignalPath) or nameof(PlayerViewModel.PositionText)
            or nameof(PlayerViewModel.DurationText) or nameof(PlayerViewModel.Position) or nameof(PlayerViewModel.Duration)
            or nameof(PlayerViewModel.HasTrack) or nameof(PlayerViewModel.PlayIcon) or nameof(PlayerViewModel.CanControl)
            or nameof(PlayerViewModel.Volume) or nameof(PlayerViewModel.IsChangingVolume))
            Pull();
    }

    private void Pull()
    {
        Title = player?.Title ?? "暂无播放";
        Artist = player?.ArtistAlbum ?? "选择曲目后，这里显示正在播放的内容";
        Status = player?.Subtitle ?? "播放服务尚未连接";
        var outputName = player?.OutputName;
        var signal = player?.SignalPath;
        OutputName = string.IsNullOrWhiteSpace(outputName) ? "当前由本机播放" : outputName;
        Signal = string.IsNullOrWhiteSpace(signal) ? "尚未确认实际设备输出" : signal;
        PositionText = player?.PositionText ?? "0:00";
        DurationText = player?.DurationText ?? "0:00";
        HasTrack = player?.HasTrack == true;
        CanTransport = player?.CanControl == true;
        PlayLabel = player?.PlayIcon == "pause" ? "暂停" : "播放";
        var duration = player?.Duration ?? 0;
        Progress = duration <= 0 ? 0 : Math.Clamp((player?.Position ?? 0) / duration * 100, 0, 100);
        if (player is null || !player.IsChangingVolume)
            VolumePercent = Math.Round(Math.Clamp(player?.Volume ?? 0, 0, 1) * 100);
    }
}
