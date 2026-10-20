using Echo.Playback;

internal static class AlphaPlanningChecks
{
    internal static AutomixAnalysis Track(double bpm = 120, bool spectral = true, Func<int, int, float>? sample = null) => new()
    {
        Status = "complete", DurationSeconds = 60, Bpm = bpm, BpmConfidence = .95,
        Windows = [Window("intro", 0, bpm, spectral, sample), Window("outro", 24, bpm, spectral, sample)],
    };

    // No invented beat phase: these fixtures exercise Alpha selection on measured windows only.
    private static AutomixWindow Window(string role, double start, double bpm, bool spectral, Func<int, int, float>? sample) => new()
    {
        Role = role, StartSeconds = start, DurationSeconds = 36, Bpm = bpm, BpmConfidence = .95,
        Energy = Enumerable.Repeat(.4, 36).ToArray(),
        AlphaSpectral = spectral ? Enumerable.Range(0, 9000).Select(i => sample?.Invoke(i / 5, i % 5) ?? (i % 5 == 4 ? .08f : .2f)).ToArray() : null,
    };

    internal static AutomixPlanInput Input(AutomixAnalysis a, AutomixAnalysis b) => new()
    {
        QueueRevision = 1, FromItemId = "a", FromTrackId = "a", ToItemId = "b", ToTrackId = "b",
        MixSampleRate = 48000, CurrentOutputFrame = 0, PositionSeconds = 10,
        Current = a, Next = b, MaxTransitionSeconds = 12,
    };

    internal static void Run()
    {
        Assert.True(AutomixAlphaRoutes.Score(120, 120, new(.7, .3, 0, .1, 4, false), null, 0) == "bass_swap", "scored low-clash route");
        Assert.True(AutomixAlphaRoutes.Score(120, 120, new(.1, .3, 0, .1, 4, false), null, 0) == "plain_blend", "normal real material remains eligible with trailingSpan=0");
        Assert.True(AutomixAlphaRoutes.Score(null, null, new(.1, .3, 0, .1, 4, false), null, 3) == "tail_ride", "measured trailing space");
        Assert.True(AutomixAlphaRoutes.Score(120, 120, new(.7, .3, 0, .1, 1, false), null, 0) == "beat_cut", "short rhythmic join");
        Assert.True(AutomixAlphaRoutes.Score(120, 120, new(.1, .3, 0, .1, 4, true), null, 0) == "show_mode", "build-up route priority");
        Assert.True(AutomixAlphaRoutes.Score(120, 120, new(.9, .8, 0, .1, 4, false), null, 0) == "learned_eq", "high-clash transient route");
        Assert.Near(AutomixAlphaRoutes.Fold(4), 1, 0, "multi-octave tempo folding");
        Assert.True(AutomixAlphaCueScore.Lengths(12, 120, 127).Contains(4), "two-bar candidate is preserved");

        var a = Track(); var b = Track();
        var input = Input(a, b);
        var plan = AutomixPlanner.Create(input);
        Assert.True(plan.AlphaDsp?.Style == "plain_blend" && plan.Schedule is null && plan.HandoffProfile == "balanced", "default planner reaches Alpha DSP");
        var json = plan.ToJson();
        Assert.True(json["alphaDsp"]?["style"]?.GetValue<string>() == "plain_blend" && !json.ContainsKey("handoffSchedule"), "native protocol has mutually exclusive Alpha plan");
        var noMaterial = Track(spectral: false);
        var legacy = AutomixPlanner.Create(Input(noMaterial, noMaterial)) with { Schedule = new(.1, .3, .6, .8, .3, .6) };
        Assert.True(ReferenceEquals(AutomixAlphaDirector.Direct(legacy, a, noMaterial), legacy), "missing spectral preserves exact original plan");
        var truncated = new AutomixAnalysis { DurationSeconds = 60, Windows = [new() { Role = "intro", DurationSeconds = 1, AlphaSpectral = new float[100] }] };
        Assert.True(ReferenceEquals(AutomixAlphaDirector.Direct(legacy, a, truncated), legacy), "partial spectral does not discard measured handoff");
        foreach (var rate in new[] { 44100, 48000, 96000 })
        {
            var candidate = legacy with { MixSampleRate = rate, HandoffProfile = "beat_cut", OverlapFrames = rate / 2,
                FadeStartOutputFrame = 100, FadeEndOutputFrame = 100 + rate / 2, CommitOutputFrame = 100 + rate / 2 };
            var cut = AutomixAlphaDirector.Direct(candidate, a, b);
            Assert.True(cut.AlphaDsp?.Style == "beat_cut" && cut.OverlapFrames == AutomixMath.Round(.03 * rate)
                && cut.FadeStartOutputFrame + cut.OverlapFrames == candidate.FadeEndOutputFrame
                && cut.CommitOutputFrame == candidate.CommitOutputFrame && cut.NextStartSeconds == candidate.NextStartSeconds, "30 ms short cut preserves commit/source clock");
        }
        var boundary = legacy with { FallbackReason = "track_boundary_fade" };
        Assert.True(ReferenceEquals(AutomixAlphaDirector.Direct(boundary, a, b), boundary), "fixed boundary remains unchanged");
        var echo = legacy with { EchoThrow = true };
        Assert.True(ReferenceEquals(AutomixAlphaDirector.Direct(echo, a, b), echo), "echo throw retains native path");
        var side = AutomixAlphaSide.Create(a.Windows![0], 0, 16)!;
        Assert.True(side.Value.Frames == 800 && AutomixAlphaSide.Create(a.Windows[0], 0, 16.1) is null, "material max 800 hops");
        Assert.True(AutomixAlphaSide.Create(a.Windows[0], -.1, 4) is null && AutomixAlphaSide.Create(a.Windows[0], 28, 8, 1.1) is null, "material clock and tempo coverage");
        var sampleA = Track(sample: (frame, band) => band == 4 ? .9f : frame % 100 == 50 ? 1f : .01f);
        var eq = AutomixAlphaDirector.Direct(legacy with { HandoffProfile = "balanced" }, sampleA, sampleA);
        Assert.True(eq.AlphaDsp is { Style: "learned_eq", EqControls.Length: 12 }
            && eq.AlphaDsp.EqControls.All(x => x is >= 0 and <= 1), "real spectral material drives learned EQ through native JSON");
        Assert.True(eq.ToJson()["alphaDsp"]?["eqControls"]?.AsArray().Count == 12, "EQ controls transported");
    }
}
