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
using Echo.Platform.MacOS;
using Echo.Platform.Windows;

namespace Echo.Desktop.Features.Floating;

public partial class DesktopLyricsWindow : Window
{
    private readonly ShellViewModel shell;
    private readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer hover = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private readonly DispatcherTimer unlockHide = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private Func<nint, bool, bool>? passthrough;
    private bool osPassthrough, hoverShow, closed;
    private long lastWheel;
    private double? pendingVolume;
    public DesktopLyricsWindow(ShellViewModel shell, LyricsViewModel lyrics, MotionPolicy motion)
    {
        this.shell = shell;
        InitializeComponent();
        DataContext = shell;
        LyricsCanvas.Bind(lyrics, shell.Floating, motion);
        poll.Tick += PollLockedPointer;
        hover.Tick += HoverTick;
        unlockHide.Tick += HideUnlock;
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
            closed = true; poll.Stop(); hover.Stop(); unlockHide.Stop();
            shell.Floating.PropertyChanged -= FloatingChanged;
            shell.Player.PropertyChanged -= PlayerChanged;
            LyricsCanvas.Dispose();
        };
        Cursor = new Cursor(StandardCursorType.Hand);
    }
    public void UsePassthrough(Func<nint, bool, bool>? bridge) => passthrough = bridge;
    private bool lastLock;
    public bool ApplyLock(bool locked)
    {
        CanResize = !locked;
        if (locked == lastLock) return SetPassthrough(locked);
        lastLock = locked;
        if (!locked)
        {
            poll.Stop();
            var released = SetPassthrough(false);
            ShowChrome(false, false);
            return released;
        }
        ShowChrome(false, true);
        var accepted = SetPassthrough(true);
        if (accepted) poll.Start();
        return accepted;
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
    private void ApplyAxis()
    {
        var vertical = shell.Floating.IsVertical;
        var locked = shell.Floating.LyricsLocked;
        if (vertical)
        {
            Root.RowDefinitions = new RowDefinitions("*");
            Root.ColumnDefinitions = new ColumnDefinitions(locked ? "0,*" : "48,*");
            Toolbar.Orientation = Orientation.Vertical;
            Toolbar.Width = locked ? 0 : 48;
            Toolbar.Height = double.NaN;
            SetCell(Toolbar, 0, 0);
            SetCell(LyricsCanvas, 0, 1);
            LyricsCanvas.Margin = new Thickness(0, 12, 12, 12);
            UnlockButton.HorizontalAlignment = HorizontalAlignment.Left;
            UnlockButton.VerticalAlignment = VerticalAlignment.Center;
            UnlockButton.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetRow(UnlockButton, 0);
            Grid.SetRowSpan(UnlockButton, 1);
            Grid.SetColumn(UnlockButton, 0);
            Grid.SetColumnSpan(UnlockButton, 2);
        }
        else
        {
            Root.ColumnDefinitions = new ColumnDefinitions("*");
            Root.RowDefinitions = new RowDefinitions(locked ? "0,*" : "Auto,*");
            Toolbar.Orientation = Orientation.Horizontal;
            Toolbar.Width = double.NaN;
            Toolbar.Height = locked ? 0 : 48;
            SetCell(Toolbar, 0, 0);
            SetCell(LyricsCanvas, 1, 0);
            LyricsCanvas.Margin = new Thickness(12, 0, 12, 8);
            UnlockButton.HorizontalAlignment = HorizontalAlignment.Center;
            UnlockButton.VerticalAlignment = VerticalAlignment.Top;
            UnlockButton.Margin = new Thickness(0, 6, 0, 0);
            Grid.SetRowSpan(UnlockButton, 2);
            Grid.SetColumnSpan(UnlockButton, 1);
        }
    }
    private static void SetCell(Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        Grid.SetRowSpan(control, 1);
        Grid.SetColumnSpan(control, 1);
    }
    private void ShowChrome(bool menu, bool locked)
    {
        var tools = menu && !locked;
        Toolbar.Opacity = tools ? 1 : 0;
        Toolbar.IsHitTestVisible = tools;
        Scrim.Opacity = tools ? 1 : 0;
        ResizeGrip.IsVisible = tools;
        UnlockButton.IsVisible = menu && locked;
        ApplyAxis();
        Cursor = new Cursor(locked ? StandardCursorType.Arrow : StandardCursorType.Hand);
    }
    private void ArmHover(bool inside)
    {
        if (shell.Floating.LyricsLocked) return;
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
    private void PollLockedPointer(object? sender, EventArgs e)
    {
        if (closed || !shell.Floating.LyricsLocked) { poll.Stop(); return; }
        if (!TryLocalCursor(out var local)) return;
        var overButton = Contains(UnlockButton, local, unlockArmed ? 16 : 4);
        var overLine = Contains(LyricsCanvas, local, 8);
        if (overButton)
        {
            unlockHide.Stop();
            unlockArmed = true;
            if (!SetPassthrough(false)) return;
            ShowChrome(true, true);
            return;
        }
        unlockArmed = false;
        if (overLine)
        {
            unlockHide.Stop();
            ShowChrome(true, true);
            SetPassthrough(true);
            return;
        }
        if (!unlockHide.IsEnabled) unlockHide.Start();
        SetPassthrough(true);
    }
    private bool unlockArmed;
    private void HideUnlock(object? sender, EventArgs e)
    {
        unlockHide.Stop();
        unlockArmed = false;
        if (!closed && shell.Floating.LyricsLocked) ShowChrome(false, true);
    }
    private bool Contains(Control control, Point local, double pad)
    {
        if (!control.IsVisible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0) return false;
        if (control.TranslatePoint(default, this) is not { } origin) return false;
        return new Rect(origin, control.Bounds.Size).Inflate(pad).Contains(local);
    }
    private bool SetPassthrough(bool enabled)
    {
        if (passthrough is null) return !enabled;
        if (osPassthrough == enabled) return true;
        if (TryGetPlatformHandle() is not { } handle) return false;
        try
        {
            if (!passthrough(handle.Handle, enabled)) return false;
            osPassthrough = enabled;
            return true;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }
    private bool TryLocalCursor(out Point local)
    {
        local = default;
        try
        {
            int x = 0, y = 0;
            var ok = OperatingSystem.IsWindows()
                ? WindowsFloatingWindow.TryGetCursor(out x, out y)
                : OperatingSystem.IsMacOS() && MacOSFloatingWindow.TryGetCursor(out x, out y);
            if (!ok) return false;
            var pixel = new PixelPoint(x, y);
            if (OperatingSystem.IsMacOS())
            {
                var scale = Screens.ScreenFromWindow(this)?.Scaling ?? RenderScaling;
                if (scale > 1) pixel = new((int)Math.Round(x * scale), (int)Math.Round(y * scale));
            }
            local = this.PointToClient(pixel);
            return true;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { return false; }
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
    private void OpenSettings(object? sender, RoutedEventArgs e)
    {
        shell.Floating.RestoreMainCommand.Execute(null);
        if (shell.QuickSettingsKind != "lyrics") shell.OpenQuickSettingsCommand.Execute("lyrics");
    }
    private void Resize(object? sender, PointerPressedEventArgs e)
    {
        if (!shell.Floating.LyricsLocked && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        { BeginResizeDrag(WindowEdge.SouthEast, e); e.Handled = true; }
    }
}
