using System.Buffers;
using System.Text.Json;
using Jint.Native;
using SkiaSharp;
using Echo.Visuals.Canvas;
using static Echo.Visuals.Cassette.CassetteEffect;

namespace Echo.Visuals.Cassette;

// Serialized worker owner: <= 48 textures / 64 MiB, one current picture, no GL context or browser.
internal sealed class CassetteScenePainter : IDisposable
{
    private sealed record Texture(SKImage Image, SKShader Shader) : IDisposable
    { public void Dispose() { Shader.Dispose(); Image.Dispose(); } }
    private readonly Dictionary<int, Texture> textures = [];
    private readonly CassetteEffect layer, particle, glyph, composite;
    private readonly CanvasPool canvases;
    private readonly SKPictureRecorder recorder = new();
    private readonly SKPaint paint = new() { IsAntialias = true };
    private SKCanvas? canvas;
    private SKPicture? result;
    private int next, width, height;
    private long bytes;
    private float time;
    private readonly float[] ripples = new float[32];
    private float ratio = 1;
    public CassetteScenePainter(CanvasPool canvases, Func<string, string> read)
    {
        this.canvases = canvases;
        layer = new(read("cassette-layer.sksl")); particle = new(read("cassette-particle.sksl"));
        glyph = new(read("cassette-glyph.sksl")); composite = new(read("cassette-composite.sksl"));
    }
    public JsValue Invoke(JsValue[] args)
    {
        float N(int i) => (float)args[i].AsNumber();
        switch (args[0].AsString())
        {
            case "upload":
                if (textures.Count >= 48) throw new InvalidDataException("Cassette texture count exceeded.");
                var image = canvases.Get((int)N(1)).SnapshotImage();
                var cost = (long)image.Width * image.Height * 4;
                if (bytes + cost > 64 * 1024 * 1024) { image.Dispose(); throw new InvalidDataException("Cassette texture budget exceeded."); }
                var tile = args[2].AsBoolean() ? SKShaderTileMode.Repeat : SKShaderTileMode.Clamp;
                var shader = image.ToShader(tile, tile, new SKSamplingOptions(SKFilterMode.Linear));
                textures[++next] = new(image, shader); bytes += cost; return next;
            case "delete":
                if (textures.Remove((int)N(1), out var texture)) { bytes -= (long)texture.Image.Width * texture.Image.Height * 4; texture.Dispose(); }
                break;
            case "bytes": return bytes;
            case "begin":
                width = (int)N(1); height = (int)N(2); time = N(3);
                if (width < 1 || height < 1 || (long)width * height > 1920 * 1080) throw new InvalidDataException("Cassette frame budget exceeded.");
                canvas = recorder.BeginRecording(SKRect.Create(width, height));
                canvas.Clear(new SKColorF(N(4), N(5), N(6), 1)); break;
            case "layer": DrawLayer(args); break;
            case "particle": DrawParticle(args); break;
            case "composite": DrawComposite(args[1].AsString()); break;
            case "glyphs": DrawGlyphs(args); break;
            case "finish": result?.Dispose(); result = recorder.EndRecording(); canvas = null; break;
            default: throw new NotSupportedException("Cassette draw operation.");
        }
        return JsValue.Undefined;
    }
    private void Bind(CassetteEffect effect, string name, int id)
    {
        var texture = textures[id]; effect.Texture(name, texture.Shader, texture.Image.Width, texture.Image.Height);
    }
    private void Quad(CassetteEffect effect, float x, float y, float w, float h, float rotation, SKBlendMode blend = SKBlendMode.SrcOver)
    {
        if (w <= 0 || h <= 0) return;
        effect.Set("draw_size", [w, h]); effect.Set("draw_center", [x, y]); effect.Set("draw_rotation", rotation);
        using var shader = effect.Shader(); paint.Shader = shader; paint.BlendMode = blend;
        canvas!.Save(); canvas.Translate(x, y); canvas.RotateRadians(rotation); canvas.Translate(-w / 2, -h / 2);
        canvas.DrawRect(SKRect.Create(w, h), paint); canvas.Restore(); paint.Shader = null;
    }
    private void DrawLayer(JsValue[] a)
    {
        using var json = JsonDocument.Parse(a[7].AsString()); var o = json.RootElement;
        var alpha = Scalar(o, "alpha", 1); if (alpha <= .001) return;
        Bind(layer, "u_tex", (int)a[1].AsNumber()); Bind(layer, "u_noise", (int)a[2].AsNumber());
        layer.Set("u_time", time); layer.Set("u_alpha", alpha);
        layer.Set("u_uvRect", Vector(o, "uvRect", 0, 0, 1, 1)); layer.Set("u_scroll", Vector(o, "scroll", 0, 0));
        layer.Set("u_tint", Vector(o, "tint", 1, 1, 1));
        layer.Set("u_additive", Flag(o, "additive") || Flag(o, "screen") ? 1 : 0);
        var ripple = Vector(o, "ripple", 0, 0, 1); layer.Set("u_ripple", [ripple[0], ripple[1], ripple[2], o.TryGetProperty("ripple", out _) ? 1 : 0]);
        layer.Set("u_waves", Vector(o, "waves", 0, 0, 0, 0));
        var sway = Vector(o, "sway", 0, 0); layer.Set("u_sway", [sway[0], sway[1], o.TryGetProperty("sway", out _) ? 1 : 0]);
        layer.Set("u_disc", [o.TryGetProperty("disc", out _) ? 1 : 0, Scalar(o, "disc")]);
        Quad(layer, (float)a[3].AsNumber(), (float)a[4].AsNumber(), (float)a[5].AsNumber(), (float)a[6].AsNumber(), Scalar(o, "rotation"),
            Flag(o, "screen") ? SKBlendMode.Screen : SKBlendMode.SrcOver);
    }
    private void DrawParticle(JsValue[] a)
    {
        Bind(particle, "u_tex", (int)a[1].AsNumber());
        particle.Set("draw_uv", [(float)a[7].AsNumber(), (float)a[8].AsNumber(), (float)a[9].AsNumber(), (float)a[10].AsNumber()]);
        particle.Set("v_color", [(float)a[11].AsNumber(), (float)a[12].AsNumber(), (float)a[13].AsNumber(), (float)a[14].AsNumber()]);
        particle.Set("v_params", [a[15].AsBoolean() ? 1 : 0, 0, 0, 0]);
        Quad(particle, (float)a[2].AsNumber(), (float)a[3].AsNumber(), (float)a[4].AsNumber(), (float)a[5].AsNumber(), (float)a[6].AsNumber());
    }
    private void DrawComposite(string jsonText)
    {
        using var json = JsonDocument.Parse(jsonText); var o = json.RootElement;
        using var scene = recorder.EndRecording();
        canvas = recorder.BeginRecording(SKRect.Create(width, height));
        using var sceneShader = scene.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, SKFilterMode.Linear, SKRect.Create(width, height));
        composite.Texture("u_scene", sceneShader, width, height);
        ratio = Scalar(o, "pixelRatio", 1);
        var incoming = o.GetProperty("ripples"); var index = 0;
        foreach (var value in incoming.EnumerateArray()) { var n = value.GetSingle(); ripples[index] = index % 4 < 2 ? n * ratio : n; index++; }
        if (index != 32) throw new InvalidDataException("Cassette ripple count mismatch.");
        composite.Set("u_ripples", ripples); composite.Set("u_rippleShape", [380 * ratio, 36 * ratio, 9 * ratio]);
        composite.Set("u_view", [width, height]); composite.Set("u_time", time); composite.Set("u_pxScale", ratio);
        composite.Set("u_retro", Flag(o, "retro") ? 1 : 0); composite.Set("u_reveal", Scalar(o, "reveal"));
        composite.Set("u_fallback", Vector(o, "fallback", 0, 0, 0));
        using var shader = composite.Shader(); paint.Shader = shader; paint.BlendMode = SKBlendMode.Src;
        canvas.DrawRect(SKRect.Create(width, height), paint); paint.Shader = null;
    }
    private void DrawGlyphs(JsValue[] a)
    {
        var count = (int)a[4].AsNumber(); if (count is < 0 or > 512) throw new InvalidDataException("Cassette glyph count exceeded.");
        using var paletteJson = JsonDocument.Parse(a[5].AsString()); var p = paletteJson.RootElement;
        Bind(glyph, "u_tex", (int)a[1].AsNumber()); Bind(glyph, "u_noise", (int)a[2].AsNumber());
        glyph.Set("u_time", time); glyph.Set("u_ripples", ripples); glyph.Set("u_rippleShape", [380 * ratio, 36 * ratio, 9 * ratio]);
        foreach (var key in new[] { "base", "sub", "hi", "halo" }) glyph.Set("u_" + key, Vector(p, key));
        glyph.Set("u_haloAlpha", Scalar(p, "haloAlpha"));
        using var json = JsonDocument.Parse(a[3].AsString());
        if (json.RootElement.GetArrayLength() != count * 17) throw new InvalidDataException("Cassette instance count mismatch.");
        var data = ArrayPool<float>.Shared.Rent(count * 17);
        try
        {
            var index = 0; foreach (var number in json.RootElement.EnumerateArray()) data[index++] = number.GetSingle();
            for (var i = 0; i < count; i++)
            {
                var row = data.AsSpan(i * 17, 17);
                glyph.Set("v_rect", row.Slice(5, 4)); glyph.Set("v_color", row.Slice(9, 4)); glyph.Set("v_params", row.Slice(13, 4));
                glyph.Set("v_sizePx", [Math.Max(1, row[2]), Math.Max(1, row[3])]);
                Quad(glyph, row[0], row[1], row[2], row[3], row[4]);
            }
        }
        finally { ArrayPool<float>.Shared.Return(data); }
    }
    public SKPicture TakePicture() { var picture = result ?? throw new InvalidOperationException("Cassette did not produce a frame."); result = null; return picture; }
    public void Dispose()
    {
        result?.Dispose(); recorder.Dispose(); paint.Dispose();
        foreach (var texture in textures.Values) texture.Dispose(); textures.Clear(); bytes = 0;
        layer.Dispose(); particle.Dispose(); glyph.Dispose(); composite.Dispose();
    }
}
