using Avalonia.Media;

namespace Echo.Desktop.Features.Dsp;

internal static class DspBandPalette
{
    private static readonly IBrush[] Colors = [new SolidColorBrush(Color.Parse("#4f9cbc")),
        new SolidColorBrush(Color.Parse("#bd9851")), new SolidColorBrush(Color.Parse("#9667c1")),
        new SolidColorBrush(Color.Parse("#599e91")), new SolidColorBrush(Color.Parse("#c17b84")),
        new SolidColorBrush(Color.Parse("#6489c6"))];
    public static IBrush Brush(int index) => Colors[Math.Clamp(index / 5, 0, Colors.Length - 1)];
}
