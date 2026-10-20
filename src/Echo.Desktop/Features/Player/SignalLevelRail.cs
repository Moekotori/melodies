using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Echo.Desktop.Features.Player;

// Live level meter: tinted zones, peak fill, RMS strip and a held peak tick. The owner publishes sparse frames; this control
// eases between them on animation frames and stops requesting frames as soon as it has settled, so a paused or hidden panel draws nothing.
public sealed class SignalLevelRail : Control
{
    private const double RailTop = 3, RailHeight = 6, AttackMs = 45, ReleaseMs = 230, RmsMs = 130, HoldMs = 1200, HoldFallPerSecond = 55;
    private static readonly (double From, double To, Color Tint)[] Zones =
    [
        (0, 60, Color.FromArgb(0x1C, 0x4B, 0x8D, 0xE5)), (60, 80, Color.FromArgb(0x1F, 0x55, 0xC9, 0x9D)),
        (80, 92, Color.FromArgb(0x24, 0xE5, 0xBD, 0x58)), (92, 100, Color.FromArgb(0x26, 0xE5, 0x62, 0x72))
    ];
    private static readonly IBrush Gradient = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops =
        {
            new(Color.Parse("#506fe0"), 0), new(Color.Parse("#4b8de5"), 0.4), new(Color.Parse("#37bfc7"), 0.6),
            new(Color.Parse("#55c99d"), 0.8), new(Color.Parse("#e5bd58"), 0.92), new(Color.Parse("#e56272"), 1)
        }
    }.ToImmutable();
    private static readonly IBrush RmsBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x78, 255, 255, 255));
    private static readonly IBrush TickBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x55, 0x80, 0x88, 0x99));
    private static readonly IBrush[] ZoneBrushes = Zones.Select(zone => (IBrush)new ImmutableSolidColorBrush(zone.Tint)).ToArray();
    private static readonly IBrush[] ZoneSolid =
        new[] { "#4b8de5", "#55c99d", "#e5bd58", "#e56272" }.Select(c => (IBrush)new ImmutableSolidColorBrush(Color.Parse(c))).ToArray();

    public static readonly StyledProperty<double> PercentProperty = AvaloniaProperty.Register<SignalLevelRail, double>(nameof(Percent));
    public static readonly StyledProperty<double> RmsPercentProperty = AvaloniaProperty.Register<SignalLevelRail, double>(nameof(RmsPercent));
    public double Percent { get => GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    public double RmsPercent { get => GetValue(RmsPercentProperty); set => SetValue(RmsPercentProperty, value); }

    private double shown, shownRms, hold;
    private TimeSpan? last;
    private TimeSpan holdUntil;
    private bool pending;

    static SignalLevelRail() => AffectsRender<SignalLevelRail>(PercentProperty, RmsPercentProperty);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PercentProperty || change.Property == RmsPercentProperty) Schedule();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        pending = false; last = null; shown = shownRms = hold = 0;
    }

    private void Schedule()
    {
        if (pending || TopLevel.GetTopLevel(this) is not { } top) return;
        pending = true; top.RequestAnimationFrame(Tick);
    }

    private void Tick(TimeSpan now)
    {
        pending = false;
        if (!IsEffectivelyVisible) { last = null; return; }
        var elapsed = last is { } previous ? Math.Clamp((now - previous).TotalMilliseconds, 0, 100) : 16;
        last = now;
        var target = Math.Clamp(Percent, 0, 100);
        var targetRms = Math.Clamp(RmsPercent, 0, 100);
        shown = Ease(shown, target, elapsed, target > shown ? AttackMs : ReleaseMs);
        shownRms = Ease(shownRms, targetRms, elapsed, RmsMs);
        if (shown >= hold) { hold = shown; holdUntil = now + TimeSpan.FromMilliseconds(HoldMs); }
        else if (now > holdUntil) hold = Math.Max(shown, hold - HoldFallPerSecond * elapsed / 1000);
        InvalidateVisual();
        if (Math.Abs(target - shown) > .05 || Math.Abs(targetRms - shownRms) > .05 || hold > shown + .05) Schedule();
        else { last = null; shown = target; shownRms = targetRms; hold = Math.Max(hold, shown); }
    }
    private static double Ease(double value, double target, double elapsedMs, double tauMs) =>
        value + (target - value) * (1 - Math.Exp(-elapsedMs / tauMs));

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width;
        if (width <= 0 || Bounds.Height <= 0) return;
        var rail = new Rect(0, RailTop, width, RailHeight);
        using (context.PushClip(new RoundedRect(rail, 3)))
        {
            for (var i = 0; i < Zones.Length; i++)
                context.DrawRectangle(ZoneBrushes[i], null, new Rect(width * Zones[i].From / 100, RailTop, width * (Zones[i].To - Zones[i].From) / 100, RailHeight));
            var fill = width * Math.Clamp(shown, 0, 100) / 100;
            if (fill > 0)
                using (context.PushClip(new Rect(0, RailTop, fill, RailHeight)))
                {
                    context.DrawRectangle(Gradient, null, rail);
                    var rms = width * Math.Clamp(shownRms, 0, 100) / 100;
                    if (rms > 0) context.DrawRectangle(RmsBrush, null, new Rect(0, RailTop + RailHeight - 2, Math.Min(rms, fill), 2));
                }
        }
        foreach (var tick in new[] { 40d, 60d, 80d })
            context.DrawRectangle(TickBrush, null, new Rect(Math.Round(width * tick / 100), RailTop + RailHeight + 2, 1, 3));
        if (hold > 1)
        {
            var x = Math.Clamp(width * hold / 100, 1, width - 1);
            context.DrawRectangle(HoldBrush(hold), null, new Rect(x - 1, RailTop - 3, 2, RailHeight + 6), 1, 1);
        }
    }
    private static IBrush HoldBrush(double percent) => percent switch
    {
        >= 92 => ZoneSolid[3], >= 80 => ZoneSolid[2], >= 60 => ZoneSolid[1], _ => ZoneSolid[0]
    };
}
