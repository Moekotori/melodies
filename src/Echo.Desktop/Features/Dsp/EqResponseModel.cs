using System.Numerics;

namespace Echo.Desktop.Features.Dsp;

internal readonly record struct EqResponseStage(string Scope, double B0, double B1, double B2, double A1, double A2)
{
    public Complex At(Complex z, Complex z2) => (B0 + B1 * z + B2 * z2) / (1 + A1 * z + A2 * z2);
    public bool IsIdentity => B0 == 1 && B1 == A1 && B2 == A2;
}

// Control-side static model. Coefficients are prepared once per edit, never per plot point.
// Ordered complex matrices follow the native L/R and 0.5 M/S encode/decode exactly.
internal sealed class EqResponseModel
{
    private readonly EqResponseStage[] stages;
    private readonly int rate;
    public bool HasScopedBands { get; }
    public bool UsesMidSide { get; }
    public bool HasCrossRoutes { get; }
    public double DynamicReserveDb { get; }

    public EqResponseModel(IReadOnlyList<EqBandViewModel> bands, int rate, double strength, double tilt)
    {
        this.rate = rate;
        stages = bands.Where(b => b.Enabled).Select(b => EqResponse.Prepare(b, rate, strength, tilt)).Where(s => !s.IsIdentity).ToArray();
        HasScopedBands = stages.Any(s => s.Scope != "stereo");
        UsesMidSide = stages.Any(s => s.Scope is "mid" or "side") && !stages.Any(s => s.Scope is "left" or "right");
        HasCrossRoutes = stages.Any(s => s.Scope is "mid" or "side") && stages.Any(s => s.Scope is "left" or "right");
        DynamicReserveDb = Math.Min(4, bands.Count(b => b.Enabled && b.SupportsDynamic && b.DynamicMode.Key == "up")) * 6;
    }

    internal EqTransferMatrix At(double hz)
    {
        var z = Complex.FromPolarCoordinates(1, -2 * Math.PI * hz / rate);
        var z2 = z * z;
        if (!HasScopedBands)
        {
            var scalar = Complex.One;
            foreach (var stage in stages) scalar *= stage.At(z, z2);
            return new(scalar, 0, 0, scalar);
        }
        var result = EqTransferMatrix.Identity;
        foreach (var stage in stages)
        {
            var h = stage.At(z, z2);
            var step = stage.Scope switch
            {
                "left" => new EqTransferMatrix(h, 0, 0, 1),
                "right" => new EqTransferMatrix(1, 0, 0, h),
                "mid" => new EqTransferMatrix((1 + h) / 2, (h - 1) / 2, (h - 1) / 2, (1 + h) / 2),
                "side" => new EqTransferMatrix((1 + h) / 2, (1 - h) / 2, (1 - h) / 2, (1 + h) / 2),
                _ => new EqTransferMatrix(h, 0, 0, h)
            };
            result = step * result;
        }
        return result;
    }

    public double Db(double hz, string scope = "stereo")
    {
        var matrix = At(hz);
        if (scope is "mid" or "side") matrix = matrix.InMidSide();
        return GainDb(scope switch { "right" or "side" => matrix.D, "rightToLeft" => matrix.B, "leftToRight" => matrix.C, _ => matrix.A });
    }

    // Frequency-domain estimate, including narrow resonances and dynamic upward reserve.
    // This cannot predict sample peaks, other DSP stages, or inter-sample overs.
    public double Maximum(IReadOnlyList<EqBandViewModel> bands)
    {
        if (stages.Length == 0) return DynamicReserveDb;
        var peak = 1d;
        void Sample(double hz) { if (hz >= 0 && hz < rate * .5) peak = Math.Max(peak, At(hz).PeakGain); }
        Sample(0); Sample(rate * .499999);
        for (var i = 0; i <= 512; i++) Sample(10 * Math.Pow(rate * .0499999, i / 512d));
        foreach (var band in bands)
        {
            if (!band.Enabled) continue;
            var center = Math.Clamp(band.FrequencyHz, band.FrequencyMinimum, rate * (band.IsCorrection ? .499999 : .45));
            for (var i = -16; i <= 16; i++) Sample(center * Math.Exp(i / (16 * Math.Max(.1, band.Q))));
        }
        return Math.Max(0, 20 * Math.Log10(peak)) + DynamicReserveDb;
    }

    internal static double GainDb(Complex value) => 20 * Math.Log10(Math.Max(1e-9, value.Magnitude));
}

internal readonly record struct EqTransferMatrix(Complex A, Complex B, Complex C, Complex D)
{
    public static EqTransferMatrix Identity => new(1, 0, 0, 1);
    public static EqTransferMatrix operator *(EqTransferMatrix x, EqTransferMatrix y)
        => new(x.A * y.A + x.B * y.C, x.A * y.B + x.B * y.D,
            x.C * y.A + x.D * y.C, x.C * y.B + x.D * y.D);
    public EqTransferMatrix InMidSide() => new((A + B + C + D) / 2, (A - B + C - D) / 2,
        (A + B - C - D) / 2, (A - B - C + D) / 2);
    public double PeakGain
    {
        get
        {
            static double Norm(Complex x) => x.Real * x.Real + x.Imaginary * x.Imaginary;
            var trace = Norm(A) + Norm(B) + Norm(C) + Norm(D);
            var determinant = Norm(A * D - B * C);
            var singular = Math.Sqrt((trace + Math.Sqrt(Math.Max(0, trace * trace - 4 * determinant))) / 2);
            return Math.Max(singular, Math.Max(A.Magnitude + B.Magnitude, C.Magnitude + D.Magnitude));
        }
    }
}
