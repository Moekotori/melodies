using System.Diagnostics;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Echo.Desktop.Controls.Lyrics;
using Echo.Desktop.Motion;

namespace Echo.Desktop.Features.Lyrics;

public partial class LyricsTransportView
{
    internal const int CapsuleCollapseMilliseconds = 280;
    private const int CapsuleExpandMilliseconds = 340;
    private MotionChannel? capsuleChannel;
    private TimeSpan capsuleDuration;
    private long capsuleStarted;
    private double capsuleTarget = 1;

    private void AnimateCapsule(double target)
    {
        var policy = shell?.Motion;
        var elapsed = capsuleStarted == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(capsuleStarted);
        var running = capsuleChannel is not null && elapsed < capsuleDuration;
        if (running && capsuleTarget == target) return;
        var current = CapsuleSurface.Expansion;
        var velocity = running ? capsuleChannel!.Value.Velocity(elapsed.TotalSeconds / capsuleDuration.TotalSeconds, capsuleDuration) : 0;
        // Freeze the presented pose before replacing the curve. All writes occur in one UI dispatch.
        CapsuleSurface.Transitions = null;
        CapsuleSurface.Expansion = current;
        capsuleTarget = target;
        if (policy?.UseSpatialMotion != true || Math.Abs(target - current) < .0001)
        {
            ClearCapsuleMotion(); CapsuleSurface.Expansion = target; return;
        }
        var distance = Math.Clamp(Math.Sqrt(Math.Abs(target - current)), .4, 1);
        var duration = policy.Duration(TimeSpan.FromMilliseconds(target > current ? CapsuleExpandMilliseconds : CapsuleCollapseMilliseconds));
        capsuleDuration = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * distance);
        capsuleChannel = MotionChannel.Create(current, target, capsuleDuration,
            new SplineEasing(.24, 0, .18, 1), velocity, bounded: true);
        CapsuleSurface.Transitions = [new DoubleTransition
        {
            Property = LyricsPlayerSurface.ExpansionProperty,
            Duration = capsuleDuration,
            Easing = capsuleChannel.Value.RenderCurve
        }];
        capsuleStarted = Stopwatch.GetTimestamp();
        CapsuleSurface.Expansion = target;
    }
    private void ClearCapsuleMotion()
    { capsuleChannel = null; capsuleStarted = 0; capsuleDuration = TimeSpan.Zero; }
}
