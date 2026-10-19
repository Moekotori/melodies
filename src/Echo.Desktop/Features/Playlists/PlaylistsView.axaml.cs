using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Echo.Contracts.Library;
using Echo.Contracts.Settings;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.LibraryDetails;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Playlists;

public partial class PlaylistsView : UserControl, IDisposable
{
    private ShellViewModel shell = null!;
    private PlaylistBrowser browser = null!;
    private AlbumsViewModel? albums;
    private AlbumWallViewport? albumWall;
    private DetailNavigator? detail;
    private CancellationTokenSource? coverRequest;
    private AlbumArtworkCache.Lease? coverLease;
    private string? loadedCoverPath;
    private bool closed, creating, railExpanded, visible = true;
    public PlaylistsViewModel Model { get; private set; } = null!;
    public PlaylistsView() { InitializeComponent(); }
    public PlaylistsView(ShellViewModel shell) : this()
    {
        this.shell = shell; Model = new(shell); DataContext = Model;
        browser = new(Model, shell.AlbumArtwork, shell.Text); BrowserHost.Content = browser;
        browser.ItemContainerTheme = (Avalonia.Styling.ControlTheme)this.FindResource("PlaylistWallRow")!;
        Model.SelectionChanged += OnSelection; Model.PropertyChanged += OnModelChanged;
        if (shell.Player is not null) shell.Player.PropertyChanged += OnPlayerChanged;
        AttachedToVisualTree += (_, _) => _ = Model.InitializeAsync();
        DetachedFromVisualTree += (_, _) => Dispose(); SizeChanged += (_, _) => ApplyLayout();
    }
    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(PlayerViewModel.CurrentFilePath)) Model.UpdatePlaying(); }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(PlaylistsViewModel.Error) or nameof(PlaylistsViewModel.Status))
            FeedbackArea.IsVisible = Model.Error.Length > 0 || Model.Status.Length > 0;
        if (args.PropertyName == nameof(PlaylistsViewModel.CoverPath)) LoadCover();
        if (args.PropertyName == nameof(PlaylistsViewModel.Search) && albums is not null) albums.Search = Model.Search;
    }
    private void OnSelection()
    {
        SetWallSearch(false); DetailSearchField.IsVisible = false; DetailMoreButton.Flyout?.Hide();
        ClosePicker(); EditorOverlay.IsVisible = false; ApplyLayout();
        RailList.SelectedItem = Model.IsManual ? Model.SelectedPlaylist : null;
        if (albums is not null) albums.PropertyChanged -= OnAlbumsChanged;
        albums?.Dispose(); albumWall?.Dispose(); albums = null; albumWall = null; AlbumsHost.Content = null; AlbumNotice.IsVisible = false;
        HeroIcon.Kind = Model.IsLiked ? "heart-filled" : Model.IsAlbums ? "album" : "queue";
        if (Model.IsAlbums)
        {
            albums = new(shell.AlbumLibrary!, text: shell.Text, preference: new LibraryViewPreferences("albums", Filter: new(FavoriteOnly: true)));
            albums.PropertyChanged += OnAlbumsChanged;
            albumWall = new(albums, shell.AlbumArtwork!); albumWall.OpenAlbum += OpenAlbum;
            AlbumsHost.Content = albumWall; _ = albums.RefreshAsync();
        }
        browser.SetSuspended(!visible || !Model.IsHome); LoadCover();
    }
    private void OnAlbumsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (albums is null) return;
        AlbumNotice.IsVisible = albums.Error.Length > 0 || !albums.IsLoading && albums.Total == 0;
        AlbumNotice.Text = albums.Error.Length > 0 ? albums.Error : Model.Text["playlists.emptyAlbums"];
    }
    private void OnSelectPlaylist(object? sender, SelectionChangedEventArgs args)
    { if (RailList.SelectedItem is PlaylistRow row && row.Id != Model.Selection) Model.OpenPlaylist(row); }
    private void OnCreate(object? sender, RoutedEventArgs args) => Edit(true);
    private void OnEdit(object? sender, RoutedEventArgs args) => Edit(false);
    private void Edit(bool create)
    {
        DetailMoreButton.Flyout?.Hide();
        creating = create; EditorTitle.Text = Model.Text[create ? "playlists.create" : "playlists.edit"];
        NameField.Text = create ? "" : Model.SelectedPlaylist?.Name;
        DescriptionField.Text = create ? "" : Model.SelectedPlaylist?.Description; EditorError.Text = "";
        EditorOverlay.IsVisible = true; NameField.Focus();
    }
    private async void OnSave(object? sender, RoutedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(NameField.Text)) { EditorError.Text = Model.Text["playlists.nameRequired"]; NameField.Focus(); return; }
        await Model.SavePlaylistAsync(NameField.Text, DescriptionField.Text ?? "", creating);
        if (closed) return;
        if (Model.Error.Length == 0) { EditorOverlay.IsVisible = false; PlayButton.Focus(); }
        else { EditorOverlay.IsVisible = true; EditorError.Text = Model.Error; }
    }
    private void OnCancelEditor(object? sender, RoutedEventArgs args) { EditorOverlay.IsVisible = false; (Model.IsHome ? CreatePlaylistButton : PlayButton).Focus(); }
    private void OnEditorKey(object? sender, KeyEventArgs args)
    { if (args.Key == Key.Escape && !Model.IsActing) { OnCancelEditor(sender, args); args.Handled = true; } }
    private void OnAddTracks(object? sender, RoutedEventArgs args)
    {
        DetailMoreButton.Flyout?.Hide();
        if (shell.TrackLibrary is null || !Model.IsManual) return;
        ClosePicker(); var picker = new PlaylistTrackPicker(shell.TrackLibrary, Model, Model.Selection);
        picker.Close += ClosePicker; PickerHost.Content = picker; PickerHost.IsVisible = true;
    }
    private void ClosePicker() { (PickerHost.Content as IDisposable)?.Dispose(); PickerHost.Content = null; PickerHost.IsVisible = false; }
    public void FocusSearch()
    {
        if (Model.IsHome) { SetWallSearch(true); HomeSearch.Focus(); }
        else { DetailSearchField.IsVisible = true; TrackSearch.Focus(); }
    }
    private void SetWallSearch(bool open)
    {
        WallSearchField.IsVisible = open;
        WallSearchIcon.Kind = open ? "close" : "search";
    }
    private void OnToggleWallSearch(object? sender, RoutedEventArgs args)
    {
        if (WallSearchField.IsVisible) { Model.PlaylistSearch = ""; SetWallSearch(false); WallSearchButton.Focus(); }
        else FocusSearch();
    }
    private void OnWallSearchKey(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape) { SetWallSearch(false); WallSearchButton.Focus(); args.Handled = true; }
        else OnBrowserSearchKey(sender, args);
    }
    private void OnClearBrowserSearch(object? sender, RoutedEventArgs args) { Model.PlaylistSearch = ""; FocusSearch(); }
    private void OnBrowserSearchKey(object? sender, KeyEventArgs args) { if (args.Key == Key.Enter) { _ = Model.LoadBrowserAsync(); args.Handled = true; } }
    private void OnToggleTrackSearch(object? sender, RoutedEventArgs args)
    {
        DetailSearchField.IsVisible = !DetailSearchField.IsVisible;
        if (DetailSearchField.IsVisible) TrackSearch.Focus();
        else { Model.Search = ""; DetailSearchButton.Focus(); }
    }
    private void OnTrackSearchKey(object? sender, KeyEventArgs args) { if (args.Key == Key.Escape) { DetailSearchField.IsVisible = false; DetailSearchButton.Focus(); args.Handled = true; } else if (args.Key == Key.Enter) { if (albums is not null) _ = albums.RefreshAsync(); else _ = Model.LoadTracksAsync(); args.Handled = true; } }
    private void OnCollapse(object? sender, RoutedEventArgs args) { railExpanded = !railExpanded; ApplyLayout(); }
    private void ApplyLayout()
    {
        var narrow = Bounds.Width is > 0 and < 740;
        Rail.IsVisible = !Model.IsHome && railExpanded;
        Workspace.ColumnDefinitions[0].Width = new(Rail.IsVisible ? narrow ? 170 : 208 : 0);
        MainPane.Margin = new(42, 24, 0, Model.IsHome ? 0 : 16);
        Hero.ColumnDefinitions[0].Width = new(narrow ? 112 : 152);
        HeroFrame.Width = HeroFrame.Height = narrow ? 112 : 152;
        Hero.ColumnSpacing = narrow ? 20 : 28;
        TrackSearch.Width = narrow ? 120 : 180;
    }
    private void OpenAlbum(AlbumRow row)
    {
        detail?.Dispose(); detail = new(shell); detail.Back += CloseAlbum;
        detail.OpenAlbum(row); detail.PersonalStateChanged += (_, _, _) => { if (albums is not null) _ = albums.RefreshAsync(); _ = Model.LoadBrowserAsync(); };
        AlbumDetailHost.Content = detail; AlbumDetailHost.IsVisible = true; Workspace.IsVisible = false; albumWall?.SetSuspended(true);
    }
    private void CloseAlbum() { detail?.Dispose(); detail = null; AlbumDetailHost.Content = null; AlbumDetailHost.IsVisible = false; Workspace.IsVisible = true; albumWall?.SetSuspended(!visible); }
    private void LoadCover()
    {
        if (visible && !closed && loadedCoverPath == Model.CoverPath && (coverRequest is not null || coverLease is not null)) return;
        ReleaseCover(); if (!visible || closed || Model.IsHome || Model.CoverPath is not { } path || shell.AlbumArtwork is null) return;
        loadedCoverPath = path;
        coverRequest = new(); _ = CoverAsync(path, coverRequest);
    }
    private async Task CoverAsync(string path, CancellationTokenSource request)
    {
        try
        {
            var lease = await shell.AlbumArtwork!.AcquireAsync(path, request.Token);
            if (closed || request.IsCancellationRequested || !ReferenceEquals(request, coverRequest)) { lease?.Dispose(); return; }
            coverLease = lease; HeroCover.Source = lease?.Image;
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(request, coverRequest)) coverRequest = null; request.Dispose(); }
    }
    private void ReleaseCover() { coverRequest?.Cancel(); coverRequest = null; loadedCoverPath = null; HeroCover.Source = null; coverLease?.Dispose(); coverLease = null; }
    public void SetWindowVisible(bool value)
    { visible = value; browser.SetSuspended(!value || !Model.IsHome); albumWall?.SetSuspended(!value || detail is not null); detail?.SetWindowVisible(value); if (value) LoadCover(); else ReleaseCover(); }
    public void Dispose()
    {
        if (closed) return; closed = true; ClosePicker(); detail?.Dispose(); albumWall?.Dispose(); if (albums is not null) albums.PropertyChanged -= OnAlbumsChanged; albums?.Dispose(); browser.Dispose(); ReleaseCover();
        Model.SelectionChanged -= OnSelection; Model.PropertyChanged -= OnModelChanged;
        if (shell.Player is not null) shell.Player.PropertyChanged -= OnPlayerChanged;
        Model.Dispose(); TrackPanel.DataContext = null;
    }
}
