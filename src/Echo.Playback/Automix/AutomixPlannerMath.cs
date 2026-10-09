namespace Echo.Playback;

internal static partial class AutomixPlanner
{
    private const double MinimumTempoRatio = 0.94;
    private const double MaximumTempoRatio = 1.06;

    private static double NormalizeBpm(double value)
    {
        while (value < 80) value *= 2;
        while (value > 180) value /= 2;
        return value;
    }

    private static double TempoRatio(double currentBpm, double nextBpm)
    {
        var raw = currentBpm / nextBpm;
        return new[] { raw, raw * 2, raw / 2 }.MinBy(candidate => Math.Abs(candidate - 1));
    }

    private static bool KeysCompatible(AutomixKey? left, AutomixKey? right)
    {
        if (left is null || right is null || left.Confidence < 0.15 || right.Confidence < 0.15) return false;
        var a = Camelot(left.Camelot);
        var b = Camelot(right.Camelot);
        if (a is null || b is null) return false;
        var distance = Math.Min(Math.Abs(a.Value.Number - b.Value.Number), 12 - Math.Abs(a.Value.Number - b.Value.Number));
        return (a.Value.Ring == b.Value.Ring && distance <= 1) || (a.Value.Number == b.Value.Number && a.Value.Ring != b.Value.Ring);
    }

    private static (int Number, char Ring)? Camelot(string value)
    {
        if (value.Length is < 2 or > 3) return null;
        var ring = value[^1];
        if (ring is not ('A' or 'B') || !int.TryParse(value[..^1], out var number) || number is < 1 or > 12) return null;
        return (number, ring);
    }

    private static double PhraseBoundary(AutomixAnalysis analysis, double earliest, double preferred, double latest, Func<double, double> penalty)
    {
        var audible = AutomixBounds.AudibleEnd(analysis);
        (double Seconds, double Score)? best = null;
        foreach (var phrase in analysis.Phrases)
        {
            if (phrase.Seconds < Math.Max(earliest, audible) || phrase.Seconds > latest) continue;
            Consider(phrase.Seconds, Bonus(phrase.Bars) + phrase.Confidence * 0.25);
        }
        foreach (var seconds in analysis.OutroSections)
        {
            if (seconds < Math.Max(earliest, audible) || seconds > latest) continue;
            Consider(seconds, 0);
        }
        return best?.Seconds ?? AutomixMath.Clamp(preferred, earliest, latest);

        void Consider(double seconds, double bonus)
        {
            var score = Math.Abs(seconds - preferred) + penalty(seconds) - bonus;
            if (best is null || score < best.Value.Score) best = (seconds, score);
        }

        static double Bonus(int bars) => bars >= 32 ? 1.2 : bars >= 16 ? 0.85 : bars >= 8 ? 0.5 : 0.15;
    }

    private static double[] Downbeats(AutomixAnalysis analysis, string role)
    {
        if (analysis.Windows is not null)
        {
            var window = AutomixWindows.Transition(analysis, role);
            return window is not null && AutomixWindows.Tempo(analysis, role) is not null ? AutomixWindows.DownbeatGrid(window) : [];
        }
        return analysis.EnergyCurve.Length == 0 ? [] : [];
    }

    private static double[] Beats(AutomixAnalysis analysis, string role)
    {
        if (analysis.Windows is not null)
        {
            var window = AutomixWindows.Transition(analysis, role);
            return window is not null && AutomixWindows.Tempo(analysis, role) is not null ? AutomixWindows.BeatGrid(window) : [];
        }
        return [];
    }

    private static double? Nearest(double[] grid, double preferred, double earliest, double latest)
    {
        double? best = null;
        foreach (var seconds in grid)
        {
            if (seconds < earliest || seconds > latest) continue;
            if (best is null || Math.Abs(seconds - preferred) < Math.Abs(best.Value - preferred)) best = seconds;
        }
        return best;
    }

    private static double? EntryDownbeat(double[] grid, double limit)
    {
        double? selected = null;
        foreach (var seconds in grid)
            if (seconds <= limit) selected = seconds;
        return selected;
    }

    private static double Average(double[] values, int start, int end, double fallback = 0.5)
    {
        if (end <= start) return fallback;
        double sum = 0;
        for (var index = start; index < end; index++) sum += values[index];
        return sum / (end - start);
    }

    private static (double Activity, double Confidence)? VocalAt(AutomixAnalysis analysis, double seconds)
    {
        if (analysis.VocalWindowSeconds <= 0) return null;
        if (seconds <= analysis.VocalWindowSeconds)
            return Read(analysis.IntroVocal, analysis.IntroVocalConfidence, 0);
        var offset = Math.Max(0, analysis.DurationSeconds - analysis.VocalWindowSeconds);
        return seconds >= offset ? Read(analysis.OutroVocal, analysis.OutroVocalConfidence, offset) : null;

        (double Activity, double Confidence)? Read(double[]? curve, double? confidence, double origin)
        {
            if (curve is not { Length: > 0 } || confidence is not >= 0.2) return null;
            var progress = AutomixMath.Clamp((seconds - origin) / analysis.VocalWindowSeconds, 0, 0.999999);
            var index = Math.Min(curve.Length - 1, (int)Math.Floor(progress * curve.Length));
            return (AutomixMath.Clamp(curve[index], 0, 1), AutomixMath.Clamp(confidence.Value, 0, 1));
        }
    }

    private static double VocalConflict(AutomixAnalysis current, AutomixAnalysis next, double start, double end, double entry, double ratio = 1)
    {
        var duration = Math.Max(0.001, end - start);
        double weighted = 0, evidence = 0;
        for (var sample = 0; sample < 9; sample++)
        {
            var progress = sample / 8d;
            var outgoing = VocalAt(current, start + duration * progress);
            var incoming = VocalAt(next, entry + duration * progress * ratio);
            if (outgoing is null || incoming is null) continue;
            var weight = Math.Min(outgoing.Value.Confidence, incoming.Value.Confidence);
            weighted += outgoing.Value.Activity * incoming.Value.Activity * weight;
            evidence += weight;
        }
        return evidence > 0 ? AutomixMath.RoundMillis(AutomixMath.Clamp(weighted / evidence, 0, 1)) : 0;
    }

    private static (double Exit, double Entry) VocalHandoff(AutomixAnalysis current, AutomixAnalysis next, double start, double end, double entry, double ratio = 1)
    {
        var duration = Math.Max(0.001, end - start);
        var points = new List<AutomixVocalPoint>();
        for (var sample = 0; sample <= 40; sample++)
        {
            var progress = sample / 40d;
            var outgoing = VocalAt(current, start + duration * progress);
            var incoming = VocalAt(next, entry + duration * progress * ratio);
            if (outgoing is null || incoming is null) continue;
            points.Add(new(progress, outgoing.Value.Activity, incoming.Value.Activity, Math.Min(outgoing.Value.Confidence, incoming.Value.Confidence)));
        }
        return AutomixVocals.Handoff(points);
    }

    private static (double Tail, double Decay, double Head, double Density) Energy(AutomixAnalysis current, AutomixAnalysis next)
    {
        var tailCurve = current.Windows is not null ? AutomixWindows.Transition(current, "outro")?.Energy ?? [] : current.EnergyCurve;
        var headCurve = next.Windows is not null ? AutomixWindows.Transition(next, "intro")?.Energy ?? [] : next.EnergyCurve;
        var buckets = Math.Max(2, Math.Min(4, tailCurve.Length / 3));
        var tail = Average(tailCurve, Math.Max(0, tailCurve.Length - buckets), tailCurve.Length);
        var lead = Average(tailCurve, Math.Max(0, tailCurve.Length - buckets * 2), Math.Max(0, tailCurve.Length - buckets), tail);
        var head = Average(headCurve, 0, Math.Min(buckets, headCurve.Length));
        return (tail, Math.Max(0, lead - tail), head, (tail + head) / 2);
    }

    private static bool BassSwap(bool beatMatch, (double Tail, double Decay, double Head, double Density) energy) =>
        beatMatch && energy.Density >= 0.55 && energy.Tail >= 0.48 && energy.Head >= 0.48;

    private static bool TailRide(AutomixAnalysis current, (double Tail, double Decay, double Head, double Density) energy)
    {
        var lufsDecay = current.IntegratedLufs is not null && current.OutroLufs is not null
            ? current.IntegratedLufs.Value - current.OutroLufs.Value : 0;
        return energy.Tail <= 0.52 && energy.Head <= 0.55 && (energy.Decay >= 0.12 || lufsDecay >= 3.5);
    }
}
