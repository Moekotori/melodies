using System.Text.Json.Nodes;

namespace Echo.Playback.Processing;

// Bounded control-plane coefficient preparation; native processing remains in Audio.Host.
public static class FirPlanBuilder
{
    public static JsonObject Create(int source, int target, string first, string next, string backend, bool sdm = false, string tier = "standard")
    {
        var factor = target / Math.Max(1, source);
        if (source < 8000 || target % source != 0 || factor is not (2 or 4 or 8 or 16 or 32) || !sdm && factor > 16) throw new IOException("FIR 处理链需要受支持的整数倍采样率。");
        if (backend is not ("cpu" or "cuda")) throw new IOException("未知的计算后端。");
        var stages = new JsonArray(); var count = (int)Math.Log2(factor); var rate = source;
        var sound = FirProfileCatalog.Sounds.FirstOrDefault(x => x.First == first && x.Next == next)?.Id;
        var design = sound is "transient" or "hq-transient" or "hq-max-transient" ? 20500 : sound is "smooth" or "hq-smooth" or "hq-gauss-xtr" or "hq-max-gauss" or "hq-apod" or "hq-max-apod" ? 20000 : 0;
        for (var index = 0; index < count; index++)
        {
            var id = rate < 50000 ? first : next;
            if (sdm && (tier == "heavy" || tier == "balanced-heavy" && index >= Math.Max(0, count - 2))) id = FirProfileCatalog.Upgrade(id);
            var profile = FirProfileCatalog.Profiles.FirstOrDefault(x => x.Id == id) ?? throw new IOException("未知的 FIR 滤波器。");
            var cutoff = Math.Clamp(.25 * Math.Max(.75, 1 - profile.Transition) * profile.CutoffScale, .001, .499);
            if (index == 0 && sdm && design > 0)
            {
                cutoff = Math.Min(design, source * .5 * .98) / (rate * 2);
                if (sound == "smooth") profile = profile with { TapCount = 127 };
            }
            else if (!sdm && index == 0 && id == "echo-apod-minring" && source is 44100 or 48000) cutoff = 20500d / (rate * 2);
            var taps = CreateTaps(profile, cutoff);
            stages.Add((JsonNode)new JsonObject { ["upsampleFactor"] = 2, ["profile"] = id, ["taps"] = new JsonArray(taps.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()) });
            rate *= 2;
        }
        return new JsonObject { ["sourceSampleRate"] = source, ["targetSampleRate"] = target, ["computeBackend"] = backend, ["filterProfile1x"] = first, ["filterProfileNx"] = next, ["firTier"] = tier, ["stages"] = stages };
    }
    public static float[] CreateTaps(FirProfile profile, double cutoff)
    {
        var count = Math.Max(3, profile.TapCount | 1); if (count > 8192) throw new IOException("FIR 超过 8192 taps。");
        var taps = new float[count]; double sum = 0, center = (count - 1) / 2d; var denominator = Bessel(profile.KaiserBeta);
        for (var index = 0; index < count; index++)
        {
            var x = index / (double)(count - 1); var centered = 2 * x - 1;
            var window = profile.Window switch
            {
                "blackman-harris" => .35875 - .48829 * Math.Cos(2 * Math.PI * x) + .14128 * Math.Cos(4 * Math.PI * x) - .01168 * Math.Cos(6 * Math.PI * x),
                "gaussian" => Math.Exp(-.5 * Math.Pow(profile.GaussianAlpha * centered, 2)),
                "kaiser" => Bessel(profile.KaiserBeta * Math.Sqrt(Math.Max(0, 1 - centered * centered))) / denominator,
                _ => .5 - .5 * Math.Cos(2 * Math.PI * x)
            };
            var offset = 2 * cutoff * (index - center); var value = 2 * cutoff * (Math.Abs(offset) < 1e-12 ? 1 : Math.Sin(Math.PI * offset) / (Math.PI * offset)) * window;
            taps[index] = (float)value; sum += value;
        }
        Normalize(taps, sum); return profile.Phase == "minimum" ? FirMinimumPhase.Convert(taps) : taps;
    }
    internal static void Normalize(float[] taps, double sum)
    { if (Math.Abs(sum) > 1e-12) for (var i = 0; i < taps.Length; i++) taps[i] = (float)(taps[i] / sum); }
    private static double Bessel(double value)
    { double sum = 1, term = 1, squared = value * value / 4; for (var i = 1; i <= 24; i++) { term *= squared / (i * i); sum += term; if (term < 1e-14) break; } return sum; }
}
