namespace Echo.Playback;

internal sealed record AutomixAlphaDsp(string Style, double[]? EqControls = null);

internal static class AutomixAlphaDirector
{
    internal static AutomixPlan Direct(AutomixPlan plan, AutomixAnalysis? current, AutomixAnalysis? next)
    {
        if (current is null || next is null || plan.Mode == "gapless_fallback" || plan.EchoThrow
            || plan.FallbackReason == "track_boundary_fade") return plan;
        var seconds = plan.OverlapFrames / (double)plan.MixSampleRate;
        if (!(seconds >= 0.4 && seconds <= 16)) return plan;
        var a = AutomixAlphaSide.Create(AutomixWindows.Transition(current, "outro"), plan.CurrentEndSeconds - seconds, seconds);
        var b = AutomixAlphaSide.Create(AutomixWindows.Transition(next, "intro"), plan.NextStartSeconds, seconds, plan.TempoRatio);
        // Keep measured beat/bar/vocal handoffs if either selected window is incomplete.
        if (a is null || b is null) return plan;
        var outgoing = AutomixWindows.Tempo(current, "outro");
        var incoming = AutomixWindows.Tempo(next, "intro");
        bool? quieter = AutomixMath.Finite(current.OutroLufs) && AutomixMath.Finite(next.IntroLufs)
            ? next.IntroLufs < current.OutroLufs : null;
        var style = plan.HandoffProfile == "beat_cut" ? "beat_cut"
            : AutomixAlphaRoutes.Choose(outgoing, incoming * plan.TempoRatio, KeySimilarity(current, next),
                AutomixAlphaProfile.Measure(a.Value, b.Value), seconds, quieter, AutomixAlphaProfile.TrailingSpan(a.Value));
        if (style == "beat_cut")
        {
            var overlap = Math.Min(plan.OverlapFrames, Math.Max(2, AutomixMath.Round(0.03 * plan.MixSampleRate)));
            var fadeStart = plan.FadeEndOutputFrame - overlap;
            return plan with { HandoffProfile = "balanced", Schedule = null, OverlapFrames = overlap,
                FadeStartOutputFrame = fadeStart, PlanId = System.FormattableString.Invariant($"{plan.QueueRevision}:{plan.FromItemId}:{plan.ToItemId}:{fadeStart}"),
                AlphaDsp = new(style) };
        }
        var controls = style == "learned_eq" ? AutomixAlphaEqFeatures.Controls(a.Value, b.Value, seconds, outgoing ?? 0, incoming ?? 0) : null;
        return plan with { HandoffProfile = "balanced", Schedule = null,
            AlphaDsp = new(style == "learned_eq" && controls is null ? "plain_blend" : style, controls) };
    }

    private static double? KeySimilarity(AutomixAnalysis a, AutomixAnalysis b)
    {
        var left = AutomixWindows.Key(a, "outro"); var right = AutomixWindows.Key(b, "intro");
        if (left is not { Confidence: >= 0.45, Chroma.Length: 12 } || right is not { Confidence: >= 0.45, Chroma.Length: 12 }) return null;
        double dot = 0, powerA = 0, powerB = 0;
        for (var i = 0; i < 12; i++)
        {
            if (!double.IsFinite(left.Chroma[i]) || !double.IsFinite(right.Chroma[i])) return null;
            dot += left.Chroma[i] * right.Chroma[i];
            powerA += left.Chroma[i] * left.Chroma[i]; powerB += right.Chroma[i] * right.Chroma[i];
        }
        return powerA > 0 && powerB > 0 ? dot / Math.Sqrt(powerA * powerB) : null;
    }
}
