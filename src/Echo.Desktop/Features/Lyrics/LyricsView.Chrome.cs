using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Echo.Desktop.Features.Lyrics;

public partial class LyricsView
{
    private IDisposable? hideHeader;
    private LyricsPlayerVisibility? playerVisibility;
    private bool pointerOverHeader;
    private bool wasPlaying;
    private void InitializeChrome()
    {
        playerVisibility = new(Transport, Model, shell.Motion, () => shell.IsQuickSettingsOpen || shell.IsQueueDrawerOpen);
        Model.Appearance.PropertyChanged += OnChromeAppearance;
        shell.PropertyChanged += OnShellTheme;
        shell.Motion.PropertyChanged += OnChromeMotion;
        Model.Changed += OnChromePlayback;
        Transport.InteractionChanged += OnTransportInteraction;
        AddHandler(PointerMovedEvent, OnStagePointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        PointerEntered += OnStagePointer;
        PointerExited += OnStagePointerLeft;
        KeyDown += OnChromeKey;
        SizeChanged += (_, _) => { Transport.Fit(Bounds.Width); playerVisibility.Refresh(); };
        ChromeFooter.Margin = new Thickness(0, 0, 0, Model.Appearance.MiniPlayer ? 22 : 0);
        ApplyChromeTheme(); ShowHeader(); playerVisibility.Reveal();
    }
    private void OnChromeAppearance(object? sender, PropertyChangedEventArgs e)
    {
        ApplyChromeTheme(); Transport.Fit(Bounds.Width);
        ChromeFooter.Margin = new Thickness(0, 0, 0, Model.Appearance.MiniPlayer ? 22 : 0);
        ShowHeader(); playerVisibility?.Refresh();
    }
    private void OnShellTheme(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Shell.ShellViewModel.IsDark)) ApplyChromeTheme();
        if (e.PropertyName is nameof(Shell.ShellViewModel.IsQuickSettingsOpen) or nameof(Shell.ShellViewModel.IsQueueDrawerOpen))
        { ShowHeader(); playerVisibility?.Refresh(); }
    }
    private void ApplyChromeTheme()
    {
        var a = Model.Appearance;
        var color = a.IsAfterglow ? a.SelectedLook.Ink : a.SelectedStyle.Ink;
        var dark = Echo.Desktop.Controls.Lyrics.LyricsBackgroundInk.ChromeDark(a,shell.IsDark) ?? (color.Length > 0 ? Brightness(Color.Parse(color)) > 0.6 : shell.IsDark);
        ChromeTheme.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Transport.ApplyPalette(dark);
        ChromeHeader.Transitions = ChromeTransitions();
        Transport.ApplyMotion();
    }
    private Transitions ChromeTransitions() => [new DoubleTransition
    { Property = OpacityProperty, Duration = shell.Motion.CanAnimate ? shell.Motion.Duration(TimeSpan.FromMilliseconds(180)) : TimeSpan.Zero }];
    private static double Brightness(Color color) => (color.R * 0.2126 + color.G * 0.7152 + color.B * 0.0722) / 255;
    private void OnChromeMotion(object? sender, PropertyChangedEventArgs e)
    {
        ChromeHeader.Transitions = ChromeTransitions();
        Transport.ApplyMotion();
        if (!shell.Motion.IsWindowVisible) { hideHeader?.Dispose(); hideHeader = null; }
        else ShowHeader();
        playerVisibility?.Refresh();
    }
    private void OnChromePlayback()
    {
        var changed = wasPlaying != Model.IsPlaying; wasPlaying = Model.IsPlaying;
        if (changed) { ShowHeader(); playerVisibility?.Reveal(); }
    }
    private void OnStagePointer(object? sender, PointerEventArgs e)
    {
        pointerOverHeader = e.GetPosition(this).Y < 68;
        ShowHeader(); playerVisibility?.PointerMoved(e.GetPosition(Transport));
    }
    private void OnStagePointerLeft(object? sender, PointerEventArgs e)
    {
        pointerOverHeader = false; playerVisibility?.PointerLeft();
    }
    private void OnChromeKey(object? sender, KeyEventArgs e) { ShowHeader(); playerVisibility?.Reveal(); }
    private void OnTransportInteraction() => playerVisibility?.Refresh();
    private void ShowHeader()
    {
        if (closed) return;
        hideHeader?.Dispose(); hideHeader = null;
        ChromeHeader.Opacity = 1; ChromeHeader.IsHitTestVisible = true;
        if (!Model.IsPlaying || !shell.Motion.IsWindowVisible) return;
        hideHeader = DispatcherTimer.RunOnce(() =>
        {
            hideHeader = null;
            if (closed || !Model.IsPlaying || pointerOverHeader || ChromeHeader.IsKeyboardFocusWithin || shell.IsQuickSettingsOpen || shell.IsQueueDrawerOpen) return;
            ChromeHeader.Opacity = 0; ChromeHeader.IsHitTestVisible = false;
        }, TimeSpan.FromSeconds(2.6));
    }
    private void OnToggleFullscreen(object? sender, RoutedEventArgs e)
    { if (TopLevel.GetTopLevel(this) is Window w) w.WindowState = w.WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen; }
    private void OnMinimize(object? sender, RoutedEventArgs e) { if (TopLevel.GetTopLevel(this) is Window w) w.WindowState = WindowState.Minimized; }
    private void OnMaximize(object? sender, RoutedEventArgs e)
    { if (TopLevel.GetTopLevel(this) is Window w) w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }
    private void OnClose(object? sender, RoutedEventArgs e) { if (TopLevel.GetTopLevel(this) is Window w) w.Close(); }
    private void DisposeChrome()
    {
        hideHeader?.Dispose(); hideHeader = null; playerVisibility?.Dispose(); playerVisibility = null;
        Model.Appearance.PropertyChanged -= OnChromeAppearance; shell.PropertyChanged -= OnShellTheme;
        shell.Motion.PropertyChanged -= OnChromeMotion; Model.Changed -= OnChromePlayback; RemoveHandler(PointerMovedEvent, OnStagePointer);
        Transport.InteractionChanged -= OnTransportInteraction;
        PointerEntered -= OnStagePointer; PointerExited -= OnStagePointerLeft; KeyDown -= OnChromeKey;
    }
}
