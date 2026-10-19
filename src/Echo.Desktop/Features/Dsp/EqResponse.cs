using System.Numerics;

namespace Echo.Desktop.Features.Dsp;

// RBJ biquad response for the editor. This is a parameter model, not an output measurement.
internal static class EqResponse
{
    public static double Db(IReadOnlyList<EqBandViewModel> bands, double frequency, int rate, double strength, double tilt, string? scope = null)
    {
        var sum = 0d;
        foreach (var band in bands)
        {
            if (!band.Enabled || scope is not null && band.Scope.Key != "stereo" && band.Scope.Key != scope) continue;
            var w = 2 * Math.PI * Math.Clamp(band.FrequencyHz, band.IsCorrection ? 1 : 10,
                band.IsCorrection ? Math.BitDecrement(rate * 0.5) : rate * 0.45) / rate;
            var c = Math.Cos(w); var s = Math.Sin(w); var alpha = s / (2 * Math.Max(band.Q, band.QMinimum));
            var tiltOffset = band.Filter.Key is "peaking" or "lowShelf" or "highShelf"
                ? tilt * (2 * Math.Log10(Math.Clamp(band.FrequencyHz, 20, 20000) / 20) / 3 - 1) : 0;
            var a = Math.Pow(10, Math.Clamp(band.GainDb * strength / 100 + tiltOffset, band.GainMinimum, band.GainMaximum) / 40);
            double b0, b1, b2, a0, a1, a2;
            var shelfAlpha = s / 2 * Math.Sqrt(Math.Max(0, (a + 1 / a) * (1 / Math.Max(band.QMinimum, band.Q) - 1) + 2));
            var beta = 2 * Math.Sqrt(a) * shelfAlpha;
            switch (band.Filter.Key)
            {
                case "lowShelf":
                    b0 = a * ((a + 1) - (a - 1) * c + beta); b1 = 2 * a * ((a - 1) - (a + 1) * c); b2 = a * ((a + 1) - (a - 1) * c - beta);
                    a0 = (a + 1) + (a - 1) * c + beta; a1 = -2 * ((a - 1) + (a + 1) * c); a2 = (a + 1) + (a - 1) * c - beta; break;
                case "highShelf":
                    b0 = a * ((a + 1) + (a - 1) * c + beta); b1 = -2 * a * ((a - 1) + (a + 1) * c); b2 = a * ((a + 1) + (a - 1) * c - beta);
                    a0 = (a + 1) - (a - 1) * c + beta; a1 = 2 * ((a - 1) - (a + 1) * c); a2 = (a + 1) - (a - 1) * c - beta; break;
                case "lowPass": b0 = (1 - c) / 2; b1 = 1 - c; b2 = b0; a0 = 1 + alpha; a1 = -2 * c; a2 = 1 - alpha; break;
                case "highPass": b0 = (1 + c) / 2; b1 = -(1 + c); b2 = b0; a0 = 1 + alpha; a1 = -2 * c; a2 = 1 - alpha; break;
                case "notch": b0 = 1; b1 = -2 * c; b2 = 1; a0 = 1 + alpha; a1 = -2 * c; a2 = 1 - alpha; break;
                default: b0 = 1 + alpha * a; b1 = -2 * c; b2 = 1 - alpha * a; a0 = 1 + alpha / a; a1 = -2 * c; a2 = 1 - alpha / a; break;
            }
            var z = Complex.FromPolarCoordinates(1, -2 * Math.PI * frequency / rate);
            var denominator = a0 + a1 * z + a2 * z * z;
            var magnitude = ((b0 + b1 * z + b2 * z * z) / denominator).Magnitude;
            sum += 20 * Math.Log10(Math.Max(1e-6, magnitude));
        }
        return double.IsFinite(sum) ? sum : -120;
    }
    public static double Maximum(IReadOnlyList<EqBandViewModel> bands, int rate, double strength, double tilt)
    {
        var maximum = 0d;
        for (var i = 0; i <= 512; i++) maximum = Math.Max(maximum, Db(bands, 20 * Math.Pow(1000, i / 512d), rate, strength, tilt));
        return maximum;
    }
}
