namespace Echo.Playback;

internal static class AutomixWindows
{
    internal const double MinimumPhaseConfidence = 0.55;
    internal const double MinimumDownbeatConfidence = 0.75;
    private const double MinimumTempoConfidence = 0.68;

    internal static double[] BeatGrid(AutomixWindow window)
    {
        var bpm = FiniteTempo(window);
        if (bpm is null || !AutomixMath.Finite(window.BeatOffsetMs)
            || (window.BeatPhaseConfidence ?? 0) < MinimumPhaseConfidence
            || window.StartSeconds < 0 || window.DurationSeconds <= 0) return [];
        var period = 60 / bpm.Value;
        var phase = Mod(window.BeatOffsetMs!.Value / 1000, period);
        var beats = new List<double>(64);
        for (var seconds = phase; seconds < window.DurationSeconds && beats.Count < 512; seconds += period)
            beats.Add(AutomixMath.RoundMillis(window.StartSeconds + seconds));
        return beats.ToArray();
    }

    internal static double[] DownbeatGrid(AutomixWindow window)
    {
        if (window.BeatsPerBar is not (3 or 4) || (window.DownbeatConfidence ?? 0) < MinimumDownbeatConfidence
            || !AutomixMath.Finite(window.DownbeatOffsetMs)) return [];
        var beats = BeatGrid(window);
        if (beats.Length == 0 || window.Bpm is not > 0) return [];
        var bar = 60 / window.Bpm.Value * window.BeatsPerBar.Value;
        var phase = Mod(window.DownbeatOffsetMs!.Value / 1000, bar);
        return beats.Where(seconds =>
        {
            var residual = Mod(seconds - window.StartSeconds - phase, bar);
            return Math.Min(residual, bar - residual) < 0.025;
        }).ToArray();
    }

    internal static double[] EnergyBoundaries(AutomixWindow window)
    {
        if (window.StartSeconds < 0 || window.DurationSeconds <= 0 || window.Energy.Length == 0) return [];
        var count = Math.Min(96, window.Energy.Length);
        var boundaries = new List<double>();
        for (var index = 1; index < count; index++)
        {
            var delta = Math.Abs(window.Energy[index] - window.Energy[index - 1]);
            if (!AutomixMath.Finite(delta) || delta < 0.14) continue;
            boundaries.Add(AutomixMath.RoundMillis(window.StartSeconds + index * window.DurationSeconds / window.Energy.Length));
        }
        return boundaries.ToArray();
    }

    internal static AutomixWindow? Transition(AutomixAnalysis analysis, string role)
    {
        var window = analysis.Windows?.FirstOrDefault(candidate => candidate.Role == role);
        if (window is null) return null;
        if (role == "outro" && window.StartSeconds + window.DurationSeconds < analysis.DurationSeconds - 0.25) return null;
        return window;
    }

    internal static double? Tempo(AutomixAnalysis analysis, string role)
    {
        if (analysis.Windows is null)
            return analysis.Bpm is > 0 && (analysis.BpmConfidence ?? 0) >= MinimumTempoConfidence ? analysis.Bpm : null;
        var window = Transition(analysis, role);
        var bpm = window is null ? null : FiniteTempo(window);
        if (bpm is null || role == "intro") return bpm;
        var head = Transition(analysis, "intro");
        var headBpm = head is null ? null : FiniteTempo(head);
        if (headBpm is null) return bpm;
        var ratio = bpm.Value / headBpm.Value;
        var deviation = new[] { ratio, ratio * 2, ratio / 2 }.Min(value => Math.Abs(value - 1));
        return deviation <= 0.03 ? bpm : null;
    }

    internal static AutomixKey? Key(AutomixAnalysis analysis, string role)
    {
        var endpoint = role == "intro" ? analysis.IntroKey : analysis.OutroKey;
        if (analysis.Windows is not null) return Transition(analysis, role) is null ? null : endpoint;
        return endpoint ?? analysis.Key;
    }

    private static double? FiniteTempo(AutomixWindow window) =>
        window.Bpm is >= 40 and <= 260 && (window.BpmConfidence ?? 0) >= MinimumTempoConfidence ? window.Bpm : null;

    private static double Mod(double value, double period) => ((value % period) + period) % period;
}

internal static class AutomixBounds
{
    internal static double EntryLimit(AutomixAnalysis analysis)
    {
        var silence = Padding(analysis.LeadingSilenceSeconds, analysis.DurationSeconds, 12);
        return silence > 0.16 ? Math.Max(0, silence - 0.04) : 0;
    }

    internal static double AudibleEnd(AutomixAnalysis analysis) =>
        Math.Max(0, analysis.DurationSeconds - Padding(analysis.TrailingSilenceSeconds, analysis.DurationSeconds, 10));

    internal static double PreferredEnd(AutomixAnalysis analysis) =>
        Math.Min(analysis.DurationSeconds, AudibleEnd(analysis) + 0.08);

    internal static double IncomingRunway(double duration, double entry, double ratio = 1) =>
        AutomixMath.Finite(duration) && duration > entry
            ? Math.Max(0, (duration - entry - Math.Min(0.2, (duration - entry) / 2)) / Math.Max(1, ratio))
            : 0;

    private static double Padding(double value, double duration, double limit) =>
        AutomixMath.Finite(value) && value > 0 && value < duration ? Math.Min(value, limit) : 0;
}

internal static class AutomixLoudness
{
    internal static double NextGainDb(AutomixPlanInput input)
    {
        var current = Measured(input.CurrentLufs) ?? Measured(input.Current?.IntegratedLufs);
        var next = Measured(input.NextLufs) ?? Measured(input.Next?.IntegratedLufs);
        if (current is null || next is null || !AutomixMath.Finite(input.CurrentReplayGainDb) || !AutomixMath.Finite(input.NextReplayGainDb))
            return 0;
        var correction = AutomixMath.Clamp((current.Value + input.CurrentReplayGainDb - next.Value - input.NextReplayGainDb) * 0.55, -3.5, 3);
        if (correction <= 0) return AutomixMath.RoundMillis(correction);
        if (input.NextPeak is not > 0) return 0;
        var headroom = Math.Max(0, -20 * Math.Log10(input.NextPeak.Value) - input.NextReplayGainDb);
        return Math.Floor(Math.Min(correction, headroom) * 1000) / 1000;
    }

    private static double? Measured(double? value) => value is >= -70 and <= 0 ? value : null;
}
