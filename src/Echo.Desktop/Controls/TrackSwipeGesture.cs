using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Echo.Desktop.Motion;

namespace Echo.Desktop.Controls;

// Horizontal swipe on `host` drags `stage`; swipe left = next (+1), right = previous (-1).
// False-touch guards: movement slop, horizontal dominance, committing distance or flick, one pointer,
// interactive-control exclusion, and a busy window while the exit/enter animation runs.
internal sealed class TrackSwipeGesture : IDisposable
{
    private const double Slop = 12, Dominance = 1.8, CommitDistance = 84, FlickDistance = 32, FlickSpeed = 0.5, ReverseSpeed = 0.35;
    private const double MaxTravel = 120, OutTravel = 72, InTravel = 56;
    private static readonly TimeSpan OutDuration = TimeSpan.FromMilliseconds(120), InDuration = TimeSpan.FromMilliseconds(280);
    private readonly Control host, stage;
    private readonly Func<bool> canSwipe;
    private readonly Func<Visual?, bool> accepts;
    private readonly Action<int> commit;
    private readonly TranslateTransform drag = new();
    private readonly MotionLane lane = new();
    private int pointerId = -1;
    private Point origin;
    private double lastX, velocity;
    private ulong lastTime;
    private bool tracking, active, ending, busy, disposed;

    public TrackSwipeGesture(Control host, Control stage, Func<bool> canSwipe, Func<Visual?, bool> accepts, Action<int> commit)
    {
        this.host = host; this.stage = stage; this.canSwipe = canSwipe; this.accepts = accepts; this.commit = commit;
        host.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        host.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
        host.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
        host.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Bubble);
    }

    // Controls that own horizontal drags or clicks must keep them; the caller may whitelist a specific button.
    public static bool IsInteractive(Visual? source, Visual root, Func<Control, bool>? allow = null)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, root); node = node.GetVisualParent())
            if (node is Control c && allow?.Invoke(c) != true && c is Button or Thumb or RangeBase or TextBox or ComboBox)
                return true;
        return false;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (disposed) return;
        if (tracking) { Abort(); return; } // a second contact is never a swipe
        if (busy || !canSwipe() || !e.GetCurrentPoint(host).Properties.IsLeftButtonPressed || !accepts(e.Source as Visual)) return;
        tracking = true; active = false; pointerId = e.Pointer.Id;
        origin = e.GetPosition(host); lastX = origin.X; lastTime = e.Timestamp; velocity = 0;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (!tracking || e.Pointer.Id != pointerId) return;
        var p = e.GetPosition(host);
        var dx = p.X - origin.X; var dy = p.Y - origin.Y;
        if (!active)
        {
            if (Math.Abs(dy) > Slop && Math.Abs(dy) >= Math.Abs(dx)) { tracking = false; return; }
            if (Math.Abs(dx) <= Slop || Math.Abs(dx) < Dominance * Math.Abs(dy)) return;
            if (!canSwipe()) { tracking = false; return; }
            active = true; e.Pointer.Capture(host);
            stage.RenderTransform = drag;
        }
        var dt = e.Timestamp - lastTime;
        if (dt > 0) velocity = velocity * 0.6 + (p.X - lastX) / dt * 0.4;
        lastX = p.X; lastTime = e.Timestamp;
        Apply(Offset(dx));
        e.Handled = true;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!tracking || e.Pointer.Id != pointerId) return;
        tracking = false;
        if (!active) return;
        active = false; e.Handled = true;
        var raw = Offset(e.GetPosition(host).X - origin.X);
        ending = true; e.Pointer.Capture(null); ending = false;
        var sign = Math.Sign(raw);
        var distance = Math.Abs(raw);
        var flick = velocity * sign;
        var go = sign != 0 && canSwipe() && ((distance >= CommitDistance && flick > -ReverseSpeed) || (distance >= FlickDistance && flick >= FlickSpeed));
        var shown = Resist(raw);
        busy = true;
        if (go) Run(() => CommitAsync(-sign, shown)); else Run(() => SettleAsync(shown));
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    { if (!ending && active) Abort(); }

    private void Abort()
    {
        var x = drag.X; tracking = false;
        if (!active) return;
        active = false; busy = true; Run(() => SettleAsync(x));
    }

    private static double Offset(double dx) => dx - Math.Sign(dx) * Slop;
    private static double Resist(double raw) => Math.Sign(raw) * MaxTravel * (1 - Math.Exp(-Math.Abs(raw) / MaxTravel));
    private void Apply(double raw)
    {
        var x = Resist(raw);
        drag.X = x;
        stage.Opacity = 1 - 0.5 * Math.Min(1, Math.Abs(x) / MaxTravel);
    }

    private async Task CommitAsync(int direction, double from)
    {
        var exit = -direction * OutTravel; // next (+1) leaves to the left
        await lane.PlayAsync(stage, stage.Opacity, 0, MotionLane.At(0, 1, from), MotionLane.At(0, 1, exit), OutDuration, MotionTokens.ExitEasing, preserveGeometry: true);
        if (disposed) return;
        commit(direction);
        await lane.PlayAsync(stage, 0, 1, MotionLane.At(0, 1, direction * InTravel), MotionLane.At(), InDuration, MotionTokens.EnterEasing, preserveGeometry: true);
    }

    private Task SettleAsync(double from) =>
        lane.PlayAsync(stage, stage.Opacity, 1, MotionLane.At(0, 1, from), MotionLane.At(), MotionTokens.Page, MotionTokens.EnterEasing, preserveGeometry: true);

    private async void Run(Func<Task> work)
    {
        try { await work(); }
        catch (Exception) { }
        finally
        {
            if (!disposed) { stage.Opacity = 1; stage.RenderTransform = null; }
            busy = false;
        }
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        host.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
        host.RemoveHandler(InputElement.PointerMovedEvent, OnMoved);
        host.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
        host.RemoveHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);
        lane.Dispose(); stage.Opacity = 1; stage.RenderTransform = null;
    }
}
