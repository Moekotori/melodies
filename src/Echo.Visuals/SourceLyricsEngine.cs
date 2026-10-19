using Echo.Visuals.Cassette;
using Echo.Visuals.SourceRuntime;
using Echo.Visuals.Canvas;
using SkiaSharp;

namespace Echo.Visuals;

// A source scene owns its resources on one serialized worker. Cassette is fully managed; Blossom still uses the source runtime.
public sealed class SourceLyricsEngine : IDisposable
{
    private readonly ISourceScript? script;
    private readonly CanvasPool? canvases;
    private readonly bool nativeCassette;
    private readonly CancellationToken cancellation;
    private CassetteEngine? cassette;
    private bool disposed;
    public SourceLyricsEngine(string mode, string accent = "#5b97dc", CancellationToken cancellation = default)
    {
        if (mode is not ("blossom" or "cassette")) throw new ArgumentOutOfRangeException(nameof(mode));
        this.cancellation = cancellation;
        nativeCassette = mode == "cassette";
        if (nativeCassette) return;
        canvases = new();
        script = OperatingSystem.IsMacOS() ? new NativeSourceScript(cancellation) : new ManagedSourceScript(cancellation);
        try
        {
            script.Function("__canvas", canvases.Invoke);
            script.Set("__accent", accent);
            script.Execute(Read("Runtime.canvas-shim.js"));
            script.Execute(Read("Runtime.source-scene-host.js"));
            script.Execute(Read("Resources.SourceLyrics." + mode + ".js"));
        }
        catch { script.Dispose(); canvases.Dispose(); throw; }
    }
    public string Build(string projectJson, int width, int height)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (projectJson.Length > 262144 || width is < 1 or > 8192 || height is < 1 or > 8192
            || (long)width * height > 33_554_432) throw new InvalidDataException("Source lyric scene budget exceeded.");
        if (nativeCassette)
        {
            cassette?.Dispose(); cassette = null;
            var next = new CassetteEngine(cancellation);
            try { var result = next.Build(projectJson, width, height); cassette = next; return result; }
            catch { next.Dispose(); throw; }
        }
        return script!.Invoke("__sourceBuild", projectJson, width, height).AsString();
    }
    public void SetCover(byte[] encoded)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!nativeCassette) return;
        if (cassette is null) throw new InvalidOperationException("Build the source scene first.");
        if (encoded.Length > 4 * 1024 * 1024) throw new InvalidDataException("Source scene artwork budget exceeded.");
        using var data = SKData.CreateCopy(encoded);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Source scene artwork is invalid.");
        if ((long)codec.Info.Width * codec.Info.Height > 1024 * 1024) throw new InvalidDataException("Source scene artwork pixel budget exceeded.");
        using var image = SKBitmap.Decode(encoded);
        using var square = new SKBitmap(256,256,SKColorType.Bgra8888,SKAlphaType.Premul);
        using (var canvas = new SKCanvas(square))
        using (var paint = new SKPaint { IsAntialias = true })
        {
            var side = Math.Min(image.Width,image.Height);
            canvas.DrawBitmap(image,SKRect.Create((image.Width-side)/2f,(image.Height-side)/2f,side,side),SKRect.Create(256,256),paint);
        }
        cassette.SetCover(square);
    }
    public SKPicture Draw(double positionMs, bool motion, double nowMs, double elapsedMs, bool playing = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (nativeCassette) return (cassette ?? throw new InvalidOperationException("Build the source scene first.")).Draw(positionMs, motion, nowMs, elapsedMs, playing);
        var id = (int)script!.Invoke("__sourceCanvas").AsNumber();
        var target = canvases!.Get(id); target.EnablePicture();
        script.Invoke("__sourceDraw", positionMs, motion, nowMs, elapsedMs, playing);
        return target.CapturePicture();
    }
    public double Pointer(double x, double y, bool press)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return nativeCassette ? (cassette ?? throw new InvalidOperationException("Build the source scene first.")).Pointer(x, y, press)
            : script!.Invoke("__sourcePointer", x, y, press).AsNumber();
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (nativeCassette) { cassette?.Dispose(); cassette = null; return; }
        try { script!.Invoke("__sourceDispose"); }
        catch (OperationCanceledException) { /* Host cancellation already transfers cleanup to the native owners below. */ }
        catch (InvalidOperationException) { /* A failed scene may no longer accept its script cleanup entry. */ }
        catch (Jint.Runtime.JavaScriptException) { /* The pool still owns all native resources after a script fault. */ }
        finally { script!.Dispose(); canvases!.Dispose(); }
    }
    private static string Read(string suffix)
    {
        var assembly = typeof(SourceLyricsEngine).Assembly;
        using var stream = assembly.GetManifestResourceStream("Echo.Visuals." + suffix)
            ?? throw new FileNotFoundException(suffix);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
