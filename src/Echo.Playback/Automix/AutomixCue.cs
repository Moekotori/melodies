namespace Echo.Playback;

internal static partial class AutomixPlanner
{
    private readonly record struct Cue(double End, double Entry, double Overlap);

    private static AutomixPlan Refine(AutomixPlan plan, AutomixPlanInput input)
    {
        var current = input.Current;
        var next = input.Next;
        if (current is null || next is null || plan.Mode is not ("phrase_crossfade" or "short_crossfade")
            || plan.HandoffProfile == "beat_cut" || plan.FallbackReason == "track_boundary_fade"
            || current.Status is "error" or "unavailable" || next.Status is "error" or "unavailable") return plan;
        var tail = AutomixWindows.Transition(current, "outro");
        var head = AutomixWindows.Transition(next, "intro");
        var baseline = new Cue(plan.CurrentEndSeconds, plan.NextStartSeconds, (double)plan.OverlapFrames / plan.MixSampleRate);
        if (tail is null || head is null || !AutomixMath.Finite(input.PositionSeconds) || baseline.Overlap < 0.5) return plan;
        var maxOverlap = Math.Max(2, Math.Min(16, input.MaxTransitionSeconds));
        var outgoingBpm = AutomixWindows.Tempo(current, "outro");
        var incomingBpm = AutomixWindows.Tempo(next, "intro");
        var lengths = AutomixAlphaCueScore.Lengths(baseline.Overlap, outgoingBpm, incomingBpm);
        var baselineScore = Score(baseline, tail, head);
        if (baselineScore is null) return plan;
        var earliest = Math.Max(AutomixBounds.AudibleEnd(current), input.PositionSeconds + 1);
        var latest = Math.Min(current.DurationSeconds, baseline.End);
        var entryLimit = AutomixBounds.EntryLimit(next);
        var ends = new List<double>(8);
        var entries = new List<double>(4);
        Keep(ends, baseline.End, earliest, latest, 8);
        Keep(ends, earliest, earliest, latest, 8);
        for (var index = current.OutroSections.Length - 1; index >= Math.Max(0, current.OutroSections.Length - 64); index--)
            Keep(ends, current.OutroSections[index], earliest, latest, 8);
        Keep(entries, baseline.Entry, 0, entryLimit, 4);
        Keep(entries, entryLimit, 0, entryLimit, 4);
        Keep(entries, Math.Max(0, baseline.Entry - 0.25), 0, entryLimit, 4);
        Keep(entries, Math.Max(0, baseline.Entry - 0.5), 0, entryLimit, 4);
        var best = baseline;
        var bestScore = baselineScore.Value;
        foreach (var end in ends)
        foreach (var entry in entries)
        {
            var ceiling = AutomixMath.Min(maxOverlap, end - input.PositionSeconds - 0.5, AutomixBounds.IncomingRunway(next.DurationSeconds, entry));
            foreach (var overlap in lengths)
            {
                if (overlap < 0.5 || overlap > ceiling) continue;
                var cue = new Cue(end, entry, overlap);
                var score = Score(cue, tail, head);
                if (score is not null && score.Value < bestScore - 1e-6) { best = cue; bestScore = score.Value; }
            }
        }
        if (bestScore >= baselineScore.Value - 0.04 || best == baseline) return plan;
        var chosenEnd = Math.Min(current.DurationSeconds, Math.Max(AutomixBounds.AudibleEnd(current), AutomixMath.RoundMillis(best.End)));
        var chosenEntry = Math.Min(entryLimit, AutomixMath.RoundMillis(best.Entry));
        var frames = Math.Max(2, AutomixMath.Round(best.Overlap * plan.MixSampleRate));
        var start = chosenEnd - frames / (double)plan.MixSampleRate;
        var fadeStart = input.CurrentOutputFrame + AutomixMath.Round((start - input.PositionSeconds) * plan.MixSampleRate);
        var conflict = VocalConflict(current, next, start, chosenEnd, chosenEntry);
        double? exit = plan.VocalExit;
        double? vocalEntry = plan.VocalEntry;
        if (plan.HandoffProfile == "rhythmic_bass_swap")
            (exit, vocalEntry) = VocalHandoff(current, next, start, chosenEnd, chosenEntry);
        return plan.Retimed(fadeStart, frames, chosenEnd, chosenEntry, conflict,
            "Alpha cue search improved envelope continuity, motion, vocal conflict or beat drift within preserved audio bounds.", exit, vocalEntry);

        double? Score(Cue cue, AutomixWindow outro, AutomixWindow intro)
        {
            var startSeconds = cue.End - cue.Overlap;
            if (!Covers(outro, startSeconds, cue.End) || !Covers(intro, cue.Entry, cue.Entry + cue.Overlap)
                || !VocalCoverage(current, "outro", startSeconds, cue.End) || !VocalCoverage(next, "intro", cue.Entry, cue.Entry + cue.Overlap))
                return null;
            var measured = AutomixAlphaCueScore.Measure(outro, intro, startSeconds, cue.Entry, cue.Overlap, outgoingBpm, incomingBpm);
            if (measured is null) return null;
            var vocal = VocalConflict(current, next, startSeconds, cue.End, cue.Entry);
            if (!double.IsFinite(vocal)) return null;
            var movement = Math.Abs(cue.Overlap - baseline.Overlap) / Math.Max(1, baseline.Overlap) * 0.12
                + Math.Abs(cue.Entry - baseline.Entry) * 0.03 + Math.Abs(cue.End - baseline.End) * 0.03;
            return measured.Value + AutomixMath.Clamp(vocal, 0, 1) * 2 + movement;
        }
    }

    private static bool Covers(AutomixWindow window, double start, double end) =>
        AutomixMath.Finite(window.StartSeconds) && window.DurationSeconds > 0 && start >= window.StartSeconds
        && end <= window.StartSeconds + window.DurationSeconds + 1e-6;

    private static bool VocalCoverage(AutomixAnalysis analysis, string role, double start, double end)
    {
        var curve = role == "intro" ? analysis.IntroVocal : analysis.OutroVocal;
        var confidence = role == "intro" ? analysis.IntroVocalConfidence : analysis.OutroVocalConfidence;
        if (curve is not { Length: > 0 } || confidence is not >= 0.2) return true;
        var duration = analysis.VocalWindowSeconds;
        var offset = role == "intro" ? 0 : Math.Max(0, analysis.DurationSeconds - duration);
        return duration > 0 && start >= offset && end <= offset + duration;
    }

    private static void Keep(List<double> points, double value, double minimum, double maximum, int limit)
    {
        if (points.Count >= limit || !AutomixMath.Finite(value) || value < minimum || value > maximum
            || points.Any(point => Math.Abs(point - value) < 0.001)) return;
        points.Add(value);
    }
}
