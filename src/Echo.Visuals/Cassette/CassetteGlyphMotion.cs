namespace Echo.Visuals.Cassette;

internal struct CassetteGlyphPose
{
    public double Dx, Dy, Rotation, Scale, Alpha, Wobble, Dissolve;
}
internal static class CassetteGlyphMotion
{
    private static double Cubic(double t) => 1 - Math.Pow(1 - t, 3);
    public static CassetteGlyphPose Sample(int glyph, int count, double seed, double age, double scatterAge, double font, double time, bool motion)
    {
        var h1 = CassetteLayout.Hash(seed, glyph, 1); var h2 = CassetteLayout.Hash(seed, glyph, 2);
        var h3 = CassetteLayout.Hash(seed, glyph, 3); var h4 = CassetteLayout.Hash(seed, glyph, 4);
        var stagger = Math.Min(48, 520d / Math.Max(1, count));
        var enter = Math.Clamp((age + 280 - glyph * stagger) / 760, 0, 1);
        var rise = 1 + 2.35 * Math.Pow(enter - 1, 3) + 1.35 * Math.Pow(enter - 1, 2);
        var p = new CassetteGlyphPose { Alpha = Math.Clamp(enter * 1.8, 0, 1), Dy = (1 - rise) * font * .95,
            Scale = .8 + .2 * Cubic(enter), Rotation = (1 - Cubic(enter)) * (h1 - .5) * .6, Wobble = .18 + (1 - enter) * 1.2 };
        if (!motion)
        {
            p.Dy = p.Rotation = 0; p.Scale = 1; p.Wobble = .08;
            p.Alpha = Math.Clamp((age + 280) / 240, 0, 1) * (scatterAge > 0 ? Math.Clamp(1 - scatterAge / 420, 0, 1) : 1);
            return p;
        }
        p.Dx += Math.Cos(time * .9 + glyph * .7 + h2 * 3) * font * .022;
        p.Dy += Math.Sin(time * 1.25 - glyph * .55) * font * .05;
        p.Rotation += Math.Sin(time * .8 + glyph * .9) * .025;
        if (scatterAge > 0)
        {
            var order = h4 < .5 ? glyph : count - 1 - glyph;
            var q = Math.Clamp((scatterAge - order * Math.Min(36, 420d / Math.Max(1, count))) / (2100 * .82), 0, 1);
            var heading = -Math.PI / 2 + .4 + (h2 - .5) * 1.4; var travel = font * (2.2 + h3 * 3.4) * Cubic(q);
            p.Dx += Math.Cos(heading) * travel + q * q * font * 1.4; p.Dy += Math.Sin(heading) * travel;
            p.Rotation += (h1 - .5) * 1.4 * q; p.Scale *= 1 + .22 * q; p.Alpha *= Math.Pow(1 - q, 1.3);
            p.Wobble += q * 1.3; p.Dissolve = Math.Clamp(q * 1.15 - .1, 0, 1);
        }
        return p;
    }
}
