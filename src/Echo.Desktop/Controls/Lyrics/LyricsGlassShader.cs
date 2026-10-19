using SkiaSharp;

namespace Echo.Desktop.Controls.Lyrics;

internal static class LyricsGlassShader
{
    // One immutable, backend-independent native program for the process; no frame cache or timer.
    private static readonly Lazy<SKRuntimeEffect?> Shared = new(Create);
    public static SKRuntimeEffect? Effect => Shared.Value;

    private static SKRuntimeEffect? Create()
    {
        try { return SKRuntimeEffect.CreateShader(Source, out _); }
        catch (NotSupportedException) { return null; }
    }

    private const string Source = """
        uniform shader backdrop;
        uniform float2 origin;
        uniform float2 size;
        uniform float dark;

        half3 sampleGlass(float2 p) {
            float blur = 1.7;
            half3 c = backdrop.eval(p).rgb * 0.28;
            c += backdrop.eval(p + float2(blur, 0)).rgb * 0.12;
            c += backdrop.eval(p - float2(blur, 0)).rgb * 0.12;
            c += backdrop.eval(p + float2(0, blur)).rgb * 0.12;
            c += backdrop.eval(p - float2(0, blur)).rgb * 0.12;
            c += backdrop.eval(p + float2(blur, blur)).rgb * 0.06;
            c += backdrop.eval(p - float2(blur, blur)).rgb * 0.06;
            c += backdrop.eval(p + float2(blur, -blur)).rgb * 0.06;
            c += backdrop.eval(p + float2(-blur, blur)).rgb * 0.06;
            return c;
        }

        half4 main(float2 p) {
            float2 center = origin + size * 0.5;
            float radius = size.y * 0.5;
            float2 local = p - center;
            float2 axis = float2(clamp(local.x, -size.x * 0.5 + radius, size.x * 0.5 - radius), 0);
            float2 radial = local - axis;
            float length = max(0.001, sqrt(dot(radial, radial)));
            float2 normal = radial / length;
            float distance = length - radius;
            float bevel = smoothstep(-9.0, -0.5, distance);
            // The inner edge acts as a shallow lens; samples stay in the bounded backdrop patch.
            float2 refracted = center + local * 0.998 - normal * bevel * 7.2;
            half3 color = sampleGlass(refracted);
            half luminance = dot(color, half3(0.2126, 0.7152, 0.0722));
            color = mix(half3(luminance), color, 1.04);
            half3 tint = mix(half3(0.985, 0.988, 1.0), half3(0.075, 0.085, 0.105), dark);
            color = mix(color, tint, mix(0.16, 0.14, dark));
            float topLight = pow(max(0.0, dot(normal, float2(-0.45, -0.893))), 5.0);
            float lowerLight = pow(max(0.0, dot(normal, float2(0.60, 0.80))), 7.0);
            // Local reflections taper along the capsule instead of forming a bright horizontal band.
            float x = clamp((p.x - origin.x) / size.x, 0.0, 1.0);
            float left = (x - 0.14) / 0.18;
            float right = (x - 0.84) / 0.16;
            float reflection = exp(-left * left) + 0.4 * exp(-right * right);
            float rimPosition = (distance + 1.8) / 1.8;
            float rim = exp(-rimPosition * rimPosition);
            color += half3(1.0) * rim * (topLight * (0.012 + reflection * 0.12) + lowerLight * 0.025);
            float y = clamp((p.y - origin.y) / size.y, 0.0, 1.0);
            color += half3(0.02) * reflection * (1.0 - y) * (1.0 - y);
            return half4(clamp(color, 0.0, 1.0), 1.0);
        }
        """;
}
