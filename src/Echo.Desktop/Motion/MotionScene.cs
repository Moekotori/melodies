using Avalonia.Animation.Easings;
using Avalonia.Media.Transformation;

namespace Echo.Desktop.Motion;

public enum MotionDirection { None, Forward, Back }

// One side of a scene: the pose a view enters from or leaves toward, with its timing.
internal readonly record struct MotionStep(double X, double Y, TimeSpan Duration, Easing Move, Easing Fade)
{
    public TransformOperations Pose => MotionLane.At(Y, 1, X);
}

// The outgoing view clears quickly while the incoming one holds its fade until then, so
// translucent theme surfaces never draw two pages' text over each other.
internal readonly record struct MotionScene(MotionStep Enter, MotionStep Exit, bool OutgoingAbove)
{
    // A vertical axis follows a vertical rail (settings categories): later entries come from below.
    public static MotionScene Page(MotionDirection direction, MotionAxis axis = MotionAxis.Horizontal)
    {
        if (direction == MotionDirection.None)
            return new(new(0, MotionTokens.PageTravel, MotionTokens.Page, MotionTokens.EnterEasing, Lead(MotionTokens.PageFadeLead, MotionTokens.EnterEasing)),
                new(0, 0, MotionTokens.Exit, MotionTokens.ExitEasing, MotionTokens.ExitEasing), false);
        var side = direction == MotionDirection.Forward ? 1 : -1;
        var vertical = axis == MotionAxis.Vertical;
        var enter = side * (vertical ? MotionTokens.SectionSlide : MotionTokens.PageSlide);
        var exit = -side * (vertical ? MotionTokens.SectionDepart : MotionTokens.PageDepart);
        return new(new(vertical ? 0 : enter, vertical ? enter : 0, MotionTokens.PageEnter, MotionTokens.SlideEasing, Lead(MotionTokens.PageFadeLead, MotionTokens.FadeEasing)),
            new(vertical ? 0 : exit, vertical ? exit : 0, MotionTokens.PageExit, MotionTokens.DepartEasing, MotionTokens.ExitEasing), false);
    }

    // Drill-in pushes from the right over a receding wall; return slides the detail away on top.
    public static MotionScene Detail(MotionDirection direction) => direction == MotionDirection.Back
        ? new(new(-MotionTokens.DetailRecede, 0, MotionTokens.ReturnEnter, MotionTokens.SlideEasing, Lead(MotionTokens.ReturnFadeLead, MotionTokens.FadeEasing)),
            new(MotionTokens.DetailReturn, 0, MotionTokens.ReturnExit, MotionTokens.DepartEasing, MotionTokens.ExitEasing), true)
        : new(new(MotionTokens.DetailTravel, 0, MotionTokens.DetailEnter, MotionTokens.SlideEasing, Lead(MotionTokens.DetailFadeLead, MotionTokens.FadeEasing)),
            new(-MotionTokens.DetailRecede, 0, MotionTokens.DetailExit, MotionTokens.DepartEasing, MotionTokens.ExitEasing), false);

    private static Easing Lead(double lead, Easing curve) => new LeadEasing(lead, curve);
}

// Holds zero for a leading fraction, then runs the inner curve: one timeline, no delay timer.
internal sealed class LeadEasing(double lead, Easing inner) : Easing
{
    public override double Ease(double progress) =>
        progress <= lead ? 0 : inner.Ease(Math.Min(1, (progress - lead) / (1 - lead)));
    public LeadEasing Copy() => new(lead, MotionChannel.Copy(inner));
}
