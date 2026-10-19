using System.Numerics;

namespace Echo.Visuals.Cassette;

internal readonly record struct CassetteLayerOptions(double Alpha = 1, double Rotation = 0,
    Vector4? UvRect = null, Vector2 Scroll = default, Vector3? Tint = null, Vector3? Ripple = null,
    Vector4 Waves = default, Vector2? Sway = null, double? Disc = null, bool Additive = false, bool Screen = false)
{
    public CassetteLayerOptions() : this(1) { }
}

internal readonly record struct CassettePalette(Vector3 Base, Vector3 Sub, Vector3 Highlight, Vector3 Halo, float HaloAlpha)
{
    internal static readonly CassettePalette[] Phases =
    [
        new(new(1,1,1), new(.93f,.95f,1), new(1,.56f,.83f), new(.23f,.11f,.39f), .78f),
        new(new(1,1,1), new(1,.94f,.98f), new(1,.88f,.56f), new(.29f,.13f,.44f), .72f),
        new(new(.96f,.95f,1), new(.85f,.9f,1), new(.97f,.49f,1), new(.09f,.18f,.55f), .8f)
    ];
    internal static readonly Vector3[] Fallbacks = [new(.55f,.8f,.9f), new(.78f,.6f,.85f), new(.12f,.18f,.45f)];
}

internal static class CassetteResources
{
    public static Stream Open(string name) => typeof(CassetteResources).Assembly
        .GetManifestResourceStream("Echo.Visuals.Resources.SourceLyrics." + name) ?? throw new FileNotFoundException(name);
    public static string Read(string name) { using var stream = Open(name); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
}
