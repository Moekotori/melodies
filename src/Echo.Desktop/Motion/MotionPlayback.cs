using System.Diagnostics;
using Avalonia;
using Avalonia.Animation.Easings;

namespace Echo.Desktop.Motion;

// Only used to retarget interrupted animations; never sampled by a UI frame loop.
internal sealed class MotionPlayback(MotionChannel opacity, MotionChannel x, MotionChannel y,
    MotionChannel scale, TimeSpan duration)
{
    private long _started;
    public void Begin() => _started = Stopwatch.GetTimestamp();
    private double Progress => _started == 0 ? 0 : Math.Clamp(Stopwatch.GetElapsedTime(_started).TotalSeconds / duration.TotalSeconds, 0, 1);
    public double Opacity => opacity.Value(Progress);
    public Matrix Transform
    {
        get
        {
            var p = Progress;
            var size = scale.Value(p);
            return new Matrix(size, 0, 0, size, x.Value(p), y.Value(p));
        }
    }
    public MotionVelocity SampleVelocity()
    {
        var p = Progress;
        double Speed(MotionChannel channel) => _started != 0 || channel.Curve is MomentumEasing ? channel.Velocity(p, duration) : 0;
        return new(Speed(opacity), Speed(x), Speed(y), Speed(scale), Stopwatch.GetTimestamp());
    }
}

internal readonly record struct MotionVelocity(double Opacity, double X, double Y, double Scale, long Timestamp)
{
    public bool IsRecent => Timestamp != 0 && Stopwatch.GetElapsedTime(Timestamp).TotalMilliseconds <= 100;
}
