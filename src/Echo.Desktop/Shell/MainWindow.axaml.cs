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
using Echo.Desktop.Features.Genres;
using Echo.Desktop.Features.Folders;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Features.Queue;
using Echo.Desktop.Features.Playlists;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Shell;

public partial class MainWindow : Window
{
    private Echo.Desktop.Bootstrap.WindowAppearanceVisualBinding? _appearanceVisuals;
    private ShellViewModel? _viewModel;
    private WindowLocalizationBinding? _localization;
    private Echo.Desktop.Bootstrap.WindowThemeBinding? _theme;
    private readonly MotionLane _layoutMotion = new();
    private LibrarySearchView? _searchPage;
    private SongsView? _songsPage;
    private Echo.Desktop.Features.History.HistoryView? _historyPage;
    private HomeView? _homePage;
    private AlbumsView? _albumsPage;
    private ArtistsView? _artistsPage;
    private GenresView? _genresPage;
    private FoldersView? _foldersPage;
    private PlaylistsView? _playlistsPage;
    private LyricsView? _lyricsPage;
    private string _previousRoute = "home";

    public MainWindow()
    {
        InitializeComponent();
        if (OperatingSystem.IsMacOS()) WindowDecorations = Avalonia.Controls.WindowDecorations.Full;
        InitializeSongDrop();
        CreateSurface();
        AddHandler(KeyDownEvent, OnGlobalSearchKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
        Closed += (_, _) => { _residency?.Dispose(); SettleLyricsStage(); ReleaseLyricsPage(); _layoutMotion.Dispose(); DetachViewModel(); };
        Closing += (_, args) =>
        {
            if (!IsExitRequested && args.CloseReason is not (WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown))
            {
                if (_viewModel?.HasCloseBehaviorPreference == true)
                {
                    if (_viewModel.CloseToTray) { args.Cancel = true; MinimizeToBackground(); }
                    else
                    {
                        args.Cancel = true;
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = StopPlaybackAndExitAsync());
                    }
                }
                else if (OperatingSystem.IsMacOS()) { args.Cancel = true; Hide(); }
                else if (_residency?.TryHideToTray() == true) args.Cancel = true;
                else if (NeedsPlaybackCloseChoice)
                {
                    args.Cancel = true;
                    ShowPlaybackCloseChoice();
                }
            }
            if (_surface is not null) PageHost.Settle();
        };
        Activated += (_, _) => _lyricsPage?.SetFocused(true);
        Deactivated += (_, _) => _lyricsPage?.SetFocused(false);
        KeyDown += OnWindowKeyDown;
        AddHandler(KeyDownEvent, CaptureShortcut, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, CaptureShortcutMouse, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        PointerPressed += OnShortcutMouse;
        KeyUp += (_, e) => _shortcutKeysDown.Remove(e.Key);
        Deactivated += (_, _) => { _shortcutKeysDown.Clear(); _viewModel?.Shortcuts.CancelRecording(); };
        Opened += (_, _) => { if (_surface is not null) PageHost.Reveal(); };
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
        if (_surface is null) CreateSurface();
        _viewModel.Shortcuts.SetCapabilities(SupportsShortcut, _viewModel.Shortcuts.GlobalAvailable, _viewModel.Shortcuts.PlatformHint);
        _localization = new WindowLocalizationBinding(this, _viewModel.Text);
        _theme = new Echo.Desktop.Bootstrap.WindowThemeBinding(this, _viewModel.Theme, _viewModel.Motion, dark => _viewModel.IsDark = dark);
        _appearanceVisuals = new(this, _viewModel);
        _viewModel.PropertyChanged += OnShellPropertyChanged;
        _viewModel.SongsImported += RefreshImportedSongs;
        MotionScope.SetPolicy(this, _viewModel.Motion);
        _viewModel.Motion.PropertyChanged += OnMotionPolicyChanged;
        UpdateMotionVisibility();
        ApplyShellAppearance();
        ShowPage();
        _residency = new(this, _viewModel);
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_surface is null) return;
        if (args.PropertyName == nameof(ShellViewModel.IsDark) && _theme?.IsApplying == false && _viewModel is not null)
            _viewModel.Theme.Mode = _viewModel.IsDark ? "深色" : "浅色";
        if (args.PropertyName is nameof(ShellViewModel.IsDark) or nameof(ShellViewModel.IsSidebarExpanded))
            ApplyShellAppearance();
        if (args.PropertyName is nameof(ShellViewModel.UiScalePercent) or nameof(ShellViewModel.SidebarReservedWidth)) ApplyShellAppearance();
        if (args.PropertyName == nameof(ShellViewModel.NavigationVersion)) NavigatePage();
        if (args.PropertyName == nameof(ShellViewModel.ConnectLaunch) && PageHost.Content is Echo.Desktop.Features.Connect.ConnectView connect)
            connect.ApplyLaunchRequest();
    }

    private void ApplyShellAppearance()
    {
        if (_viewModel is null || _surface is null) return;
        RequestedThemeVariant = _viewModel.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        var previous = ShellGrid.ColumnDefinitions[0].Width.Value;
        var width = _viewModel.SidebarReservedWidth;
        UiScaleHost.LayoutTransform = new Avalonia.Media.ScaleTransform(_viewModel.UiScalePercent / 100d, _viewModel.UiScalePercent / 100d);
        QuickSettingsScaleHost.LayoutTransform = UiScaleHost.LayoutTransform;
        SearchScaleHost.LayoutTransform = UiScaleHost.LayoutTransform;
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
        _residency?.Dispose(); _residency = null;
        _appearanceVisuals?.Dispose(); _appearanceVisuals = null;
        _theme?.Dispose(); _theme = null;
        _localization?.Dispose(); _localization = null;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnShellPropertyChanged;
            _viewModel.SongsImported -= RefreshImportedSongs;
            _viewModel.CancelSongImportCommand.Execute(null);
            _viewModel.Motion.PropertyChanged -= OnMotionPolicyChanged;
        }
        _viewModel = null;
        MotionScope.SetPolicy(this, null);
        if (_surface is not null) PageHost.Settle();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Handled) return;
        if (_viewModel?.IsSetupOpen == true) return;
        if (TryShortcut(args)) { args.Handled = true; return; }
        if (args.Key == Key.Escape && _viewModel?.IsQueueDrawerOpen == true)
        { _viewModel.CloseQueueDrawerCommand.Execute(null); args.Handled = true; return; }
        if (args.Key == Key.Escape && _viewModel?.ActiveRouteId == "lyrics")
        { _viewModel.NavigateCommand.Execute(_previousRoute); args.Handled = true; return; }
        if (args.Key == Key.Escape) { _viewModel?.CloseQueueDrawerCommand.Execute(null); _viewModel?.DismissNoticeCommand.Execute(null); }
        if (args.Key == Key.F11) WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
    }

    private void ShowPage(MotionDirection direction = MotionDirection.None)
    {
        if (_viewModel is null || _surface is null) return;
        ++_pageRequest;
        _shownRoute = _viewModel.ActiveRouteId;
        if (ShowStandaloneLyrics()) return;
        ApplyShellAppearance();
        ReleaseLibraryPage(retiring: true);
        if (_viewModel.ActiveRouteId == "settings")
        {
            ReleaseLyricsPage();
            _previousRoute = "settings";
            PageHost.Present(new SettingsView(_viewModel.Settings), direction);
            return;
        }
        ReleaseLyricsPage();
        Control page = _viewModel.ActiveRouteId switch
        {
            "home" => _homePage = new HomeView(_viewModel),
            "radio" => new Echo.Desktop.Features.Radio.RadioView(_viewModel),
            "podcasts" => new Echo.Desktop.Features.Podcasts.PodcastView(_viewModel),
            "connect" => new Echo.Desktop.Features.Connect.ConnectView(_viewModel),
            "dsp" => new Echo.Desktop.Features.Dsp.DspView(_viewModel),
            "remote" when _viewModel.RemoteSources is not null => new Echo.Desktop.Features.Remote.RemoteView(_viewModel),
            "queue" when _viewModel.Player is not null => new QueueView(_viewModel.Player, _viewModel.Text, false),
            "history" when _viewModel.HistoryLibrary is not null => _historyPage = new Echo.Desktop.Features.History.HistoryView(_viewModel),
            "search" when _viewModel.TrackLibrary is not null => _searchPage = new LibrarySearchView(_viewModel),
            "songs" when _viewModel.TrackLibrary is not null => _songsPage = new SongsView(_viewModel),
            "albums" when _viewModel.AlbumLibrary is not null => _albumsPage = new AlbumsView(_viewModel),
            "artists" when _viewModel.ArtistLibrary is not null => _artistsPage = new ArtistsView(_viewModel),
            "genres" when _viewModel.GenreLibrary is not null => _genresPage = new GenresView(_viewModel),
            "folders" when _viewModel.FolderLibrary is not null => _foldersPage = new FoldersView(_viewModel),
            "playlists" when _viewModel.PlaylistLibrary is not null => _playlistsPage = new PlaylistsView(_viewModel),
            _ => new PendingPage(_viewModel.ActivePageTitle, _viewModel.ActiveRouteId)
        };
        page.Margin = page switch
        {
            Echo.Desktop.Features.Dsp.DspView or Echo.Desktop.Features.Remote.RemoteView or Echo.Desktop.Features.Radio.RadioView
                or Echo.Desktop.Features.Podcasts.PodcastView or Echo.Desktop.Features.Connect.ConnectView or PlaylistsView => default,
            SongsView => new Thickness(42, 24, 0, 0),
            HomeView or AlbumsView or ArtistsView or GenresView or LibrarySearchView or FoldersView or Echo.Desktop.Features.History.HistoryView
                => new Thickness(42, 24, 42, 0),
            _ => (Thickness)this.FindResource("EchoPageInset")!
        };
        if (_viewModel.ActiveRouteId != "lyrics") _previousRoute = _viewModel.ActiveRouteId;
        // Only the home dashboard and placeholder pages scroll as a whole; the rest own their viewport.
        PageHost.Present(page is not (HomeView or PendingPage) ? page : new ScrollViewer
        {
            Content = page, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        }, direction);
    }
    public void ReleaseLibraryPage(bool retiring = false)
    {
        _homePage?.SetWindowVisible(false); if (!retiring) _homePage?.Dispose(); _homePage = null;
        // Retiring pages keep their artwork until visual detach, which already owns Dispose.
        if (!retiring) _historyPage?.Dispose();
        _historyPage = null;
        if (!retiring) { _songsPage?.Dispose(); _searchPage?.Dispose(); _albumsPage?.Dispose(); _artistsPage?.Dispose(); _genresPage?.Dispose(); _foldersPage?.Dispose(); _playlistsPage?.Dispose(); }
        _songsPage = null; _searchPage = null;
        _albumsPage = null; _artistsPage = null; _genresPage = null; _foldersPage = null;
        _playlistsPage = null;
    }
    public void ReleaseLyricsPage() { _lyricsPage?.Dispose(); _lyricsPage = null; }
    private void UpdateMotionVisibility()
    {
        var visible = IsVisible && WindowState != WindowState.Minimized;
        if (_viewModel is not null) _viewModel.Motion.IsWindowVisible = visible;
        _homePage?.SetWindowVisible(visible);
        _albumsPage?.SetWindowVisible(visible);
        _artistsPage?.SetWindowVisible(visible);
        _genresPage?.SetWindowVisible(visible);
        _foldersPage?.SetWindowVisible(visible);
        _playlistsPage?.SetWindowVisible(visible);
        _searchPage?.SetWindowVisible(visible);
    }
    private void OnMotionPolicyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(MotionPolicy.CanAnimate)) return;
        _layoutMotion.Cancel();
        if (_surface is null) return;
        PageArea.RenderTransform = MotionLane.At();
    }
}
