using Avalonia.Media;

namespace Echo.Desktop.Theming;

public static class ThemeColorMath
{
    public static (double Hue, double Saturation, double Brightness) ToHsv(Color color)
    {
        var r = color.R / 255d; var g = color.G / 255d; var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var range = max - min;
        var hue = range == 0 ? 0 : max == r ? 60 * ((g - b) / range % 6)
            : max == g ? 60 * ((b - r) / range + 2) : 60 * ((r - g) / range + 4);
        return ((hue + 360) % 360, max == 0 ? 0 : range / max, max);
    }
    public static Color FromHsv(double hue, double saturation, double brightness)
    {
        hue = double.IsFinite(hue) ? (hue % 360 + 360) % 360 : 0;
        saturation = double.IsFinite(saturation) ? Math.Clamp(saturation, 0, 1) : 0;
        brightness = double.IsFinite(brightness) ? Math.Clamp(brightness, 0, 1) : 0;
        var chroma = brightness * saturation;
        var second = chroma * (1 - Math.Abs(hue / 60 % 2 - 1)); var offset = brightness - chroma;
        var (r, g, b) = hue switch
        {
            < 60 => (chroma, second, 0d), < 120 => (second, chroma, 0d), < 180 => (0d, chroma, second),
            < 240 => (0d, second, chroma), < 300 => (second, 0d, chroma), _ => (chroma, 0d, second)
        };
        byte Channel(double v) => (byte)Math.Clamp(Math.Round((v + offset) * 255), 0, 255);
        return Color.FromRgb(Channel(r), Channel(g), Channel(b));
    }
    public static bool TryHex(string? value, out string hex)
    {
        hex = "";
        if (value is null || !value.StartsWith('#') || value.Length is not (4 or 7 or 9) || !Color.TryParse(value, out var color)) return false;
        hex = color.A == 255 ? $"#{color.R:x2}{color.G:x2}{color.B:x2}" : color.ToString(); return true;
    }
    public static double Contrast(string foreground, string background)
    {
        static double L(Color c)
        {
            static double C(byte b) { var n = b / 255d; return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); }
            return .2126 * C(c.R) + .7152 * C(c.G) + .0722 * C(c.B);
        }
        var fg = Color.Parse(foreground); var bg = Color.Parse(background);
        fg = Mix(bg, fg, fg.A / 255d);
        var a = L(fg); var b = L(bg); return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
    public static Color Mix(Color a, Color b, double weight) => Color.FromRgb(
        (byte)Math.Clamp(a.R + (b.R - a.R) * weight, 0, 255), (byte)Math.Clamp(a.G + (b.G - a.G) * weight, 0, 255),
        (byte)Math.Clamp(a.B + (b.B - a.B) * weight, 0, 255));
    public static Color Saturate(Color c, double amount)
    {
        var gray = .2126 * c.R + .7152 * c.G + .0722 * c.B;
        byte C(byte v) => (byte)Math.Clamp(gray + (v - gray) * amount, 0, 255);
        return Color.FromArgb(c.A, C(c.R), C(c.G), C(c.B));
    }

    // Legacy presets use an opaque text-like gray for borders. Keep its hue, but
    // bound decorative contrast; focus and selected accents keep their full color.
    public static Color SurfaceBorder(Color border, Color background, double maxContrast)
    {
        Color At(double weight)
        {
            var mixed = Mix(background, border, weight);
            return Color.FromArgb(border.A, mixed.R, mixed.G, mixed.B);
        }
        var low = 0d; var high = 1d;
        if (Contrast(border.ToString(), background.ToString()) <= maxContrast) return border;
        for (var i = 0; i < 8; i++)
        {
            var middle = (low + high) / 2;
            if (Contrast(At(middle).ToString(), background.ToString()) <= maxContrast) low = middle;
            else high = middle;
        }
        return At(low);
    }

    public static Color PageGradientStop(Color primary, Color stop)
    {
        var mixed = Mix(primary, stop, .22);
        return Color.FromArgb(stop.A, mixed.R, mixed.G, mixed.B);
    }
}
