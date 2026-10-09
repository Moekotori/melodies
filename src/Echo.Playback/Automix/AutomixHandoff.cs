namespace Echo.Playback;

internal readonly record struct AutomixVocalPoint(double Progress, double Outgoing, double Incoming, double Evidence);

internal static class AutomixVocals
{
    internal static (double Exit, double Entry) Handoff(IReadOnlyList<AutomixVocalPoint> points)
    {
        var candidates = points
            .Where(point => point.Progress is >= 0.32 and <= 0.9 && point.Evidence >= 0.2)
            .Select(point => new AutomixVocalPoint(
                AutomixMath.Clamp(point.Progress, 0, 1), AutomixMath.Clamp(point.Outgoing, 0, 1),
                AutomixMath.Clamp(point.Incoming, 0, 1), AutomixMath.Clamp(point.Evidence, 0, 1)))
            .OrderBy(point => point.Progress)
            .ToArray();
        if (candidates.Length == 0) return (0.55, 0.7);
        var rests = candidates.Where((candidate, index) => candidate.Progress <= 0.82 && candidate.Outgoing <= 0.18
            && candidates.Skip(index + 1).Any(later => later.Outgoing >= 0.42)).ToArray();
        if (rests.Length > 0) return Timing(rests[^1].Progress);
        var release = candidates.Select((candidate, index) => (candidate, index)).FirstOrDefault(item =>
            item.candidate.Progress >= 0.42 && item.candidate.Outgoing <= 0.24
            && candidates.Take(item.index).Any(earlier => earlier.Outgoing >= 0.42)
            && candidates.Skip(item.index + 1).All(later => later.Outgoing < 0.42));
        if (release.candidate.Progress > 0) return Timing(release.candidate.Progress);
        var sustain = candidates.Where(candidate => candidate.Progress is >= 0.44 and <= 0.86).ToArray();
        if (sustain.Length >= 8 && sustain.All(candidate => candidate.Outgoing >= 0.42)) return (0.76, 0.88);
        var chosen = candidates[0];
        var best = double.PositiveInfinity;
        foreach (var candidate in candidates)
        {
            var score = (candidate.Outgoing * candidate.Incoming * 2.4
                + Math.Max(0, candidate.Outgoing - 0.32) * 0.8
                + Math.Abs(candidate.Progress - 0.66) * 0.22) / candidate.Evidence;
            if (score < best) { best = score; chosen = candidate; }
        }
        if (chosen.Outgoing > 0.42)
        {
            var later = candidates.FirstOrDefault(candidate => candidate.Progress > chosen.Progress && candidate.Outgoing < 0.24);
            if (later.Progress > 0) chosen = later;
        }
        return Timing(chosen.Progress);
    }

    private static (double Exit, double Entry) Timing(double progress)
    {
        var exit = AutomixMath.RoundMillis(AutomixMath.Clamp(progress - 0.16, 0.38, 0.76));
        var entry = AutomixMath.RoundMillis(AutomixMath.Clamp(progress - 0.04, exit + 0.06, 0.88));
        return (exit, entry);
    }
}

internal static class AutomixHandoff
{
    internal static AutomixSchedule? Schedule(AutomixPlan plan, AutomixAnalysis? current, AutomixAnalysis? next)
    {
        if (current is null || next is null || plan.Mode != "beat_match" || plan.HandoffProfile != "rhythmic_bass_swap") return null;
        var outgoing = AutomixWindows.Transition(current, "outro");
        var incoming = AutomixWindows.Transition(next, "intro");
        var bpm = AutomixWindows.Tempo(current, "outro");
        if (outgoing is null || incoming is null || bpm is null) return null;
        var duration = (double)plan.OverlapFrames / plan.MixSampleRate;
        var start = plan.CurrentEndSeconds - duration;
        var period = 60 / bpm.Value;
        if (duration < 2 || plan.TempoRatio <= 0) return null;
        var outgoingBeats = AutomixWindows.BeatGrid(outgoing);
        var incomingBeats = AutomixWindows.BeatGrid(incoming);
        if (!outgoingBeats.Any(beat => Math.Abs(beat - start) < 0.025)
            || !incomingBeats.Any(beat => Math.Abs(beat - plan.NextStartSeconds) < 0.025)) return null;
        var outgoingBars = AutomixWindows.DownbeatGrid(outgoing);
        var incomingBars = AutomixWindows.DownbeatGrid(incoming);
        var barAligned = outgoing.BeatsPerBar == incoming.BeatsPerBar
            && outgoingBars.Any(beat => Math.Abs(beat - start) < 0.025)
            && incomingBars.Any(beat => Math.Abs(beat - plan.NextStartSeconds) < 0.025);
        (double High, double Low)? timing = barAligned ? Select(true) : null;
        timing ??= Select(false);
        if (timing is null) return null;
        var high = timing.Value.High;
        var low = timing.Value.Low;
        var highWidth = Math.Max(0.08, Math.Min(0.16, period * 0.25));
        var lowWidth = Math.Max(0.12, Math.Min(0.3, period * 0.5));
        var holdStart = Math.Max(high + highWidth, low - Math.Min(period, duration * 0.15));
        var holdEnd = low + lowWidth;
        if (holdEnd >= duration * 0.9 || holdEnd - holdStart < 0.08) return null;
        return new(high / duration, (high + highWidth) / duration, low / duration, (low + lowWidth) / duration,
            holdStart / duration, holdEnd / duration);

        (double High, double Low)? Select(bool useBars)
        {
            var grid = useBars ? outgoingBars : outgoingBeats;
            var spacing = period * (useBars ? outgoing.BeatsPerBar!.Value : 1);
            var highPoint = Nearest(grid, duration * 0.35, duration * 0.1, duration * 0.5);
            if (highPoint is null) return null;
            var lowPoint = Nearest(grid, useBars ? highPoint.Value + spacing : duration * 0.65, highPoint.Value + spacing - 0.002, duration * 0.8);
            return lowPoint is null ? null : (highPoint.Value, lowPoint.Value);
        }

        double? Nearest(double[] grid, double target, double min, double max)
        {
            double? selected = null;
            foreach (var beat in grid)
            {
                var time = beat - start;
                if (time < min || time > max) continue;
                if (selected is null || Math.Abs(time - target) < Math.Abs(selected.Value - target)) selected = time;
            }
            return selected;
        }
    }
}
