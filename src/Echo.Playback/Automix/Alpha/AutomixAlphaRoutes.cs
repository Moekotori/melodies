namespace Echo.Playback;

// ECHO/QPlayer RouteGates and RouteScore, including supplied tie order.
internal static class AutomixAlphaRoutes
{
    internal static string Choose(double? outgoing, double? incoming, double? keySimilarity,
        AutomixAlphaProfile material, double seconds, bool? incomingQuieter, double? trailingSpan)
    {
        if (Score(outgoing, incoming, material, incomingQuieter, trailingSpan) is { } scored) return scored;
        if (!KnownTempo(outgoing, incoming)) return "plain_blend";
        var ratio = Fold(incoming!.Value / outgoing!.Value);
        var conflict = Difference(ratio) >= 0.10 || keySimilarity < 0.70;
        if (!conflict) return "plain_blend";
        if (material.OverlapSeconds < 1.5) return "beat_cut";
        if (material.BuildUp && material.DropReliable >= 0.25) return "show_mode";
        if (material.TailDecayDbPerSec >= -3 && material.HeadOnset <= 0.25) return "tail_ride";
        if (material.LowClash >= 0.5 && material.DropReliable >= 0.25 && seconds >= 2) return "learned_eq";
        return "bass_swap";
    }

    internal static string? Score(double? outgoing, double? incoming, AutomixAlphaProfile material,
        bool? incomingQuieter, double? trailingSpan)
    {
        var known = KnownTempo(outgoing, incoming);
        var tempoOk = known && Difference(Fold(incoming!.Value / outgoing!.Value)) <= 0.10;
        var beatsA = known ? material.OverlapSeconds * outgoing!.Value / 60 : (double?)null;
        var beatsB = known ? material.OverlapSeconds * incoming!.Value / 60 : (double?)null;
        var longWindow = beatsA >= 8 && beatsB >= 8;
        var overlapOk = material.OverlapSeconds >= 1.5;
        var tailRide = incomingQuieter is null && trailingSpan >= 3 && !longWindow;
        string? best = null;
        double bestScore = 0;
        if (tempoOk && material.LowClash >= 0.6 && overlapOk) Consider("bass_swap", 12 * Bonus(material.LowClash));
        if (tempoOk && overlapOk && !tailRide) Consider("plain_blend", 10);
        if (tailRide) Consider("tail_ride", 10);
        if (!overlapOk && beatsA is >= 1 and < 4) Consider("beat_cut", 1);
        if (material.BuildUp) Consider("show_mode", 30 * Bonus(material.DropReliable));
        if (tempoOk && material.DropReliable >= 0.7 && material.LowClash >= 0.6)
            Consider("learned_eq", 30 * material.LowClash * Bonus(material.DropReliable));
        return best;

        void Consider(string style, double score) { if (score > bestScore) { best = style; bestScore = score; } }
    }

    internal static double Fold(double ratio)
    {
        if (!double.IsFinite(ratio) || ratio <= 0) return double.NaN;
        while (ratio > Math.Sqrt(2)) ratio /= 2;
        while (ratio < 1 / Math.Sqrt(2)) ratio *= 2;
        return ratio;
    }
    private static bool KnownTempo(double? a, double? b) => a is > 0 && b is > 0
        && double.IsFinite(a.Value) && double.IsFinite(b.Value);
    private static double Difference(double ratio) => Math.Abs(1 - ratio) / ((1 + ratio) * 0.5);
    private static double Bonus(double value) => 0.7 + 0.3 * AutomixMath.Clamp(value, 0, 1);
}
