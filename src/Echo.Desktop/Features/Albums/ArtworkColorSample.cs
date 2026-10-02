using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Echo.Desktop.Features.Albums;

public sealed record ArtworkColorSample(Color Average, Color Dominant)
{
    // Runs in the existing artwork worker. Same hue-bin/mid-tone weighting as lyricsReadableColor.ts.
    internal static ArtworkColorSample? Read(Bitmap bitmap)
    {
        try { return ReadPixels(bitmap); }
        catch (Exception error) when (error is NotSupportedException or InvalidOperationException or ArgumentException) { return null; }
    }
    private static ArtworkColorSample? ReadPixels(Bitmap bitmap)
    {
        using var copy = new WriteableBitmap(bitmap.PixelSize, new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var pixels = copy.Lock(); bitmap.CopyPixels(pixels);
        var bins = new double[24, 4]; double r = 0, g = 0, b = 0, total = 0;
        var stepX = Math.Max(1, bitmap.PixelSize.Width / 32); var stepY = Math.Max(1, bitmap.PixelSize.Height / 32);
        for (var y = 0; y < bitmap.PixelSize.Height; y += stepY)
            for (var x = 0; x < bitmap.PixelSize.Width; x += stepX)
            {
                var offset = y * pixels.RowBytes + x * 4;
                var alpha = Marshal.ReadByte(pixels.Address, offset + 3) / 255d;
                if (alpha < .08) continue;
                var red = Marshal.ReadByte(pixels.Address, offset); var green = Marshal.ReadByte(pixels.Address, offset + 1); var blue = Marshal.ReadByte(pixels.Address, offset + 2);
                r += red * alpha; g += green * alpha; b += blue * alpha; total += alpha;
                var max = Math.Max(red, Math.Max(green, blue)) / 255d; var min = Math.Min(red, Math.Min(green, blue)) / 255d;
                var lightness = (max + min) / 2; var delta = max - min;
                var saturation = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * lightness - 1));
                if (saturation < .12 || lightness is < .1 or > .92) continue;
                var hue = max == red / 255d ? ((green - blue) / 255d / delta + 6) % 6
                    : max == green / 255d ? (blue - red) / 255d / delta + 2 : (red - green) / 255d / delta + 4;
                var bin = Math.Clamp((int)(hue * 4), 0, 23);
                var weight = alpha * (.35 + saturation * 1.9) * (1 - Math.Min(.82, Math.Abs(lightness - .55) * 1.4));
                bins[bin, 0] += red * weight; bins[bin, 1] += green * weight; bins[bin, 2] += blue * weight; bins[bin, 3] += weight;
            }
        if (total == 0) return null;
        var winner = 0; double best = 0;
        for (var i = 0; i < 24; i++)
        {
            var weight = bins[i, 3] + (bins[(i + 23) % 24, 3] + bins[(i + 1) % 24, 3]) * .64;
            if (weight > best) { best = weight; winner = i; }
        }
        var average = Rgb(r / total, g / total, b / total);
        if (best == 0) return new(average, average);
        r = g = b = 0; double dominantTotal = 0;
        foreach (var index in new[] { (winner + 23) % 24, winner, (winner + 1) % 24 })
        { r += bins[index, 0]; g += bins[index, 1]; b += bins[index, 2]; dominantTotal += bins[index, 3]; }
        return new(average, Rgb(r / dominantTotal, g / dominantTotal, b / dominantTotal));
    }
    private static Color Rgb(double r, double g, double b) => Color.FromRgb((byte)Math.Clamp(Math.Round(r), 0, 255), (byte)Math.Clamp(Math.Round(g), 0, 255), (byte)Math.Clamp(Math.Round(b), 0, 255));
}
