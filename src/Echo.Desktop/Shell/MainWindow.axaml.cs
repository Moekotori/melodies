using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Echo.Desktop.Motion;
using Echo.Desktop.Features.Home;
using Echo.Desktop.Features.Settings;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Features.Queue;

namespace Echo.Desktop.Shell;

public partial class MainWindow : Window
{
    private ShellViewModel? _viewModel;
    private readonly MotionLane _layoutMotion = new();
    private AlbumsView? _albumsPage;
    private LyricsView? _lyricsPage;
    private string _previousRoute = "home";

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += (_, _) => { ReleaseLyricsPage(); _layoutMotion.Dispose(); DetachViewModel(); };
        Activated += (_, _) => _lyricsPage?.SetFocused(true);
        Deactivated += (_, _) => _lyricsPage?.SetFocused(false);
        KeyDown += OnWindowKeyDown;
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
        _viewModel.PropertyChanged += OnShellPropertyChanged;
        MotionScope.SetPolicy(this, _viewModel.Motion);
        _viewModel.Motion.PropertyChanged += OnMotionPolicyChanged;
        UpdateMotionVisibility();
        ApplyShellAppearance();
        ShowPage();
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
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
        ShellGrid.ColumnDefinitions[0].Width = new GridLength(width);
        if (previous != width)
        {
            // FLIP: calculate the target layout once; animate only a render translation.
            var offset = previous - width + MotionLane.Current(PageArea).Value.M31;
            _layoutMotion.Cancel();
            _ = _layoutMotion.PlayAsync(PageArea, 1, 1, MotionLane.At(x: offset), MotionLane.At(),
                _viewModel.Motion.UseSpatialMotion ? MotionTokens.Reveal : TimeSpan.Zero);
        }
    }

    private void DetachViewModel()
    {
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
        if (args.Key == Key.Escape && _viewModel?.ActiveRouteId == "lyrics")
        { _viewModel.NavigateCommand.Execute(_previousRoute); args.Handled = true; return; }
        if (args.Key == Key.Escape) { _viewModel?.CloseQueueDrawerCommand.Execute(null); _viewModel?.DismissNoticeCommand.Execute(null); }
        if (args.Key == Key.Q && args.KeyModifiers.HasFlag(KeyModifiers.Control))
        { _viewModel?.ToggleQueueDrawerCommand.Execute(null); args.Handled = true; }
        if (args.Key == Key.F11) WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
        if ((args.Key is Key.F or Key.K) && args.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (_albumsPage is not null) _albumsPage.FocusSearch();
            else _viewModel?.ShowPendingCommand.Execute("全局搜索");
            args.Handled = true;
        }
    }

    private void ShowPage()
    {
        if (_viewModel is null) return;
        if (ShowStandaloneLyrics()) return;
        ApplyShellAppearance();
        ReleaseLibraryPage();
        if (_viewModel.ActiveRouteId == "settings")
        {
            ReleaseLyricsPage();
            _previousRoute = "settings";
            PageHost.Content = new SettingsView(_viewModel.Settings);
            return;
        }
        ReleaseLyricsPage();
        if (_viewModel.Player is not null) _viewModel.Player.Queue.ShowHistory = _viewModel.ActiveRouteId == "history";
        Control page = _viewModel.ActiveRouteId switch
        {
            "home" => new HomeView(),
            "queue" or "history" when _viewModel.Player is not null => new QueueView { DataContext = _viewModel.Player },
            "albums" when _viewModel.AlbumLibrary is not null => _albumsPage = new AlbumsView(_viewModel),
            "lyrics" when _viewModel.LyricsSource is not null => _lyricsPage = new LyricsView(_viewModel, _previousRoute),
            _ => new PendingPage(_viewModel.ActivePageTitle, _viewModel.ActiveRouteId)
        };
        page.Margin = new Thickness(42, 24, 42, 28);
        if (_viewModel.ActiveRouteId != "lyrics") _previousRoute = _viewModel.ActiveRouteId;
        if (page is LyricsView)
        { page.Margin = default; PageHost.Content = page; return; }
        if (page is AlbumsView)
        { page.Margin = new Thickness(42, 24, 42, 0); PageHost.Content = page; return; }
        if (page is QueueView) { PageHost.Content = page; return; }
        PageHost.Content = new ScrollViewer
        {
            Content = page, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
    }
    public void ReleaseLibraryPage() { _albumsPage?.Dispose(); _albumsPage = null; }
    public void ReleaseLyricsPage() { _lyricsPage?.Dispose(); _lyricsPage = null; }
    private void UpdateMotionVisibility()
    {
        var visible = IsVisible && WindowState != WindowState.Minimized;
        if (_viewModel is not null) _viewModel.Motion.IsWindowVisible = visible;
        _albumsPage?.SetWindowVisible(visible);
    }
    private void OnMotionPolicyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(MotionPolicy.CanAnimate)) return;
        _layoutMotion.Cancel();
        PageArea.RenderTransform = MotionLane.At();
    }
}
