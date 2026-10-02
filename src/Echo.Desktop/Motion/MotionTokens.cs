using Avalonia.Animation.Easings;

namespace Echo.Desktop.Motion;

public static class MotionTokens
{
    public static TimeSpan Feedback => TimeSpan.FromMilliseconds(100);
    public static TimeSpan Reveal => TimeSpan.FromMilliseconds(180);
    public static TimeSpan Page => TimeSpan.FromMilliseconds(220);
    public static TimeSpan Exit => TimeSpan.FromMilliseconds(180);
    public static TimeSpan Reduced => TimeSpan.FromMilliseconds(80);
    public const double PageTravel = 8;
    public const double SurfaceTravel = 5;
    public static Easing EnterEasing => new SplineEasing(0.22, 0.68, 0, 1);
    public static Easing ExitEasing => new SplineEasing(0.16, 1, 0.3, 1);
}
