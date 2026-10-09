using Avalonia.Media;

namespace Echo.Desktop.Features.Dsp;

internal static class DspBandPalette
{
    private static readonly IBrush[] Colors = [new SolidColorBrush(Color.Parse("#5C6FE6")),
        new SolidColorBrush(Color.Parse("#3C9EAE")), new SolidColorBrush(Color.Parse("#3E9A6E")),
        new SolidColorBrush(Color.Parse("#C4923A")), new SolidColorBrush(Color.Parse("#D16B78")),
        new SolidColorBrush(Color.Parse("#8B68D6")), new SolidColorBrush(Color.Parse("#5E86D4"))];
    public static IBrush Brush(int index) => Colors[((index % Colors.Length) + Colors.Length) % Colors.Length];
}
