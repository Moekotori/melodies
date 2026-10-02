using Avalonia.Media;

namespace Echo.Desktop.Theming;

public static class ThemeColorMath
{
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
}
