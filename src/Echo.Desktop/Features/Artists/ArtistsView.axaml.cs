using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Echo.Contracts.Library;
using Echo.Desktop.Features.LibraryDetails;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Artists;

public partial class ArtistsView : UserControl, IDisposable
{
    private readonly ArtistsViewModel _model;
    private readonly DetailNavigator _details;
    private readonly ArtistWallViewport _viewport;
    public ArtistsViewModel Model => _model;
    public ArtistWallViewport Viewport => _viewport;
    private ShellViewModel? _shell;
    private bool _closed;
    public ArtistsView() { InitializeComponent(); _model = null!; _details = null!; _viewport = null!; }
    public ArtistsView(ShellViewModel shell) : this()
    {
        _shell = shell; shell.Theme.Changed += ThemeChanged;
        _model = new(shell.ArtistLibrary!, shell.QueryPreference("artists")); DataContext = _model;
        _model.PreferenceChanged += () => { try { shell.RememberQuery(_model.Preference); } catch (ArgumentException) { } };
        _details = new(shell); _details.Back += CloseDetail; DetailHost.Content = _details;
        _viewport = new(_model, shell.AlbumArtwork!); ViewportHost.Content = _viewport;
        _viewport.OpenArtist += OpenDetail; ThemeChanged();
        _model.PropertyChanged += ArtworkChanged;
        AttachedToVisualTree += (_, _) => _ = _model.LoadAsync();
        DetachedFromVisualTree += (_, _) => Dispose();
    }
    private void ThemeChanged() { if (_shell is not null) _model.ShowAlbumArtwork = _shell.Theme.ArtistAlbumArtwork; }
    private void ArtworkChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) { if (args.PropertyName == nameof(ArtistsViewModel.ShowAlbumArtwork) && _shell is not null) _shell.Theme.ArtistAlbumArtwork = _model.ShowAlbumArtwork; }
    public void FocusSearch() => SearchField.Focus();
    private void OnRefresh(object? sender, RoutedEventArgs args) { _viewport.ReloadArtwork(); _ = _model.LoadAsync(); }
    private void OnSearchKey(object? sender, KeyEventArgs args) { if (args.Key == Key.Enter) { _ = _model.LoadAsync(); args.Handled = true; } }
    private void OnClearSearch(object? sender, RoutedEventArgs args) { _model.Search = ""; FocusSearch(); }
    public void OpenDetail(ArtistRow artist) { _details.OpenArtist(artist); Wall.IsVisible = false; DetailHost.IsVisible = true; _viewport.SetSuspended(true); }
    private void CloseDetail() { DetailHost.IsVisible = false; Wall.IsVisible = true; _viewport.SetSuspended(false); }
    public void SetWindowVisible(bool visible) { _details.SetWindowVisible(visible); _viewport.SetSuspended(!visible || DetailHost.IsVisible); }
    public void Dispose() { if (_closed) return; _closed = true; if (_shell is not null) _shell.Theme.Changed -= ThemeChanged; _model.PropertyChanged -= ArtworkChanged; _details.Dispose(); _viewport.Dispose(); _model.Dispose(); }
}
