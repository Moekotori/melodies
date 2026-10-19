using Avalonia.Input;
using Avalonia.Media;

namespace Echo.Desktop.Features.Lyrics;

public partial class LyricsTransportView
{
    private readonly TranslateTransform expandedContentOffset = new();
    private readonly TranslateTransform idleContentOffset = new();
    private bool UsesCapsuleChoreography => Model?.Appearance.MiniPlayer != false && shell?.Motion.UseSpatialMotion == true;

    private void UpdateMorphContent()
    {
        if (!UsesCapsuleChoreography) return;
        // Shape, ink and small offsets share one finite animation. Reversing the
        // capsule therefore reverses the content from exactly its current pose.
        var progress = Math.Clamp(CapsuleSurface.Expansion, 0, 1);
        var expanded = Reveal(progress, .42, .94);
        var idle = 1 - Reveal(progress, .04, .3);
        ExpandedLayout.Opacity = expanded;
        IdleSurface.Opacity = idle;
        expandedContentOffset.Y = (1 - expanded) * (shell?.Motion.Travel(3) ?? 0);
        idleContentOffset.Y = -(1 - idle) * (shell?.Motion.Travel(1.5) ?? 0);
        SetExpandedInteraction(Display == LyricsPlayerDisplay.Expanded && expanded >= .5);
    }

    private void SetExpandedInteraction(bool enabled)
    {
        BarSurface.IsHitTestVisible = enabled;
        KeyboardNavigation.SetTabNavigation(BarSurface, enabled ? KeyboardNavigationMode.Continue : KeyboardNavigationMode.None);
    }

    private static double Reveal(double progress, double start, double end)
    {
        var value = Math.Clamp((progress - start) / (end - start), 0, 1);
        return value * value * (3 - 2 * value);
    }
}
