using System.Text.Json;
using Echo.Visuals.Canvas;
using Jint.Native;
using SkiaSharp;

namespace Echo.Visuals.Backgrounds;

// Fixed fullscreen draw protocol; one scene, <=4 textures / 16 MiB, <=2 programs during a switch.
internal sealed class BackgroundGpu(CanvasPool canvases) : IDisposable
{
    private sealed class Program(string source) : IDisposable
    {
        public readonly SKRuntimeEffect Effect = SKRuntimeEffect.CreateShader(BackgroundShaderSource.Convert(source), out var error)
            ?? throw new InvalidDataException("Background shader: " + error);
        public SKRuntimeEffectUniforms Uniforms = null!;
        public SKRuntimeEffectChildren Children = null!;
        public string[] ChildNames=[];
        public readonly Dictionary<string,float[]> Buffers=new(StringComparer.Ordinal);
        public readonly float[] NativeSize=new float[2];
        public string? TextureJson;
        public long TextureGeneration=-1;
        public void Initialize() { Uniforms = new(Effect); Children = new(Effect); ChildNames=Children.Names.ToArray(); }
        public void Dispose() { Uniforms.Dispose(); Effect.Dispose(); }
    }
    private readonly Dictionary<int, Program> programs = [];
    private readonly Dictionary<int, BackgroundMipmaps> images = [];
    private long imageBytes,textureGeneration;
    private readonly SKShader black=SKShader.CreateColor(SKColors.Black);
    private readonly float[] emptySize=[1,1];
    private SKPicture? picture;
    public string Diagnostic { get; private set; } = "";
    public JsValue Invoke(JsValue[] a)
    {
        int I(int i) => (int)a[i].AsNumber();
        switch (a[0].AsString())
        {
            case "compile":
                if (programs.Count >= 2 || a[2].AsString().Length > 131072) throw new InvalidDataException("Background program budget exceeded.");
                try { var program = new Program(a[2].AsString()); program.Initialize(); programs.Add(I(1), program); }
                catch(Exception error) { Diagnostic = error.Message; throw; }
                break;
            case "uniform":
                var queried=programs[I(1)];var name=a[2].AsString();
                return queried.Uniforms.Contains(name)||queried.Children.Names.Contains(name)||queried.Uniforms.Contains(name+"_size");
            case "deleteProgram": if (programs.Remove(I(1), out var old)) old.Dispose(); break;
            case "upload":
                var image = canvases.Get(I(2)).SnapshotImage();
                if (images.Remove(I(1), out var previous)) { imageBytes -= previous.Bytes; previous.Dispose(); }
                var mipmaps = new BackgroundMipmaps(image); var cost = mipmaps.Bytes;
                if (images.Count >= 4 || imageBytes + cost > 16 * 1024 * 1024) { mipmaps.Dispose(); throw new InvalidDataException("Background texture budget exceeded."); }
                images[I(1)] = mipmaps; imageBytes += cost; textureGeneration++; break;
            case "deleteTexture":
                if (images.Remove(I(1), out var removed)) { imageBytes -= removed.Bytes; removed.Dispose(); textureGeneration++; }
                break;
            case "draw": Draw(programs[I(1)], I(2), I(3), a[4].AsString(), a[5].AsString()); break;
        }
        return JsValue.Undefined;
    }
    private void Draw(Program p, int width, int height, string json, string textureJson)
    {
        if (width < 1 || height < 1 || (long)width * height > 1920 * 1080) throw new InvalidDataException("Background frame budget exceeded.");
        if(p.NativeSize[0]!=width||p.NativeSize[1]!=height)
        {p.NativeSize[0]=width;p.NativeSize[1]=height;p.Uniforms["native_size"]=p.NativeSize;}
        using var values=JsonDocument.Parse(json);
        foreach(var entry in values.RootElement.EnumerateObject())
        {
            var name=entry.Name;
            if(!p.Uniforms.Contains(name))continue;
            var count=entry.Value.GetArrayLength();
            if(count==1){p.Uniforms[name]=entry.Value[0].GetSingle();continue;}
            if(!p.Buffers.TryGetValue(name,out var items)||items.Length!=count)p.Buffers[name]=items=new float[count];
            var index=0;foreach(var value in entry.Value.EnumerateArray())items[index++]=value.GetSingle();
            p.Uniforms[name]=items;
        }
        if(p.TextureGeneration!=textureGeneration||!string.Equals(p.TextureJson,textureJson,StringComparison.Ordinal))
        {
            BindTextures(p,textureJson);p.TextureGeneration=textureGeneration;p.TextureJson=textureJson;
        }
        using var shaderOutput=p.Effect.ToShader(p.Uniforms,p.Children);
        using var paint=new SKPaint{Shader=shaderOutput};
        using var recorder=new SKPictureRecorder();var canvas=recorder.BeginRecording(SKRect.Create(width,height));
        canvas.DrawRect(SKRect.Create(width,height),paint);picture?.Dispose();picture=recorder.EndRecording();
    }
    private void BindTextures(Program p,string json)
    {
        using var textures=JsonDocument.Parse(json);
        foreach(var child in p.ChildNames)
        {
            var split=child.LastIndexOf("_mip",StringComparison.Ordinal);var name=split<0?child:child[..split];
            var level=split<0?0:int.Parse(child.AsSpan(split+4),System.Globalization.CultureInfo.InvariantCulture);
            if(textures.RootElement.TryGetProperty(name,out var value)&&images.TryGetValue(value.GetInt32(),out var mipmaps))
            {p.Children[child]=mipmaps.Shaders[level];p.Uniforms[child+"_size"]=mipmaps.Sizes[level];}
            else {p.Children[child]=black;p.Uniforms[child+"_size"]=emptySize;}
        }
    }
    public SKPicture TakePicture() { var value = picture ?? throw new InvalidOperationException("Background did not draw."); picture = null; return value; }
    public void Dispose()
    {
        picture?.Dispose(); foreach (var p in programs.Values) p.Dispose(); foreach (var image in images.Values) image.Dispose();
        programs.Clear(); images.Clear(); black.Dispose(); imageBytes = 0;
    }
}
