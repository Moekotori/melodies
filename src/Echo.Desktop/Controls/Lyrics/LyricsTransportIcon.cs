using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Echo.Desktop.Controls.Lyrics;

// Small, optically balanced transport symbols; immutable vector geometry, no bitmap masks.
public sealed class LyricsTransportIcon : Control
{
    public static readonly StyledProperty<string> KindProperty = AvaloniaProperty.Register<LyricsTransportIcon, string>(nameof(Kind), "play");
    public static readonly StyledProperty<IBrush?> InkProperty = AvaloniaProperty.Register<LyricsTransportIcon, IBrush?>(nameof(Ink));
    public string Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public IBrush? Ink { get => GetValue(InkProperty); set => SetValue(InkProperty, value); }
    private readonly Pen outline = new(Brushes.White, 1.7) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
    private sealed record Symbol(Geometry? Fill, Geometry? Stroke);
    private static Symbol Shape(string? fill = null, string? stroke = null) => new(
        fill is null ? null : Geometry.Parse(fill), stroke is null ? null : Geometry.Parse(stroke));
    private const string Heart = "M12 20.1 C10.5 19 3.1 14.4 3.1 8.9 C3.1 4.2 8.8 2.6 12 6.7 C15.2 2.6 20.9 4.2 20.9 8.9 C20.9 14.4 13.5 19 12 20.1 Z";
    private static readonly IReadOnlyDictionary<string, Symbol> Symbols = new Dictionary<string, Symbol>
    {
        ["play"] = Shape("M7 4.8 Q7 3.6 8.2 4.3 L20 11.1 Q21.5 12 20 12.9 L8.2 19.7 Q7 20.4 7 19.2 Z"),
        ["pause"] = Shape("M6 4 H9 Q10 4 10 5 V19 Q10 20 9 20 H6 Q5 20 5 19 V5 Q5 4 6 4 Z M15 4 H18 Q19 4 19 5 V19 Q19 20 18 20 H15 Q14 20 14 19 V5 Q14 4 15 4 Z"),
        ["previous"] = Shape("M3 5 H4.5 V19 H3 Z M12 5.6 Q12 4.8 11.2 5.4 L4.9 11.4 Q4.3 12 4.9 12.6 L11.2 18.6 Q12 19.2 12 18.4 Z M21 5.6 Q21 4.8 20.2 5.4 L13.9 11.4 Q13.3 12 13.9 12.6 L20.2 18.6 Q21 19.2 21 18.4 Z"),
        ["next"] = Shape("M19.5 5 H21 V19 H19.5 Z M3 5.6 Q3 4.8 3.8 5.4 L10.1 11.4 Q10.7 12 10.1 12.6 L3.8 18.6 Q3 19.2 3 18.4 Z M12 5.6 Q12 4.8 12.8 5.4 L19.1 11.4 Q19.7 12 19.1 12.6 L12.8 18.6 Q12 19.2 12 18.4 Z"),
        ["heart"] = Shape(stroke: Heart), ["heart-filled"] = Shape(Heart),
        ["queue"] = Shape(stroke: "M4 6.5 H20 M4 12 H20 M4 17.5 H14"),
        ["sequence"] = Shape(stroke: "M4 6.5 H20 M4 12 H20 M4 17.5 H14"),
        ["repeat"] = Shape(stroke: "M5 9 V8 Q5 5.5 8 5.5 H19 M16 2.5 L19 5.5 L16 8.5 M19 15 V16 Q19 18.5 16 18.5 H5 M8 15.5 L5 18.5 L8 21.5"),
        ["repeat-one"] = Shape(stroke: "M5 9 V8 Q5 5.5 8 5.5 H19 M16 2.5 L19 5.5 L16 8.5 M19 15 V16 Q19 18.5 16 18.5 H5 M8 15.5 L5 18.5 L8 21.5 M11 11 L12.5 10 V14.5"),
        ["shuffle"] = Shape(stroke: "M3 6 H5 C10 6 12 18 17 18 H21 M18 15 L21 18 L18 21 M3 18 H5 C7 18 8 16 9 14 M12 10 C14 7 15 6 17 6 H21 M18 3 L21 6 L18 9"),
        ["captions"] = Shape(stroke: "M5 5 H19 Q21 5 21 7 V17 Q21 19 19 19 H5 Q3 19 3 17 V7 Q3 5 5 5 Z M7 9 H11 M7 13 H17 M14 9 H17"),
        ["volume"] = Shape("M3 9 H7 L13 4.5 V19.5 L7 15 H3 Q2 15 2 14 V10 Q2 9 3 9 Z", "M16 8 Q19 12 16 16 M19 5 Q24 12 19 19")
    };

    static LyricsTransportIcon() => AffectsRender<LyricsTransportIcon>(KindProperty, InkProperty);
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == InkProperty) outline.Brush = Ink ?? Brushes.White;
    }
    public override void Render(DrawingContext context)
    {
        var scale = Math.Min(Bounds.Width, Bounds.Height) / 24;
        if (scale <= 0) return;
        using var transform = context.PushTransform(Matrix.CreateScale(scale, scale)
            * Matrix.CreateTranslation((Bounds.Width - 24 * scale) / 2, (Bounds.Height - 24 * scale) / 2));
        var ink = Ink ?? Brushes.White;
        if (Kind == "more")
        {
            for (var x = 5; x <= 19; x += 7) context.DrawEllipse(ink, null, new Point(x, 12), 1.4, 1.4);
            return;
        }
        if (!Symbols.TryGetValue(Kind, out var symbol)) return;
        if (symbol.Fill is { } fill) context.DrawGeometry(ink, null, fill);
        if (symbol.Stroke is { } stroke) context.DrawGeometry(null, outline, stroke);
    }
}
