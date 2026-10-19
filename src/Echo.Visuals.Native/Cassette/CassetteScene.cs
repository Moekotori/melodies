using System.Numerics;

namespace Echo.Visuals.Cassette;

internal readonly record struct CassetteTitleState(double Width, double Height, double Reveal, double Ink, double Neon);

// Artwork coordinates and compositing order are retained from cassetteScenePass.ts.
internal sealed class CassetteScene(CassetteScenePainter painter, CassetteArt art)
{
    public void Draw(CassetteLayout layout, double ratio, ReadOnlySpan<double> weights, double time, bool motion,
        double spin, double spinning, CassetteTitleState? title, CassetteParticles particles)
    {
        var ss = layout.Scale * ratio;
        double X(double x) => (layout.SceneX + x * layout.Scale) * ratio;
        double Y(double y) => (layout.SceneY + y * layout.Scale) * ratio;
        var t = motion ? time : 0; Span<double> stacked = stackalloc double[3]; var total = 0d;
        for (var phase = 0; phase < 3; phase++)
        {
            if (weights[phase] <= .0001) continue;
            total += weights[phase]; stacked[phase] = total == weights[phase] ? 1 : weights[phase] / total;
            var id = CassetteArt.PhaseAssets[phase][0];
            Layer(id, X(1920), Y(1080), 5760 * ss, 2160 * ss,
                new(stacked[phase], Scroll: new((float)Fract(t * (phase == 2 ? -.01 : .0064)), 0), Ripple: motion ? new(.05f,.07f,1) : null));
        }
        var day = weights[0]; var sunset = weights[1]; var night = weights[2];
        if (day > 0)
        {
            if (motion)
            {
                var cycle = t % 46 / 18;
                if (cycle <= 1) Layer("paperPlane", X(-1040 + cycle * 5400), Y(1741 - cycle * 2500), 893 * ss, 890 * ss,
                    new(.9 * day * Math.Min(1, Math.Min(cycle / .12, (1 - cycle) / .12))));
                particles.EmitBirds(painter, art["birds"], weights, layout, ratio);
            }
            Layer("cityDay", X(1920), Y(1605), 3840 * ss, 1110 * ss, new(day));
        }
        if (sunset > 0)
        {
            Layer("clouds", X(1920), Y(2244.17), 5760 * ss, 2160 * ss, new(.4 * sunset,
                Scroll: new((float)Fract(t * .01),0), Tint: new(.906f,.71f,.831f), Ripple: motion ? new(.1f,.1f,1) : null));
            Layer("citySunset", X(1920), Y(1080), 3840 * ss, 2160 * ss, new(sunset, Waves: motion ? new(.03f,3,5,0) : default));
            Layer("moon", X(1904.5), Y(509.5), 157 * ss, 157 * ss, new(sunset));
            if (motion)
            {
                particles.EmitBirds(painter, art["birds"], weights, layout, ratio);
                particles.EmitFireflies(painter, art["glow"], sunset, layout, ratio);
            }
            double Flicker(int i) => motion ? .86 + .14 * Math.Sin(t * 3 + i * 2.1) * Math.Sin(t * .7 + i) : 1;
            Layer("streetLight", X(3282.7), Y(1743), 1000 * ss, 1504.5 * ss, new(.8 * sunset * Flicker(0), Additive:true));
            Layer("streetLight", X(536.5), Y(2051), 550 * ss, 1003 * ss, new(.8 * sunset * Flicker(1), Additive:true));
        }
        if (night > 0)
        {
            Layer("aurora", X(1920), Y(747), 8192 * ss, 4096 * ss, new(night * (motion ? .42 + .16 * Math.Sin(t * .5) : .5),
                Waves: motion ? new(.05f,.6f,3,1.57f) : default));
            if (motion)
            {
                particles.EmitStars(painter, art["starGlow"], night, layout, ratio, 2);
                particles.EmitMeteors(painter, art["streak"], night, layout, ratio, false);
                particles.EmitMeteors(painter, art["glow"], night, layout, ratio, true);
                particles.EmitStars(painter, art["starCross"], night, layout, ratio, 0);
                particles.EmitStars(painter, art["starGlow"], night, layout, ratio, 1);
            }
            Layer("clouds", X(1920), Y(2244.17), 5760 * ss, 2160 * ss, new(.6 * night,
                Scroll: new((float)Fract(t * .01),0), Tint: new(.906f,.71f,.831f), Ripple: motion ? new(.1f,.1f,1) : null));
            Layer("cityNight", X(1920), Y(1603.5), 3840 * ss, 1113 * ss, new(night, Sway: motion ? new(.4f,1.4f) : null));
        }
        Tape(layout, ratio, stacked, spin, spinning, motion, title);
        if (day > 0)
        {
            ReadOnlySpan<(string Id, double X, double Y, double W, double H, double Rotation, double Alpha)> flares =
            [("flareRing",683.5,1514.2,1807,1788,0,1), ("flareHalo",1168.8,1336.7,1915,1842,0,1),
             ("flareDot",1954.9,489.3,275,261,0,1), ("flareHalo",2375.9,148.9,967,930,2.04,.8), ("flareRing",2860.8,-330.3,1701,1683,0,.3)];
            double Sway(int i) => motion ? Math.Sin(t * .45 + i * 1.7) * 10 : 0;
            for (var i = 0; i < flares.Length; i++)
            { var f = flares[i]; Layer(f.Id, X(f.X + Sway(i)), Y(f.Y - Sway(i + 3)), f.W * ss, f.H * ss, new(f.Alpha * .45 * day, f.Rotation, Screen:true)); }
        }
    }
    private void Tape(CassetteLayout layout, double ratio, ReadOnlySpan<double> stacked, double spin, double spinning, bool motion, CassetteTitleState? title)
    {
        var ts = layout.Scale * ratio;
        double X(double x) => (layout.TapeX + x * layout.Scale) * ratio;
        double Y(double y) => (layout.TapeY + y * layout.Scale) * ratio;
        const double angle = .46775;
        for (var phase = 0; phase < 3; phase++)
        {
            if (stacked[phase] <= 0) continue;
            Layer(CassetteArt.PhaseAssets[phase][2], X(1064), Y(747.5), 2128 * ts, 1495 * ts, new(stacked[phase]));
            if (title is null) Layer(CassetteArt.PhaseAssets[phase][3], X(614.5), Y(686.5), 941 * ts, 569 * ts, new(stacked[phase]));
        }
        if (title is { Reveal: > 0 } info)
        {
            var shown = info.Width * info.Reveal; var cx = 606 - info.Width / 2 + shown / 2; var dx = cx - 606;
            var rx = 606 + dx * Math.Cos(angle); var ry = 694 + dx * Math.Sin(angle); var uv = new Vector4(0,0,(float)info.Reveal,1);
            if (info.Ink > 0) Layer("title", X(rx), Y(ry), shown * ts, info.Height * ts, new(.9 * info.Ink, angle, uv));
            if (info.Neon > 0) Layer("titleNeon", X(rx), Y(ry), shown * ts, info.Height * ts, new(.9 * info.Neon, angle, uv));
        }
        for (var phase = 0; phase < 3; phase++)
        {
            if (stacked[phase] <= 0) continue;
            Layer(CassetteArt.PhaseAssets[phase][4], X(490), Y(456.5), 261 * ts, 261 * ts, new(stacked[phase], spin));
            Layer(CassetteArt.PhaseAssets[phase][5], X(897.5), Y(656.5), 266 * ts, 266 * ts, new(stacked[phase], spin));
        }
        if (art.Has("cover"))
        {
            var waves = motion && spinning > .01 ? new Vector4((float)(.06 * spinning),15,25,1.93f) : default;
            Layer("cover", X(489.7), Y(464.4), 361.2 * ts, 361.2 * ts, new(.26, spin, Disc:.2, Waves:waves));
            Layer("cover", X(904.5), Y(667), 496.3 * ts, 496.3 * ts, new(.26, spin + 1.22, Disc:.16, Waves:waves));
        }
    }
    private void Layer(string id, double x, double y, double w, double h, CassetteLayerOptions options)
    { var texture = art[id]; if (texture != 0) painter.Layer(texture, art.Noise, x, y, w, h, options); }
    private static double Fract(double n) => n - Math.Floor(n);
}
