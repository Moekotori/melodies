using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;

namespace Echo.Desktop.Motion;

// Two retained sibling panes (a wall and its detail): one arrives while the other leaves, then
// hides. Holds two lanes and no visual tree. The task is true when this request settled; callers
// release pane content or suspend walls only then, so nothing blanks while it is still visible.
public sealed class MotionPanes : IDisposable
{
    private readonly MotionLane _enter = new();
    private readonly MotionLane _exit = new();
    private int _generation;

    public async Task<bool> Show(Control incoming, Control outgoing, MotionDirection direction)
    {
        if (incoming.IsVisible && !outgoing.IsVisible) return true;
        var generation = ++_generation;
        var reversing = incoming.IsVisible;
        var inOpacity = reversing ? MotionLane.CurrentOpacity(incoming) : 0;
        TransformOperations? inPose = reversing ? MotionLane.Current(incoming) : null;
        var outOpacity = MotionLane.CurrentOpacity(outgoing);
        var outPose = MotionLane.Current(outgoing);
        _enter.Cancel();
        _exit.Cancel();
        var scene = MotionScene.Detail(direction);
        incoming.ZIndex = scene.OutgoingAbove ? 0 : 1;
        outgoing.ZIndex = scene.OutgoingAbove ? 1 : 0;
        incoming.IsVisible = true;
        Activate(incoming, true);
        Activate(outgoing, false);
        if (!outgoing.IsVisible || MotionScope.GetPolicy(incoming) is not { CanAnimate: true } || !incoming.IsAttachedToVisualTree())
        { Settle(incoming, outgoing); return true; }
        await Task.WhenAll(
            _enter.PlayAsync(incoming, inOpacity, 1, inPose ?? scene.Enter.Pose, MotionLane.At(),
                scene.Enter.Duration, scene.Enter.Move, fadeEasing: reversing ? null : scene.Enter.Fade),
            _exit.PlayAsync(outgoing, outOpacity, 0, outPose, scene.Exit.Pose,
                scene.Exit.Duration, scene.Exit.Move, fadeEasing: scene.Exit.Fade));
        if (generation != _generation) return false;
        Settle(incoming, outgoing);
        return true;
    }
    private static void Activate(Control pane, bool active)
    {
        pane.IsHitTestVisible = active;
        KeyboardNavigation.SetTabNavigation(pane, active ? KeyboardNavigationMode.Continue : KeyboardNavigationMode.None);
        if (!active && pane.IsKeyboardFocusWithin) TopLevel.GetTopLevel(pane)?.FocusManager?.Focus(null);
    }
    private static void Settle(Control incoming, Control outgoing)
    {
        outgoing.IsVisible = false;
        incoming.Opacity = outgoing.Opacity = 1;
        incoming.RenderTransform = outgoing.RenderTransform = MotionLane.At();
    }
    public void Dispose()
    {
        ++_generation;
        _enter.Cancel();
        _exit.Cancel();
    }
}
