using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Echo.Desktop.Features.Lyrics;

public partial class LyricsTransportView
{
    private bool keyboardInteraction;
    private readonly RectangleGeometry contentClip = new();
    public LyricsPlayerDisplay Display { get; private set; } = LyricsPlayerDisplay.Expanded;
    public bool IsCompact => Display == LyricsPlayerDisplay.Compact;
    public bool IsHidden => Display == LyricsPlayerDisplay.Hidden;
    public bool HasProtectedInteraction => (keyboardInteraction && IsKeyboardFocusWithin) ||
        signalFlyout.IsOpen || VolumeButton.Flyout?.IsOpen == true || MoreButton.Flyout?.IsOpen == true || Model?.Player.IsSeeking == true;
    public bool IsInteracting => IsPointerOver || HasProtectedInteraction;
    public void NotePointerActivity() => keyboardInteraction = false;
    public void ApplyMotion()
    {
        StopMotion();
        var policy = shell?.Motion;
        if (!UsesCapsuleChoreography && policy?.CanAnimate == true)
        {
            var fade = policy.Duration(TimeSpan.FromMilliseconds(120));
            ExpandedLayout.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = fade, Easing = new SplineEasing(.22, 0, .2, 1) }];
            IdleSurface.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = fade, Easing = new SplineEasing(.22, 0, .2, 1) }];
        }
        ApplyPresentation();
    }
    private void UpdateContentClip()
    {
        var bounds = CapsuleSurface.CapsuleBounds;
        var location = CapsuleSurface.TranslatePoint(bounds.TopLeft, ExpandedLayout)
            ?? new Point(bounds.X - BarSurface.Padding.Left, bounds.Y - BarSurface.Padding.Top);
        contentClip.Rect = new Rect(location, bounds.Size);
        contentClip.RadiusX = contentClip.RadiusY = Math.Max(0, bounds.Height / 2);
    }
    public void Present(LyricsPlayerDisplay display)
    {
        if (Display == display) return;
        Display = display; ApplyPresentation();
    }
    private void ApplyPresentation()
    {
        var expanded = Display == LyricsPlayerDisplay.Expanded;
        var hidden = Display == LyricsPlayerDisplay.Hidden;
        SetExpandedInteraction(expanded && (!UsesCapsuleChoreography || ExpandedLayout.Opacity >= .5));
        if (!UsesCapsuleChoreography)
        {
            expandedContentOffset.Y = idleContentOffset.Y = 0;
            ExpandedLayout.Opacity = expanded ? 1 : 0;
            IdleSurface.Opacity = Display == LyricsPlayerDisplay.Compact ? 1 : 0;
        }
        IdleSurface.IsHitTestVisible = Display == LyricsPlayerDisplay.Compact;
        if (Model?.Appearance.MiniPlayer != false) AnimateCapsule(expanded ? 1 : 0);
        else { CapsuleSurface.Transitions = null; ClearCapsuleMotion(); CapsuleSurface.Expansion = expanded ? 1 : 0; }
        UpdateMorphContent();
        UpdateContentClip();
        TransportLayout.IsHitTestVisible = !hidden;
        AnimatePlayerFrame(hidden);
    }
    public void StopMotion()
    {
        ExpandedLayout.Transitions = null; IdleSurface.Transitions = null;
        CapsuleSurface.Transitions = null; TransportLayout.Transitions = null;
        ClearCapsuleMotion();
        ClearFrameMotion();
    }
}
