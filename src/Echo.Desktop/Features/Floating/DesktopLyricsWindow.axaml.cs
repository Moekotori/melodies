using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Floating;

public partial class DesktopLyricsWindow : Window
{
    private readonly ShellViewModel shell;
    public DesktopLyricsWindow(ShellViewModel shell, LyricsViewModel lyrics, MotionPolicy motion)
    {
        this.shell = shell; InitializeComponent(); DataContext = shell;
        LyricsCanvas.Bind(lyrics, shell.Floating, motion);
        PointerEntered += (_, _) => Hover(true); PointerExited += (_, _) => Hover(false);
        Activated += (_, _) => LyricsCanvas.SetFocused(true);
        Deactivated += (_, _) => LyricsCanvas.SetFocused(false);
        Closed += (_, _) => LyricsCanvas.Dispose();
    }
    private void Hover(bool show)
    {
        var visible = show && !shell.Floating.LyricsLocked;
        Toolbar.Opacity = HoverSurface.Opacity = visible ? 1 : 0;
        Toolbar.IsHitTestVisible = visible; ResizeGrip.IsVisible = visible;
    }
    private void Drag(object? sender, PointerPressedEventArgs e)
    {
        if (shell.Floating.LyricsLocked || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is Avalonia.Visual visual && (visual is Button || visual.GetVisualAncestors().OfType<Button>().Any())) return;
        BeginMoveDrag(e); e.Handled = true;
    }
    private void Resize(object? sender, PointerPressedEventArgs e)
    {
        if (!shell.Floating.LyricsLocked && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        { BeginResizeDrag(WindowEdge.SouthEast, e); e.Handled = true; }
    }
    public void ApplyLock(bool locked) { CanResize = !locked; Hover(false); }
}
