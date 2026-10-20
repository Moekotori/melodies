using Avalonia.Animation.Easings;

namespace Echo.Desktop.Motion;

public static class MotionTokens
{
    public static TimeSpan Feedback => TimeSpan.FromMilliseconds(100);
    public static TimeSpan Reveal => TimeSpan.FromMilliseconds(180);
    public static TimeSpan Page => TimeSpan.FromMilliseconds(220);
    public static TimeSpan Exit => TimeSpan.FromMilliseconds(180);
    public static TimeSpan Drawer => TimeSpan.FromMilliseconds(240);
    public static TimeSpan DrawerExit => TimeSpan.FromMilliseconds(190);
    public static TimeSpan Reduced => TimeSpan.FromMilliseconds(80);
    public const double PageTravel = 8;
    public const double SurfaceTravel = 5;
    public static Easing EnterEasing => new SplineEasing(0.22, 0.68, 0, 1);
    public static Easing ExitEasing => new SplineEasing(0.16, 1, 0.3, 1);
    public static Easing DrawerEasing => new SplineEasing(0.33, 0.7, 0.2, 1);

    // Scene choreography, after the Electron route scenes (route-slide / detail / lyrics sheet).
    public static TimeSpan PageEnter => TimeSpan.FromMilliseconds(280);
    public static TimeSpan PageExit => TimeSpan.FromMilliseconds(150);
    public static TimeSpan DetailEnter => TimeSpan.FromMilliseconds(360);
    public static TimeSpan DetailExit => TimeSpan.FromMilliseconds(180);
    public static TimeSpan ReturnEnter => TimeSpan.FromMilliseconds(300);
    public static TimeSpan ReturnExit => TimeSpan.FromMilliseconds(200);
    public static TimeSpan SheetRise => TimeSpan.FromMilliseconds(440);
    public static TimeSpan SheetDrop => TimeSpan.FromMilliseconds(320);
    public const double PageSlide = 40;
    public const double PageDepart = 28;
    public const double SectionSlide = 24;
    public const double SectionDepart = 14;
    public const double DetailTravel = 56;
    public const double DetailRecede = 18;
    public const double DetailReturn = 48;
    // Fraction of an incoming fade held at zero while the outgoing view clears.
    public const double PageFadeLead = 0.22;
    public const double DetailFadeLead = 0.2;
    public const double ReturnFadeLead = 0.12;
    public static Easing SlideEasing => new SplineEasing(0.22, 1, 0.36, 1);
    public static Easing DepartEasing => new SplineEasing(0.4, 0, 1, 1);
    public static Easing FadeEasing => new SplineEasing(0.25, 0.1, 0.25, 1);
    public static Easing SheetEasing => new SplineEasing(0.32, 0.72, 0, 1);
    public static Easing SheetDropEasing => new SplineEasing(0.4, 0, 0.2, 1);
}
