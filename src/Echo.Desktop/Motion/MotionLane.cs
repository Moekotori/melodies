using System.Globalization;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media.Transformation;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Transport;
using Avalonia.Threading;

namespace Echo.Desktop.Motion;

// One finite compositor animation per owner. No per-frame UI property updates.
public sealed class MotionLane : AvaloniaObject, IDisposable
{
    private static readonly AttachedProperty<MotionPlayback?> PlaybackProperty =
        AvaloniaProperty.RegisterAttached<MotionLane, Control, MotionPlayback?>("Playback");
    private static readonly AttachedProperty<MotionVelocity> VelocityProperty =
        AvaloniaProperty.RegisterAttached<MotionLane, Control, MotionVelocity>("Velocity");
    private CancellationTokenSource? _pending;
    private TaskCompletionSource? _started;
    private Control? _target;
    private CompositionVisual? _visual;
    public bool IsRunning => _pending is not null;
    // Completes once the compositor owns the current timeline, or the request settled without one.
    // Work started after it no longer delays the motion: the render thread keeps animating.
    public Task Started => _started?.Task ?? Task.CompletedTask;

    public async Task PlayAsync(Control target, double fromOpacity, double toOpacity,
        TransformOperations fromTransform, TransformOperations toTransform,
        TimeSpan duration, Easing? easing = null, bool preserveGeometry = false, Easing? fadeEasing = null)
    {
        var velocity = target.GetValue(PlaybackProperty)?.SampleVelocity() ?? target.GetValue(VelocityProperty);
        Cancel();
        target.ClearValue(VelocityProperty);
        if (!velocity.IsRecent) velocity = default;
        var pending = new CancellationTokenSource();
        var token = pending.Token;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = pending;
        _started = started;
        _target = target;
        var from = fromTransform.Value;
        var to = toTransform.Value;
        if (MotionScope.GetPolicy(target) is { } policy)
        {
            if (!policy.CanAnimate) duration = TimeSpan.Zero;
            else duration = policy.Duration(duration);
            var amount = policy.UseSpatialMotion ? policy.Travel(1) : 0;
            // Current poses are already themed. Scaling them again breaks reversals and FLIP.
            if (!preserveGeometry)
            {
                if (!velocity.IsRecent)
                    from = new Matrix(1 + (from.M11 - 1) * amount, from.M12, from.M21, 1 + (from.M22 - 1) * amount, from.M31 * amount, from.M32 * amount);
                to = new Matrix(1 + (to.M11 - 1) * amount, to.M12, to.M21, 1 + (to.M22 - 1) * amount, to.M31 * amount, to.M32 * amount);
            }
            fromTransform = At(from.M32, from.M11, from.M31); toTransform = At(to.M32, to.M11, to.M31);
        }
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
            var opacityChannel = MotionChannel.Create(fromOpacity, toOpacity, duration, fadeEasing ?? easing, velocity.Opacity, bounded: true);
            var xChannel = MotionChannel.Create(from.M31, to.M31, duration, easing, velocity.X);
            var yChannel = MotionChannel.Create(from.M32, to.M32, duration, easing, velocity.Y);
            // The framework animates Translation as one vector. Existing owners move on one axis.
            var movementChannel = Math.Abs(to.M31 - from.M31) >= Math.Abs(to.M32 - from.M32) ? xChannel : yChannel;
            var moveCurve = movementChannel.Curve;
            xChannel = xChannel with { Curve = moveCurve };
            yChannel = yChannel with { Curve = moveCurve };
            var scaleChannel = MotionChannel.Create(from.M11, to.M11, duration, easing, velocity.Scale);
            var playback = new MotionPlayback(opacityChannel, xChannel, yChannel, scaleChannel, duration);
            target.SetValue(PlaybackProperty, playback);
            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.Duration = duration;
            fade.InsertKeyFrame(0, (float)fromOpacity);
            fade.InsertKeyFrame(1, (float)toOpacity, opacityChannel.RenderCurve);
            var move = compositor.CreateVector3DKeyFrameAnimation();
            move.Duration = duration;
            move.InsertKeyFrame(0, new Vector3D(from.M31 - to.M31, from.M32 - to.M32, 0));
            move.InsertKeyFrame(1, default, movementChannel.RenderCurve);
            var scale = compositor.CreateVector3DKeyFrameAnimation();
            scale.Duration = duration;
            scale.InsertKeyFrame(0, new Vector3D(from.M11 / to.M11, from.M22 / to.M22, 1));
            scale.InsertKeyFrame(1, new Vector3D(1, 1, 1), scaleChannel.RenderCurve);
            var compositionStarted = new TaskCompletionSource();
            CompositionBatch? startBatch = null;
            compositor.RequestCompositionUpdate(() =>
            {
                if (token.IsCancellationRequested) { compositionStarted.TrySetCanceled(token); return; }
                playback.Begin();
                if (fromOpacity != toOpacity) visual.StartAnimation("Opacity", fade);
                if (from.M31 != to.M31 || from.M32 != to.M32) visual.StartAnimation("Translation", move);
                if (from.M11 != to.M11) visual.StartAnimation("Scale", scale);
                startBatch = compositor.RequestCompositionBatchCommitAsync();
                compositionStarted.TrySetResult();
            });
            await compositionStarted.Task.WaitAsync(token);
            await startBatch!.Processed.WaitAsync(token);
            started.TrySetResult();
            await Task.Delay(duration, token);
            // Retire only after a real final composition frame; bound occluded-backend waiting.
            var finalBatch = compositor.RequestCompositionBatchCommitAsync();
            try { await finalBatch.Rendered.WaitAsync(TimeSpan.FromMilliseconds(80), token); }
            catch (TimeoutException) { }
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        finally
        {
            started.TrySetResult();
            if (ReferenceEquals(_pending, pending))
            {
                // Batch notifications can run inline inside a render-loop commit.
                // Queue UI cleanup outside that commit, and reject superseded cleanup.
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!ReferenceEquals(_pending, pending)) return;
                    StopVisual();
                    _pending = null;
                    _target = null;
                }, DispatcherPriority.Normal);
            }
            pending.Dispose();
        }
    }

    public void Cancel()
    {
        if (_target?.GetValue(PlaybackProperty) is { } playback)
            _target.SetValue(VelocityProperty, playback.SampleVelocity());
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
        // Avalonia's transform parser does not accept exponent notation (e.g. 1E-13px).
        TransformOperations.Parse(string.Create(CultureInfo.InvariantCulture, $"translate({x:0.#########}px, {y:0.#########}px) scale({scale:0.#########})"));
    public static TransformOperations Current(Control target)
    {
        var matrix = target.GetValue(PlaybackProperty)?.Transform ?? target.RenderTransform?.Value ?? Matrix.Identity;
        return At(matrix.M32, matrix.M11, matrix.M31);
    }
    public static double CurrentOpacity(Control target) => target.GetValue(PlaybackProperty)?.Opacity ?? target.Opacity;
}
