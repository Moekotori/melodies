using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Albums;

public sealed partial class AlbumTrackItem : ObservableObject
{
    private readonly IAlbumPersonalState? _state;
    private readonly CancellationToken _cancellation;
    private readonly Action<string> _reportError;
    [ObservableProperty] private bool favorite;
    [ObservableProperty] private bool isPlaying;
    public TrackRow Row { get; }
    public string Number { get; }
    public string Duration => TimeSpan.FromSeconds(Math.Max(0, Row.DurationSeconds)).ToString(Row.DurationSeconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    public string Signal => string.Join("  ", new[] { Row.Codec.ToUpperInvariant(), Row.BitDepth > 0 ? $"{Row.BitDepth} bit" : "",
        Row.SampleRate > 0 ? $"{Row.SampleRate / 1000d:0.##} kHz" : "" }.Where(s => s.Length > 0));
    public bool CanFavorite => _state is not null;
    public string Heart => Favorite ? "♥" : "♡";
    partial void OnFavoriteChanged(bool value) => OnPropertyChanged(nameof(Heart));
    public AlbumTrackItem(TrackRow row, int index, IAlbumPersonalState? state, CancellationToken cancellation, Action<string> reportError)
    {
        Row = row; _state = state; _cancellation = cancellation; _reportError = reportError;
        Favorite = row.Favorite;
        Number = row.DiscNumber is > 1 ? $"{row.DiscNumber}-{row.TrackNumber ?? index}" : $"{row.TrackNumber ?? index}";
    }
    [RelayCommand] private async Task ToggleFavoriteAsync()
    {
        if (_state is null) return;
        try { var next = !Favorite; await _state.SetTrackFavoriteAsync(Row.Id, next, _cancellation); if (!_cancellation.IsCancellationRequested) Favorite = next; }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_cancellation.IsCancellationRequested) _reportError("收藏保存失败，请重试。"); }
    }
}
