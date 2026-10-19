using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Desktop.Controls;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Floating;

public partial class DesktopLyricsWindow : Window
{
    private readonly ShellViewModel shell;
    private readonly DispatcherTimer hover = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private bool hoverShow, closed;
    private (bool Menu, bool Locked, bool Settings)? shownChrome;
    private readonly Cursor handCursor = new(StandardCursorType.Hand);
    private readonly Cursor arrowCursor = new(StandardCursorType.Arrow);
    private long lastWheel;
    private double? pendingVolume;
    public DesktopLyricsWindow(ShellViewModel shell, LyricsViewModel lyrics, MotionPolicy motion)
    {
        this.shell = shell;
        InitializeComponent();
        DataContext = shell;
        LyricsCanvas.Bind(lyrics, shell.Floating, motion);
        InitializePointerPolling();
        hover.Tick += HoverTick;
        shell.Floating.PropertyChanged += FloatingChanged;
        shell.Player.PropertyChanged += PlayerChanged;
        SyncPlayIcon();
        ApplyAxis();
        PointerEntered += (_, _) => ArmHover(true);
        PointerExited += (_, _) => ArmHover(false);
        PointerWheelChanged += WheelVolume;
        Activated += (_, _) => LyricsCanvas.SetFocused(true);
        Deactivated += (_, _) => LyricsCanvas.SetFocused(false);
        Closed += (_, _) =>
        {
            closed = true; hover.Stop(); DisposePointerPolling();
            handCursor.Dispose(); arrowCursor.Dispose();
            shell.Floating.PropertyChanged -= FloatingChanged;
            shell.Player.PropertyChanged -= PlayerChanged;
            settingsWindow?.Close();
            LyricsCanvas.Dispose();
        };
        Cursor = handCursor;
    }
    private void FloatingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FloatingWindowsViewModel.TextDirection) or nameof(FloatingWindowsViewModel.LyricsLocked)) ApplyAxis();
    }
    private void PlayerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.PlayIcon) or null) SyncPlayIcon();
    }
    private void SyncPlayIcon() => PlayGlyph.Kind = shell.Player.PlayIcon == "pause" ? "lyric-pause" : "lyric-play";
    private void ShowChrome(bool menu, bool locked)
    {
        var stamp = (menu, locked, settingsWindow is not null);
        if (shownChrome == stamp) return;
        shownChrome = stamp;
        var tools = (menu || settingsWindow is not null) && !locked;
        Toolbar.Opacity = tools ? 1 : 0;
        Toolbar.IsHitTestVisible = tools;
        Scrim.Opacity = tools ? 1 : 0;
        StartGrip.IsVisible = EndGrip.IsVisible = tools;
        UnlockButton.IsVisible = menu && locked;
        ApplyAxis();
        Cursor = locked ? arrowCursor : handCursor;
    }
    private void ArmHover(bool inside)
    {
        if (closed || !IsVisible || WindowState == WindowState.Minimized || shell.Floating.LyricsLocked) return;
        hoverShow = inside;
        hover.Stop();
        hover.Interval = TimeSpan.FromMilliseconds(inside ? 160 : 140);
        hover.Start();
    }
    private void HoverTick(object? sender, EventArgs e)
    {
        hover.Stop();
        if (!closed && !shell.Floating.LyricsLocked) ShowChrome(hoverShow, false);
    }
    private async void WheelVolume(object? sender, PointerWheelEventArgs e)
    {
        e.Handled = true;
        var now = Environment.TickCount64;
        if (now - lastWheel < 80 || shell.Player is not { VolumeLocked: false } player) return;
        lastWheel = now;
        var current = pendingVolume ?? player.Volume;
        var next = Math.Clamp(Math.Round((current + (e.Delta.Y > 0 ? 0.04 : -0.04)) * 100) / 100, 0, 1);
        if (Math.Abs(next - current) < 0.001) return;
        pendingVolume = next;
        try { await player.ChangeVolumeAsync(next); }
        finally { if (pendingVolume == next) pendingVolume = null; }
    }
    private void Drag(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (shell.Floating.LyricsLocked)
        {
            if (point.Properties.IsRightButtonPressed) { shell.Floating.LyricsLocked = false; e.Handled = true; }
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;
        if (e.Source is Visual visual && (visual is Button || visual.GetVisualAncestors().OfType<Button>().Any())) return;
        BeginMoveDrag(e);
        e.Handled = true;
    }
    private void ResizeStart(object? sender, PointerPressedEventArgs e) => ResizeEdge(e, true);
    private void ResizeEnd(object? sender, PointerPressedEventArgs e) => ResizeEdge(e, false);
    private void ResizeEdge(PointerPressedEventArgs e, bool start)
    {
        if (shell.Floating.LyricsLocked || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        BeginResizeDrag(shell.Floating.IsVertical ? start ? WindowEdge.North : WindowEdge.South : start ? WindowEdge.West : WindowEdge.East, e);
        e.Handled = true;
    }
}
