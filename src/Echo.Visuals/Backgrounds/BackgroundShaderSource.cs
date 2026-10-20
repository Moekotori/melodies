using System.Text.RegularExpressions;

namespace Echo.Visuals.Backgrounds;

internal static partial class BackgroundShaderSource
{
    public static string Convert(string source)
    {
        source = BackgroundShaderCompatibility.Apply(BackgroundShaderDerivatives.Apply(source));
        source = source.Replace("gl_FragColor", "native_color");
        source = Version().Replace(source, ""); source = Precision().Replace(source, "");
        var outputs = Output().Matches(source).Select(m => m.Groups[1].Value).ToArray();
        var output = outputs.FirstOrDefault() ?? "native_color";
        source = Output().Replace(source, ""); source = Varying().Replace(source, "");
        var samplers = Sampler().Matches(source).Select(m => m.Groups[1].Value).ToArray();
        source = Sampler().Replace(source, m => $"uniform shader {m.Groups[1].Value}; uniform float2 {m.Groups[1].Value}_size;");
        source = Vector().Replace(source, "float$1"); source = Matrix().Replace(source, "float$1x$1");
        foreach (var name in samplers)
            source = Regex.Replace(source, @"\btextureLod\(\s*" + name + @"\s*,", "lod_" + name + "(");
        foreach (var name in samplers) source = Regex.Replace(source, @"\btexture(?:2D)?\(\s*" + name + @"\s*,", "sample_" + name + "(");
        source = source.Replace("gl_FragCoord.xy", "float2(coord.x, native_size.y-coord.y)");
        source = Regex.Replace(source, @"\bvoid\s+main\s*\(\s*\)\s*\{", $"half4 main(float2 coord) {{ float4 {output}; float2 v_uv=float2(coord.x/native_size.x,1.0-coord.y/native_size.y);");
        var mainIndex=source.IndexOf("half4 main(",StringComparison.Ordinal);
        source=source[..mainIndex]+Regex.Replace(source[mainIndex..], @"\breturn\s*;", $"return half4({output});");
        var end = source.LastIndexOf('}'); source = source[..end] + $"return half4({output});\n" + source[end..];
        source = BackgroundMipmaps.Declarations(source, samplers);
        var functions = BackgroundMipmaps.Functions(samplers)+string.Join("\n", samplers.Select(n => $"float4 sample_{n}(float2 uv){{return {n}.eval(float2(uv.x,1.0-uv.y)*{n}_size);}}"));
        // A scene may declare history after cover helpers. Hoist uniforms so sampling
        // functions always follow their declarations and precede every caller.
        var uniforms = string.Join("\n", UniformDeclaration().Matches(source).Select(m => m.Value));
        source = UniformDeclaration().Replace(source, "");
        return "uniform float2 native_size;\n" + uniforms + "\n" + functions + "\n" + source;
    }
    [GeneratedRegex(@"#version[^\n]*")] private static partial Regex Version();
    [GeneratedRegex(@"precision\s+\w+\s+\w+\s*;")] private static partial Regex Precision();
    [GeneratedRegex(@"out\s+vec4\s+(\w+)\s*;")] private static partial Regex Output();
    [GeneratedRegex(@"(?:varying|in)\s+vec2\s+v_uv\s*;")] private static partial Regex Varying();
    [GeneratedRegex(@"uniform\s+sampler2D\s+(\w+)\s*;")] private static partial Regex Sampler();
    [GeneratedRegex(@"\buniform\s+\w+\s+\w+(?:\s*\[\s*\d+\s*\])?\s*;")] private static partial Regex UniformDeclaration();
    [GeneratedRegex(@"\bvec([234])\b")] private static partial Regex Vector();
    [GeneratedRegex(@"\bmat([234])\b")] private static partial Regex Matrix();
}
