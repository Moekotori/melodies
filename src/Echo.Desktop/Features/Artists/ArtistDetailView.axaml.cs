using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
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
    private readonly CancellationTokenSource _lifetime = new();
    private AlbumArtworkCache.Lease? _portrait;
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
            PageContent.Margin = new(inset, 20, inset, 36);
            Hero.Height = Math.Clamp((TopLevel.GetTopLevel(this)?.Bounds.Height ?? 900) * .35, narrow ? 280 : 300, narrow ? 340 : 360);
            HeroMark.FontSize = Math.Clamp(Bounds.Width * .13, 72, 156);
            var pageWidth = TopLevel.GetTopLevel(this)?.Bounds.Width ?? Bounds.Width;
            var titleLength = artist.Name.EnumerateRunes().Count();
            ArtistTitle.FontSize = titleLength > 72 ? Math.Clamp(pageWidth * .024, 28, 34)
                : titleLength > 36 ? Math.Clamp(pageWidth * .028, 30, 40) : Math.Clamp(pageWidth * .032, 32, 46);
            if (narrow) ArtistTitle.FontSize = Math.Min(34, ArtistTitle.FontSize);
            ArtistTitle.LineHeight = ArtistTitle.FontSize * 1.18;
            HeroCopy.Margin = new(narrow ? 24 : 40);
        };
        _ = _model.LoadAsync(); _ = _model.Overview.LoadAsync(); if (artist.PortraitPath is { } portraitPath) _ = LoadPortraitAsync(shell.AlbumArtwork!, portraitPath);
    }
    private async Task LoadPortraitAsync(AlbumArtworkCache cache, string path)
    {
        var lease = await cache.AcquireArtistAsync(path, _lifetime.Token);
        if (_closed) { lease?.Dispose(); return; }
        _portrait = lease; Portrait.Source = lease?.Image;
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
    private async void OnCopy(object? sender, RoutedEventArgs args)
    { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetValueAsync(DataFormat.Text, $"{_model.Artist.Name}\n{_model.Meta}\n{_model.Duration}"); }
    public void SetWindowVisible(bool visible) { _visible = visible; _wall.SetSuspended(!visible || !_model.IsAlbums); }
    public void Dispose()
    {
        if (_closed) return; _closed = true; _lifetime.Cancel(); _model.Dispose(); _wall.Dispose(); _albums.Dispose();
        Portrait.Source = null; _portrait?.Dispose(); _portrait = null; Back = null; OpenAlbum = null; OpenArtist = null;
        _pageInset?.Dispose(); _pageInset = null;
    }
}
