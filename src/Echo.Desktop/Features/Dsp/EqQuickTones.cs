namespace Echo.Desktop.Features.Dsp;

// ECHO's restrained simple-tone voicings, interpolated in logarithmic frequency.
// Applying a tone produces an ordinary native EQ draft; no second DSP or hidden state.
internal sealed record EqQuickTone(string Label, (double Hz, double Db)[] Anchors)
{
    public double Gain(double hz)
    {
        if (hz <= Anchors[0].Hz) return Anchors[0].Db;
        for (var i = 1; i < Anchors.Length; i++)
        {
            var (upperHz, upperDb) = Anchors[i];
            if (hz > upperHz) continue;
            var (lowerHz, lowerDb) = Anchors[i - 1];
            var fraction = Math.Log(hz / lowerHz) / Math.Log(upperHz / lowerHz);
            return Math.Round(lowerDb + (upperDb - lowerDb) * fraction, 1);
        }
        return Anchors[^1].Db;
    }
}

internal static class EqQuickTones
{
    public static readonly double[] Frequencies = [20, 25, 31.5, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500, 630, 800, 1000, 1250, 1600, 2000, 2500, 3150, 4000, 5000, 6300, 8000, 10000, 12500, 16000, 20000];
    private static readonly Dictionary<string, EqQuickTone> Tones = new(StringComparer.Ordinal)
    {
        ["bass"] = new("低频增强", [(20, .4), (31.5, .8), (50, 1.2), (80, 1.5), (100, 1.3), (125, .9), (160, .5), (200, .1), (315, -.3), (500, -.1), (1000, 0), (10000, .1), (20000, 0)]),
        ["vocal"] = new("人声清晰", [(20, -.2), (80, -.3), (160, -.2), (315, 0), (500, .2), (800, .5), (1250, .8), (2000, 1), (3150, .6), (5000, .1), (6300, -.3), (8000, -.4), (12500, -.1), (20000, 0)]),
        ["air"] = new("空气感", [(20, 0), (1000, 0), (3150, .1), (5000, .3), (6300, .5), (8000, .7), (10000, .9), (12500, 1.1), (16000, 1), (20000, .6)]),
        ["warm"] = new("温暖", [(20, .2), (40, .5), (80, .8), (125, .8), (200, .6), (315, .4), (500, .2), (1000, 0), (2000, -.1), (4000, -.3), (8000, -.5), (12500, -.4), (20000, -.2)]),
        ["flat"] = new("平直", [(20, 0), (20000, 0)])
    };
    public static bool TryGet(string id, out EqQuickTone tone) => Tones.TryGetValue(id, out tone!);
}
