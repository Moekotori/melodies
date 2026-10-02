using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Echo.Desktop.Theming;

// Eight native wash styles. At most six gradient layers; no images or per-row effects.
public sealed class RainbowBackdrop : Control
{
    public string Style { get; set; } = "aurora";
    public double Phase { get; set; }
    public double Intensity { get; set; } = 1;
    public bool Dark { get; set; }
    public override void Render(DrawingContext context)
    {
        if (Style == "accents" || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        using (context.PushClip(new Rect(Bounds.Size)))
        using (context.PushOpacity(Math.Min(.85, (Dark ? .36 : .28) * Intensity)))
        {
            var phase = Phase * Math.PI * 2;
            var stops = new GradientStops();
            for (var i = 0; i <= 6; i++) stops.Add(new(ThemeColorMath.FromHsv((Phase * 360 + i * 60) % 360, .68, 1), i / 6d));
            if (Style is "aurora" or "ribbon" or "prism" or "tide")
            {
                var slope = Style == "prism" ? Math.Sin(phase) : Style == "tide" ? .9 : .25;
                var brush = new LinearGradientBrush { StartPoint = new RelativePoint(-.5 + Phase % 1, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1.5 + Phase % 1, slope, RelativeUnit.Relative), GradientStops = stops };
                if (Style == "ribbon") using (context.PushOpacity(.7)) context.FillRectangle(brush, new Rect(0, Bounds.Height * .28, Bounds.Width, Bounds.Height * .44));
                else context.FillRectangle(brush, new Rect(Bounds.Size));
            }
            else if (Style == "wheel")
            {
                var brush = new ConicGradientBrush { Center = new RelativePoint(.5, .5, RelativeUnit.Relative), Angle = Phase * 360, GradientStops = stops };
                context.FillRectangle(brush, new Rect(Bounds.Size));
            }
            else
            {
                var count = Style == "breathe" ? 1 : 4;
                for (var i = 0; i < count; i++)
                {
                    var c = ThemeColorMath.FromHsv((Phase * 360 + i * 90) % 360, .65, 1);
                    var x = .5 + Math.Sin(phase + i * 1.8) * .3; var y = .5 + Math.Cos(phase * .7 + i) * .3;
                    var brush = new RadialGradientBrush { Center = new RelativePoint(x, y, RelativeUnit.Relative), GradientOrigin = new RelativePoint(x, y, RelativeUnit.Relative),
                        RadiusX = new RelativeScalar(.65, RelativeUnit.Relative), RadiusY = new RelativeScalar(.8, RelativeUnit.Relative), GradientStops = new() { new(c, 0), new(Color.FromArgb(0, c.R, c.G, c.B), 1) } };
                    using (context.PushOpacity(Style == "breathe" ? .5 + .5 * Math.Sin(phase) : .7)) context.FillRectangle(brush, new Rect(Bounds.Size));
                }
            }
        }
    }
}
