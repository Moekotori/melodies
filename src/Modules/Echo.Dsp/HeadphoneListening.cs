using System.Text.Json.Nodes;

namespace Echo.Dsp;

internal static class HeadphoneListening
{
    private sealed record Candidate(CorrectionBand[] Bands, double Preamp, double Scale, double Average, double Worst, double Peak);
    public static JsonObject Create(JsonObject original)
    {
        var originalBands = original["bands"]!.AsArray().OfType<JsonObject>().Select(x => new CorrectionBand(EqPresetImport.Number(x, "frequencyHz"), EqPresetImport.Number(x, "gainDb"), EqPresetImport.Number(x, "q"), x["filterType"]!.ToString(), x["enabled"]?.ToString() != "false")).ToArray();
        var source = originalBands.Select(x => Wide(x) && x.GainDb < -4 ? x with { GainDb = -4 } : x).ToArray();
        var full = Evaluate(source, 1, EqPresetImport.Number(original, "preampDb"));
        var best = full;
        if (!Meets(full))
        {
            double low = .35, high = 1; best = Evaluate(source, low);
            for (var i = 0; i < 10; i++) { var mid = (low + high) / 2; var candidate = Evaluate(source, mid); if (Meets(candidate)) { best = candidate; low = mid; } else high = mid; }
            if (best.Average < full.Average) best = full;
        }
        for (var step = 0; step < 8 && best.Worst < -5; step++)
        {
            var lifted = best.Bands.Select(x => Wide(x) && x.GainDb < -1.5 ? x with { GainDb = Round(Math.Min(-1.5, x.GainDb + .5)) } : x).ToArray();
            if (lifted.SequenceEqual(best.Bands)) break; best = Evaluate(lifted, 1) with { Scale = best.Scale };
        }
        var restored = best.Bands.Select((x, i) => source[i].GainDb > 0 ? x with { GainDb = source[i].GainDb } : x).ToArray();
        double lower = Math.Max(.35, best.Scale), upper = 1;
        for (var i = 0; i < 10; i++) { var mid = (lower + upper) / 2; var candidate = Evaluate(restored, mid); if (Meets(candidate) && candidate.Worst >= -5.05) { best = candidate; lower = mid; } else upper = mid; }
        var output = (JsonObject)original.DeepClone(); var bands = output["bands"]!.AsArray();
        for (var i = 0; i < best.Bands.Length; i++)
        {
            var band = best.Bands[i]; bands[i]!["gainDb"] = band.GainDb;
            if (band.Kind is "lowShelf" or "highShelf")
            {
                var previous = originalBands[i]; var a = Math.Pow(10, previous.GainDb / 40); var rawQ = Math.Sqrt(1 / ((a + 1 / a) / previous.Q - (a + 1 / a) + 2));
                bands[i]!["q"] = EqPresetImport.ShelfSlope(band.GainDb, rawQ);
            }
        }
        output["preampDb"] = best.Preamp; output["name"] = original["name"] + "（听感调整）";
        if (output["attribution"] is JsonObject attribution) attribution["modifications"] = attribution["modifications"] + " ECHO listening adjustment: eased broad vocal cuts, reduced boosts and recalculated preamp.";
        return output;
    }
    private static bool Wide(CorrectionBand band) => band.Enabled && band.Kind == "peaking" && band.FrequencyHz is >= 800 and <= 5000 && band.Q <= 1.2 && band.GainDb < 0;
    private static bool Meets(Candidate value) => value.Average >= -4.05 && value.Peak <= .05;
    private static double Round(double x) => Math.Floor(x * 10 + .5) / 10;
    private static Candidate Evaluate(CorrectionBand[] source, double scale, double? published = null)
    {
        var bands = source.Select(x => x.Enabled && x.GainDb > 0 && x.Kind is "peaking" or "lowShelf" or "highShelf" ? x with { GainDb = Round(Math.Clamp(x.GainDb * scale, -24, 12)) } : x).ToArray();
        double Db(double frequency) => CorrectionResponse.Db(bands, frequency, 48000, 100, 0);
        var peak = Math.Max(Enumerable.Range(0, 64).Max(i => Db(20 * Math.Pow(1000, i / 63d))), bands.Where(x => x.Enabled).Select(x => Db(x.FrequencyHz)).DefaultIfEmpty(0).Max());
        var preamp = peak <= .05 ? 0 : Math.Clamp(Round(-(peak + .1)), -12, 6);
        var vocals = Enumerable.Range(0, 18).Select(i => Db(250 * Math.Pow(16, i / 17d))).ToArray();
        if (scale == 1 && published is { } p && p <= 0 && peak + p <= .05 && p >= preamp - .3 && vocals.Average() + p >= -4.05) preamp = p;
        return new(bands, preamp, scale, vocals.Average() + preamp, vocals.Min() + preamp, peak + preamp);
    }
}
