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
    private IDisposable? hideChrome;
    private bool pointerOverChrome;
    private bool wasPlaying;
    private void InitializeChrome()
    {
        Model.Appearance.PropertyChanged += OnChromeAppearance;
        shell.PropertyChanged += OnShellTheme;
        shell.Motion.PropertyChanged += OnChromeMotion;
        Model.Changed += OnChromePlayback;
        Transport.InteractionChanged += OnTransportInteraction;
        AddHandler(PointerMovedEvent, OnStagePointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        PointerEntered += (_, _) => ShowChrome();
        KeyDown += (_, _) => ShowChrome();
        SizeChanged += (_, _) => Transport.Fit(Bounds.Width);
        ApplyChromeTheme(); ShowChrome();
    }
    private void OnChromeAppearance(object? sender, PropertyChangedEventArgs e) { ApplyChromeTheme(); Transport.Fit(Bounds.Width); ShowChrome(); }
    private void OnShellTheme(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Shell.ShellViewModel.IsDark)) ApplyChromeTheme();
        if (e.PropertyName is nameof(Shell.ShellViewModel.IsQuickSettingsOpen) or nameof(Shell.ShellViewModel.IsQueueDrawerOpen)) ShowChrome();
    }
    private void ApplyChromeTheme()
    {
        var a = Model.Appearance;
        var color = a.IsAfterglow ? a.SelectedLook.Ink : a.SelectedStyle.Ink;
        var dark = color.Length > 0 ? Brightness(Color.Parse(color)) > 0.6 : shell.IsDark;
        ChromeTheme.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Transport.ApplyPalette(dark);
        ChromeHeader.Transitions = ChromeTransitions(); ChromeFooter.Transitions = ChromeTransitions();
        Transport.ApplyMotion();
    }
    private Transitions ChromeTransitions() => [new DoubleTransition
    { Property = OpacityProperty, Duration = shell.Motion.CanAnimate ? TimeSpan.FromMilliseconds(180) : TimeSpan.Zero }];
    private static double Brightness(Color color) => (color.R * 0.2126 + color.G * 0.7152 + color.B * 0.0722) / 255;
    private void OnChromeMotion(object? sender, PropertyChangedEventArgs e)
    {
        ChromeHeader.Transitions = ChromeTransitions(); ChromeFooter.Transitions = ChromeTransitions();
        Transport.ApplyMotion();
        if (!shell.Motion.IsWindowVisible) { hideChrome?.Dispose(); hideChrome = null; }
        else ShowChrome();
    }
    private void OnChromePlayback()
    {
        var changed = wasPlaying != Model.IsPlaying; wasPlaying = Model.IsPlaying;
        if (changed) ShowChrome();
    }
    private void OnStagePointer(object? sender, PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        pointerOverChrome = p.Y < 68 || (p.Y > Bounds.Height - 150 && Math.Abs(p.X - Bounds.Width / 2) < Transport.Bounds.Width / 2 + 28);
        ShowChrome(expand: false);
        Transport.SetCompact(Model.IsPlaying && !Transport.IsInteracting && !shell.IsQueueDrawerOpen && !shell.IsQuickSettingsOpen);
    }
    private void OnTransportInteraction()
    {
        ShowChrome(expand: Transport.IsInteracting);
        Transport.SetCompact(Model.IsPlaying && !Transport.IsInteracting && !shell.IsQueueDrawerOpen && !shell.IsQuickSettingsOpen);
    }
    private void ShowChrome(bool expand = true)
    {
        if (closed) return;
        hideChrome?.Dispose(); hideChrome = null;
        ChromeHeader.Opacity = ChromeFooter.Opacity = 1;
        ChromeHeader.IsHitTestVisible = ChromeFooter.IsHitTestVisible = true;
        if (expand || !Model.IsPlaying) Transport.SetCompact(false);
        if (!Model.IsPlaying || !shell.Motion.IsWindowVisible) return;
        hideChrome = DispatcherTimer.RunOnce(() =>
        {
            hideChrome = null;
            if (closed || !Model.IsPlaying || Transport.IsInteracting || ChromeHeader.IsKeyboardFocusWithin || shell.IsQuickSettingsOpen || shell.IsQueueDrawerOpen) return;
            Transport.SetCompact(true);
            if (pointerOverChrome) return;
            ChromeHeader.Opacity = 0; ChromeHeader.IsHitTestVisible = false;
            if (Model.Appearance.PlayerAutoHide)
            { ChromeFooter.Opacity = 0; ChromeFooter.IsHitTestVisible = false; }
        }, TimeSpan.FromSeconds(2.6));
    }
    private void OnToggleFullscreen(object? sender, RoutedEventArgs e)
    { if (TopLevel.GetTopLevel(this) is Window w) w.WindowState = w.WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen; }
    private void OnMinimize(object? sender, RoutedEventArgs e) { if (TopLevel.GetTopLevel(this) is Window w) w.WindowState = WindowState.Minimized; }
    private void OnMaximize(object? sender, RoutedEventArgs e)
    { if (TopLevel.GetTopLevel(this) is Window w) w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }
    private void OnClose(object? sender, RoutedEventArgs e) { if (TopLevel.GetTopLevel(this) is Window w) w.Close(); }
    private void OnDragWindow(object? sender, PointerPressedEventArgs e)
    { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && TopLevel.GetTopLevel(this) is Window w && w.WindowState != WindowState.FullScreen) w.BeginMoveDrag(e); }
    private void DisposeChrome()
    {
        hideChrome?.Dispose(); hideChrome = null;
        Model.Appearance.PropertyChanged -= OnChromeAppearance; shell.PropertyChanged -= OnShellTheme;
        shell.Motion.PropertyChanged -= OnChromeMotion; Model.Changed -= OnChromePlayback; RemoveHandler(PointerMovedEvent, OnStagePointer);
        Transport.InteractionChanged -= OnTransportInteraction;
    }
}
