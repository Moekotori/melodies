using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Echo.Desktop.Theming;

// A fixed palette shared by visible playlist placeholders; rebuilt only with the theme.
internal static class PlaylistCoverPalette
{
    public static void Apply(ResourceDictionary resources, Func<string, Color> color, bool dark)
    {
        string[] tones = ["accent", "secondary", "success", "danger", "warning", "muted"];
        for (var index = 0; index < tones.Length; index++)
        {
            var tint = color(tones[index]);
            resources[$"PlaylistCoverBackground{index}"] = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = new()
                {
                    new(ThemeColorMath.Mix(color("panel"), tint, dark ? .24 : .18), 0),
                    new(ThemeColorMath.Mix(color("panel"), tint, dark ? .08 : .05), 1)
                }
            };
            resources[$"PlaylistCoverForeground{index}"] = new SolidColorBrush(ThemeColorMath.Mix(tint, color("heading"), .22));
        }
    }
}
