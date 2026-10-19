using Avalonia;
using Avalonia.Media;
using Echo.Desktop.Features.Lyrics;

namespace Echo.Desktop.Controls.Lyrics;

// Shared reading overrides are loaded after the individual Electron stylesheets.
internal static class LyricsReadingPresentation
{
    public const string Sans = "avares://Echo.Desktop/Resources/Fonts#Outfit, PingFang SC, Microsoft YaHei UI, Noto Sans CJK SC, DejaVu Sans";
    public const string Serif = "Noto Serif SC, Source Han Serif SC, Songti SC, STSong, SimSun";

    public static string Font(LyricsLayout layout) => layout switch
    {
        LyricsLayout.Folded or LyricsLayout.Editorial or LyricsLayout.Slices or LyricsLayout.Curtain => Serif,
        LyricsLayout.Poster => "Bahnschrift, Arial Narrow, " + Sans,
        _ => Sans
    };

    public static double Opacity(int distance, double percent)
    {
        var alpha = (Math.Abs(distance), distance < 0) switch
        {
            (1, false) => 1.4694, (2, false) => 1.1020,
            (1, true) => 1.0612, (2, true) => .8163,
            (_, true) => .5714, _ => .7347
        };
        return Math.Clamp(percent / 100 * alpha, 0, 1);
    }

    public static double Blur(int distance) => Math.Abs(distance) switch
    {
        1 or 2 => 2,
        3 => distance < 0 ? 2.5 : 2.6,
        _ => 3
    };

    public static double Duration(double duration) => duration <= 0 ? 360 : duration < 100 ? 0 : duration < 180 ? 60 : 360;

    public static LinearGradientBrush Fade { get; } = new()
    {
        StartPoint = new(0, 0, RelativeUnit.Relative), EndPoint = new(0, 1, RelativeUnit.Relative),
        GradientStops = [new(Colors.Transparent, 0), new(Colors.White, .15), new(Colors.White, .82), new(Colors.Transparent, 1)]
    };
}
