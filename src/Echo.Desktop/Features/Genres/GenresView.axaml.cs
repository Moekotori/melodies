using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Echo.Contracts.Library;
using Echo.Desktop.Features.LibraryDetails;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Genres;

public partial class GenresView : UserControl, IDisposable
{
    private readonly GenresViewModel _model;
    private readonly DetailNavigator _details;
    private readonly GenreWallViewport _viewport;
    private readonly MotionPanes _panes = new();
    public GenresViewModel Model => _model;
    public GenreWallViewport Viewport => _viewport;
    private bool _closed;
    public GenresView() { InitializeComponent(); _model = null!; _details = null!; _viewport = null!; }
    public GenresView(ShellViewModel shell) : this()
    {
        _model = new(shell.GenreLibrary!, shell.QueryPreference("genres"), shell.Text); DataContext = _model;
        _model.PreferenceChanged += () => { try { shell.RememberQuery(_model.Preference); } catch (ArgumentException) { } };
        _details = new(shell); _details.Back += CloseDetail; DetailHost.Content = _details;
        _viewport = new(_model, shell.AlbumArtwork!); ViewportHost.Content = _viewport;
        _viewport.OpenGenre += OpenDetail;
        AttachedToVisualTree += (_, _) => _ = _model.LoadAsync();
        SizeChanged += (_, _) => ArrangeToolbar();
        DetachedFromVisualTree += (_, _) => Dispose();
    }
    private void ArrangeToolbar()
    {
        var narrow = Bounds.Width < 660;
        Toolbar.RowDefinitions = new(narrow ? "Auto,Auto" : "Auto");
        Toolbar.ColumnDefinitions = new(narrow ? "*" : "*,Auto");
        var controls = Toolbar.Children[1];
        Grid.SetRow(controls, narrow ? 1 : 0); Grid.SetColumn(controls, narrow ? 0 : 1);
        controls.Margin = narrow ? new(0, 10, 0, 0) : default;
        SearchArea.Width = Math.Clamp(Bounds.Width - (narrow ? 0 : Math.Max(230, controls.DesiredSize.Width + 24)), 120, 520);
    }
    private void OnSelectSort(object? sender, RoutedEventArgs args)
    {
        if (sender is Button button && int.TryParse(button.Tag?.ToString(), out var sort))
        { _model.SortIndex = sort; FilterButton.Flyout?.Hide(); FilterButton.Focus(); }
    }
    private void OnLocalSource(object? sender, RoutedEventArgs args)
    { FilterButton.Flyout?.Hide(); FilterButton.Focus(); }
    private void OnToggleDirection(object? sender, RoutedEventArgs args) => _model.Descending = !_model.Descending;
    public void FocusSearch() => SearchField.Focus();
    private void OnRefresh(object? sender, RoutedEventArgs args) { _viewport.ReloadArtwork(); _ = _model.LoadAsync(); }
    private void OnSearchKey(object? sender, KeyEventArgs args) { if (args.Key == Key.Enter) { _ = _model.LoadAsync(); args.Handled = true; } }
    private void OnClearSearch(object? sender, RoutedEventArgs args) { _model.Search = ""; FocusSearch(); }
    public async void OpenDetail(GenreRow genre)
    {
        _details.OpenGenre(genre);
        if (await _panes.Show(DetailHost, Wall, MotionDirection.Forward) && !_closed) _viewport.SetSuspended(true);
    }
    private async void CloseDetail()
    {
        _viewport.SetSuspended(false);
        if (await _panes.Show(Wall, DetailHost, MotionDirection.Back) && !_closed) _details.Release();
    }
    public void SetWindowVisible(bool visible) { _details.SetWindowVisible(visible); _viewport.SetSuspended(!visible || DetailHost.IsVisible); }
    public void Dispose() { if (_closed) return; _closed = true; _panes.Dispose(); _details.Dispose(); _viewport.Dispose(); _model.Dispose(); }
}
