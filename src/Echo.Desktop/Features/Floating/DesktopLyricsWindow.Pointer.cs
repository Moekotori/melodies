using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Echo.Platform.MacOS;
using Echo.Platform.Windows;

namespace Echo.Desktop.Features.Floating;

public partial class DesktopLyricsWindow
{
    private readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer unlockHide = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private Func<nint, bool, bool>? passthrough;
    private bool osPassthrough, lastLock;

    private void InitializePointerPolling()
    {
        poll.Tick += PollLockedPointer;
        unlockHide.Tick += HideUnlock;
        PropertyChanged += PointerVisibilityChanged;
    }
    public void UsePassthrough(Func<nint, bool, bool>? bridge) => passthrough = bridge;
    private void PointerVisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Visual.IsVisibleProperty || args.Property == WindowStateProperty)
            UpdatePointerPolling(!lastLock || SetPassthrough(true));
    }
    private void UpdatePointerPolling(bool accepted)
    {
        if (!closed && IsVisible && WindowState != WindowState.Minimized && lastLock && accepted)
        { if (!poll.IsEnabled) poll.Start(); return; }
        poll.Stop(); unlockHide.Stop(); hover.Stop(); unlockArmed = false;
        if (!IsVisible || WindowState == WindowState.Minimized) ShowChrome(false, lastLock);
    }
    private void DisposePointerPolling()
    {
        PropertyChanged -= PointerVisibilityChanged;
        poll.Stop(); unlockHide.Stop();
        poll.Tick -= PollLockedPointer;
        unlockHide.Tick -= HideUnlock;
    }
    public bool ApplyLock(bool locked)
    {
        CanResize = !locked;
        if (lastLock != locked) ShowChrome(!locked && IsPointerOver, locked);
        lastLock = locked;
        var accepted = SetPassthrough(locked);
        UpdatePointerPolling(accepted);
        return accepted;
    }
    private void PollLockedPointer(object? sender, EventArgs e)
    {
        if (closed || !IsVisible || WindowState == WindowState.Minimized || !lastLock) { poll.Stop(); return; }
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
        if (UnlockButton.IsVisible && !unlockHide.IsEnabled) unlockHide.Start();
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
}
