using System.Diagnostics;
using Avalonia;
using Avalonia.Animation.Easings;

namespace Echo.Desktop.Motion;

// Only used to retarget interrupted animations; never sampled by a UI frame loop.
internal sealed class MotionPlayback(double fromOpacity, double toOpacity, Matrix from, Matrix to,
    TimeSpan duration, Easing easing)
{
    private readonly long _started = Stopwatch.GetTimestamp();
    private double Progress => easing.Ease(Math.Clamp(Stopwatch.GetElapsedTime(_started).TotalSeconds / duration.TotalSeconds, 0, 1));
    public double Opacity => fromOpacity + (toOpacity - fromOpacity) * Progress;
    public Matrix Transform
    {
        get
        {
            var p = Progress;
            return new Matrix(from.M11 + (to.M11 - from.M11) * p, 0, 0,
                from.M22 + (to.M22 - from.M22) * p,
                from.M31 + (to.M31 - from.M31) * p, from.M32 + (to.M32 - from.M32) * p);
        }
    }
}
