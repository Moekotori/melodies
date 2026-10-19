using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace Echo.Visuals.Canvas;

// Shared by a scene's surfaces: 64 CSS font styles, each with at most 12 resolved typefaces.
// Returned fonts are borrowed until the next cache operation. The scene disposes the cache, not individual fonts.
public sealed partial class CanvasFontCache : IDisposable
{
    private sealed class Style(float size, SKFontStyle weight, string[] families) : IDisposable
    {
        private readonly List<SKFont> fonts = [];
        public SKFont Resolve(string text, int scalar)
        {
            foreach (var font in fonts) if (Covers(font.Typeface, text)) return font;
            SKTypeface? face = null;
            foreach (var family in families)
            {
                var candidate = SKFontManager.Default.MatchFamily(family, weight);
                if (candidate is null) continue;
                if (Covers(candidate, text)) { face = candidate; break; }
                candidate.Dispose();
            }
            face ??= SKFontManager.Default.MatchCharacter(null, weight, [], scalar) ?? SKTypeface.FromFamilyName("sans-serif", weight);
            if (fonts.Count == 12) { fonts[0].Typeface.Dispose(); fonts[0].Dispose(); fonts.RemoveAt(0); }
            var resolved = new SKFont(face, size) { Edging = SKFontEdging.Antialias, Subpixel = true,
                Embolden = weight.Weight >= 600 && face.FontWeight < 600 };
            fonts.Add(resolved); return resolved;
        }
        private static bool Covers(SKTypeface face, string text)
        {
            foreach (var rune in text.EnumerateRunes())
                if (!Rune.IsWhiteSpace(rune) && face.GetGlyph(rune.Value) == 0) return false;
            return true;
        }
        public void Dispose() { foreach (var font in fonts) { font.Typeface.Dispose(); font.Dispose(); } fonts.Clear(); weight.Dispose(); }
    }
    private readonly Dictionary<string, (Style Style, LinkedListNode<string> Node)> styles = [];
    private readonly LinkedList<string> recent = [];
    public SKFont Get(string css, string text)
    {
        if (!styles.TryGetValue(css, out var entry))
        {
            var match = FontPattern().Match(css);
            var size = match.Success ? float.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 16;
            var weight = match.Success && match.Groups[1].Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 400;
            var families = match.Success ? match.Groups[3].Value.Split(',').Select(s => s.Trim(' ', '\'', '"')).ToArray() : ["sans-serif"];
            if (styles.Count == 64)
            {
                var stale = recent.First!; styles[stale.Value].Style.Dispose(); styles.Remove(stale.Value); recent.RemoveFirst();
            }
            entry = (new Style(Math.Clamp(size, 1, 1024), new SKFontStyle(weight, 5, SKFontStyleSlant.Upright), families), recent.AddLast(css));
            styles.Add(css, entry);
        }
        else { recent.Remove(entry.Node); recent.AddLast(entry.Node); }
        var scalar = text.Length > 0 && Rune.TryGetRuneAt(text, 0, out var rune) ? rune.Value : ' ';
        return entry.Style.Resolve(text, scalar);
    }
    public void Dispose() { foreach (var entry in styles.Values) entry.Style.Dispose(); styles.Clear(); recent.Clear(); }
    [GeneratedRegex(@"(?:(\d{3})\s+)?([\d.]+)px\s+(.+)")]
    private static partial Regex FontPattern();
}
