using System.Text.Json;
using Echo.Visuals.Cassette;
using Echo.Visuals.SourceRuntime;
using Jint.Runtime.Interop;
using Echo.Visuals.Canvas;
using SkiaSharp;

namespace Echo.Visuals;

// A source scene owns one bounded interpreter and one raster pool on its serialized worker.
public sealed class SourceLyricsEngine : IDisposable
{
    private readonly ISourceScript script;
    private readonly CanvasPool canvases = new();
    private readonly CassetteScenePainter? cassette;
    public SourceLyricsEngine(string mode, string accent = "#5b97dc", CancellationToken cancellation = default)
    {
        if (mode is not ("blossom" or "cassette")) throw new ArgumentOutOfRangeException(nameof(mode));
        script = OperatingSystem.IsMacOS() ? new NativeSourceScript(cancellation) : new ManagedSourceScript(cancellation);
        try
        {
            script.Function("__canvas", canvases.Invoke);
            script.Set("__accent", accent);
            if (mode == "cassette")
            {
                cassette = new(canvases, file => Read("Resources.SourceLyrics." + file));
                script.Function("__cassette", cassette.Invoke);
                using var index = JsonDocument.Parse(Read("Resources.SourceLyrics.asset-index.json"));
                var assets = index.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
                script.Function("__asset", args =>
                {
                    var name = assets[args[0].AsString()];
                    using var stream = typeof(SourceLyricsEngine).Assembly.GetManifestResourceStream("Echo.Visuals.Resources.SourceLyrics.Cassette." + name)
                        ?? throw new FileNotFoundException(name);
                    using var bitmap = SKBitmap.Decode(stream) ?? throw new InvalidDataException(name);
                    var id = canvases.Import(bitmap);
                    return $"{{\"id\":{id},\"width\":{bitmap.Width},\"height\":{bitmap.Height}}}";
                });
            }
            script.Execute(Read("Runtime.canvas-shim.js"));
            script.Execute(Read("Runtime.source-scene-host.js"));
            script.Execute(Read("Resources.SourceLyrics." + mode + ".js"));
        }
        catch { script.Dispose(); cassette?.Dispose(); canvases.Dispose(); throw; }
    }
    public string Build(string projectJson, int width, int height)
    {
        if (projectJson.Length > 262144 || width is < 1 or > 8192 || height is < 1 or > 8192
            || (long)width * height > 33_554_432) throw new InvalidDataException("Source lyric scene budget exceeded.");
        return script.Invoke("__sourceBuild", projectJson, width, height).AsString();
    }
    public void SetCover(byte[] encoded)
    {
        if (cassette is null) return;
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
        var id = canvases.Import(square);
        script.Invoke("__sourceCover", id, 256, 256);
    }
    public SKPicture Draw(double positionMs, bool motion, double nowMs, double elapsedMs, bool playing = false)
    {
        if (cassette is not null)
        {
            script.Invoke("__sourceDraw", positionMs, motion, nowMs, elapsedMs, playing);
            return cassette.TakePicture();
        }
        var id = (int)script.Invoke("__sourceCanvas").AsNumber();
        var target = canvases.Get(id); target.EnablePicture();
        script.Invoke("__sourceDraw", positionMs, motion, nowMs, elapsedMs, playing);
        return target.CapturePicture();
    }
    public double Pointer(double x, double y, bool press) => script.Invoke("__sourcePointer", x, y, press).AsNumber();
    public void Dispose()
    {
        try { script.Invoke("__sourceDispose"); }
        catch (OperationCanceledException) { /* Host cancellation already transfers cleanup to the native owners below. */ }
        catch (InvalidOperationException) { /* A failed scene may no longer accept its script cleanup entry. */ }
        catch (Jint.Runtime.JavaScriptException) { /* The pool still owns all native resources after a script fault. */ }
        finally { script.Dispose(); cassette?.Dispose(); canvases.Dispose(); }
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
