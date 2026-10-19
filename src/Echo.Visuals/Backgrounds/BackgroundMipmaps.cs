using SkiaSharp;

namespace Echo.Visuals.Backgrounds;

// Explicit textureLod equivalent: original 512px cover, seven box-reduced levels, trilinear interpolation.
internal sealed class BackgroundMipmaps : IDisposable
{
    public SKImage[] Levels { get; }
    public SKShader[] Shaders {get;}
    public float[][] Sizes {get;}
    public long Bytes {get;}
    public BackgroundMipmaps(SKImage image)
    {
        var levels=new List<SKImage>{image};var shaders=new List<SKShader>();
        try
        {
            for(var level=1;level<=6;level++)
            {
                using var source=SKBitmap.FromImage(levels[^1]);
                using var reduced=source.Resize(new SKImageInfo(Math.Max(1,image.Width>>level),Math.Max(1,image.Height>>level)),new SKSamplingOptions(SKFilterMode.Linear));
                levels.Add(SKImage.FromBitmap(reduced));
            }
            Levels=levels.ToArray();Sizes=new float[Levels.Length][];
            for(var i=0;i<Levels.Length;i++)
            {
                var value=Levels[i];
                shaders.Add(value.ToShader(SKShaderTileMode.Clamp,SKShaderTileMode.Clamp,new SKSamplingOptions(SKFilterMode.Linear)));
                Sizes[i]=[value.Width,value.Height];Bytes+=(long)value.Width*value.Height*4;
            }
            Shaders=shaders.ToArray();
        }
        catch{foreach(var shader in shaders)shader.Dispose();foreach(var level in levels)level.Dispose();throw;}
    }
    public void Dispose(){foreach(var shader in Shaders)shader.Dispose();foreach(var image in Levels)image.Dispose();}
    public static string Declarations(string source,string[] samplers)
    {
        foreach(var name in samplers)
            for(var i=0;i<=6;i++)source=$"uniform shader {name}_mip{i}; uniform float2 {name}_mip{i}_size;\n"+source;
        return source;
    }
    public static string Functions(string[] samplers)=>string.Join("\n",samplers.Select(name=>
        $"float4 lod_{name}(float2 uv,float lod){{float2 p=float2(uv.x,1.0-uv.y);float l=clamp(lod,0.0,6.0);"+
        string.Join("",Enumerable.Range(0,6).Select(i=>$"if(l<{i+1}.0)return mix({name}_mip{i}.eval(p*{name}_mip{i}_size),{name}_mip{i+1}.eval(p*{name}_mip{i+1}_size),l-{i}.0);"))+
        $"return {name}_mip6.eval(p*{name}_mip6_size);}}"));
}
