namespace Echo.Playback;

// Exact QPlayer EqFeatureExtractor positions, genre gates and per-hop averaging.
internal static class AutomixAlphaEqFeatures
{
    internal static double[]? Controls(AutomixAlphaSide a, AutomixAlphaSide b, double seconds, double bpmA, double bpmB)
    {
        var frames = Math.Min(a.Frames, b.Frames);
        if (frames is < 20 or > 800) return null;
        double onsetPeak = 0, onsetSum = 0, lowSum = 0, midSum = 0, energyPeak = 0, energySum = 0;
        var peakFrame = 0;
        for (var f = 0; f < frames; f++)
        {
            double delta = 0;
            if (f > 0) for (var k = 1; k <= 3; k++) delta += Math.Max(0, a.At(f, k) - a.At(f - 1, k));
            onsetPeak = Math.Max(onsetPeak, delta); onsetSum += delta;
            lowSum += Ratio(a, f, 1); midSum += Ratio(a, f, 2);
            var energy = BandSum(a, f) + BandSum(b, f);
            energySum += energy;
            if (energy > energyPeak) { energyPeak = energy; peakFrame = f; }
        }
        var onsetMean = onsetSum / Math.Max(1, frames - 1);
        var onset = (onsetPeak - onsetMean) / (onsetPeak + onsetMean + 1e-6);
        var edm = Sigmoid((bpmA - 115) / 5) * onset;
        var hiphop = Sigmoid((90 - bpmA) / 5) * onset * lowSum / frames;
        var dnb = Sigmoid((bpmA - 160) / 5) * onset;
        var rock = Sigmoid((bpmA - 105) / 8) * (1 - onset) * midSum / frames;
        Span<double> genres = [edm, hiphop, dnb, rock, 1 - Math.Max(Math.Max(edm, hiphop), Math.Max(dnb, rock))];
        double genreSum = 0;
        for (var k = 0; k < genres.Length; k++) { genres[k] = Math.Max(0, genres[k]); genreSum += genres[k]; }
        genreSum = Math.Max(genreSum, 1e-6);
        Span<float> row = stackalloc float[36]; row.Clear();
        Span<double> result = stackalloc double[12];
        var sum = new double[12];
        var model = new AutomixAlphaEqModel();
        row[9] = (float)(seconds / 8);
        row[10] = (float)(Math.Abs(bpmA - bpmB) / Math.Max((bpmA + bpmB) / 2, 1e-3));
        row[11] = 0; // Upstream prepareLearnedEq supplies keyMatch=0.
        row[12] = (float)onset; row[13] = (float)(bpmA / 180);
        row[29] = (float)((double)peakFrame / frames);
        row[30] = (float)Math.Min(5, energyPeak / Math.Max(energySum / frames, 1e-3));
        for (var k = 0; k < 5; k++) row[31 + k] = (float)(genres[k] / genreSum);
        for (var f = 0; f < frames; f++)
        {
            var past = Math.Max(0, f - 5); var future = Math.Min(frames - 1, f + 5);
            double flux = 0;
            for (var k = 0; k < 3; k++)
            {
                row[k] = (float)a.At(f, k + 1); row[3 + k] = (float)b.At(f, k + 1);
                row[17 + k] = (float)(a.At(f, k + 1) - a.At(past, k + 1));
                row[20 + k] = (float)(b.At(f, k + 1) - b.At(past, k + 1));
                row[23 + k] = (float)(a.At(future, k + 1) - a.At(f, k + 1));
                row[26 + k] = (float)(b.At(future, k + 1) - b.At(f, k + 1));
                if (f > 0) flux += Math.Max(0, a.At(f, k + 1) - a.At(f - 1, k + 1));
            }
            row[6] = (float)Centroid(a, f); row[7] = (float)Centroid(b, f); row[8] = (float)((double)f / frames);
            row[14] = (float)Ratio(a, f, 1); row[15] = (float)Ratio(b, f, 1); row[16] = (float)flux;
            if (!model.Forward(row, result)) return null;
            for (var k = 0; k < 12; k++) sum[k] += result[k];
        }
        for (var k = 0; k < 12; k++) sum[k] /= frames;
        return sum;
    }

    private static double BandSum(AutomixAlphaSide side, int f) => side.At(f, 1) + side.At(f, 2) + side.At(f, 3);
    private static double Ratio(AutomixAlphaSide side, int f, int band) => side.At(f, band) / Math.Max(BandSum(side, f), 1e-6);
    private static double Centroid(AutomixAlphaSide side, int f) =>
        AutomixMath.Clamp((side.At(f, 0) - side.At(f - 1, 0)) * 4 + 0.5, 0, 1);
    private static double Sigmoid(double v) => 1 / (1 + Math.Exp(-AutomixMath.Clamp(v, -60, 60)));
}
