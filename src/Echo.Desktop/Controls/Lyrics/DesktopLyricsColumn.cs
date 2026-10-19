using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Echo.Desktop.Features.Floating;

namespace Echo.Desktop.Controls.Lyrics;

// Vertical-rl with mixed orientation: CJK stays upright, Latin runs rotate 90° clockwise.
internal sealed class DesktopLyricsColumn : IDisposable
{
    private static readonly Vector[] Outline = [new(-1.4, 0), new(1.4, 0), new(0, -1.4), new(0, 1.4), new(-1, -1), new(1, -1), new(-1, 1), new(1, 1)];
    private readonly Piece[] pieces;
    private readonly double advance;
    public double Width { get; }
    public double Height { get; }
    public int Layouts { get; }

    public DesktopLyricsColumn(string text, Typeface face, double size, Color playedTop, Color playedBottom, Color unplayedTop, Color unplayedBottom, Color outlinePlayed, Color outlineUnplayed, double lineAdvance = DesktopLyricsLayout.LineHeight, double columnWidth = 1, double tracking = 0)
    {
        advance = size * lineAdvance;
        var built = new List<Piece>();
        foreach (var (body, rotated) in Split(text))
        {
            var playedBrush = Brush(playedTop, playedBottom, rotated);
            var restBrush = Brush(unplayedTop, unplayedBottom, rotated);
            if (!rotated)
            {
                var lines = Math.Max(1, body.Count(c => c == '\n') + 1);
                using var probe = Shape(body, face, size, Brushes.Transparent, lines, 100000, TextAlignment.Left, lineAdvance, tracking);
                var maxWidth = Math.Max(1, probe.Width);
                built.Add(new Piece(
                    Shape(body, face, size, Brush(outlineUnplayed, outlineUnplayed, true), lines, maxWidth, TextAlignment.Center, lineAdvance, tracking),
                    Shape(body, face, size, Brush(outlinePlayed, outlinePlayed, true), lines, maxWidth, TextAlignment.Center, lineAdvance, tracking),
                    Shape(body, face, size, restBrush, lines, maxWidth, TextAlignment.Center, lineAdvance, tracking),
                    Shape(body, face, size, playedBrush, lines, maxWidth, TextAlignment.Center, lineAdvance, tracking),
                    Shape(body, face, size, new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)), lines, maxWidth, TextAlignment.Center, lineAdvance, tracking),
                    false));
                continue;
            }
            built.Add(new Piece(
                Shape(body, face, size, Brush(outlineUnplayed, outlineUnplayed, false), 1, 100000, TextAlignment.Left, lineAdvance, tracking),
                Shape(body, face, size, Brush(outlinePlayed, outlinePlayed, false), 1, 100000, TextAlignment.Left, lineAdvance, tracking),
                Shape(body, face, size, restBrush, 1, 100000, TextAlignment.Left, lineAdvance, tracking),
                Shape(body, face, size, playedBrush, 1, 100000, TextAlignment.Left, lineAdvance, tracking),
                Shape(body, face, size, new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)), 1, 100000, TextAlignment.Left, lineAdvance, tracking),
                true));
        }
        pieces = built.ToArray();
        double width = size;
        double height = 0;
        var layouts = 0;
        foreach (var piece in pieces)
        {
            width = Math.Max(width, piece.Rotated ? piece.Rest.Height : piece.Rest.Width);
            height += piece.Rotated ? piece.Rest.Width : piece.Rest.Height;
            layouts += 5;
        }
        Width = Math.Max(width, size * columnWidth);
        Height = Math.Max(height, size);
        Layouts = Math.Max(layouts, 1);
    }

    public void Draw(DrawingContext dc, double progress, bool strokeOn, bool shadowOn)
    {
        double y = 0;
        foreach (var piece in pieces)
        {
            DrawPiece(dc, piece, y, shadowOn, strokeOn, played: false);
            y += piece.Rotated ? piece.Rest.Width : piece.Rest.Height;
        }
        var reveal = Math.Clamp(progress, 0, 1) * Math.Max(1, Height);
        using (dc.PushClip(new Rect(-8, 0, Width + 16, reveal)))
        {
            y = 0;
            foreach (var piece in pieces)
            {
                DrawPiece(dc, piece, y, false, strokeOn, played: true);
                y += piece.Rotated ? piece.Rest.Width : piece.Rest.Height;
            }
        }
    }

    public double WrappedWidth(double height) => Width * Math.Max(1, Math.Ceiling(Height / WrapHeight(height)));
    private double WrapHeight(double height) => Math.Max(advance, Math.Floor(height / advance) * advance);
    public void DrawWrapped(DrawingContext dc, double progress, double height)
    {
        var page = WrapHeight(height); var count = Math.Max(1, (int)Math.Ceiling(Height / page));
        for (var i = 0; i < count; i++)
        {
            using (dc.PushClip(new Rect(-Width * i, 0, Width, page)))
            using (dc.PushTransform(Matrix.CreateTranslation(-Width * i, -page * i))) Draw(dc, progress, false, false);
        }
    }

    public void Dispose()
    {
        foreach (var piece in pieces) piece.Dispose();
    }

    private void DrawPiece(DrawingContext dc, Piece piece, double y, bool shadowOn, bool strokeOn, bool played)
    {
        if (!piece.Rotated)
        {
            var origin = new Point((Width - piece.Rest.Width) / 2, y);
            if (!played)
            {
                if (shadowOn) piece.Shadow.Draw(dc, origin + new Vector(0, 1));
                if (strokeOn)
                    foreach (var offset in Outline) piece.Stroke.Draw(dc, origin + offset);
                piece.Rest.Draw(dc, origin);
                return;
            }
            if (strokeOn)
                foreach (var offset in Outline) piece.PlayedStroke.Draw(dc, origin + offset);
            piece.Played.Draw(dc, origin);
            return;
        }
        var advance = piece.Rest.Width;
        var dest = new Point(Width / 2, y + advance / 2);
        var matrix = Matrix.CreateTranslation(-piece.Rest.Width / 2, -piece.Rest.Height / 2)
            * Matrix.CreateRotation(Math.PI / 2)
            * Matrix.CreateTranslation(dest.X, dest.Y);
        using (dc.PushTransform(matrix))
        {
            if (!played)
            {
                if (shadowOn) piece.Shadow.Draw(dc, new(0, 1));
                if (strokeOn)
                    foreach (var offset in Outline) piece.Stroke.Draw(dc, new(offset.X, offset.Y));
                piece.Rest.Draw(dc, default);
                return;
            }
            if (strokeOn)
                foreach (var offset in Outline) piece.PlayedStroke.Draw(dc, new(offset.X, offset.Y));
            piece.Played.Draw(dc, default);
        }
    }

    private static List<(string Body, bool Rotated)> Split(string text)
    {
        var result = new List<(string, bool)>();
        var buffer = new StringBuilder();
        bool? rotated = null;
        foreach (var rune in text.EnumerateRunes())
        {
            var turn = DesktopLyricsLayout.ChWidth(rune.ToString()) == 1;
            if (rotated is not null && turn != rotated)
            {
                result.Add((buffer.ToString(), rotated.Value));
                buffer.Clear();
            }
            if (rotated is null || turn != rotated)
            {
                rotated = turn;
                if (!turn) buffer.Clear();
            }
            if (!turn && buffer.Length > 0) buffer.Append('\n');
            buffer.Append(rune);
        }
        if (buffer.Length > 0 && rotated is not null) result.Add((buffer.ToString(), rotated.Value));
        if (result.Count == 0) result.Add((" ", false));
        return result;
    }

    private static TextLayout Shape(string text, Typeface face, double size, IBrush brush, int lines, double maxWidth, TextAlignment align, double lineAdvance, double tracking) => new(text, face, size, brush,
        textAlignment: align, textWrapping: TextWrapping.NoWrap, maxWidth: maxWidth, maxLines: lines, lineHeight: size * lineAdvance, letterSpacing: size * tracking);

    private static IBrush Brush(Color top, Color bottom, bool rotated) => top == bottom ? new SolidColorBrush(top) : new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = rotated ? new RelativePoint(0, 1, RelativeUnit.Relative) : new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops = { new(top, 0), new(bottom, 1) }
    };

    private sealed class Piece : IDisposable
    {
        public TextLayout Stroke { get; }
        public TextLayout PlayedStroke { get; }
        public TextLayout Rest { get; }
        public TextLayout Played { get; }
        public TextLayout Shadow { get; }
        public bool Rotated { get; }
        public Piece(TextLayout stroke, TextLayout playedStroke, TextLayout rest, TextLayout played, TextLayout shadow, bool rotated)
        {
            Stroke = stroke; PlayedStroke = playedStroke; Rest = rest; Played = played; Shadow = shadow; Rotated = rotated;
        }
        public void Dispose()
        {
            Stroke.Dispose(); PlayedStroke.Dispose(); Rest.Dispose(); Played.Dispose(); Shadow.Dispose();
        }
    }
}
