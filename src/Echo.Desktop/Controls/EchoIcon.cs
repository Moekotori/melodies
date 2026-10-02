using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Echo.Desktop.Controls;

// The original two alpha layers are retained. Each instance owns at most two
// 64px masks (32 KiB total), disposed when it leaves the visual tree.
public class EchoIcon : Control
{
    public static readonly StyledProperty<string> KindProperty = AvaloniaProperty.Register<EchoIcon, string>(nameof(Kind), "home");
    public static readonly StyledProperty<string> ScopeProperty = AvaloniaProperty.Register<EchoIcon, string>(nameof(Scope), "sidebar");
    public static readonly StyledProperty<IBrush?> BaseBrushProperty = AvaloniaProperty.Register<EchoIcon, IBrush?>(nameof(BaseBrush));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty = AvaloniaProperty.Register<EchoIcon, IBrush?>(nameof(AccentBrush));
    public static readonly StyledProperty<double> StrokeWidthProperty = AvaloniaProperty.Register<EchoIcon, double>(nameof(StrokeWidth));
    public string Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public string Scope { get => GetValue(ScopeProperty); set => SetValue(ScopeProperty, value); }
    public IBrush? BaseBrush { get => GetValue(BaseBrushProperty); set => SetValue(BaseBrushProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public double StrokeWidth { get => GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }

    private IconLayer? _base;
    private IconLayer? _accent;
    private bool _attached;

    static EchoIcon() => AffectsRender<EchoIcon>(KindProperty, ScopeProperty, BaseBrushProperty, AccentBrushProperty, StrokeWidthProperty);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        LoadLayers();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        ReleaseLayers();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_attached && (change.Property == KindProperty || change.Property == ScopeProperty)) LoadLayers();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        _base?.Draw(context, bounds, BaseBrush ?? Brushes.Gray, StrokeWidth);
        _accent?.Draw(context, bounds, AccentBrush ?? Brushes.Gray, StrokeWidth);
    }

    private void LoadLayers()
    {
        ReleaseLayers();
        _base = IconLayer.Load(Scope, Kind, "base");
        _accent = IconLayer.Load(Scope, Kind, "accent");
        InvalidateVisual();
    }

    private void ReleaseLayers()
    {
        _base?.Dispose(); _accent?.Dispose();
        _base = null; _accent = null;
    }

    private sealed class IconLayer : IDisposable
    {
        private Bitmap? _bitmap;
        private ImageBrush? _mask;
        private readonly List<VectorPart> _parts = [];
        private Rect _viewBox = new(0, 0, 24, 24);

        public static IconLayer? Load(string scope, string kind, string layer)
        {
            // Only bundled names are resolved; user input cannot become a resource path.
            if (scope is not ("sidebar" or "player" or "glyph") || !kind.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) return null;
            var root = $"avares://Echo.Desktop/Resources/Icons/{scope}/{kind}-{layer}";
            var png = new Uri(root + ".png");
            var svg = new Uri(root + ".svg");
            var result = new IconLayer();
            if (AssetLoader.Exists(svg))
            {
                using var stream = AssetLoader.Open(svg);
                result.ReadSvg(XDocument.Load(stream));
            }
            else if (AssetLoader.Exists(png))
            {
                using var stream = AssetLoader.Open(png);
                result._bitmap = Bitmap.DecodeToWidth(stream, 64);
                result._mask = new ImageBrush(result._bitmap) { Stretch = Stretch.Fill };
            }
            else return null;
            return result;
        }

        private void ReadSvg(XDocument document)
        {
            if (document.Root is not { } root) return;
            var box = (root.Attribute("viewBox")?.Value ?? "0 0 24 24").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Parse).ToArray();
            if (box.Length == 4) _viewBox = new Rect(box[0], box[1], box[2], box[3]);
            foreach (var node in root.Descendants())
            {
                Geometry? geometry = node.Name.LocalName switch
                {
                    "path" => Geometry.Parse(node.Attribute("d")?.Value ?? ""),
                    "circle" => new EllipseGeometry(new Rect(N(node, "cx") - N(node, "r"), N(node, "cy") - N(node, "r"), N(node, "r") * 2, N(node, "r") * 2)),
                    "ellipse" => new EllipseGeometry(new Rect(N(node, "cx") - N(node, "rx"), N(node, "cy") - N(node, "ry"), N(node, "rx") * 2, N(node, "ry") * 2)),
                    "rect" => new RectangleGeometry(new Rect(N(node, "x"), N(node, "y"), N(node, "width"), N(node, "height")), N(node, "rx"), N(node, "rx")),
                    "line" => Geometry.Parse(FormattableString.Invariant($"M{N(node, "x1")} {N(node, "y1")} L{N(node, "x2")} {N(node, "y2")}")),
                    "polyline" => Geometry.Parse("M" + (node.Attribute("points")?.Value ?? "")),
                    "polygon" => Geometry.Parse("M" + (node.Attribute("points")?.Value ?? "") + "Z"),
                    _ => null
                };
                if (geometry is null) continue;
                _parts.Add(new VectorPart(geometry, Inherited(node, "fill") != "none",
                    Inherited(node, "stroke") is { } stroke && stroke != "none" ? Parse(Inherited(node, "stroke-width") ?? "1") : 0));
            }
        }

        public void Draw(DrawingContext context, Rect bounds, IBrush brush, double strokeWidth)
        {
            if (_mask is not null)
            {
                using (context.PushOpacityMask(_mask, bounds)) context.DrawRectangle(brush, null, bounds);
                return;
            }
            var transform = Matrix.CreateTranslation(-_viewBox.X, -_viewBox.Y) * Matrix.CreateScale(bounds.Width / _viewBox.Width, bounds.Height / _viewBox.Height);
            using (context.PushTransform(transform))
                foreach (var part in _parts)
                    context.DrawGeometry(part.Filled ? brush : null,
                        part.Stroke > 0 ? new Pen(brush, strokeWidth > 0 ? strokeWidth : part.Stroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round) : null, part.Geometry);
        }

        private static string? Inherited(XElement node, string attribute) => node.AncestorsAndSelf().Select(n => n.Attribute(attribute)?.Value).FirstOrDefault(v => v is not null);
        private static double N(XElement node, string name) => Parse(node.Attribute(name)?.Value ?? "0");
        private static double Parse(string value) => double.Parse(value, CultureInfo.InvariantCulture);
        public void Dispose() { _bitmap?.Dispose(); _bitmap = null; _mask = null; _parts.Clear(); }
        private sealed record VectorPart(Geometry Geometry, bool Filled, double Stroke);
    }
}
