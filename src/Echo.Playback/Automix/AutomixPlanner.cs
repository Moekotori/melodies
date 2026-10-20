namespace Echo.Playback;

internal static partial class AutomixPlanner
{
    internal static AutomixPlan Create(AutomixPlanInput input)
    {
        var plan = Refine(Transition(input), input);
        plan.Schedule = AutomixHandoff.Schedule(plan, input.Current, input.Next);
        return AutomixAlphaDirector.Direct(plan, input.Current, input.Next);
    }

    private static AutomixPlan Transition(AutomixPlanInput input)
    {
        if (input.CurrentIsDsd || input.NextIsDsd) return Fallback(input, "dsd_direct");
        if (Math.Abs(input.PlaybackRate - 1) > 0.0001) return Fallback(input, "playback_rate_active");
        var current = input.Current;
        var next = input.Next;
        var boundary = AutomixMath.Clamp(input.TrackBoundaryFadeMs / 1000, 0, 5);
        if (current is not null && next is not null && boundary > 0)
        {
            var rate = Math.Max(8000, input.MixSampleRate);
            var remaining = Math.Max(0, current.DurationSeconds - input.PositionSeconds);
            var boundarySeconds = AutomixMath.Min(boundary, remaining - 0.2, next.DurationSeconds - 0.2);
            if (boundarySeconds > 0)
            {
                var boundaryFade = input.CurrentOutputFrame + Math.Max(0, AutomixMath.Round((remaining - boundarySeconds) * rate));
                var boundaryOverlap = Math.Max(2, AutomixMath.Round(boundarySeconds * rate));
                return Plan(input, "short_crossfade", "balanced", boundaryFade, boundaryOverlap, current.DurationSeconds, 0, 1, "track_boundary_fade");
            }
        }
        if (current is null || next is null || current.Status is "error" or "unavailable" || next.Status is "error" or "unavailable")
            return Fallback(input, "analysis_unavailable", "short_crossfade");
        var left = current.DurationSeconds - input.PositionSeconds;
        if (left < 4 || next.DurationSeconds < 4) return Fallback(input, "short_track", "short_crossfade");

        var sampleRate = Math.Max(8000, input.MixSampleRate);
        var maxTransition = AutomixMath.Clamp(input.MaxTransitionSeconds, 2, 16);
        var currentBpm = Tempo(current, "outro");
        var nextBpm = Tempo(next, "intro");
        var rawRatio = currentBpm is not null && nextBpm is not null ? TempoRatio(currentBpm.Value, nextBpm.Value) : 1;
        var currentDownbeats = Downbeats(current, "outro");
        var nextDownbeats = Downbeats(next, "intro");
        var floor = AutomixBounds.EntryLimit(next);
        var currentBeats = Beats(current, "outro");
        var nextEntryDownbeat = EntryDownbeat(nextDownbeats, floor);
        var nextEntryBeat = EntryDownbeat(Beats(next, "intro"), floor);
        var currentMetre = AutomixWindows.Transition(current, "outro")?.BeatsPerBar ?? current.BeatsPerBar;
        var nextMetre = AutomixWindows.Transition(next, "intro")?.BeatsPerBar ?? next.BeatsPerBar;
        var barAligned = currentDownbeats.Length > 0 && nextEntryDownbeat is not null && currentMetre == nextMetre;
        var entryPoint = barAligned ? nextEntryDownbeat : nextEntryBeat;
        if (current.Status == "complete" && next.Status == "complete" && currentBpm is not null && nextBpm is not null
            && (rawRatio < MinimumTempoRatio || rawRatio > MaximumTempoRatio))
            return Musical(input, current, next, "tempo_incompatible");
        var outgoingKey = AutomixWindows.Key(current, "outro");
        var incomingKey = AutomixWindows.Key(next, "intro");
        if (outgoingKey is { Confidence: >= 0.45 } && incomingKey is { Confidence: >= 0.45 } && !KeysCompatible(outgoingKey, incomingKey))
            return Musical(input, current, next, "key_incompatible");

        var canMatch = current.Status == "complete" && next.Status == "complete" && currentBpm is not null && nextBpm is not null
            && rawRatio is >= MinimumTempoRatio and <= MaximumTempoRatio && KeysCompatible(outgoingKey, incomingKey)
            && (barAligned || currentBeats.Length > 0) && entryPoint is not null;
        var beat = currentBpm is null ? 0.5 : 60 / currentBpm.Value;
        var bar = beat * (currentMetre == 3 ? 3 : 4);
        var energy = Energy(current, next);
        var matchBars = energy.Density >= 0.62 ? 2 : energy.Density >= 0.4 ? 3 : 4;
        var phraseLimit = energy.Density >= 0.62 ? 5 : energy.Density >= 0.4 ? 7 : 10;
        var ride = TailRide(current, energy);
        var preferred = AutomixBounds.PreferredEnd(current);
        var runway = preferred - input.PositionSeconds - 0.5;
        if (runway < 0.5) return Fallback(input, "short_track", "short_crossfade");
        var ceiling = AutomixMath.Min(maxTransition, runway, AutomixBounds.IncomingRunway(next.DurationSeconds, floor, canMatch ? rawRatio : 1));
        if (ceiling < 2d / sampleRate) return Fallback(input, "short_track", "short_crossfade");
        var nextStart = canMatch && entryPoint is not null ? entryPoint.Value : floor;
        var quantum = barAligned && ceiling >= bar ? bar : beat;
        var grid = barAligned && (quantum == bar || currentBeats.Length == 0) ? currentDownbeats : currentBeats;
        var desired = AutomixMath.Clamp(bar * matchBars, Math.Min(4, maxTransition), maxTransition) * (ride ? 1.35 : 1);
        var matched = canMatch ? Match(desired) : null;
        var exhausted = canMatch && matched is null;
        canMatch = matched is not null;
        double overlapSeconds;
        double end;
        if (matched is not null) { overlapSeconds = matched.Value.Overlap; end = matched.Value.End; }
        else
        {
            nextStart = floor;
            var baseOverlap = AutomixMath.Clamp(Math.Min(Math.Min(maxTransition, phraseLimit), Math.Min(left * 0.18, next.DurationSeconds * 0.14)),
                Math.Min(3, maxTransition), maxTransition);
            overlapSeconds = Math.Min(ceiling, ride ? baseOverlap * 1.35 : baseOverlap);
            end = PhraseEnd(overlapSeconds);
        }
        var ratio = canMatch ? AutomixMath.RoundMillis(rawRatio) : 1;
        var profile = ride ? "tail_ride" : BassSwap(canMatch, energy) ? "rhythmic_bass_swap" : "balanced";
        var conflict = VocalConflict(current, next, end - overlapSeconds, end, nextStart, ratio);
        if (conflict >= 0.45)
        {
            var cut = BeatCut(input, current, next, "vocal_conflict");
            if (cut is not null) return cut;
        }
        if (conflict >= 0.3 && overlapSeconds > bar)
        {
            if (canMatch)
            {
                var shortened = Match(bar);
                if (shortened is not null) { overlapSeconds = shortened.Value.Overlap; end = shortened.Value.End; }
            }
            else
            {
                overlapSeconds = Math.Max(0.5, Math.Min(overlapSeconds, bar));
                end = PhraseEnd(overlapSeconds);
            }
            conflict = VocalConflict(current, next, end - overlapSeconds, end, nextStart, ratio);
        }
        var fadeSource = Math.Max(input.PositionSeconds, end - overlapSeconds);
        var fadeStart = input.CurrentOutputFrame + AutomixMath.Round((fadeSource - input.PositionSeconds) * sampleRate);
        var overlap = Math.Max(2, AutomixMath.Round(Math.Max(0.001, end - fadeSource) * sampleRate));
        var handoff = VocalHandoff(current, next, fadeSource, end, nextStart, ratio);
        return Plan(input, canMatch ? "beat_match" : "phrase_crossfade", profile, fadeStart, overlap, end, nextStart, ratio,
            canMatch ? null : exhausted ? "short_track" : "beat_or_key_incompatible", conflict,
            profile == "rhythmic_bass_swap" ? handoff.Exit : null, profile == "rhythmic_bass_swap" ? handoff.Entry : null);

        double? Tempo(AutomixAnalysis analysis, string role)
        {
            var bpm = AutomixWindows.Tempo(analysis, role);
            return bpm is null ? null : NormalizeBpm(bpm.Value);
        }

        double PhraseEnd(double overlap) => PhraseBoundary(current, input.PositionSeconds + overlap + 0.5, preferred, current.DurationSeconds,
            seconds => VocalConflict(current, next, seconds - overlap, seconds, nextStart) * 4);

        (double Overlap, double End)? Match(double desiredSeconds)
        {
            var units = (int)Math.Floor(ceiling / quantum + 1e-6);
            if (units < 1) return null;
            for (var count = (int)AutomixMath.Clamp(AutomixMath.Round(desiredSeconds / quantum), 1, units); count >= 1; count--)
            {
                var overlap = count * quantum;
                var start = Nearest(grid, preferred - overlap,
                    Math.Max(input.PositionSeconds + 0.5, preferred - ceiling), current.DurationSeconds - quantum);
                if (start is null) continue;
                var fadeEnd = Math.Min(current.DurationSeconds, Math.Max(preferred, start.Value + overlap));
                if (fadeEnd - start.Value <= ceiling + 1e-6) return (fadeEnd - start.Value, fadeEnd);
            }
            return null;
        }
    }
}
