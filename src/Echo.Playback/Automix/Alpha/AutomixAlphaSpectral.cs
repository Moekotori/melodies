namespace Echo.Playback;

// Port of ECHO/QPlayer AlphaSpectralFeatures. See THIRD_PARTY_NOTICES.md.
internal static class AutomixAlphaSpectral
{
    internal const double HopSeconds = 0.02;
    internal const int Stride = 5;
    internal const int MaximumFrames = 1800;

    internal static float[]? Measure(ReadOnlySpan<float> samples, int rate, CancellationToken token = default)
    {
        if (rate is < 8000 or > 48000) return null;
        var frames = Math.Min(MaximumFrames, (int)(samples.Length / (rate * HopSeconds)));
        if (frames < 20) return null;
        var values = new float[frames * Stride];
        var low = new LowPass(rate, 200);
        var high = new LowPass(rate, 2000);
        var coefficient = 1 - Math.Exp(-2 * Math.PI * 200 / rate);
        double onePole = 0;
        for (var frame = 0; frame < frames; frame++)
        {
            if ((frame & 63) == 0) token.ThrowIfCancellationRequested();
            // Absolute boundaries avoid fractional-hop drift at 11025 Hz.
            var start = (int)AutomixMath.Round(frame * rate * HopSeconds);
            var end = Math.Min(samples.Length, (int)AutomixMath.Round((frame + 1) * rate * HopSeconds));
            double rms = 0, lowPower = 0, midPower = 0, highPower = 0, polePower = 0;
            for (var i = start; i < end; i++)
            {
                double sample = samples[i];
                if (!double.IsFinite(sample)) return null;
                var lo = low.Process(sample);
                var upToHigh = high.Process(sample);
                var mid = upToHigh - lo;
                var hi = sample - upToHigh;
                onePole += coefficient * (sample - onePole);
                rms += sample * sample;
                lowPower += lo * lo;
                midPower += mid * mid;
                highPower += hi * hi;
                polePower += onePole * onePole;
            }
            var count = end - start;
            var offset = frame * Stride;
            values[offset] = (float)Math.Sqrt(rms / count);
            values[offset + 1] = (float)Math.Sqrt(lowPower / count);
            values[offset + 2] = (float)Math.Sqrt(midPower / count);
            values[offset + 3] = (float)Math.Sqrt(highPower / count);
            values[offset + 4] = (float)Math.Sqrt(polePower / count);
        }
        return values;
    }

    private struct LowPass
    {
        private readonly double b0, b1, a1, a2;
        private double z1, z2;
        internal LowPass(int rate, double hz)
        {
            var omega = 2 * Math.PI * hz / rate;
            var cosine = Math.Cos(omega);
            var alpha = Math.Sin(omega) / (2 * 0.7071);
            var denominator = 1 + alpha;
            b0 = (1 - cosine) * 0.5 / denominator;
            b1 = (1 - cosine) / denominator;
            a1 = -2 * cosine / denominator;
            a2 = (1 - alpha) / denominator;
        }
        internal double Process(double sample)
        {
            var result = b0 * sample + z1;
            z1 = b1 * sample - a1 * result + z2;
            z2 = b0 * sample - a2 * result;
            return result;
        }
    }
}
