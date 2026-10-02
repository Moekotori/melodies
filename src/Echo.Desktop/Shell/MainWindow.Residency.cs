using Avalonia.Controls;
using Avalonia.VisualTree;
using Echo.Desktop.Bootstrap;
using Echo.Desktop.Motion;

namespace Echo.Desktop.Shell;

public partial class MainWindow
{
    private MainWindowSurface? _surface;
    private WindowResidencyBinding? _residency;
    public bool IsSurfaceResident => _surface is not null;
    public bool IsExitRequested { get; private set; }
    private LayoutTransformControl UiScaleHost => this.FindControl<LayoutTransformControl>("UiScaleHost")!;
    private LayoutTransformControl QuickSettingsScaleHost => this.FindControl<LayoutTransformControl>("QuickSettingsScaleHost")!;
    private Grid ShellGrid => this.FindControl<Grid>("ShellGrid")!;
    private Grid PageArea => this.FindControl<Grid>("PageArea")!;
    private MotionPresenter PageHost => this.FindControl<MotionPresenter>("PageHost")!;
    private ContentControl LyricsStageHost => this.FindControl<ContentControl>("LyricsStageHost")!;
    private LayoutTransformControl SearchScaleHost => this.FindControl<LayoutTransformControl>("SearchScaleHost")!;
    private Echo.Desktop.Features.Search.GlobalSearchOverlay GlobalSearchOverlay => this.FindControl<Echo.Desktop.Features.Search.GlobalSearchOverlay>("GlobalSearchOverlay")!;

    private void CreateSurface()
    {
        _surface = new MainWindowSurface();
        // Existing window-scoped consumers keep their control lookup entry point.
        NameScope.SetNameScope(this, NameScope.GetNameScope(_surface));
        Content = _surface;
        GlobalSearchOverlay.OpenRequested += OpenGlobalResult;
    }

    internal void UnloadSurface()
    {
        if (_surface is null) return;
        if (FocusManager?.GetFocusedElement() is Avalonia.Visual focused && _surface.IsVisualAncestorOf(focused))
            FocusManager.Focus(null);
        _viewModel?.Shortcuts.CancelRecording();
        _viewModel?.CloseQueueDrawerCommand.Execute(null);
        _viewModel?.CloseQuickSettingsCommand.Execute(null);
        _viewModel?.CloseSidebarDrawerCommand.Execute(null);
        _viewModel?.CloseGlobalSearchCommand.Execute(null);
        _viewModel?.Player?.AudioSettings.CancelPendingRead();
        _layoutMotion.Cancel();
        ReleaseLibraryPage(); ReleaseLyricsPage();
        PageHost.Content = null; PageHost.Settle();
        LyricsStageHost.Content = null;
        _appearanceVisuals?.Dispose(); _appearanceVisuals = null;
        _theme?.Dispose(); _theme = null;
        var retiring = _surface;
        Content = null;
        // The namescope holds controls too: dropping Content alone retains the tree.
        NameScope.SetNameScope(this, null);
        retiring.DataContext = null;
        _surface = null;
        _viewModel?.Player?.Queue.SetPresentationActive(false);
        _viewModel?.AlbumArtwork?.ClearUnused();
    }

    internal void RestoreSurface()
    {
        if (_surface is not null || _viewModel is null || IsExitRequested) return;
        try
        {
            CreateSurface();
            _theme = new WindowThemeBinding(this, _viewModel.Theme, _viewModel.Motion, dark => _viewModel.IsDark = dark);
            _appearanceVisuals = new(this, _viewModel);
            _viewModel.Player?.Queue.SetPresentationActive(true);
            UpdateMotionVisibility(); ApplyShellAppearance(); ShowPage();
            PageHost.Reveal();
        }
        catch
        {
            UnloadSurface();
            throw;
        }
    }

    public bool TryHideToTray() => _residency?.TryHideToTray() == true;
    public bool EnsureTrayRecovery() => _residency?.EnsureTray() == true;
    public void RestoreMainWindow()
    {
        if (IsExitRequested) return;
        try { RestoreSurface(); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException)
        { ShowRestoreFailure(); }
        ShowInTaskbar = true;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show(); Activate();
    }
    internal void ShowRestoreFailure()
    {
        if (_viewModel is null) return;
        var message = _viewModel.Text["ultralight.restoreFailed"];
        _viewModel.Settings.Feedback = message;
        var retry = new Button { Content = _viewModel.Text["ultralight.restore"] };
        retry.Click += (_, _) => RestoreMainWindow();
        var exit = new Button { Content = _viewModel.Text["ultralight.exit"] };
        exit.Click += (_, _) => RequestExit();
        Content = new StackPanel { Margin = new Avalonia.Thickness(32), Spacing = 16,
            Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, retry, exit } };
    }
    internal void BeginExit() { IsExitRequested = true; _residency?.Dispose(); }
    public void RequestExit() { BeginExit(); Close(); }
}
