using Jint.Native;
using SkiaSharp;

namespace Echo.Visuals.Canvas;

internal sealed class CanvasGradient : IDisposable
{
    private readonly string kind;
    private readonly float[] values;
    private readonly List<(float Stop, SKColor Color)> stops = [];
    private SKShader? shader;
    public CanvasGradient(SKShader shader){this.shader=shader;kind="pattern";values=[];}
    public CanvasGradient(JsValue[] args)
    { kind = args[2].AsString(); values = args.Skip(3).Select(a => (float)a.AsNumber()).ToArray(); }
    public void Add(float offset,string color) { stops.Add((Math.Clamp(offset,0,1),CanvasSurface.Color(color))); shader?.Dispose();shader=null; }
    public SKShader Shader()
    {
        if(shader is not null)return shader;
        var ordered=stops.OrderBy(s=>s.Stop).ToArray();
        var colors=ordered.Select(s=>s.Color).ToArray();var positions=ordered.Select(s=>s.Stop).ToArray();
        if(colors.Length<2){colors=[colors.FirstOrDefault(SKColors.Transparent),colors.FirstOrDefault(SKColors.Transparent)];positions=[0,1];}
        shader=kind switch
        {
            "linear" => SKShader.CreateLinearGradient(new(values[0],values[1]),new(values[2],values[3]),colors,positions,SKShaderTileMode.Clamp),
            "radial" => SKShader.CreateTwoPointConicalGradient(new(values[0],values[1]),Math.Max(0,values[2]),new(values[3],values[4]),Math.Max(.001f,values[5]),colors,positions,SKShaderTileMode.Clamp),
            _ => SKShader.CreateSweepGradient(new(values[1],values[2]),colors,positions)
        };
        return shader;
    }
    public void Dispose()=>shader?.Dispose();
}
