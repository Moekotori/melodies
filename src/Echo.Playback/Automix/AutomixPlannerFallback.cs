namespace Echo.Playback;

internal static partial class AutomixPlanner
{
    private static AutomixPlan Fallback(AutomixPlanInput input, string reason, string mode = "gapless_fallback")
    {
        var rate = Math.Max(8000, input.MixSampleRate);
        var duration = input.Current?.DurationSeconds ?? input.PositionSeconds;
        var remaining = Math.Max(0, duration - input.PositionSeconds);
        var maxTransition = AutomixMath.Clamp(input.MaxTransitionSeconds, 2, 16);
        var overlapSeconds = mode == "short_crossfade"
            ? AutomixMath.Min(AutomixMath.Clamp(remaining * 0.2, 1, Math.Min(3, maxTransition)), remaining,
                AutomixBounds.IncomingRunway(input.Next?.DurationSeconds ?? 0, 0))
            : 0;
        var bounded = (long)Math.Floor(overlapSeconds * rate);
        if (mode == "short_crossfade" && bounded < 2) mode = "gapless_fallback";
        var overlap = mode == "gapless_fallback" ? 1 : bounded;
        var fadeStart = input.CurrentOutputFrame + Math.Max(0, (long)Math.Floor(remaining * rate) - overlap);
        return Plan(input, mode, "balanced", fadeStart, overlap, duration, 0, 1, reason);
    }

    private static AutomixPlan? BeatCut(AutomixPlanInput input, AutomixAnalysis current, AutomixAnalysis next, string reason)
    {
        var energy = Energy(current, next);
        if (energy.Tail < 0.62 || energy.Head < 0.68) return null;
        var entryLimit = AutomixBounds.EntryLimit(next);
        var entry = EntryDownbeat(Downbeats(next, "intro"), entryLimit) ?? EntryDownbeat(Beats(next, "intro"), entryLimit);
        if (entry is null) return null;
        var rate = Math.Max(8000, input.MixSampleRate);
        var audible = AutomixBounds.PreferredEnd(current);
        var earliest = Math.Max(input.PositionSeconds + 0.08, AutomixBounds.AudibleEnd(current));
        var cut = Nearest(Downbeats(current, "outro"), Math.Max(earliest, audible), earliest, current.DurationSeconds)
            ?? Nearest(Beats(current, "outro"), Math.Max(earliest, audible), earliest, current.DurationSeconds);
        if (cut is null) return null;
        var fadeSource = Math.Max(input.PositionSeconds, cut.Value - 0.04);
        var fadeStart = input.CurrentOutputFrame + AutomixMath.Round((fadeSource - input.PositionSeconds) * rate);
        var overlap = Math.Max(2, AutomixMath.Round((cut.Value - fadeSource) * rate));
        var tempo = AutomixWindows.Tempo(current, "outro");
        var bpm = tempo is null ? (double?)null : NormalizeBpm(tempo.Value);
        var conflict = VocalConflict(current, next, Math.Max(input.PositionSeconds, cut.Value - 0.5), cut.Value, entry.Value);
        var send = AutomixMath.RoundMillis(AutomixMath.Clamp(0.38 + energy.Density * 0.3 - conflict * 0.24, 0.28, 0.68));
        var feedback = AutomixMath.RoundMillis(AutomixMath.Clamp(0.3 + energy.Density * 0.22 - conflict * 0.16, 0.24, 0.52));
        return Plan(input, "short_crossfade", "beat_cut", fadeStart, overlap, cut.Value, entry.Value, 1, reason, conflict,
            echo: true, delay: AutomixMath.RoundMillis(bpm is null ? 0.25 : AutomixMath.Clamp(30 / bpm.Value, 0.08, 1)),
            send: send, feedback: feedback);
    }

    private static AutomixPlan Musical(AutomixPlanInput input, AutomixAnalysis current, AutomixAnalysis next, string reason)
    {
        var cut = BeatCut(input, current, next, reason);
        if (cut is not null) return cut;
        var rate = Math.Max(8000, input.MixSampleRate);
        var maxTransition = AutomixMath.Clamp(input.MaxTransitionSeconds, 2, 16);
        var audible = AutomixBounds.PreferredEnd(current);
        var preferred = audible > input.PositionSeconds + 0.25 ? audible : current.DurationSeconds;
        var available = Math.Max(0, preferred - input.PositionSeconds);
        var energy = Energy(current, next);
        var outgoing = AutomixWindows.Tempo(current, "outro");
        var bpm = outgoing is null ? (double?)null : NormalizeBpm(outgoing.Value);
        var downbeats = Downbeats(next, "intro");
        var floor = AutomixBounds.EntryLimit(next);
        var entryBeat = EntryDownbeat(downbeats, floor);
        var ride = TailRide(current, energy);
        var preferredOverlap = (energy.Density >= 0.62 ? 3.2 : energy.Density >= 0.4 ? 4 : 4.8) * (ride ? 1.4 : 1);
        var useful = AutomixMath.Min(maxTransition, Math.Max(0, available - 0.25), next.DurationSeconds * 0.12,
            AutomixBounds.IncomingRunway(next.DurationSeconds, entryBeat ?? floor));
        if (useful < 2d / rate) return Fallback(input, "short_track", "short_crossfade");
        var overlapSeconds = AutomixMath.Clamp(preferredOverlap, Math.Min(2.75, useful), useful);
        var end = SelectEnd();
        var conflict = VocalConflict(current, next, end - overlapSeconds, end, entryBeat ?? floor);
        if (conflict >= 0.3 && overlapSeconds > 1)
        {
            overlapSeconds = Math.Max(0.5, Math.Min(overlapSeconds, bpm is null ? 2 : 240 / bpm.Value));
            end = SelectEnd();
            conflict = VocalConflict(current, next, end - overlapSeconds, end, entryBeat ?? floor);
        }
        var fadeSource = Math.Max(input.PositionSeconds, end - overlapSeconds);
        var fadeStart = input.CurrentOutputFrame + AutomixMath.Round((fadeSource - input.PositionSeconds) * rate);
        var overlap = Math.Max(2, AutomixMath.Round(Math.Max(0.001, end - fadeSource) * rate));
        var nextStart = EntryDownbeat(downbeats, floor) ?? floor;
        var handoff = VocalHandoff(current, next, fadeSource, end, nextStart);
        return Plan(input, "short_crossfade", ride ? "tail_ride" : "rhythmic_bass_swap", fadeStart, overlap, end, nextStart, 1, reason,
            conflict, ride ? null : handoff.Exit, ride ? null : handoff.Entry);

        double SelectEnd() => PhraseBoundary(current, input.PositionSeconds + overlapSeconds,
            Math.Max(input.PositionSeconds + overlapSeconds, preferred),
            Math.Min(current.DurationSeconds, Math.Max(input.PositionSeconds + overlapSeconds, preferred + 1)),
            seconds => VocalConflict(current, next, seconds - overlapSeconds, seconds, entryBeat ?? floor) * 4);
    }

    private static AutomixPlan Plan(AutomixPlanInput input, string mode, string profile, long fadeStart, long overlap,
        double end, double nextStart, double tempo, string? reason, double? conflict = null, double? exit = null, double? entry = null,
        bool echo = false, double? delay = null, double? send = null, double? feedback = null) => new()
    {
        PlanId = AutomixMath.Id(input, fadeStart), QueueRevision = input.QueueRevision,
        FromItemId = input.FromItemId, FromTrackId = input.FromTrackId, ToItemId = input.ToItemId, ToTrackId = input.ToTrackId,
        MixSampleRate = Math.Max(8000, input.MixSampleRate), Mode = mode, HandoffProfile = profile,
        CurrentStartSeconds = AutomixMath.RoundMillis(input.PositionSeconds), CurrentEndSeconds = AutomixMath.RoundMillis(end),
        FadeStartOutputFrame = fadeStart, FadeEndOutputFrame = fadeStart + overlap, CommitOutputFrame = fadeStart + overlap,
        NextStartSeconds = AutomixMath.RoundMillis(nextStart), OverlapFrames = overlap,
        NextGainDb = AutomixLoudness.NextGainDb(input), TempoRatio = tempo, FallbackReason = reason, VocalConflict = conflict,
        VocalExit = exit, VocalEntry = entry, EchoThrow = echo, EchoDelaySeconds = delay, EchoSend = send, EchoFeedback = feedback,
    };

}
