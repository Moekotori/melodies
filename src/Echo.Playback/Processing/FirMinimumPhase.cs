using System.Numerics;

namespace Echo.Playback.Processing;

internal static class FirMinimumPhase
{
    internal static float[] Convert(float[] linear)
    {
        var size = 8; while (size < linear.Length * 4) size <<= 1;
        var data = new Complex[size]; for (var i = 0; i < linear.Length; i++) data[i] = linear[i];
        Fft(data, false);
        for (var i = 0; i < size; i++) data[i] = Math.Log(Math.Max(1e-12, data[i].Magnitude));
        Fft(data, true);
        for (var i = 1; i < size / 2; i++) data[i] = new Complex(data[i].Real * 2, 0);
        for (var i = size / 2; i < size; i++) data[i] = Complex.Zero;
        data[0] = new Complex(data[0].Real, 0); Fft(data, false);
        for (var i = 0; i < size; i++) data[i] = Complex.Exp(data[i]);
        Fft(data, true); var result = new float[linear.Length]; double sum = 0;
        for (var i = 0; i < result.Length; i++) { result[i] = (float)data[i].Real; sum += result[i]; }
        FirPlanBuilder.Normalize(result, sum); return result;
    }
    private static void Fft(Complex[] data, bool inverse)
    {
        for (int i = 1, j = 0; i < data.Length; i++)
        {
            var bit = data.Length >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit;
            if (i < j) (data[i], data[j]) = (data[j], data[i]);
        }
        for (var length = 2; length <= data.Length; length <<= 1)
        {
            var step = Complex.FromPolarCoordinates(1, (inverse ? 2 : -2) * Math.PI / length);
            for (var start = 0; start < data.Length; start += length)
            {
                var phase = Complex.One;
                for (var offset = 0; offset < length / 2; offset++)
                {
                    var even = data[start + offset]; var odd = data[start + offset + length / 2] * phase;
                    data[start + offset] = even + odd; data[start + offset + length / 2] = even - odd; phase *= step;
                }
            }
        }
        if (inverse) for (var i = 0; i < data.Length; i++) data[i] /= data.Length;
    }
}
