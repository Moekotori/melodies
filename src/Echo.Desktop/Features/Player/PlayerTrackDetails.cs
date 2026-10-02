using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Library;
using Echo.Playback;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Player;

// Holds one active-track DTO. Reads and writes run through the shared library lanes.
public sealed partial class PlayerTrackDetails : ObservableObject, IDisposable
{
    private readonly LocalizationService Text;
    public PlayerTrackDetails(LocalizationService? text = null)
    { Text = text ?? new(); Text.Changed += RefreshLanguage; artist = Text.Translate("就绪"); }
    private void RefreshLanguage()
    {
        OnPropertyChanged(nameof(FavoriteLabel));
        if (current is null) Artist = Text.Translate("就绪");
    }
    private IPlayerTrackLibrary? library;
    private QueueTrack? current;
    private TrackRow? row;
    private CancellationTokenSource? request;
    private bool disposed;
    [ObservableProperty] private string title = "ECHO";
    [ObservableProperty] private string artist = "就绪";
    [ObservableProperty] private string album = "";
    [ObservableProperty] private string format = "";
    [ObservableProperty] private string specifications = "";
    [ObservableProperty] private bool canFavorite;
    [ObservableProperty] private bool isFavorite;
    [ObservableProperty] private string message = "";
    public string FavoriteLabel => IsFavorite ? Text.Translate("取消喜欢") : CanFavorite ? Text.Translate("喜欢") : Text.Translate("喜欢（需要曲库中的歌曲）");
    public string HeartIcon => IsFavorite ? "heart-filled" : "heart";
    public bool HasFormat => Format.Length > 0;
    public bool HasSpecifications => Specifications.Length > 0;
    public string ArtistAlbum => string.Join(" · ", new[] { Artist, Album }.Where(x => x.Length > 0));
    partial void OnArtistChanged(string value) => OnPropertyChanged(nameof(ArtistAlbum));
    partial void OnAlbumChanged(string value) => OnPropertyChanged(nameof(ArtistAlbum));
    partial void OnIsFavoriteChanged(bool value) { OnPropertyChanged(nameof(FavoriteLabel)); OnPropertyChanged(nameof(HeartIcon)); }
    partial void OnCanFavoriteChanged(bool value) => OnPropertyChanged(nameof(FavoriteLabel));
    partial void OnFormatChanged(string value) => OnPropertyChanged(nameof(HasFormat));
    partial void OnSpecificationsChanged(string value) => OnPropertyChanged(nameof(HasSpecifications));

    public void AttachLibrary(IPlayerTrackLibrary? value)
    { library = value; var active = current; current = null; Update(active); }

    public void Update(QueueTrack? track)
    {
        if (disposed || ReferenceEquals(current, track)) return;
        request?.Cancel(); request?.Dispose(); request = null;
        current = track; row = null; CanFavorite = false; IsFavorite = false; Message = "";
        Title = track?.Metadata?.GetValueOrDefault("title") ?? (track is null ? "ECHO" : Path.GetFileNameWithoutExtension(track.FilePath));
        Artist = track?.Metadata?.GetValueOrDefault("artist") is { Length: > 0 } artist ? artist : track is null ? Text.Translate("就绪") : Text.Translate("未知艺术家");
        Album = track?.Metadata?.GetValueOrDefault("album") ?? "";
        Format = track?.Metadata?.GetValueOrDefault("codec")?.ToUpperInvariant() ?? "";
        Specifications = "";
        if (track is null || library is null || string.IsNullOrWhiteSpace(track.FilePath)) return;
        request = new(); _ = LoadAsync(track, library, request.Token);
    }

    private async Task LoadAsync(QueueTrack track, IPlayerTrackLibrary source, CancellationToken token)
    {
        try
        {
            var found = await source.FindTrackByPathAsync(track.FilePath, token);
            if (token.IsCancellationRequested || disposed || current != track || found is null) return;
            row = found; Title = found.Title; Artist = found.Artist.Length == 0 ? Text.Translate("未知艺术家") : found.Artist;
            Album = found.Album; Format = found.Codec.ToUpperInvariant();
            Specifications = string.Join(" · ", new[] { found.BitDepth > 0 ? $"{found.BitDepth} bit" : "",
                found.SampleRate > 0 ? $"{found.SampleRate / 1000d:0.##} kHz" : "" }.Where(x => x.Length > 0));
            IsFavorite = found.Favorite; CanFavorite = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!token.IsCancellationRequested && !disposed) Message = Text.Translate("歌曲资料暂时无法读取。"); }
    }

    [RelayCommand] private async Task ToggleFavoriteAsync()
    {
        if (row is not { } active || library is null || request is null || disposed) return;
        var token = request.Token;
        try
        {
            Message = ""; var next = !IsFavorite;
            await library.SetTrackFavoriteAsync(active.Id, next, token);
            if (!token.IsCancellationRequested && !disposed && row == active) IsFavorite = next;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!token.IsCancellationRequested && !disposed) Message = Text.Translate("喜欢状态未保存，请重试。"); }
    }

    public void Dispose()
    { Text.Changed -= RefreshLanguage; disposed = true; request?.Cancel(); request?.Dispose(); request = null; current = null; row = null; library = null; }
}
