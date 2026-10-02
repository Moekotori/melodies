using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Echo.Desktop.Motion;
using Echo.Desktop.Features.Home;
using Echo.Desktop.Features.Settings;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.Songs;
using Echo.Desktop.Features.Search;
using Echo.Desktop.Features.Artists;
using Echo.Desktop.Features.Folders;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Features.Queue;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Shell;

public partial class MainWindow : Window
{
    private ShellViewModel? _viewModel;
    private WindowLocalizationBinding? _localization;
    private Echo.Desktop.Bootstrap.WindowThemeBinding? _theme;
    private readonly MotionLane _layoutMotion = new();
    private LibrarySearchView? _searchPage;
    private SongsView? _songsPage;
    private AlbumsView? _albumsPage;
    private ArtistsView? _artistsPage;
    private FoldersView? _foldersPage;
    private LyricsView? _lyricsPage;
    private string _previousRoute = "home";

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += (_, _) => { ReleaseLyricsPage(); _layoutMotion.Dispose(); DetachViewModel(); };
        Closing += (_, _) => PageHost.Settle();
        Activated += (_, _) => _lyricsPage?.SetFocused(true);
        Deactivated += (_, _) => _lyricsPage?.SetFocused(false);
        KeyDown += OnWindowKeyDown;
        AddHandler(KeyDownEvent, CaptureShortcut, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, CaptureShortcutMouse, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        PointerPressed += OnShortcutMouse;
        KeyUp += (_, e) => _shortcutKeysDown.Remove(e.Key);
        Deactivated += (_, _) => { _shortcutKeysDown.Clear(); _viewModel?.Shortcuts.CancelRecording(); };
        Opened += (_, _) => PageHost.Reveal();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty || args.Property == WindowStateProperty)
                UpdateMotionVisibility();
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        DetachViewModel();
        _viewModel = DataContext as ShellViewModel;
        if (_viewModel is null) return;
        _viewModel.Shortcuts.SetCapabilities(SupportsShortcut, _viewModel.Shortcuts.GlobalAvailable, _viewModel.Shortcuts.PlatformHint);
        _localization = new WindowLocalizationBinding(this, _viewModel.Text);
        _theme = new Echo.Desktop.Bootstrap.WindowThemeBinding(this, _viewModel.Theme, _viewModel.Motion, dark => _viewModel.IsDark = dark);
        _viewModel.PropertyChanged += OnShellPropertyChanged;
        MotionScope.SetPolicy(this, _viewModel.Motion);
        _viewModel.Motion.PropertyChanged += OnMotionPolicyChanged;
        UpdateMotionVisibility();
        ApplyShellAppearance();
        ShowPage();
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShellViewModel.IsDark) && _theme?.IsApplying == false && _viewModel is not null)
            _viewModel.Theme.Mode = _viewModel.IsDark ? "深色" : "浅色";
        if (args.PropertyName is nameof(ShellViewModel.IsDark) or nameof(ShellViewModel.IsSidebarExpanded))
            ApplyShellAppearance();
        if (args.PropertyName is nameof(ShellViewModel.UiScalePercent) or nameof(ShellViewModel.SidebarReservedWidth)) ApplyShellAppearance();
        if (args.PropertyName == nameof(ShellViewModel.NavigationVersion)) ShowPage();
    }

    private void ApplyShellAppearance()
    {
        if (_viewModel is null) return;
        RequestedThemeVariant = _viewModel.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        var previous = ShellGrid.ColumnDefinitions[0].Width.Value;
        var width = _viewModel.SidebarReservedWidth;
        UiScaleHost.LayoutTransform = new Avalonia.Media.ScaleTransform(_viewModel.UiScalePercent / 100d, _viewModel.UiScalePercent / 100d);
        QuickSettingsScaleHost.LayoutTransform = UiScaleHost.LayoutTransform;
        ShellGrid.ColumnDefinitions[0].Width = new GridLength(width);
        if (previous != width)
        {
            // FLIP: calculate the target layout once; animate only a render translation.
            var offset = previous - width + MotionLane.Current(PageArea).Value.M31;
            _layoutMotion.Cancel();
            _ = _layoutMotion.PlayAsync(PageArea, 1, 1, MotionLane.At(x: offset), MotionLane.At(),
                _viewModel.Motion.UseSpatialMotion ? MotionTokens.Reveal : TimeSpan.Zero, preserveGeometry: true);
        }
    }

    private void DetachViewModel()
    {
        _theme?.Dispose(); _theme = null;
        _localization?.Dispose(); _localization = null;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnShellPropertyChanged;
            _viewModel.Motion.PropertyChanged -= OnMotionPolicyChanged;
        }
        _viewModel = null;
        MotionScope.SetPolicy(this, null);
        PageHost.Settle();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Handled) return;
        if (TryShortcut(args)) { args.Handled = true; return; }
        if (args.Key == Key.Escape && _viewModel?.IsQueueDrawerOpen == true)
        { _viewModel.CloseQueueDrawerCommand.Execute(null); args.Handled = true; return; }
        if (args.Key == Key.Escape && _viewModel?.ActiveRouteId == "lyrics")
        { _viewModel.NavigateCommand.Execute(_previousRoute); args.Handled = true; return; }
        if (args.Key == Key.Escape) { _viewModel?.CloseQueueDrawerCommand.Execute(null); _viewModel?.DismissNoticeCommand.Execute(null); }
        if (args.Key == Key.F11) WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
    }

    private void ShowPage()
    {
        if (_viewModel is null) return;
        if (ShowStandaloneLyrics()) return;
        ApplyShellAppearance();
        ReleaseLibraryPage(retiring: true);
        if (_viewModel.ActiveRouteId == "settings")
        {
            ReleaseLyricsPage();
            _previousRoute = "settings";
            PageHost.Content = new SettingsView(_viewModel.Settings);
            return;
        }
        ReleaseLyricsPage();
        Control page = _viewModel.ActiveRouteId switch
        {
            "home" => new HomeView(),
            "queue" or "history" when _viewModel.Player is not null => new QueueView(_viewModel.Player, _viewModel.Text, _viewModel.ActiveRouteId == "history"),
            "search" when _viewModel.TrackLibrary is not null => _searchPage = new LibrarySearchView(_viewModel),
            "songs" when _viewModel.TrackLibrary is not null => _songsPage = new SongsView(_viewModel),
            "albums" when _viewModel.AlbumLibrary is not null => _albumsPage = new AlbumsView(_viewModel),
            "artists" when _viewModel.ArtistLibrary is not null => _artistsPage = new ArtistsView(_viewModel),
            "folders" when _viewModel.FolderLibrary is not null => _foldersPage = new FoldersView(_viewModel),
            _ => new PendingPage(_viewModel.ActivePageTitle, _viewModel.ActiveRouteId)
        };
        page.Margin = new Thickness(42, 24, 42, 28);
        if (_viewModel.ActiveRouteId != "lyrics") _previousRoute = _viewModel.ActiveRouteId;
        if (page is AlbumsView or ArtistsView or SongsView or LibrarySearchView or FoldersView)
        { page.Margin = new Thickness(42, 24, 42, 0); PageHost.Content = page; return; }
        if (page is QueueView) { PageHost.Content = page; return; }
        PageHost.Content = new ScrollViewer
        {
            Content = page, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
    }
    public void ReleaseLibraryPage(bool retiring = false)
    {
        // Retiring pages keep their artwork until visual detach, which already owns Dispose.
        if (!retiring) { _songsPage?.Dispose(); _searchPage?.Dispose(); _albumsPage?.Dispose(); _artistsPage?.Dispose(); _foldersPage?.Dispose(); }
        _songsPage = null; _searchPage = null;
        _albumsPage = null; _artistsPage = null; _foldersPage = null;
    }
    public void ReleaseLyricsPage() { _lyricsPage?.Dispose(); _lyricsPage = null; }
    private void UpdateMotionVisibility()
    {
        var visible = IsVisible && WindowState != WindowState.Minimized;
        if (_viewModel is not null) _viewModel.Motion.IsWindowVisible = visible;
        _albumsPage?.SetWindowVisible(visible);
        _artistsPage?.SetWindowVisible(visible);
        _foldersPage?.SetWindowVisible(visible);
        _searchPage?.SetWindowVisible(visible);
    }
    private void OnMotionPolicyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(MotionPolicy.CanAnimate)) return;
        _layoutMotion.Cancel();
        PageArea.RenderTransform = MotionLane.At();
    }
}
