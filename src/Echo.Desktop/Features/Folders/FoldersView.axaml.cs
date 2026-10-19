using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Echo.Contracts.Library;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Folders;

public partial class FoldersView : UserControl, IDisposable
{
    private ShellViewModel _shell = null!;
    private bool _closed;
    private bool _visible = true;
    private FolderWallViewport? _wall;
    public FoldersViewModel Model { get; private set; } = null!;
    public static readonly StyledProperty<Echo.Desktop.Features.Albums.AlbumArtworkCache?> ArtworkProperty =
        AvaloniaProperty.Register<FoldersView, Echo.Desktop.Features.Albums.AlbumArtworkCache?>(nameof(Artwork));
    public Echo.Desktop.Features.Albums.AlbumArtworkCache? Artwork
    { get => GetValue(ArtworkProperty); private set => SetValue(ArtworkProperty, value); }
    public FoldersView() { InitializeComponent(); }
    public FoldersView(ShellViewModel shell) : this()
    {
        _shell = shell; Model = new(shell); DataContext = Model;
        Artwork = shell.AlbumArtwork;
        _wall = new(Model.Wall, shell.AlbumArtwork, Model.Text);
        _wall.Open += node => Model.SelectedNode = node;
        WallHost.Content = _wall;
        Model.Wall.Changed += ApplyLayout;
        Model.PropertyChanged += OnModelChanged;
        AttachedToVisualTree += (_, _) => _ = Model.InitializeAsync();
        DetachedFromVisualTree += (_, _) => Dispose();
        SizeChanged += (_, _) => ApplyLayout();
        MainPane.SizeChanged += (_, _) => ApplyLayout();
        HeaderActions.SizeChanged += (_, _) => ApplyLayout();
        KeyDown += (_, args) =>
        {
            if (args.Handled || args.Key != Key.Escape || !Model.HasFolder) return;
            if (SearchInput.IsVisible) SetSearchOpen(false, clear: true); else Model.BackFolderCommand.Execute(null);
            args.Handled = true;
        };
        ApplyLayout();
        ImportButton.IsEnabled = shell.ImportLibrary is not null;
        RescanButton.IsVisible = shell.ImportLibrary is not null;
    }
    private void ApplyLayout()
    {
        var narrow = Bounds.Width is > 0 and < 860;
        var home = !Model.HasFolder;
        MainPane.RowDefinitions[1].Height = home ? new(1, GridUnitType.Star) : GridLength.Auto;
        MainPane.RowDefinitions[4].Height = home ? new(0) : new(1, GridUnitType.Star);
        WallPane.IsVisible = home;

        _wall?.SetSuspended(!_visible || !WallPane.IsVisible);
        var coverSize = narrow ? 112 : 160;
        HeroSurface.Padding = new(narrow ? 18 : 24);
        Hero.ColumnDefinitions[0].Width = new(coverSize);
        CoverStack.Width = CoverStack.Height = coverSize;
        Hero.ColumnSpacing = narrow ? 20 : 28;
        FolderTitle.FontSize = narrow ? 28 : 40;
        SearchInput.Width = Math.Clamp(Bounds.Width * .34, 160, 280);
        BreadcrumbBar.MaxWidth = Math.Max(80, Bounds.Width - HeaderActions.Bounds.Width - 56);
        // Keep search and sort usable in the minimum desktop window.
        LosslessToggle.IsVisible = !narrow;
        FilterBar.ColumnDefinitions[1].Width = new(narrow ? 0 : 1, narrow ? Avalonia.Controls.GridUnitType.Pixel : Avalonia.Controls.GridUnitType.Auto);
    }
    public void FocusSearch() { if (Model.HasFolder) SetSearchOpen(true); else WallHost.Focus(); }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(FoldersViewModel.CurrentFolder)) { _coverSamples = []; ReleaseArtwork(); SetSearchOpen(Model.HasFolder && Model.Search.Length > 0, focus: false); ApplyLayout(); }
        if (args.PropertyName == nameof(FoldersViewModel.Search) && Model.Search.Length > 0) SetSearchOpen(true, focus: false);
        if (args.PropertyName == nameof(FoldersViewModel.Tracks)) _ = LoadArtworkAsync();
    }
    private void OnSearchKey(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape) { SetSearchOpen(false, clear: true); args.Handled = true; }
        else if (args.Key == Key.Enter) { _ = Model.LoadAsync(); args.Handled = true; }
    }
    private void OnToggleSearch(object? sender, RoutedEventArgs args) => SetSearchOpen(SearchToggle.IsChecked == true, clear: SearchToggle.IsChecked != true);
    private void SetSearchOpen(bool open, bool clear = false, bool focus = true)
    {
        SearchInput.IsVisible = open; SearchToggle.IsChecked = open;
        if (clear) Model.Search = "";
        if (open && focus) { SearchField.Focus(); SearchField.SelectAll(); }
        else if (!open && focus) SearchToggle.Focus();
    }
    private void OnClearSearch(object? sender, RoutedEventArgs args) { Model.Search = ""; FocusSearch(); }
    private void OnBreadcrumb(object? sender, RoutedEventArgs args)
    { if (sender is Button { Tag: FolderRow row }) Model.SelectBreadcrumb(row); }
    private void OnOpenFolder(object? sender, RoutedEventArgs args) => _ = Model.OpenFolderAsync();
    private async void OnCopyPath(object? sender, RoutedEventArgs args)
    {
        if (Model.CurrentFolder is null || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        try { await clipboard.SetValueAsync(DataFormat.Text, Model.CurrentFolder.Path); }
        catch (Exception) { if (!_closed) Model.Error = Model.Text["folders.copyError"]; }
    }
    public void SetWindowVisible(bool visible)
    {
        _visible = visible;
        _wall?.SetSuspended(!visible || Model.HasFolder);
        Artwork = visible && !_closed ? _shell.AlbumArtwork : null;
        if (visible) _ = LoadArtworkAsync(); else ReleaseArtwork();
    }
    public void Dispose()
    {
        if (_closed) return;
        _closed = true; _scan?.Cancel(); Artwork = null; ReleaseArtwork();
        Model.Wall.Changed -= ApplyLayout; _wall?.Dispose(); _wall = null;
        Model.PropertyChanged -= OnModelChanged; Model.Dispose();
        TrackPanel.DataContext = null;
    }
}
