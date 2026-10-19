using System.Text.Json.Nodes;

namespace Echo.Playback.Processing;

public sealed record FirProfile(string Id, int TapCount, string Window, string Phase, double Transition,
    double CutoffScale, double GaussianAlpha, double KaiserBeta);
public sealed record SdmSound(string Id, string First, string Next);
public static class FirProfileCatalog
{
    public static IReadOnlyList<FirProfile> Profiles { get; } = Load("FirProfiles.json").AsArray().Select(x => new FirProfile(
        x!["id"]!.ToString(), (int)Number(x, "tapCount"), x["window"]!.ToString(), x["phase"]!.ToString(), Number(x, "transitionRatio"),
        Number(x, "cutoffScale", 1), Number(x, "gaussianAlpha", 3.2), Number(x, "kaiserBeta", 12))).ToArray();
    public static IReadOnlyList<SdmSound> Sounds { get; } = Load("SdmSounds.json").AsArray().Select(x => new SdmSound(x!["id"]!.ToString(), x["first"]!.ToString(), x["next"]!.ToString())).ToArray();
    private static readonly JsonObject Upgrades = Load("FirHeavyUpgrades.json").AsObject();
    public static string Upgrade(string id) => Upgrades[id]?.ToString() ?? id;
    private static double Number(JsonNode? node, string key, double fallback = 0) => node?[key] is { } value ? value.GetValue<double>() : fallback;
    private static JsonNode Load(string resource)
    {
        using var stream = typeof(FirProfileCatalog).Assembly.GetManifestResourceStream("Echo.Playback.Processing." + resource) ?? throw new IOException("Missing FIR profile catalog.");
        return JsonNode.Parse(stream)!;
    }
}
