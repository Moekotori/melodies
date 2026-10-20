namespace Echo.Playback;

// ECHO Alpha's bounded envelope score. Energy curves are relative measurements, not LUFS.
internal static class AutomixAlphaCueScore
{
    internal static double? Measure(AutomixWindow tail, AutomixWindow head, double start, double entry,
        double overlap, double? outgoingBpm, double? incomingBpm)
    {
        if (!double.IsFinite(overlap) || overlap <= 0) return null;
        double total = 0, squares = 0, gap = 0, meanA = 0, meanB = 0;
        for (var index = 0; index < 24; index++)
        {
            var progress = (index + 0.5) / 24;
            var a = EnergyAt(tail, start + progress * overlap);
            var b = EnergyAt(head, entry + progress * overlap);
            if (a is null || b is null) return null;
            var outgoing = Math.Cos(progress * Math.PI / 2);
            var incoming = Math.Sin(progress * Math.PI / 2);
            var mixed = a.Value * outgoing * outgoing + b.Value * incoming * incoming;
            total += mixed; squares += mixed * mixed;
            gap += Math.Abs(a.Value - b.Value) * outgoing * incoming;
            meanA += a.Value; meanB += b.Value;
        }
        var mean = total / 24;
        var outgoingDirection = Direction(tail, start, outgoingBpm);
        var incomingDirection = Direction(head, entry, incomingBpm);
        var ripple = Math.Sqrt(Math.Max(0, squares / 24 - mean * mean)) / Math.Max(0.05, mean);
        var mismatch = Clamp((Math.Abs(meanA - meanB) / 24 - 0.1) / 0.9);
        var motion = 0.5 * (outgoingDirection is null ? 0 : Clamp(outgoingDirection.Value / 0.2))
            + 0.5 * (incomingDirection is null ? 0 : Clamp(-incomingDirection.Value / 0.2));
        return ripple + gap / 24 + mismatch * 0.2 + motion * 0.2 + Drift(outgoingBpm, incomingBpm, overlap) * 0.25;
    }

    internal static double? EnergyAt(AutomixWindow window, double seconds)
    {
        var progress = (seconds - window.StartSeconds) / window.DurationSeconds;
        if (!double.IsFinite(progress) || progress < 0 || progress >= 1 || window.Energy.Length == 0) return null;
        var value = window.Energy[(int)Math.Floor(progress * window.Energy.Length)];
        return double.IsFinite(value) && value is >= 0 and <= 1 ? value : null;
    }

    internal static double[] Lengths(double baseline, double? outgoing, double? incoming)
    {
        var lengths = new List<double>(9) { baseline, baseline * 0.75, baseline * 0.5, baseline * 1.5, baseline * 2 };
        if (outgoing is not > 0 || incoming is not > 0 || !double.IsFinite(outgoing.Value) || !double.IsFinite(incoming.Value))
            return lengths.ToArray();
        var ratio = AutomixAlphaRoutes.Fold(incoming.Value / outgoing.Value);
        if (!double.IsFinite(ratio) || Math.Abs(1 - ratio) / ((1 + ratio) * 0.5) >= 0.1) return lengths.ToArray();
        var barA = 240 / outgoing.Value;
        var barB = barA / ratio;
        foreach (var bars in new[] { 2, 4, 6, 8 })
        {
            var seconds = Math.Max(barA, AutomixMath.Round(Math.Min(bars * barA, bars * barB) / barA) * barA);
            if (seconds is >= 0.5 and <= 16 && !lengths.Any(value => Math.Abs(value - seconds) < 0.001)) lengths.Add(seconds);
        }
        return lengths.ToArray();
    }

    private static double? Direction(AutomixWindow window, double seconds, double? bpm)
    {
        var span = bpm is not null ? 240 / bpm.Value : 2;
        double left = 0, right = 0;
        for (var index = 0; index < 8; index++)
        {
            var offset = span * (index + 0.5) / 8;
            var before = EnergyAt(window, seconds - offset);
            var after = EnergyAt(window, seconds + offset);
            if (before is null || after is null) return null;
            left += before.Value; right += after.Value;
        }
        return left + right > 1e-9 ? (right - left) / (left + right) : 0;
    }

    private static double Drift(double? outgoing, double? incoming, double seconds)
    {
        if (outgoing is null || incoming is null) return 0;
        var best = incoming.Value;
        foreach (var candidate in new[] { incoming.Value * 2, incoming.Value / 2 })
            if (Math.Abs(candidate - outgoing.Value) < Math.Abs(best - outgoing.Value)) best = candidate;
        return Clamp((Math.Abs(outgoing.Value - best) / 60 * seconds - 0.25) / 1.25);
    }
    private static double Clamp(double value) => AutomixMath.Clamp(value, 0, 1);
}
