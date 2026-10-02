using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Playlists;

public sealed partial class PlaylistTrackItem : ObservableObject
{
    private readonly Func<PlaylistTrackItem, Task> favoriteAction;
    public PlaylistTrackRow Entry { get; }
    public TrackRow Row => Entry.Track;
    public string Number { get; }
    public string Duration => TimeSpan.FromSeconds(Math.Max(0, Row.DurationSeconds)).ToString(Row.DurationSeconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    public string Signal => string.Join(" · ", new[] { Row.Codec.ToUpperInvariant(), Row.BitDepth > 0 ? $"{Row.BitDepth} bit" : "",
        Row.SampleRate > 0 ? $"{Row.SampleRate / 1000d:0.##} kHz" : "" }.Where(s => s.Length > 0));
    [ObservableProperty] private bool favorite;
    [ObservableProperty] private bool isPlaying;
    public string HeartIcon => Favorite ? "heart-filled" : "heart";
    partial void OnFavoriteChanged(bool value) => OnPropertyChanged(nameof(HeartIcon));
    public PlaylistTrackItem(PlaylistTrackRow entry, int ordinal, Func<PlaylistTrackItem, Task> favoriteAction)
    { Entry = entry; Number = ordinal.ToString(); Favorite = Row.Favorite; this.favoriteAction = favoriteAction; }
    [RelayCommand] private Task FavoriteAsync() => favoriteAction(this);
}
