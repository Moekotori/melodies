namespace Echo.Playback;

internal readonly record struct AutomixAlphaSide(float[] Values, int Frames, int Offset = 0, double Speed = 1)
{
    internal double At(int frame, int column) => Values[Math.Clamp(Offset + (int)AutomixMath.Round(frame * Speed), 0, Values.Length / 5 - 1) * 5 + column];

    internal static AutomixAlphaSide? Create(AutomixWindow? window, double seconds, double duration, double speed = 1)
    {
        if (window?.AlphaSpectral is not { Length: >= 100 and <= 9000 } values || values.Length % 5 != 0
            || !double.IsFinite(seconds) || !double.IsFinite(window.StartSeconds) || window.StartSeconds < 0
            || !double.IsFinite(window.DurationSeconds) || window.DurationSeconds <= 0
            || seconds < window.StartSeconds - 1e-6 || !double.IsFinite(duration) || duration <= 0
            || !double.IsFinite(speed) || speed <= 0) return null;
        var frames = AutomixMath.Round(duration / AutomixAlphaSpectral.HopSeconds);
        var offset = AutomixMath.Round((seconds - window.StartSeconds) / AutomixAlphaSpectral.HopSeconds);
        if (frames is < 20 or > 800 || offset < 0
            || offset + AutomixMath.Round((frames - 1) * speed) >= values.Length / 5
            || seconds + duration * speed > window.StartSeconds + window.DurationSeconds + 1e-6) return null;
        foreach (var value in values) if (!float.IsFinite(value) || value < 0) return null;
        return new(values, (int)frames, (int)offset, speed);
    }
}

internal readonly record struct AutomixAlphaProfile(double LowClash, double DropReliable, double TailDecayDbPerSec,
    double HeadOnset, double OverlapSeconds, bool BuildUp)
{
    internal static AutomixAlphaProfile Measure(AutomixAlphaSide a, AutomixAlphaSide b)
    {
        var frames = Math.Min(a.Frames, b.Frames);
        double allA = 0, allB = 0, lowA = 0, lowB = 0, combined = 0, peak = 0;
        double sy = 0, sxy = 0, first = 0, second = 0;
        for (var f = 0; f < frames; f++)
        {
            var va = a.At(f, 0); var vb = b.At(f, 0);
            allA += va; allB += vb; lowA += a.At(f, 4); lowB += b.At(f, 4);
            combined += va + vb; peak = Math.Max(peak, va + vb);
            var db = 20 * Math.Log10(Math.Max(va, 1e-6));
            sy += db; sxy += f * db;
            if (f < frames / 2) first += va; else second += va;
        }
        double sx = frames * (frames - 1) / 2d;
        double sxx = frames * (frames - 1) * (2 * frames - 1) / 6d;
        var denominator = frames * sxx - sx * sx;
        var decay = denominator > 1e-9 ? (frames * sxy - sx * sy) / denominator * 50 : 0;
        var mean = combined / frames;
        return new(AutomixMath.Clamp(Math.Min(lowA / Math.Max(allA, 1e-9), lowB / Math.Max(allB, 1e-9)), 0, 1),
            Math.Max(0, (peak - mean) / Math.Max(peak + mean, 1e-9)), decay, Onset(b), frames / 50d,
            decay > -0.5 && frames >= 8 && second / (frames - frames / 2) > first / (frames / 2) * 1.15);
    }

    internal static double TrailingSpan(AutomixAlphaSide side)
    {
        double peak = 0;
        for (var f = 0; f < side.Frames; f++) peak = Math.Max(peak, side.At(f, 0));
        if (peak <= 0) return 0;
        for (var f = side.Frames - 1; f >= 0; f--)
            if (side.At(f, 0) >= peak * 0.1) return (side.Frames - 1 - f) * 0.02;
        return 0;
    }

    private static double Onset(AutomixAlphaSide side)
    {
        var count = Math.Min(side.Frames, 100);
        double peak = 0, sum = 0;
        for (var f = 1; f < count; f++)
        {
            var delta = Math.Max(0, side.At(f, 0) - side.At(f - 1, 0));
            peak = Math.Max(peak, delta); sum += delta;
        }
        var mean = sum / Math.Max(1, count - 1);
        return Math.Max(0, (peak - mean) / Math.Max(peak + mean, 1e-9));
    }
}
