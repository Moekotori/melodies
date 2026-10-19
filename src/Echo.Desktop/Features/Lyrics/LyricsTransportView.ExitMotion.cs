using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Echo.Desktop.Motion;

namespace Echo.Desktop.Features.Lyrics;

public partial class LyricsTransportView
{
    private MotionChannel? frameOpacityChannel;
    private MotionChannel frameYChannel, frameScaleChannel;
    private TimeSpan frameDuration;
    private long frameStarted;
    private bool frameTargetHidden;

    private void AnimatePlayerFrame(bool hidden)
    {
        if (frameOpacityChannel is not null && frameTargetHidden == hidden) return;
        var policy = shell?.Motion;
        var elapsed = frameStarted == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(frameStarted);
        var running = frameOpacityChannel is not null && elapsed < frameDuration;
        var progress = running ? elapsed.TotalSeconds / frameDuration.TotalSeconds : 1;
        var opacity = TransportLayout.Opacity;
        var pose = TransportLayout.RenderTransform?.Value ?? Matrix.Identity;
        var spatial = policy?.UseSpatialMotion == true && Model?.Appearance.MiniPlayer != false;
        var fromY = spatial ? pose.M32 : 0;
        var fromScale = spatial ? pose.M11 : 1;
        var toY = hidden && spatial ? 8 : 0;
        var toScale = hidden && spatial ? .985 : 1;
        var toOpacity = hidden ? 0 : 1;
        TransportLayout.Transitions = null;
        TransportLayout.Opacity = opacity;
        TransportLayout.RenderTransform = MotionLane.At(fromY, fromScale);
        frameTargetHidden = hidden;
        if (policy?.CanAnimate != true || (Math.Abs(toOpacity - opacity) < .0001 && Math.Abs(toY - fromY) < .0001 && Math.Abs(toScale - fromScale) < .0001))
        {
            ClearFrameMotion(); TransportLayout.Opacity = toOpacity;
            TransportLayout.RenderTransform = MotionLane.At(toY, toScale); return;
        }
        frameDuration = policy.Duration(TimeSpan.FromMilliseconds(Model?.Appearance.MiniPlayer != false ? 260 : 160));
        frameYChannel = MotionChannel.Create(fromY, toY, frameDuration, new SplineEasing(.22, 0, .2, 1),
            running ? frameYChannel.Velocity(progress, frameDuration) : 0);
        frameScaleChannel = MotionChannel.Create(fromScale, toScale, frameDuration, new SplineEasing(.22, 0, .2, 1),
            running ? frameScaleChannel.Velocity(progress, frameDuration) : 0);
        var move = Math.Abs(toY - fromY) > .0001 ? frameYChannel : frameScaleChannel;
        frameYChannel = frameYChannel with { Curve = move.Curve };
        frameScaleChannel = frameScaleChannel with { Curve = move.Curve };
        frameOpacityChannel = MotionChannel.Create(opacity, toOpacity, frameDuration, new SplineEasing(.22, 0, .2, 1),
            running ? frameOpacityChannel!.Value.Velocity(progress, frameDuration) : 0, bounded: true);
        TransportLayout.Transitions = [new DoubleTransition
            { Property = OpacityProperty, Duration = frameDuration, Easing = frameOpacityChannel.Value.RenderCurve },
            new TransformOperationsTransition
            { Property = RenderTransformProperty, Duration = frameDuration, Easing = move.RenderCurve }];
        frameStarted = Stopwatch.GetTimestamp();
        TransportLayout.Opacity = toOpacity;
        TransportLayout.RenderTransform = MotionLane.At(toY, toScale);
    }
    private void ClearFrameMotion()
    { frameOpacityChannel = null; frameStarted = 0; frameDuration = TimeSpan.Zero; }
}
