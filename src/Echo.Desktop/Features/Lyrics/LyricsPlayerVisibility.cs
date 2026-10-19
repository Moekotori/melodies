using Avalonia;
using Avalonia.Threading;
using Echo.Desktop.Motion;

namespace Echo.Desktop.Features.Lyrics;

public enum LyricsPlayerDisplay { Expanded, Compact, Hidden }

// One owner and at most one pending edge delay. Pointer movement cannot queue timers.
internal sealed class LyricsPlayerVisibility(
    LyricsTransportView transport, LyricsViewModel model, MotionPolicy motion, Func<bool> externalInteraction) : IDisposable
{
    private IDisposable? pending;
    private LyricsPlayerDisplay? pendingTarget;
    private long pendingDeadline;
    private Point? pointer;
    private bool disposed;
    public bool HasPendingChange => pending is not null;
    private bool Protected => transport.HasProtectedInteraction || externalInteraction();
    private bool CanCompact => model.Appearance.PlayerCompact;
    private Rect ExpandedBounds => new(0, 0, transport.Bounds.Width, transport.Bounds.Height);
    private bool NearControls => pointer is { } point &&
        ExpandedBounds.Inflate(transport.Display == LyricsPlayerDisplay.Expanded ? 18 : 8).Contains(point);
    private bool NearStageEdge => pointer is { } point && ExpandedBounds.Inflate(new Thickness(80, 96)).Contains(point);

    public void PointerMoved(Point point)
    {
        pointer = point; transport.NotePointerActivity(); Refresh();
    }
    public void PointerLeft() { pointer = null; transport.NotePointerActivity(); Refresh(); }
    public void Reveal()
    {
        if (disposed) return;
        Cancel(); transport.Present(LyricsPlayerDisplay.Expanded);
        if (!Protected && motion.IsWindowVisible && !NearControls)
            Schedule(CanCompact ? LyricsPlayerDisplay.Compact : LyricsPlayerDisplay.Hidden, TimeSpan.FromMilliseconds(900));
    }
    public void Refresh()
    {
        if (disposed) return;
        if (!motion.IsWindowVisible) { Cancel(); transport.ApplyMotion(); return; }
        if (Protected || NearControls)
        { Cancel(); transport.Present(LyricsPlayerDisplay.Expanded); return; }
        if (!CanCompact)
        {
            if (!model.Appearance.PlayerAutoHide || NearStageEdge)
            { Cancel(); transport.Present(LyricsPlayerDisplay.Expanded); }
            else Schedule(LyricsPlayerDisplay.Hidden, TimeSpan.FromMilliseconds(400));
            return;
        }
        if (transport.Display == LyricsPlayerDisplay.Expanded)
            Schedule(LyricsPlayerDisplay.Compact, TimeSpan.FromMilliseconds(220));
        else if (NearStageEdge || !model.Appearance.PlayerAutoHide)
        { Cancel(); transport.Present(LyricsPlayerDisplay.Compact); }
        else if (transport.Display == LyricsPlayerDisplay.Compact)
            Schedule(LyricsPlayerDisplay.Hidden, TimeSpan.FromMilliseconds(Math.Max(620,
                motion.UseSpatialMotion ? motion.Duration(TimeSpan.FromMilliseconds(LyricsTransportView.CapsuleCollapseMilliseconds)).TotalMilliseconds + 80 : 0)));
        else Cancel();
    }
    private void Schedule(LyricsPlayerDisplay target, TimeSpan delay)
    {
        if (target == LyricsPlayerDisplay.Hidden && !model.Appearance.PlayerAutoHide) return;
        var deadline = Environment.TickCount64 + (long)delay.TotalMilliseconds;
        if ((pendingTarget == target && pendingDeadline <= deadline) || transport.Display == target) return;
        Cancel(); pendingTarget = target; pendingDeadline = deadline;
        pending = DispatcherTimer.RunOnce(() =>
        {
            pending = null; pendingTarget = null;
            if (disposed || !motion.IsWindowVisible) return;
            if (Protected || NearControls || (target == LyricsPlayerDisplay.Hidden && (NearStageEdge || !model.Appearance.PlayerAutoHide)))
            { Refresh(); return; }
            transport.Present(target); Refresh();
        }, delay);
    }
    private void Cancel() { pending?.Dispose(); pending = null; pendingTarget = null; pendingDeadline = 0; }
    public void Dispose() { disposed = true; Cancel(); transport.StopMotion(); }
}
