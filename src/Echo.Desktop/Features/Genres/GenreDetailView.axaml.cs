using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Genres;

public partial class GenreDetailView : UserControl, IDisposable
{
    private readonly GenreDetailViewModel _model;
    private readonly AlbumsViewModel _albums;
    private readonly AlbumWallViewport _wall;
    private readonly ShellViewModel _shell;
    private readonly GenreRow _genre;
    private readonly List<AlbumArtworkCache.Lease> _covers = [];
    private CancellationTokenSource? _coverRequest;
    private bool _closed, _visible = true;
    public event Action? Back;
    public event Action<AlbumRow>? OpenAlbum;
    public GenreDetailView() { InitializeComponent(); _model = null!; _albums = null!; _wall = null!; _shell = null!; _genre = null!; }
    public GenreDetailView(GenreRow genre, ShellViewModel shell) : this()
    {
        _shell = shell; _genre = genre;
        _model = new(genre, shell.GenreLibrary!, shell.AlbumLibrary as IAlbumPersonalState, shell.Player, shell.Text);
        DataContext = _model;
        _albums = new(shell.AlbumLibrary!, text: shell.Text, genreId: genre.Id);
        _wall = new(_albums, shell.AlbumArtwork!); _wall.OpenAlbum += album => OpenAlbum?.Invoke(album);
        var area = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 10 };
        var status = new TextBlock(); status.Classes.Add("muted");
        _albums.PropertyChanged += (_, _) => status.Text = _albums.Error.Length > 0 ? _albums.Error : _albums.CountText;
        area.Children.Add(status); Grid.SetRow(_wall, 1); area.Children.Add(_wall); AlbumsHost.Content = area;
        SizeChanged += (_, _) =>
        {
            var narrow = Bounds.Width < 740;
            var size = Bounds.Width < 510 ? 112 : narrow ? 160 : 244;
            CoverFrame.Width = CoverFrame.Height = size;
            Hero.ColumnDefinitions = new($"{size},*,{(narrow ? 0 : 150)}");
            Hero.ColumnSpacing = narrow ? 16 : 28; Facts.IsVisible = !narrow;
        };
        _ = _model.LoadAsync(); _ = _albums.RefreshAsync(); _ = LoadCoversAsync();
    }
    private async Task LoadCoversAsync()
    {
        _coverRequest?.Cancel(); using var request = new CancellationTokenSource(); _coverRequest = request;
        try
        {
            var page = await _shell.GenreLibrary!.GetGenreAlbumsAsync(_genre.Id, new(Limit: 4), request.Token);
            foreach (var album in page.Items)
            {
                var lease = await _shell.AlbumArtwork!.AcquireAsync(album.SamplePath, request.Token);
                if (_closed || !_visible || request.IsCancellationRequested) { lease?.Dispose(); return; }
                if (lease?.Image is not null) _covers.Add(lease); else lease?.Dispose();
            }
            if (request.IsCancellationRequested || _closed) return;
            Mosaic.Children.Clear();
            for (var i = 0; i < _covers.Count; i++)
            {
                var image = new Image { Source = _covers[i].Image, Stretch = Avalonia.Media.Stretch.UniformToFill };
                if (_covers.Count == 1) { Grid.SetColumnSpan(image, 2); Grid.SetRowSpan(image, 2); }
                else if (_covers.Count == 2) { Grid.SetColumn(image, i); Grid.SetRowSpan(image, 2); }
                else { Grid.SetRow(image, i / 2); Grid.SetColumn(image, i % 2); }
                Mosaic.Children.Add(image);
            }
            Mosaic.IsVisible = _covers.Count > 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* A missing hero image never blocks the genre contents. */ }
        finally { if (ReferenceEquals(_coverRequest, request)) _coverRequest = null; }
    }
    private void ReleaseCovers()
    { _coverRequest?.Cancel(); _coverRequest = null; Mosaic.Children.Clear(); Mosaic.IsVisible = false; foreach (var lease in _covers) lease.Dispose(); _covers.Clear(); }
    private void OnBack(object? sender, RoutedEventArgs args) => Back?.Invoke();
    private void OnTab(object? sender, RoutedEventArgs args)
    { if (sender is Button button && int.TryParse(button.Tag?.ToString(), out var tab)) { _model.ActiveTab = tab; _wall.SetSuspended(!_visible || !_model.IsAlbums); } }
    private void OnRefresh(object? sender, RoutedEventArgs args)
    { _model.RetryCommand.Execute(null); _ = _albums.RefreshAsync(); ReleaseCovers(); _shell.AlbumArtwork!.ClearUnused(); if (_visible) _ = LoadCoversAsync(); }
    private void OnPlay(object? sender, EventArgs args) { if (sender is AlbumTrackRow { DataContext: AlbumTrackItem item }) _ = _model.PlayTrackAsync(item.Row); }
    private void OnNext(object? sender, EventArgs args) { if (sender is AlbumTrackRow { DataContext: AlbumTrackItem item }) _ = _model.AddTrackAsync(item.Row, true); }
    private void OnAdd(object? sender, EventArgs args) { if (sender is AlbumTrackRow { DataContext: AlbumTrackItem item }) _ = _model.AddTrackAsync(item.Row, false); }
    public void SetWindowVisible(bool visible)
    { if (_closed || _visible == visible) return; _visible = visible; _wall.SetSuspended(!visible || !_model.IsAlbums); if (visible) _ = LoadCoversAsync(); else ReleaseCovers(); }
    public void Dispose()
    { if (_closed) return; _closed = true; ReleaseCovers(); _model.Dispose(); _wall.Dispose(); _albums.Dispose(); Back = null; OpenAlbum = null; }
}
