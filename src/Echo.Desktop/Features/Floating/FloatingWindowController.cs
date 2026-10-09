using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Localization;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Desktop.Theming;

namespace Echo.Desktop.Features.Floating;

// Main-window lifetime owns at most two windows, one shared lyric load/cover lease and one save debounce.
public sealed class FloatingWindowController : IDisposable
{
    private readonly Window main;
    private readonly ShellViewModel shell;
    private readonly Func<nint, bool, bool>? passthrough;
    private readonly DispatcherTimer geometry = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private LyricsViewModel? lyrics;
    private Echo.Contracts.Lyrics.LyricsDocument? visibilityDocument;
    private bool trackHasText;
    private bool disposed, applying;
    private int screenUpdateQueued;
    public MiniPlayerWindow? MiniWindow { get; private set; }
    public DesktopLyricsWindow? LyricsWindow { get; private set; }
    public FloatingWindowController(Window main, ShellViewModel shell, Func<nint, bool, bool>? passthrough = null)
    {
        this.main = main; this.shell = shell; this.passthrough = passthrough;
        shell.Floating.SupportsPassthrough = passthrough is not null;
        shell.Floating.PreferencesChanged += Apply; shell.Floating.ResetRequested += Reset;
        shell.Floating.RestoreRequested += RestoreMain;
        shell.Floating.MinimizeRequested += MinimizeMain;
        shell.Floating.PropertyChanged += TypographyChanged;
        geometry.Tick += SaveGeometry;
        main.Screens.Changed += ScreensChanged;
    }
    private void Apply()
    {
        if (disposed || applying || shell.Floating.Restoring) return;
        applying = true;
        try
        {
            var settings = shell.Floating;
            if (!settings.MiniEnabled && MiniWindow is { } mini)
            { settings.Remember(true, FloatingWindowPlacement.Capture(mini)); MiniWindow = null; mini.Close(); }
            if (!settings.LyricsEnabled && LyricsWindow is { } desktop)
            { settings.Remember(false, FloatingWindowPlacement.Capture(desktop)); LyricsWindow = null; desktop.Close(); }
            if (settings.MiniEnabled || settings.LyricsEnabled)
            {
                if (lyrics is null)
                {
                    lyrics = new(shell.Player!, shell.LyricsSource!, shell.LyricsAppearance, shell.AlbumArtwork, shell.LyricsSelection);
                    lyrics.Changed += LyricsChanged;
                }
                if (settings.MiniEnabled && MiniWindow is null)
                {
                    var window = new MiniPlayerWindow(shell, lyrics); MiniWindow = window;
                    BindLifetime(window, true);
                    FloatingWindowPlacement.Place(window, main, settings.MiniBounds, true);
                    window.LayoutChanged += () => FloatingWindowPlacement.Place(window, main, FloatingWindowPlacement.Capture(window), true);
                    window.Show();
                }
                if (settings.LyricsEnabled && LyricsWindow is null)
                {
                    var policy = new MotionPolicy(shell.Text);
                    var window = new DesktopLyricsWindow(shell, lyrics, policy); LyricsWindow = window;
                    BindLifetime(window, false, policy);
                    window.UsePassthrough(passthrough);
                    DesktopLyricsPlacement.Apply(window, main, settings, settings.LyricsBounds);
                    window.Show();
                }
            }
            if (MiniWindow is not null) MiniWindow.Topmost = settings.MiniTopmost;
            if (LyricsWindow is not null)
            {
                LyricsWindow.UsePassthrough(passthrough);
                if (settings.LyricsLocked && passthrough is null)
                { settings.LyricsLocked = false; settings.Diagnostic = shell.Text["floating.unsupported"]; }
                if (!LyricsWindow.ApplyLock(settings.LyricsLocked))
                { settings.LyricsLocked = false; settings.Diagnostic = shell.Text["floating.lockFailed"]; LyricsWindow.ApplyLock(false); }
                UpdateLyricsVisibility();
            }
            if (MiniWindow is null && LyricsWindow is null && lyrics is not null)
            { lyrics.Changed -= LyricsChanged; lyrics.Dispose(); lyrics = null; geometry.Stop(); }
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        { shell.Floating.Diagnostic = shell.Text["floating.openFailed"]; }
        finally { applying = false; }
    }
    private void LyricsChanged() => UpdateLyricsVisibility();
    private void UpdateLyricsVisibility()
    {
        if (LyricsWindow is not { } window || lyrics is null) return;
        // Whole document, not the currently empty intro/interlude row.
        if (!ReferenceEquals(visibilityDocument, lyrics.Document))
        {
            visibilityDocument = lyrics.Document;
            trackHasText = lyrics.Document.Lines.Any(line => !string.IsNullOrWhiteSpace(line.Text));
        }
        shell.Floating.CanNudgeLyrics = lyrics.Document.IsSynced && trackHasText;
        var show = !shell.Floating.HideWithoutLyrics || lyrics.IsLoading || trackHasText;
        if (show && !window.IsVisible) window.Show();
        else if (!show && window.IsVisible) window.Hide();
    }
    private void BindLifetime(Window window, bool mini, MotionPolicy? policy = null)
    {
        var localization = new WindowLocalizationBinding(window, shell.Text);
        void Theme()
        {
            window.RequestedThemeVariant = main.RequestedThemeVariant;
            foreach (var dark in new[] { false, true })
                window.Resources.ThemeDictionaries[dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light]
                    = ThemeResourcePalette.Create(shell.Theme.Tone(dark), shell.Theme.Snapshot, dark, null);
        }
        void Motion(object? sender, PropertyChangedEventArgs e)
        {
            if (policy is null) return;
            policy.SelectedChoice = policy.Choices.First(c => c.Mode == shell.Motion.SelectedChoice.Mode);
            policy.SystemAllowsAnimation = shell.Motion.SystemAllowsAnimation;
            policy.ThemeAllowsAnimation = shell.Motion.ThemeAllowsAnimation;
        }
        void Visibility(object? sender, AvaloniaPropertyChangedEventArgs e)
        { if (policy is not null) policy.IsWindowVisible = window.IsVisible && window.WindowState != WindowState.Minimized; }
        void Changed(object? sender, EventArgs e) { geometry.Stop(); geometry.Start(); }
        Theme(); Motion(null, new(null));
        shell.Theme.Changed += Theme; shell.Motion.PropertyChanged += Motion;
        window.PropertyChanged += Visibility;
        window.PositionChanged += Changed; window.SizeChanged += Changed;
        window.Closed += (_, _) =>
        {
            localization.Dispose(); shell.Theme.Changed -= Theme; shell.Motion.PropertyChanged -= Motion;
            window.PropertyChanged -= Visibility; window.PositionChanged -= Changed; window.SizeChanged -= Changed;
            policy?.ReleaseLocalization();
            if (mini && ReferenceEquals(MiniWindow, window)) { MiniWindow = null; shell.Floating.MiniEnabled = false; }
            if (!mini && ReferenceEquals(LyricsWindow, window)) { LyricsWindow = null; shell.Floating.LyricsEnabled = false; }
        };
    }
    private void SaveGeometry(object? sender, EventArgs e)
    {
        geometry.Stop();
        if (MiniWindow is { } mini) shell.Floating.Remember(true, FloatingWindowPlacement.Capture(mini));
        if (LyricsWindow is { } desktop) shell.Floating.Remember(false, FloatingWindowPlacement.Capture(desktop));
    }
    private void Reset()
    {
        if (MiniWindow is { } mini) FloatingWindowPlacement.Place(mini, main, null, true);
        if (LyricsWindow is { } desktop) DesktopLyricsPlacement.Apply(desktop, main, shell.Floating, null);
    }
    private void ScreensChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref screenUpdateQueued, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref screenUpdateQueued, 0);
            if (disposed) return;
            if (MiniWindow is { } mini) FloatingWindowPlacement.Place(mini, main, FloatingWindowPlacement.Capture(mini), true);
            if (LyricsWindow is { } desktop) DesktopLyricsPlacement.Apply(desktop, main, shell.Floating, FloatingWindowPlacement.Capture(desktop));
        });
    }
    private void TypographyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (disposed || shell.Floating.Restoring || LyricsWindow is not { } window) return;
        if (e.PropertyName is nameof(FloatingWindowsViewModel.FontSize) or nameof(FloatingWindowsViewModel.SecondarySize)
            or nameof(FloatingWindowsViewModel.ScalePercent) or nameof(FloatingWindowsViewModel.DoubleLine)
            or nameof(FloatingWindowsViewModel.TextDirection))
            DesktopLyricsPlacement.Apply(window, main, shell.Floating, FloatingWindowPlacement.Capture(window));
    }
    private void RestoreMain()
    {
        if (main is MainWindow shellWindow) { shellWindow.RestoreMainWindow(); return; }
        main.Show(); if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal; main.Activate();
    }
    private void MinimizeMain()
    { if (MiniWindow is { IsVisible: true }) main.WindowState = WindowState.Minimized; }
    public void Dispose()
    {
        if (disposed) return;
        SaveGeometry(null, EventArgs.Empty); disposed = true; geometry.Stop(); geometry.Tick -= SaveGeometry;
        main.Screens.Changed -= ScreensChanged;
        shell.Floating.PreferencesChanged -= Apply; shell.Floating.ResetRequested -= Reset; shell.Floating.RestoreRequested -= RestoreMain;
        shell.Floating.MinimizeRequested -= MinimizeMain; shell.Floating.PropertyChanged -= TypographyChanged;
        var mini = MiniWindow; var desktop = LyricsWindow; MiniWindow = null; LyricsWindow = null;
        mini?.Close(); desktop?.Close();
        if (lyrics is not null) { lyrics.Changed -= LyricsChanged; lyrics.Dispose(); lyrics = null; }
    }
}
