using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Echo.Desktop.Features.Lyrics;

// Stage controls use their own contrast palette, independent of the library theme.
internal static class LyricsChromePalette
{
    public static void Apply(Control owner, bool dark)
    {
        var resources = owner.Resources;
        resources["LyricsChromeInk"] = Brush(dark ? "#F4F4F7" : "#252833");
        resources["LyricsChromeSurface"] = Brush(dark ? "#D020222A" : "#DAFFFFFF");
        resources["LyricsChromeMenu"] = Brush(dark ? "#24262E" : "#FCFCFE");
        resources["LyricsChromeHover"] = Brush(dark ? "#28FFFFFF" : "#18252833");
        resources["LyricsChromeShade"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = [new(Color.Parse(dark ? "#30000000" : "#38FFFFFF"), 0), new(Colors.Transparent, 1)]
        };
    }

    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));
}
