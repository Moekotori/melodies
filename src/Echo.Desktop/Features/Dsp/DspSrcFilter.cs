namespace Echo.Desktop.Features.Dsp;

internal static class DspSrcFilter
{
    public static double[] Lowpass(int factor)
    {
        var taps = factor == 4 ? 127 : 63;
        var center = (taps - 1) / 2.0;
        var cutoff = 0.5 / factor;
        var window = new double[taps];
        double sum = 0;
        for (var n = 0; n < taps; n++)
        {
            var offset = n - center;
            var sinc = Math.Abs(offset) < 1e-9 ? 2 * cutoff : Math.Sin(2 * Math.PI * cutoff * offset) / (Math.PI * offset);
            var hann = 0.5 - 0.5 * Math.Cos(2 * Math.PI * n / (taps - 1));
            window[n] = sinc * hann;
            sum += window[n];
        }
        if (sum == 0) return window;
        for (var n = 0; n < taps; n++) window[n] /= sum;
        return window;
    }
}
