namespace Echo.Playback;

internal readonly record struct AutomixTempoReading(
    double? Bpm, double? BpmConfidence, double? BeatOffsetMs, double? BeatPhaseConfidence,
    int? BeatsPerBar, double? DownbeatOffsetMs, double? DownbeatConfidence);

internal static class AutomixTempo
{
    internal static AutomixTempoReading Measure(float[] samples, int sampleRate)
    {
        if (sampleRate < 8000 || samples.Length < sampleRate * 2) return default;
        var count = Math.Min(samples.Length, sampleRate * 36);
        var hop = Math.Max(1, (int)Math.Round(sampleRate * 0.01));
        var onsets = new float[(count + hop - 1) / hop];
        float previous = 0, mean = 0, onsetSum = 0, peak = 0;
        for (var frame = 0; frame < onsets.Length; frame++)
        {
            var start = frame * hop;
            var end = Math.Min(count, start + hop);
            double energy = 0;
            for (var index = start; index < end; index++) energy += samples[index] * (double)samples[index];
            var level = Math.Sqrt(energy / (end - start));
            var onset = Math.Max(0, level - previous);
            onsets[frame] = (float)onset;
            mean += (float)level;
            onsetSum += (float)onset;
            peak = Math.Max(peak, (float)onset);
            previous = (float)level;
        }
        if (peak < Math.Max(1e-6, mean / onsets.Length * 0.1)) return default;
        var floor = onsetSum / onsets.Length * 0.45f;
        for (var index = 0; index < onsets.Length; index++) onsets[index] = Math.Max(0, onsets[index] - floor);
        var minLag = Math.Max(2, (int)Math.Round(60 / 200d * sampleRate / hop));
        var maxLag = Math.Min(onsets.Length - 1, (int)Math.Round(60 / 60d * sampleRate / hop));
        var bestLag = 0;
        var bestScore = 0d;
        for (var lag = minLag; lag <= maxLag; lag++)
        {
            var score = Correlation(onsets, lag);
            if (score > bestScore) { bestScore = score; bestLag = lag; }
        }
        if (bestLag == 0 || bestScore < 0.08) return default;
        var period = Refine(onsets, bestLag);
        var bpm = 60d * sampleRate / (period * hop);
        while (bpm < 80) { bpm *= 2; period /= 2; }
        while (bpm > 180) { bpm /= 2; period *= 2; }
        var phase = Phase(onsets, period);
        int? metre = null;
        double? downbeat = null, downbeatConfidence = null;
        if (phase.Offset is not null && bestScore >= 0.68)
            (metre, downbeat, downbeatConfidence) = Bars(onsets, period, phase.Offset.Value, hop / (double)sampleRate);
        return new(Round(bpm), Round(AutomixMath.Clamp(bestScore, 0, 1)),
            phase.Offset is null ? null : (double)Math.Round(phase.Offset.Value * hop * 1000 / sampleRate),
            Round(phase.Confidence), metre, downbeat, downbeatConfidence);
    }

    private static double Correlation(float[] onsets, int lag)
    {
        double product = 0, left = 1e-12, right = 1e-12;
        for (var index = lag; index < onsets.Length; index++)
        {
            var a = onsets[index];
            var b = onsets[index - lag];
            product += a * b;
            left += a * a;
            right += b * b;
        }
        return product / Math.Sqrt(left * right);
    }

    private static double Refine(float[] onsets, int lag)
    {
        for (var multiple = 4; multiple >= 1; multiple--)
        {
            var centre = lag * multiple;
            if (centre + multiple + 1 >= onsets.Length / 2) continue;
            var peak = centre;
            var score = -1d;
            for (var candidate = centre - multiple; candidate <= centre + multiple; candidate++)
            {
                var candidateScore = Correlation(onsets, candidate);
                if (candidateScore > score) { score = candidateScore; peak = candidate; }
            }
            var before = Correlation(onsets, peak - 1);
            var after = Correlation(onsets, peak + 1);
            var curvature = before - 2 * score + after;
            if (curvature >= -1e-6) continue;
            var refined = (peak + AutomixMath.Clamp((before - after) / (2 * curvature), -0.5, 0.5)) / multiple;
            if (Math.Abs(refined - lag) <= 0.5) return refined;
        }
        return lag;
    }

    private static (double? Offset, double Confidence) Phase(float[] onsets, double periodFrames)
    {
        var bins = Math.Min(128, Math.Max(8, (int)Math.Ceiling(periodFrames)));
        var votes = new double[bins];
        var support = new double[bins];
        double weight = 0;
        var peaks = 0;
        for (var index = 0; index < onsets.Length; index++)
        {
            if (!Peak(onsets, index)) continue;
            weight += onsets[index];
            peaks++;
        }
        if (peaks < 4 || weight <= 1e-9) return (null, 0);
        var maximum = 2 * weight / peaks;
        weight = 0;
        for (var index = 0; index < onsets.Length; index++)
        {
            if (!Peak(onsets, index)) continue;
            var strength = Math.Min(onsets[index], maximum);
            var position = (index % periodFrames) / periodFrames * bins;
            var bin = (int)Math.Floor(position);
            var fraction = position - bin;
            votes[bin] += strength * (1 - fraction);
            votes[(bin + 1) % bins] += strength * fraction;
            support[bin] += 1 - fraction;
            support[(bin + 1) % bins] += fraction;
            weight += strength;
        }
        var winner = 0;
        var best = 0d;
        for (var bin = 0; bin < bins; bin++)
        {
            var score = votes[(bin + bins - 1) % bins] + votes[bin] + votes[(bin + 1) % bins];
            if (score > best) { best = score; winner = bin; }
        }
        var peakSupport = support[(winner + bins - 1) % bins] + support[winner] + support[(winner + 1) % bins];
        var confidence = AutomixMath.Clamp(Math.Min(best / weight, peakSupport / peaks), 0, 1);
        if (peakSupport < 4) return (null, 0);
        if (confidence < AutomixWindows.MinimumPhaseConfidence) return (null, confidence);
        var shift = (votes[(winner + 1) % bins] - votes[(winner + bins - 1) % bins]) / best;
        var phase = ((winner + shift) % bins + bins) % bins;
        return (phase * periodFrames / bins, confidence);
    }

    private static (int? Metre, double? Offset, double? Confidence) Bars(float[] onsets, double periodFrames, double phaseFrames, double frameSeconds)
    {
        if (periodFrames <= 0 || phaseFrames < 0 || frameSeconds <= 0) return (null, null, null);
        var count = Math.Min(128, (int)Math.Floor((onsets.Length - phaseFrames) / periodFrames));
        if (count < 16) return (null, null, null);
        var strengths = new float[count];
        var radius = Math.Min(3, periodFrames * 0.12);
        for (var beat = 0; beat < count; beat++)
        {
            var centre = phaseFrames + beat * periodFrames;
            var from = Math.Max(0, (int)Math.Ceiling(centre - radius));
            var to = Math.Min(onsets.Length - 1, (int)Math.Floor(centre + radius));
            for (var frame = from; frame <= to; frame++) strengths[beat] = Math.Max(strengths[beat], onsets[frame]);
        }
        (int Metre, int Phase, double Confidence)? only = null;
        var found = 0;
        foreach (var metre in new[] { 3, 4 })
        {
            var bars = count / metre;
            var votes = new int[metre];
            var first = new int[metre];
            var second = new int[metre];
            var active = 0;
            for (var bar = 0; bar < bars; bar++)
            {
                float best = 0, runner = 0, floor = float.PositiveInfinity;
                var phase = 0;
                for (var beat = 0; beat < metre; beat++)
                {
                    var strength = strengths[bar * metre + beat];
                    floor = Math.Min(floor, strength);
                    if (strength > best) { runner = best; best = strength; phase = beat; }
                    else runner = Math.Max(runner, strength);
                }
                if (best <= 1e-6) continue;
                active++;
                if (floor < best * 0.08 || (best - runner) / best < 0.28) continue;
                votes[phase]++;
                (bar < bars / 2f ? first : second)[phase]++;
            }
            for (var phase = 0; phase < metre; phase++)
            {
                var confidence = votes[phase] / (double)Math.Max(1, active);
                if (votes[phase] < 4 || first[phase] < 2 || second[phase] < 2 || confidence < AutomixWindows.MinimumDownbeatConfidence) continue;
                found++;
                only = (metre, phase, confidence);
            }
        }
        if (found != 1 || only is null) return (null, null, null);
        return (only.Value.Metre,
            Math.Round((phaseFrames + only.Value.Phase * periodFrames) * frameSeconds * 1000, MidpointRounding.AwayFromZero),
            Round(only.Value.Confidence));
    }

    private static bool Peak(float[] onsets, int index) => onsets[index] > 0
        && onsets[index] >= (index > 0 ? onsets[index - 1] : 0)
        && onsets[index] > (index + 1 < onsets.Length ? onsets[index + 1] : 0);

    private static double Round(double value) =>
        Math.Round(value * 1000, MidpointRounding.AwayFromZero) / 1000;
}
