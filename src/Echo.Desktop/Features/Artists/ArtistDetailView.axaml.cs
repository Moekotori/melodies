using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Artists;

public partial class ArtistDetailView : UserControl, IDisposable
{
    private readonly ArtistDetailViewModel _model;
    private readonly AlbumsViewModel _albums;
    private readonly AlbumWallViewport _wall;
    private ArtistHeroArtwork? _heroArtwork;
    private LibraryDetails.DetailPageInsetLease? _pageInset;
    private bool _closed, _visible = true;
    public event Action? Back;
    public event Action<AlbumRow>? OpenAlbum;
    public event Action<ArtistRow>? OpenArtist;
    public ArtistDetailView()
    {
        InitializeComponent(); _model = null!; _albums = null!; _wall = null!;
        AttachedToVisualTree += (_, _) => _pageInset ??= LibraryDetails.DetailPageInsetLease.Acquire(this);
        DetachedFromVisualTree += (_, _) => { _pageInset?.Dispose(); _pageInset = null; };
    }
    public ArtistDetailView(ArtistRow artist, ShellViewModel shell) : this()
    {
        _model = new(artist, shell.ArtistLibrary!, shell.AlbumLibrary as IAlbumPersonalState, shell.Player, shell.Text); DataContext = _model;
        OverviewPanel.PlayTrack += track => _ = _model.PlayTrackAsync(track);
        OverviewPanel.PlayNext += track => _ = _model.AddTrackAsync(track, true);
        OverviewPanel.AddToQueue += track => _ = _model.AddTrackAsync(track, false);
        OverviewPanel.OpenArtist += target => OpenArtist?.Invoke(target);
        OverviewPanel.OpenAlbum += album => OpenAlbum?.Invoke(album);
        _albums = new(shell.AlbumLibrary!, artist); _wall = new(_albums, shell.AlbumArtwork!);
        _wall.OpenAlbum += album => OpenAlbum?.Invoke(album);
        var albumArea = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var status = new TextBlock { Margin = new(0, 0, 0, 12) };
        var refresh = new Button { Content = "刷新专辑", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        refresh.Click += (_, _) => _ = _albums.RefreshAsync();
        var toolbar = new Grid(); toolbar.Children.Add(status); toolbar.Children.Add(refresh);
        _albums.PropertyChanged += (_, _) => status.Text = _albums.Error.Length > 0 ? _albums.Error : _albums.CountText;
        albumArea.Children.Add(toolbar); Grid.SetRow(_wall, 1); albumArea.Children.Add(_wall); AlbumsHost.Content = albumArea;
        _wall.SetSuspended(true);
        HeroMark.Text = ArtistMonogram.Hero(artist.Name);
        SizeChanged += (_, _) =>
        {
            var narrow = Bounds.Width < 760;
            var inset = Bounds.Width < 800 ? 22d : 42d;
            PageContent.Width = Math.Min(1120, Math.Max(0, Bounds.Width - inset * 2));
            Hero.MinHeight = narrow ? 276 : 248;
            HeroMark.FontSize = narrow ? 88 : 116;
            var titleLength = artist.Name.EnumerateRunes().Count();
            ArtistTitle.FontSize = titleLength > 72 ? 28 : titleLength > 36 ? 34 : narrow ? 34 : 44;
            ArtistTitle.LineHeight = ArtistTitle.FontSize * 1.18;
            HeroCopy.Margin = new(narrow ? 22 : 28, 58, narrow ? 22 : 28, 24);
            HeroCopy.MaxWidth = Math.Min(760, Math.Max(180, PageContent.Width - (narrow ? 44 : 56)));
            if (PreviewTrackList.ItemsPanelRoot is Avalonia.Controls.Primitives.UniformGrid grid) grid.Columns = narrow ? 1 : 2;
        };
        _heroArtwork = new(artist, shell.AlbumArtwork!, _model.Overview, Portrait);
        _ = _model.LoadAsync(); _ = _model.Overview.LoadAsync();
    }
    private void OnBack(object? sender, RoutedEventArgs args) => Back?.Invoke();
    private void OnTab(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button button || !int.TryParse(button.Tag?.ToString(), out var tab)) return;
        _model.ActiveTab = tab; _wall.SetSuspended(!_visible || !_model.IsAlbums);
        if (tab == 1 && _albums.Revision is null) _ = _albums.RefreshAsync();
    }
    private void OnPlay(object? sender, EventArgs args) { if (sender is AlbumTrackRow { DataContext: AlbumTrackItem item }) _ = _model.PlayTrackAsync(item.Row); }
    private void OnNext(object? sender, EventArgs args) { if (sender is AlbumTrackRow { DataContext: AlbumTrackItem item }) _ = _model.AddTrackAsync(item.Row, true); }
    private void OnAdd(object? sender, EventArgs args) { if (sender is AlbumTrackRow { DataContext: AlbumTrackItem item }) _ = _model.AddTrackAsync(item.Row, false); }
    private void OnOverviewPlay(TrackRow track) => _ = _model.PlayTrackAsync(track);
    private void OnOverviewNext(TrackRow track) => _ = _model.AddTrackAsync(track, true);
    private void OnOverviewAdd(TrackRow track) => _ = _model.AddTrackAsync(track, false);
    private async void OnExternal(object? sender, RoutedEventArgs args)
    { if (sender is Button { Tag: string url }) await ArtistExternalLink.OpenAsync(this, url, _model.Overview); }
    public void SetWindowVisible(bool visible) { _visible = visible; _wall.SetSuspended(!visible || !_model.IsAlbums); }
    public void Dispose()
    {
        if (_closed) return; _closed = true; _heroArtwork?.Dispose(); _heroArtwork = null; _model.Dispose(); _wall.Dispose(); _albums.Dispose();
        Back = null; OpenAlbum = null; OpenArtist = null;
        _pageInset?.Dispose(); _pageInset = null;
    }
}
