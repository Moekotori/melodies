using Avalonia.Animation.Easings;

namespace Echo.Desktop.Motion;

internal readonly record struct MotionChannel(double From, double To, Easing Curve)
{
    // SplineEasing caches its solver state; render and UI estimation must not share it.
    public Easing RenderCurve => Copy(Curve);
    internal static Easing Copy(Easing curve) => curve switch
    {
        SplineEasing spline => new SplineEasing(spline.X1, spline.Y1, spline.X2, spline.Y2),
        LeadEasing lead => lead.Copy(),
        _ => curve
    };
    public static MotionChannel Create(double from, double to, TimeSpan duration, Easing curve,
        double velocity, bool bounded = false)
    {
        var distance = to - from;
        if (Math.Abs(distance) > 0.00001 && Math.Abs(velocity) > 0.00001)
        {
            var slope = Math.Clamp(velocity * duration.TotalSeconds / distance, -8, 8);
            var minimum = double.NegativeInfinity;
            var maximum = double.PositiveInfinity;
            if (bounded)
            {
                minimum = Math.Min(-from / distance, (1 - from) / distance);
                maximum = Math.Max(-from / distance, (1 - from) / distance);
            }
            curve = new MomentumEasing(slope, minimum, maximum);
        }
        return new(from, to, curve);
    }
    public double Value(double progress) => From + (To - From) * Curve.Ease(progress);
    public double Velocity(double progress, TimeSpan duration)
    {
        if (progress >= 1) return 0;
        // Stay above the framework spline solver's tolerance, otherwise nearby samples
        // can quantize to the same value and incorrectly report zero velocity.
        const double step = 0.005;
        var left = Math.Max(0, progress - step);
        var right = Math.Min(1, progress + step);
        return (Value(right) - Value(left)) / ((right - left) * duration.TotalSeconds);
    }
}

// Quintic Hermite: carry initial velocity, arrive with zero velocity/acceleration.
internal sealed class MomentumEasing(double slope, double minimum, double maximum) : Easing
{
    public override double Ease(double progress)
    {
        var t = Math.Clamp(progress, 0, 1);
        var value = slope * t + (10 - 6 * slope) * t * t * t
            + (-15 + 8 * slope) * t * t * t * t + (6 - 3 * slope) * t * t * t * t * t;
        return Math.Clamp(value, minimum, maximum);
    }
}
