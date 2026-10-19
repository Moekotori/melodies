using System.Numerics;
using SkiaSharp;

namespace Echo.Visuals.Cassette;

// Serialized worker owner: <= 48 textures / 64 MiB, one current picture. No script or JSON in drawing.
internal sealed partial class CassetteScenePainter : IDisposable
{
    private sealed record Texture(SKImage Image, SKShader Shader) : IDisposable
    { public void Dispose() { Shader.Dispose(); Image.Dispose(); } }
    private readonly Dictionary<int, Texture> textures = [];
    private readonly CassetteEffect layer, particle, glyph, composite;
    private readonly SKPictureRecorder recorder = new();
    private readonly SKPaint paint = new() { IsAntialias = true };
    private SKCanvas? canvas;
    private SKPicture? result;
    private int next, width, height;
    private long bytes;
    private float time, ratio = 1;
    private readonly float[] ripples = new float[32];

    public CassetteScenePainter()
    {
        try
        {
            layer = new(CassetteResources.Read("cassette-layer.sksl"));
            particle = new(CassetteResources.Read("cassette-particle.sksl"));
            glyph = new(CassetteResources.Read("cassette-glyph.sksl"));
            composite = new(CassetteResources.Read("cassette-composite.sksl"));
        }
        catch { layer?.Dispose(); particle?.Dispose(); glyph?.Dispose(); composite?.Dispose(); recorder.Dispose(); paint.Dispose(); throw; }
    }
    public int Upload(SKBitmap bitmap, bool repeat = false)
    {
        if (textures.Count >= 48) throw new InvalidDataException("Cassette texture count exceeded.");
        var cost = (long)bitmap.Width * bitmap.Height * 4;
        if (bytes + cost > 64 * 1024 * 1024) throw new InvalidDataException("Cassette texture budget exceeded.");
        var image = SKImage.FromBitmap(bitmap);
        try
        {
            var tile = repeat ? SKShaderTileMode.Repeat : SKShaderTileMode.Clamp;
            var shader = image.ToShader(tile, tile, new SKSamplingOptions(SKFilterMode.Linear));
            textures.Add(++next, new(image, shader)); bytes += cost; return next;
        }
        catch { image.Dispose(); throw; }
    }
    public long TextureBytes => bytes;
    public void Delete(int id)
    {
        if (textures.Remove(id, out var texture))
        { bytes -= (long)texture.Image.Width * texture.Image.Height * 4; texture.Dispose(); }
    }
    public void Begin(int w, int h, double seconds, Vector3 fallback)
    {
        if (w < 1 || h < 1 || (long)w * h > 1920 * 1080) throw new InvalidDataException("Cassette frame budget exceeded.");
        width = w; height = h; time = (float)seconds;
        canvas = recorder.BeginRecording(SKRect.Create(width, height));
        canvas.Clear(new SKColorF(fallback.X, fallback.Y, fallback.Z, 1));
    }
    private void Bind(CassetteEffect effect, string name, int id)
    {
        var texture = textures[id]; effect.Texture(name, texture.Shader, texture.Image.Width, texture.Image.Height);
    }
    private void Quad(CassetteEffect effect, double x, double y, double w, double h, double rotation, SKBlendMode blend = SKBlendMode.SrcOver)
    {
        if (w <= 0 || h <= 0) return;
        effect.Set("draw_size", [(float)w, (float)h]); effect.Set("draw_center", [(float)x, (float)y]); effect.Set("draw_rotation", (float)rotation);
        using var shader = effect.Shader(); paint.Shader = shader; paint.BlendMode = blend;
        canvas!.Save(); canvas.Translate((float)x, (float)y); canvas.RotateRadians((float)rotation); canvas.Translate((float)-w / 2, (float)-h / 2);
        canvas.DrawRect(SKRect.Create((float)w, (float)h), paint); canvas.Restore(); paint.Shader = null;
    }
    public void Layer(int texture, int noise, double x, double y, double w, double h, CassetteLayerOptions o)
    {
        if (o.Alpha <= .001) return;
        Bind(layer, "u_tex", texture); Bind(layer, "u_noise", noise);
        layer.Set("u_time", time); layer.Set("u_alpha", (float)o.Alpha);
        layer.Set("u_uvRect", o.UvRect ?? new Vector4(0, 0, 1, 1)); layer.Set("u_scroll", o.Scroll);
        layer.Set("u_tint", o.Tint ?? Vector3.One); layer.Set("u_additive", o.Additive || o.Screen ? 1 : 0);
        var ripple = o.Ripple ?? new Vector3(0, 0, 1);
        layer.Set("u_ripple", new Vector4(ripple, o.Ripple.HasValue ? 1 : 0)); layer.Set("u_waves", o.Waves);
        layer.Set("u_sway", new Vector3(o.Sway ?? Vector2.Zero, o.Sway.HasValue ? 1 : 0));
        layer.Set("u_disc", new Vector2(o.Disc.HasValue ? 1 : 0, (float)(o.Disc ?? 0)));
        Quad(layer, x, y, w, h, o.Rotation, o.Screen ? SKBlendMode.Screen : SKBlendMode.SrcOver);
    }
    public void Particle(int texture, double x, double y, double w, double h, double rotation, Vector4 uv, Vector4 color, bool additive)
    {
        Bind(particle, "u_tex", texture); particle.Set("draw_uv", uv); particle.Set("v_color", color);
        particle.Set("v_params", new Vector4(additive ? 1 : 0, 0, 0, 0)); Quad(particle, x, y, w, h, rotation);
    }
    public void Finish() { result?.Dispose(); result = recorder.EndRecording(); canvas = null; }
    public SKPicture TakePicture() { var picture = result ?? throw new InvalidOperationException("Cassette did not produce a frame."); result = null; return picture; }
    public void Dispose()
    {
        result?.Dispose(); recorder.Dispose(); paint.Dispose();
        foreach (var texture in textures.Values) texture.Dispose(); textures.Clear(); bytes = 0;
        layer.Dispose(); particle.Dispose(); glyph.Dispose(); composite.Dispose();
    }
}
