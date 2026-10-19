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
    // Keep source precision through phase blending; convert to float only at the shader boundary.
    internal static ReadOnlySpan<double> Source =>
    [1,1,1, .93,.95,1, 1,.56,.83, .23,.11,.39, .78,
     1,1,1, 1,.94,.98, 1,.88,.56, .29,.13,.44, .72,
     .96,.95,1, .85,.9,1, .97,.49,1, .09,.18,.55, .8];
    internal static ReadOnlySpan<double> Fallbacks => [.55,.8,.9, .78,.6,.85, .12,.18,.45];
}

internal static class CassetteResources
{
    public static Stream Open(string name) => typeof(CassetteResources).Assembly
        .GetManifestResourceStream("Echo.Visuals.Resources.SourceLyrics." + name) ?? throw new FileNotFoundException(name);
    public static string Read(string name) { using var stream = Open(name); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
}
