using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Echo.Contracts.Library;
using Echo.Desktop.Features.LibraryDetails;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Artists;

public partial class ArtistsView : UserControl, IDisposable
{
    private readonly ArtistsViewModel _model;
    private readonly DetailNavigator _details;
    private readonly ArtistWallViewport _viewport;
    private readonly MotionPanes _panes = new();
    public ArtistsViewModel Model => _model;
    public ArtistWallViewport Viewport => _viewport;
    private ShellViewModel? _shell;
    private bool _closed;
    public ArtistsView() { InitializeComponent(); _model = null!; _details = null!; _viewport = null!; }
    public ArtistsView(ShellViewModel shell) : this()
    {
        _shell = shell; shell.Theme.Changed += ThemeChanged;
        if (shell.ArtistLibrary is IArtistImportNotifications notifications) notifications.ArtistImportChanged += ImportChanged;
        _model = new(shell.ArtistLibrary!, shell.QueryPreference("artists")); DataContext = _model;
        _model.GroupingIndex = shell.ArtistGroupingIndex;
        _model.PreferenceChanged += () => { try { shell.RememberQuery(_model.Preference); } catch (ArgumentException) { } };
        _details = new(shell); _details.Back += CloseDetail; DetailHost.Content = _details;
        _viewport = new(_model, shell.AlbumArtwork!); ViewportHost.Content = _viewport;
        _viewport.OpenArtist += OpenDetail; ThemeChanged();
        _model.PropertyChanged += ArtworkChanged;
        shell.Text.Changed += RefreshArtistTitle; RefreshArtistTitle();
        AttachedToVisualTree += (_, _) => _ = _model.LoadAsync();
        DetachedFromVisualTree += (_, _) => Dispose();
    }
    private void ThemeChanged() { if (_shell is not null) _model.ShowAlbumArtwork = _shell.Theme.ArtistAlbumArtwork; }
    private void ImportChanged(ArtistImportSettings settings) => Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (!_closed) _ = _model.LoadAsync(); });
    private void ArtworkChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (_shell is null) return;
        if (args.PropertyName == nameof(ArtistsViewModel.ShowAlbumArtwork)) _shell.Theme.ArtistAlbumArtwork = _model.ShowAlbumArtwork;
        if (args.PropertyName == nameof(ArtistsViewModel.GroupingIndex))
        { _shell.ArtistGroupingIndex = _model.GroupingIndex; RefreshArtistTitle(); }
    }
    private void RefreshArtistTitle() => ArtistsTitle.Text = _shell?.Text[_model.GroupingIndex == 1 ? "navigation.album-artists" : "sidebar.songArtists"];
    public void FocusSearch() { if (CreditsHost.IsVisible && CreditsHost.Content is ArtistCreditsView credits) credits.FocusSearch(); else SearchField.Focus(); }
    private async void OnCredits(object? sender, RoutedEventArgs args)
    {
        if (_shell?.ArtistLibrary is not IArtistCreditLibrary library) return;
        var view = new ArtistCreditsView(library, library as Echo.Core.Contracts.ICoreTaskControl);
        view.Back += CloseCredits; CreditsHost.Content = view;
        if (await _panes.Show(CreditsHost, Wall, MotionDirection.Forward) && !_closed) _viewport.SetSuspended(true);
    }
    private async void CloseCredits()
    {
        _viewport.SetSuspended(false);
        if (await _panes.Show(Wall, CreditsHost, MotionDirection.Back) && !_closed)
        { (CreditsHost.Content as IDisposable)?.Dispose(); CreditsHost.Content = null; _ = _model.LoadAsync(); }
    }
    private void OnRefresh(object? sender, RoutedEventArgs args) { _viewport.ReloadArtwork(); _ = _model.LoadAsync(); }
    private void OnSearchKey(object? sender, KeyEventArgs args) { if (args.Key == Key.Enter) { _ = _model.LoadAsync(); args.Handled = true; } }
    private void OnClearSearch(object? sender, RoutedEventArgs args) { _model.Search = ""; FocusSearch(); }
    public async void OpenDetail(ArtistRow artist)
    {
        _details.OpenArtist(artist);
        if (await _panes.Show(DetailHost, Wall, MotionDirection.Forward) && !_closed) _viewport.SetSuspended(true);
    }
    private async void CloseDetail()
    {
        _viewport.SetSuspended(false);
        if (await _panes.Show(Wall, DetailHost, MotionDirection.Back) && !_closed) _details.Release();
    }
    public void SetWindowVisible(bool visible) { _details.SetWindowVisible(visible); _viewport.SetSuspended(!visible || DetailHost.IsVisible || CreditsHost.IsVisible); }
    public void Dispose() { if (_closed) return; _closed = true; if (_shell is not null) { if (_shell.ArtistLibrary is IArtistImportNotifications notifications) notifications.ArtistImportChanged -= ImportChanged; _shell.Theme.Changed -= ThemeChanged; _shell.Text.Changed -= RefreshArtistTitle; } _model.PropertyChanged -= ArtworkChanged; _panes.Dispose(); (CreditsHost.Content as IDisposable)?.Dispose(); _details.Dispose(); _viewport.Dispose(); _model.Dispose(); }
}
