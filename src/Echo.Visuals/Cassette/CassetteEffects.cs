using System.Text.Json;
using SkiaSharp;

namespace Echo.Visuals.Cassette;

// Shader math is generated from the original GLSL. Skia executes it on the application's existing GPU backend.
internal sealed class CassetteEffect : IDisposable
{
    private readonly SKRuntimeEffect effect;
    private readonly SKRuntimeEffectUniforms uniforms;
    private readonly SKRuntimeEffectChildren children;
    public CassetteEffect(string source)
    {
        effect = SKRuntimeEffect.CreateShader(source, out var error)
            ?? throw new InvalidDataException("Cassette shader: " + error);
        uniforms = new(effect); children = new(effect);
    }
    public void Set(string name, ReadOnlySpan<float> value) { if (uniforms.Contains(name)) uniforms[name] = value; }
    public void Set(string name, float value) { if (uniforms.Contains(name)) uniforms[name] = value; }
    public void Texture(string name, SKShader shader, float width, float height)
    { children[name] = shader; Set(name + "_size", [width, height]); }
    public SKShader Shader() => effect.ToShader(uniforms, children);
    public void Dispose() { uniforms.Dispose(); effect.Dispose(); }
    public static float[] Vector(JsonElement json, string name, params float[] fallback)
        => json.TryGetProperty(name, out var value) ? value.EnumerateArray().Select(v => v.GetSingle()).ToArray() : fallback;
    public static float Scalar(JsonElement json, string name, float fallback = 0)
        => json.TryGetProperty(name, out var value) ? value.GetSingle() : fallback;
    public static bool Flag(JsonElement json, string name) => json.TryGetProperty(name, out var value) && value.GetBoolean();
}
