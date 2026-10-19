using System.Numerics;
using SkiaSharp;

namespace Echo.Visuals.Cassette;

internal sealed partial class CassetteScenePainter
{
    public void Composite(ReadOnlySpan<float> incoming, double pixelRatio, bool retro, double reveal, Vector3 fallback)
    {
        if (incoming.Length != 32) throw new InvalidDataException("Cassette ripple count mismatch.");
        using var scene = recorder.EndRecording();
        canvas = recorder.BeginRecording(SKRect.Create(width, height));
        using var sceneShader = scene.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, SKFilterMode.Linear, SKRect.Create(width, height));
        composite.Texture("u_scene", sceneShader, width, height); ratio = (float)pixelRatio;
        for (var i = 0; i < 32; i++) ripples[i] = i % 4 < 2 ? incoming[i] * ratio : incoming[i];
        composite.Set("u_ripples", ripples); composite.Set("u_rippleShape", [380 * ratio, 36 * ratio, 9 * ratio]);
        composite.Set("u_view", [width, height]); composite.Set("u_time", time); composite.Set("u_pxScale", ratio);
        composite.Set("u_retro", retro ? 1 : 0); composite.Set("u_reveal", (float)reveal); composite.Set("u_fallback", fallback);
        using var shader = composite.Shader(); paint.Shader = shader; paint.BlendMode = SKBlendMode.Src;
        canvas.DrawRect(SKRect.Create(width, height), paint); paint.Shader = null;
    }
    public void Glyphs(int texture, int noise, ReadOnlySpan<float> data, int count, CassettePalette palette)
    {
        if (count is < 0 or > 512 || data.Length < count * 17) throw new InvalidDataException("Cassette glyph count exceeded.");
        Bind(glyph, "u_tex", texture); Bind(glyph, "u_noise", noise);
        glyph.Set("u_time", time); glyph.Set("u_ripples", ripples); glyph.Set("u_rippleShape", [380 * ratio, 36 * ratio, 9 * ratio]);
        glyph.Set("u_base", palette.Base); glyph.Set("u_sub", palette.Sub); glyph.Set("u_hi", palette.Highlight);
        glyph.Set("u_halo", palette.Halo); glyph.Set("u_haloAlpha", palette.HaloAlpha);
        for (var i = 0; i < count; i++)
        {
            var row = data.Slice(i * 17, 17);
            glyph.Set("v_rect", row.Slice(5, 4)); glyph.Set("v_color", row.Slice(9, 4)); glyph.Set("v_params", row.Slice(13, 4));
            glyph.Set("v_sizePx", [Math.Max(1, row[2]), Math.Max(1, row[3])]);
            Quad(glyph, row[0], row[1], row[2], row[3], row[4]);
        }
    }
}
