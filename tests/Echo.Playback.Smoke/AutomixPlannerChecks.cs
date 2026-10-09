using Echo.Playback;

internal static class AutomixPlannerChecks
{
    public static void Run()
    {
        var deadline = AutomixPlanner.Create(Input(AutomixAnalysis.Unavailable(10, "analysis_deadline"),
            AutomixAnalysis.Unavailable(30, "analysis_deadline"), 1, 48_000, false));
        Check(deadline.Mode == "short_crossfade" && deadline.OverlapFrames == 86_400
            && deadline.FadeStartOutputFrame == 393_600
            && deadline.FadeEndOutputFrame == deadline.FadeStartOutputFrame + deadline.OverlapFrames
            && deadline.CommitOutputFrame == deadline.FadeEndOutputFrame
            && deadline.FallbackReason == "analysis_unavailable", "deadline fallback keeps the short crossfade");

        var matched = AutomixPlanner.Create(Input(Track(120, "8B"), Track(120, "8B"), 10, 480_000, false));
        Check(matched.Mode == "beat_match" && matched.TempoRatio == 1 && matched.HandoffProfile == "rhythmic_bass_swap"
            && matched.OverlapFrames == 192_000 && matched.FallbackReason is null
            && matched.FadeEndOutputFrame == matched.FadeStartOutputFrame + matched.OverlapFrames
            && matched.CommitOutputFrame == matched.FadeEndOutputFrame, "compatible grids beat-match");

        var incompatible = AutomixPlanner.Create(Input(Track(120, "8B"), Track(100, "8B"), 10, 480_000, false));
        Check(incompatible.Mode != "beat_match" && incompatible.FallbackReason == "tempo_incompatible",
            "tempo outside the native stretch becomes a musical handoff");

        var direct = AutomixPlanner.Create(Input(Track(120, "8B"), Track(120, "8B"), 10, 480_000, true));
        Check(direct.Mode == "gapless_fallback" && direct.FallbackReason == "dsd_direct" && direct.OverlapFrames == 1,
            "DSD does not enter the mixer");
    }

    private static AutomixPlanInput Input(AutomixAnalysis current, AutomixAnalysis next, double position, long frame, bool dsd) => new()
    {
        QueueRevision = 1, FromItemId = "a", FromTrackId = "track-a", ToItemId = "b", ToTrackId = "track-b",
        MixSampleRate = 48_000, CurrentOutputFrame = frame, PositionSeconds = position,
        Current = current, Next = next, CurrentIsDsd = dsd, NextIsDsd = dsd,
    };

    private static AutomixAnalysis Track(double bpm, string camelot)
    {
        var key = new AutomixKey(0, "major", camelot, 0.8);
        var energy = Enumerable.Repeat(0.7, 18).ToArray();
        AutomixWindow Side(string role, double start) => new()
        {
            Role = role, StartSeconds = start, DurationSeconds = 36, Energy = energy,
            Bpm = bpm, BpmConfidence = 0.9, BeatOffsetMs = 0, BeatPhaseConfidence = 0.8,
            BeatsPerBar = 4, DownbeatOffsetMs = 0, DownbeatConfidence = 0.9,
        };
        return new AutomixAnalysis
        {
            Status = "complete", DurationSeconds = 180, Bpm = bpm, BpmConfidence = 0.9,
            Windows = [Side("intro", 0), Side("outro", 144)], Key = key, IntroKey = key, OutroKey = key, BeatsPerBar = 4,
        };
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
