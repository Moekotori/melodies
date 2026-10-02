using System.Globalization;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media.Transformation;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace Echo.Desktop.Motion;

// One finite compositor animation per owner. No per-frame UI property updates.
public sealed class MotionLane : AvaloniaObject, IDisposable
{
    private static readonly AttachedProperty<MotionPlayback?> PlaybackProperty =
        AvaloniaProperty.RegisterAttached<MotionLane, Control, MotionPlayback?>("Playback");
    private CancellationTokenSource? _pending;
    private Control? _target;
    private CompositionVisual? _visual;
    public bool IsRunning => _pending is not null;

    public async Task PlayAsync(Control target, double fromOpacity, double toOpacity,
        TransformOperations fromTransform, TransformOperations toTransform,
        TimeSpan duration, Easing? easing = null)
    {
        Cancel();
        var pending = new CancellationTokenSource();
        var token = pending.Token;
        _pending = pending;
        _target = target;
        var from = fromTransform.Value;
        var to = toTransform.Value;
        easing ??= MotionTokens.EnterEasing;
        target.Opacity = fromOpacity;
        target.RenderTransform = fromTransform;
        try
        {
            if (duration > TimeSpan.Zero)
            {
                // Record the new page's layout and draw list before starting the clock.
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
                token.ThrowIfCancellationRequested();
                _visual = ElementComposition.GetElementVisual(target);
            }
            target.Opacity = toOpacity;
            target.RenderTransform = toTransform;
            if (_visual is null || duration <= TimeSpan.Zero) return;
            var visual = _visual;
            var compositor = visual.Compositor;
            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.Duration = duration;
            fade.InsertKeyFrame(0, (float)fromOpacity);
            fade.InsertKeyFrame(1, (float)toOpacity, easing);
            var move = compositor.CreateVector3DKeyFrameAnimation();
            move.Duration = duration;
            move.InsertKeyFrame(0, new Vector3D(from.M31 - to.M31, from.M32 - to.M32, 0));
            move.InsertKeyFrame(1, default, easing);
            var scale = compositor.CreateVector3DKeyFrameAnimation();
            scale.Duration = duration;
            scale.InsertKeyFrame(0, new Vector3D(from.M11 / to.M11, from.M22 / to.M22, 1));
            scale.InsertKeyFrame(1, new Vector3D(1, 1, 1), easing);
            var started = new TaskCompletionSource();
            compositor.RequestCompositionUpdate(() =>
            {
                if (token.IsCancellationRequested) { started.TrySetCanceled(token); return; }
                target.SetValue(PlaybackProperty, new MotionPlayback(fromOpacity, toOpacity, from, to, duration, easing));
                visual.StartAnimation("Opacity", fade);
                visual.StartAnimation("Translation", move);
                visual.StartAnimation("Scale", scale);
                started.TrySetResult();
            });
            await started.Task.WaitAsync(token);
            // Single completion wait with commit slack; this is not a frame timer.
            await Task.Delay(duration + TimeSpan.FromMilliseconds(32), token);
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_pending, pending))
            {
                StopVisual();
                _pending = null;
                _target = null;
            }
            pending.Dispose();
        }
    }

    public void Cancel()
    {
        var pending = _pending;
        _pending = null;
        pending?.Cancel();
        StopVisual();
        _target = null;
    }
    private void StopVisual()
    {
        if (_visual is { } visual)
        {
            visual.StopAnimation("Opacity");
            visual.StopAnimation("Translation");
            visual.StopAnimation("Scale");
            visual.Translation = default;
            visual.Scale = new Vector3D(1, 1, 1);
            visual.Opacity = (float)(_target?.Opacity ?? 1);
            _visual = null;
        }
        _target?.ClearValue(PlaybackProperty);
    }
    public void Dispose() => Cancel();
    public static TransformOperations At(double y = 0, double scale = 1, double x = 0) =>
        TransformOperations.Parse(string.Create(CultureInfo.InvariantCulture, $"translate({x}px, {y}px) scale({scale})"));
    public static TransformOperations Current(Control target)
    {
        var matrix = target.GetValue(PlaybackProperty)?.Transform ?? target.RenderTransform?.Value ?? Matrix.Identity;
        return At(matrix.M32, matrix.M11, matrix.M31);
    }
    public static double CurrentOpacity(Control target) => target.GetValue(PlaybackProperty)?.Opacity ?? target.Opacity;
}
