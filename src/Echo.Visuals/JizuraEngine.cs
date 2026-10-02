using System.Reflection;
using Jint;
using Jint.Native;
using Jint.Runtime.Interop;
using Echo.Visuals.Canvas;

namespace Echo.Visuals;

// One scene owns one interpreter and raster pool. Caller must serialize all operations on its worker.
public sealed class JizuraEngine : IDisposable
{
    private readonly Engine engine;
    private readonly CanvasPool canvases = new();
    private readonly Assembly assets = typeof(JizuraEngine).Assembly;
    private readonly string[] scripts;
    public JizuraEngine(CancellationToken cancellation = default)
    {
        engine = new Engine(options => options.LimitMemory(128*1024*1024).TimeoutInterval(TimeSpan.FromSeconds(8)).MaxStatements(16_000_000).CancellationToken(cancellation));
        try
        {
        engine.SetValue("__canvas",new ClrFunction(engine,"__canvas",(_,args)=>canvases.Invoke(args)));
        engine.Execute(Read("canvas-shim.js"));
        engine.Execute("const ECHO_JIZURA_LOOKS="+Read("looks.json")+";");
        scripts=assets.GetManifestResourceNames().Where(n=>n.Contains(".Resources.Jizura.",StringComparison.Ordinal)&&n.EndsWith(".js",StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        foreach(var script in scripts)
        {
            var source=ReadFull(script);
            if(script.EndsWith("001-02_fonts.js",StringComparison.Ordinal))
            {
                var start=source.IndexOf("  const img = x.getImageData(0, 0, S, S).data;",StringComparison.Ordinal);
                var end=source.IndexOf("  return { ch, res, pieces };",start,StringComparison.Ordinal);
                if(start<0||end<0)throw new InvalidDataException("Original glyph decomposition changed.");
                source=source[..start]+"  const pieces=JSON.parse(__canvas(cv.nativeId,'decompose',res)).map(p=>({...p,id:++_pid,res,frags:null,cv:new NativeCanvas(p.nativeId,p.width,p.height)}));cv.dispose();\n"+source[end..];
            }
            // The original random monochrome texture loop allocates interpreter values per pixel.
            // Keep its 4 x 256px textures, but generate those pixels directly on the raster worker.
            if(script.EndsWith("011-09_render.js",StringComparison.Ordinal))
            {
                const string loop="for (let i = 0; i < id.data.length; i += 4) { const v = Math.random() * 255; id.data[i] = id.data[i + 1] = id.data[i + 2] = v; id.data[i + 3] = 255; }\n      x.putImageData(id, 0, 0);";
                source=source.Replace("\r\n","\n",StringComparison.Ordinal);
                if(!source.Contains(loop,StringComparison.Ordinal))throw new InvalidDataException("Original texture initialization changed.");
                source=source.Replace(loop,"__canvas(g.nativeId,'grain');",StringComparison.Ordinal);
                const string paper="const id = x.getImageData(0, 0, w, h);\n    for (let i = 0; i < id.data.length; i += 4) { const n = (Math.random() - 0.5) * 22; id.data[i] += n; id.data[i + 1] += n; id.data[i + 2] += n; }\n    x.putImageData(id, 0, 0);";
                if(!source.Contains(paper,StringComparison.Ordinal))throw new InvalidDataException("Original paper texture changed.");
                source=source.Replace(paper,"__canvas(p.nativeId,'paperNoise');",StringComparison.Ordinal);
            }
            engine.Execute(source);
        }
        engine.Execute(Read("scene-adapter.js"));engine.Invoke("__initialize");
        }
        catch { engine.Dispose(); canvases.Dispose(); throw; }
    }
    public int ScriptCount => scripts.Length;
    public string Build(string projectJson)
    {if(projectJson.Length>262144)throw new InvalidDataException("Afterglow project budget exceeded.");return engine.Invoke("__build",projectJson).AsString();}
    public byte[] Draw(double seconds,bool motion)
    {var id=(int)engine.Invoke("__draw",seconds,motion).AsNumber();return canvases.Get(id).Encode();}
    public RasterFrame DrawRaster(double seconds,bool motion)
    {var id=(int)engine.Invoke("__draw",seconds,motion).AsNumber();return canvases.Get(id).Snapshot();}
    private string Read(string file)=>ReadFull(assets.GetManifestResourceNames().Single(n=>n.EndsWith("."+file,StringComparison.Ordinal)));
    private string ReadFull(string name)
    {using var stream=assets.GetManifestResourceStream(name)??throw new FileNotFoundException(name);using var reader=new StreamReader(stream);return reader.ReadToEnd();}
    public void Dispose(){engine.Dispose();canvases.Dispose();}
}
