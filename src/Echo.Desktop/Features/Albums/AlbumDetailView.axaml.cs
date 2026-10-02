using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Player;

namespace Echo.Desktop.Features.Albums;

public partial class AlbumDetailView : UserControl, IDisposable
{
    private readonly AlbumDetailViewModel _model;
    private readonly CancellationTokenSource _coverRequest = new();
    private AlbumArtworkCache.Lease? _lease;
    private bool _closed;
    public event Action? Back;
    public event Action<string, bool, int>? PersonalStateChanged;
    public AlbumDetailView() { InitializeComponent(); _model = null!; }
    public AlbumDetailView(AlbumRow album, IAlbumLibrary library, AlbumArtworkCache artwork, PlayerViewModel player) : this()
    {
        _model = new(album, library, player);
        DataContext = _model;
        _model.ObservePlayback();
        _model.PersonalStateChanged += () => PersonalStateChanged?.Invoke(album.Id, _model.Favorite, _model.Rating);
        _model.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(AlbumDetailViewModel.Rating)) UpdateRating(); };
        SizeChanged += (_, _) => UpdateHeroLayout();
        UpdateRating();
        _ = _model.LoadAsync(); _ = LoadCoverAsync(artwork, album.SamplePath);
    }
    private async Task LoadCoverAsync(AlbumArtworkCache artwork, string path)
    {
        var lease = await artwork.AcquireAsync(path, _coverRequest.Token);
        if (_closed) { lease?.Dispose(); return; }
        _lease = lease; Cover.Source = lease?.Image; GalleryCover.Source = lease?.Image;
    }
    private void OnBack(object? sender, RoutedEventArgs args) => Back?.Invoke();
    private void OnTrackPlay(object? sender, EventArgs args)
    { if (sender is AlbumTrackRow { DataContext: AlbumTrackItem track }) _ = _model.PlayTrackAsync(track.Row); }
    private void OnMenuNext(object? sender, EventArgs args)
    { if (sender is AlbumTrackRow { DataContext: AlbumTrackItem track }) _ = _model.AddTrackAsync(track.Row, true); }
    private void OnMenuAdd(object? sender, EventArgs args)
    { if (sender is AlbumTrackRow { DataContext: AlbumTrackItem track }) _ = _model.AddTrackAsync(track.Row, false); }
    private void OnTab(object? sender, RoutedEventArgs args)
    { if (sender is Button button && int.TryParse(button.Tag?.ToString(), out var index)) _model.ActiveTab = index; }
    private void OnRating(object? sender, RoutedEventArgs args)
    { if (sender is Button button && int.TryParse(button.Tag?.ToString(), out var value)) _ = _model.SetRatingAsync(value); }
    private void UpdateRating()
    {
        Button[] buttons = [Rate1, Rate2, Rate3, Rate4, Rate5];
        for (var i = 0; i < buttons.Length; i++)
        { buttons[i].Content = _model.Rating > i ? "★" : "☆"; buttons[i].Classes.Set("selected", _model.Rating > i); }
    }
    private void UpdateHeroLayout()
    {
        var narrow = Bounds.Width < 900;
        var size = Math.Clamp(Bounds.Width * .25, 180, 280);
        Hero.ColumnDefinitions[0].Width = new(size); Hero.ColumnDefinitions[2].Width = new(narrow ? 0 : 200);
        Hero.ColumnSpacing = narrow ? 24 : 40; Facts.IsVisible = !narrow;
        CoverFrame.Width = size; CoverFrame.Height = size;
        var titleSize = Math.Clamp((TopLevel.GetTopLevel(this)?.Bounds.Width ?? Bounds.Width) * .038, 38, 60);
        if (_model.Album.Title.Length > 40) titleSize *= .75;
        else if (_model.Album.Title.Length > 25) titleSize *= .85;
        AlbumTitle.FontSize = narrow ? Math.Min(38, titleSize) : titleSize; AlbumTitle.LineHeight = AlbumTitle.FontSize * 1.1;
        var compactRows = Bounds.Width < 650;
        TrackHeader.ColumnDefinitions[0].Width = new(compactRows ? 28 : 46);
        TrackHeader.ColumnDefinitions[2].Width = new(compactRows ? 0 : 230);
        TrackHeader.ColumnDefinitions[3].Width = new(compactRows ? 48 : 68);
        TrackHeader.ColumnSpacing = compactRows ? 8 : 18; SignalHeader.IsVisible = !compactRows;
    }
    private async void OnCopyFolder(object? sender, RoutedEventArgs args)
    { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetValueAsync(DataFormat.Text, _model.Folder); }
    private async void OnCopyInfo(object? sender, RoutedEventArgs args)
    { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetValueAsync(DataFormat.Text, $"{_model.Album.Title}\n{_model.Album.Artist}\n{_model.Meta}\n{_model.DurationLabel}"); }
    public void Dispose()
    {
        if (_closed) return;
        _closed = true; _coverRequest.Cancel(); _model.Dispose();
        Cover.Source = null; GalleryCover.Source = null; _lease?.Dispose(); _lease = null; Back = null; PersonalStateChanged = null;
    }
}
