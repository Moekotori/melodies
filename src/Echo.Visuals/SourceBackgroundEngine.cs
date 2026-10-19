using Echo.Visuals.Backgrounds;
using Echo.Visuals.Canvas;
using Echo.Visuals.SourceRuntime;
using SkiaSharp;

namespace Echo.Visuals;

// Presentation-only owner. Original renderer state and native drawing resources share one serialized worker.
public sealed class SourceBackgroundEngine : IDisposable
{
    private readonly CanvasPool canvases = new();
    private readonly ISourceScript script;
    private readonly BackgroundGpu gpu;
    private bool geometric,coverFlow;
    private int frameRate=30;
    public SourceBackgroundEngine(CancellationToken cancellation = default)
    {
        script = OperatingSystem.IsMacOS() ? new NativeSourceScript(cancellation) : new ManagedSourceScript(cancellation);
        gpu = new(canvases);
        try
        {
            script.Function("__canvas", canvases.Invoke); script.Function("__background", gpu.Invoke); script.Set("__accent", "#5b97dc");
            script.Function("__backgroundImage", args =>
            {
                var name = args[0].AsString();
                if (name is not ("ambient.png" or "geometry.png")) throw new InvalidDataException("Unknown background asset.");
                using var stream = typeof(SourceBackgroundEngine).Assembly.GetManifestResourceStream("Echo.Visuals.Resources.SourceLyrics.Geometric." + name)!;
                using var original = SKBitmap.Decode(stream); var w = (int)args[1].AsNumber(); var h = (int)args[2].AsNumber();
                if (w < 1 || h < 1 || w * h > 1024 * 576) throw new InvalidDataException("Geometric asset budget exceeded.");
                using var image = original.Resize(new SKImageInfo(w, h), new SKSamplingOptions(SKCubicResampler.Mitchell));
                return canvases.Import(image);
            });
            script.Execute(Read("Runtime.canvas-shim.js")); script.Execute(Read("Runtime.source-scene-host.js"));
            script.Execute(Read("Runtime.background-gl.js")); script.Execute(Read("Resources.SourceLyrics.background.js"));
        }
        catch { gpu.Dispose(); canvases.Dispose(); script.Dispose(); throw; }
    }
    public void Build(string json, int width, int height)
    {
        if (json.Length > 65536 || width is < 1 or > 8192 || height is < 1 or > 8192 || (long)width * height > 33_554_432)
            throw new InvalidDataException("Background input budget exceeded.");
        coverFlow = json.Contains("\"coverFlow\"",StringComparison.Ordinal);
        geometric = json.Contains("\"geometricAmbience\"", StringComparison.Ordinal);
        try { script.Invoke("__backgroundBuild", json, width, height); }
        catch(InvalidOperationException e) { throw new InvalidOperationException(gpu.Diagnostic.Length>0?gpu.Diagnostic:e.Message,e); }
    }
    public void Update(string json,int width,int height)
    {
        if(json.Length>65536||width is <1 or >8192||height is <1 or >8192||(long)width*height>33_554_432)
            throw new InvalidDataException("Background input budget exceeded.");
        script.Invoke("__backgroundUpdate",json,width,height);
    }
    public void ClearCover()=>script.Invoke("__backgroundClearCover");
    public void SetCover(byte[] encoded)
    {
        if (encoded.Length > 4 * 1024 * 1024) throw new InvalidDataException("Background cover budget exceeded.");
        using var data = SKData.CreateCopy(encoded); using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Invalid cover.");
        if ((long)codec.Info.Width * codec.Info.Height > 1024 * 1024) throw new InvalidDataException("Background cover dimensions exceeded.");
        using var source = SKBitmap.Decode(encoded);
        if(coverFlow)
        {
            // Original Cover Flow samples the entire artwork directly at 64px; do not crop or build texture mips first.
            using var probe=source.Resize(new SKImageInfo(64,64),new SKSamplingOptions(SKFilterMode.Linear));
            script.Invoke("__backgroundCover",canvases.Import(probe),64,64);
            return;
        }
        using var bitmap = new SKBitmap(512, 512, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        using (var image = SKImage.FromBitmap(source))
        {
            var side = Math.Min(source.Width, source.Height);
            canvas.DrawImage(image, SKRect.Create((source.Width-side)/2f,(source.Height-side)/2f,side,side), SKRect.Create(512,512), new SKSamplingOptions(SKFilterMode.Linear));
        }
        script.Invoke("__backgroundCover", canvases.Import(bitmap), 512, 512);
    }
    public SKPicture Draw(double nowMs, double elapsedMs, string spectrum = "[]", bool playing = false,double telemetryTimestamp=0,int targetFrameRate=30)
    {
        if (spectrum.Length > 4096) throw new InvalidDataException("Background spectrum budget exceeded.");
        targetFrameRate=Math.Clamp(targetFrameRate,1,60);
        if(frameRate!=targetFrameRate){script.Invoke("__backgroundFrameRate",targetFrameRate);frameRate=targetFrameRate;}
        script.Invoke("__backgroundDraw", nowMs, Math.Clamp(elapsedMs, 0, 250), spectrum, playing,telemetryTimestamp);
        if (!geometric) return gpu.TakePicture();
        var id = (int)script.Invoke("__backgroundCanvas").AsNumber(); var surface = canvases.Get(id);
        using var image = surface.SnapshotImage(); using var recorder = new SKPictureRecorder();
        recorder.BeginRecording(SKRect.Create(image.Width, image.Height)).DrawImage(image, 0, 0);
        return recorder.EndRecording();
    }
    public void Pointer(double x, double y, bool press) => script.Invoke("__backgroundPointer", x, y, press);
    public void PointerLeave() => script.Invoke("__backgroundPointerLeave");
    public void Dispose()
    {
        try { script.Invoke("__backgroundDispose"); }
        catch (Exception error) when (error is OperationCanceledException or InvalidOperationException or Jint.Runtime.JavaScriptException) { }
        finally { script.Dispose(); gpu.Dispose(); canvases.Dispose(); }
    }
    private static string Read(string name)
    {
        using var stream = typeof(SourceBackgroundEngine).Assembly.GetManifestResourceStream("Echo.Visuals." + name)!;
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
}
