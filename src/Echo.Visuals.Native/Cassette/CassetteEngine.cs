using System.Numerics;
using SkiaSharp;

namespace Echo.Visuals.Cassette;

// Control presentation owner only. The caller supplies the confirmed playback position on one worker.
public sealed partial class CassetteEngine : IDisposable
{
    private readonly CancellationToken cancellation;
    private readonly CassetteScenePainter painter;
    private readonly CassetteArt art;
    private readonly CassetteRasterizer rasterizer = new();
    private readonly CassetteScene scene;
    private readonly CassetteParticles particles = new();
    private CassetteProject project = null!;
    private CassetteLayout layout = null!;
    private CassetteTimedLine[] timeline = [];
    private int[] cellOfLine = [];
    private readonly double[] weights = new double[3];
    private readonly float[] ripples = new float[32];
    private readonly List<(double X, double Y, double At)> pointerRipples = new(3);
    private readonly List<(CassetteRegion Bounds, double Time)> hitBoxes = new(4);
    private double ratio, time, spinAngle, spinSpeed, titleTyped, reveal, sinceCheck;
    private int width, height, target, frameNumber;
    private bool motion = true, disposed;
    private (double Position, double At)? wordClock;
    private (double Width, double Height, int Length)? titleInfo;

    public CassetteEngine(CancellationToken cancellation = default)
    {
        this.cancellation = cancellation; cancellation.ThrowIfCancellationRequested();
        painter = new();
        try { art = new(painter, cancellation); scene = new(painter, art); }
        catch { painter.Dispose(); rasterizer.Dispose(); throw; }
    }
    public string Build(string json, int w, int h)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (json.Length > 262144 || w is < 1 or > 8192 || h is < 1 or > 8192 || (long)w * h > 33_554_432)
            throw new InvalidDataException("Cassette scene input budget exceeded.");
        if (project is not null) throw new InvalidOperationException("A cassette engine owns one scene; create a new owner when rebuilding.");
        cancellation.ThrowIfCancellationRequested(); project = CassetteProject.Parse(json);
        timeline = CassetteTimeline.Create(project); layout = CassetteLayout.Create(w, h);
        ratio = Math.Min(1, Math.Min(2, Math.Sqrt(1920d * 1080 / ((double)w * h))));
        width = Math.Max(1, (int)Math.Floor(w * ratio)); height = Math.Max(1, (int)Math.Floor(h * ratio));
        cellOfLine = new int[timeline.Length]; var seed = project.Seed * 131 + timeline.Length * 7;
        for (var i = 0; i < timeline.Length; i++)
            cellOfLine[i] = CassetteLayout.PickCell(layout.Cells.Length, seed, i, i > 0 ? cellOfLine[i-1] : -1, i > 1 ? cellOfLine[i-2] : -1);
        using var title = rasterizer.Title(project.Title, 30 * layout.Scale * ratio * 2);
        if (title is not null)
        {
            art.Set("title", title.Ink); art.Set("titleNeon", title.Neon);
            titleInfo = (title.Width, title.Height, title.Length);
        }
        target = ResolvePhase(); art.Ensure(target, weights);
        return FormattableString.Invariant($"{{\"lines\":{timeline.Length},\"kind\":\"cassette\"}}");
    }
    private int ResolvePhase()
    {
        if (project.Phase == "auto") { var now = DateTime.Now; var hours = now.Hour + now.Minute / 60d; return hours >= 7 && hours < 18 ? 0 : hours >= 18 && hours < 22 ? 1 : 2; }
        return project.Phase switch { "day" => 0, "sunset" => 1, "night" => 2, _ => throw new InvalidDataException("Unknown cassette phase.") };
    }
    public void SetCover(SKBitmap cover) { Check(); art.Set("cover", cover); }
    public SKPicture Draw(double positionMs, bool animate, double nowMs, double elapsedMs, bool playing = false)
    {
        Check(); frameNumber++;
        if (motion != animate) { motion = animate; if (!motion) particles.Reset(); }
        var dt = Math.Clamp(elapsedMs / 1000, 0, .1); if (motion) time += dt;
        sinceCheck += elapsedMs;
        if (sinceCheck >= 20000) { sinceCheck = 0; var next = ResolvePhase(); if (next != target) { target = next; art.Ensure(target, weights); } }
        var sum = weights[0] + weights[1] + weights[2]; var released = false;
        var step = motion ? Math.Min(1, Math.Max(0, elapsedMs) / 2600) : 1;
        for (var i = 0; i < 3; i++)
        {
            var previous = weights[i];
            weights[i] = sum <= 0 ? i == target ? 1 : 0 : i == target ? Math.Min(1, previous + step) : Math.Max(0, previous - step);
            released |= previous > 0 && weights[i] <= 0;
        }
        var total = weights[0] + weights[1] + weights[2];
        for (var i = 0; i < 3; i++) weights[i] /= total;
        if (released) art.Ensure(target, weights);
        reveal = Math.Min(1, reveal + (motion ? elapsedMs / 900 : 1));
        if (motion) particles.Step(dt, weights);
        var desired = playing ? 1 : 0;
        spinSpeed = Math.Abs(spinSpeed - desired) < .05 ? desired : spinSpeed + (desired - spinSpeed) * Math.Min(1, dt * .7 * 1.5);
        if (motion) spinAngle = (spinAngle + dt * spinSpeed * 100 * Math.PI / 180) % (Math.PI * 2);
        if (titleInfo is { } ti) titleTyped = motion ? Math.Min(ti.Length, titleTyped + dt / .035) : ti.Length;
        var songMs = SongTime(positionMs, nowMs, playing);
        Span<CassetteVisibleLine> buffer = stackalloc CassetteVisibleLine[4];
        var visible = buffer[..CassetteTimeline.Visible(timeline, songMs, buffer)];
        BuildGlyphs(visible, songMs); BuildRipples(visible);
        var palette = BlendPalette(); var fallback = BlendFallback();
        painter.Begin(width, height, time, fallback);
        CassetteTitleState? title = titleInfo is { } info ? new(info.Width, info.Height,
            Math.Min(1, Math.Ceiling(titleTyped) / Math.Max(1, info.Length)), weights[0] + weights[1], weights[2]) : null;
        scene.Draw(layout, ratio, weights, time, motion, spinAngle, spinSpeed, title, particles);
        painter.Composite(ripples, ratio, project.Retro, reveal, fallback);
        for (var i = 0; i < batchCount; i++)
        { var batch = batches[i]; painter.Glyphs(batch.Entry.Texture, art.Noise, batch.Entry.Instances, batch.Count, palette); }
        painter.Finish(); return painter.TakePicture();
    }
    private double SongTime(double raw, double now, bool playing)
    {
        if (wordClock is { } previous && playing)
        {
            var elapsed = Math.Max(0, now - previous.At); var expected = previous.Position + elapsed;
            var correction = raw - expected;
            if (Math.Abs(correction) < 320) raw = expected + Math.Clamp(correction, -elapsed * .1, elapsed * .1);
        }
        wordClock = (raw, now); return raw;
    }
    private CassettePalette BlendPalette()
    {
        var sum = weights[0] + weights[1] + weights[2];
        Vector3 Mix(int key)
        {
            // Accumulate in double like the source, then round each uniform once at the Skia boundary.
            double x = 0, y = 0, z = 0;
            for (var i = 0; i < 3; i++)
            {
                var offset = i * 13 + key * 3;
                var weight = sum <= 0 ? i == target ? 1 : 0 : weights[i] / sum;
                x += CassettePalette.Source[offset] * weight; y += CassettePalette.Source[offset+1] * weight; z += CassettePalette.Source[offset+2] * weight;
            }
            return new((float)x,(float)y,(float)z);
        }
        double alpha = 0; for (var i = 0; i < 3; i++) alpha += CassettePalette.Source[i*13+12] * (sum <= 0 ? i == target ? 1 : 0 : weights[i] / sum);
        return new(Mix(0), Mix(1), Mix(2), Mix(3), (float)alpha);
    }
    private Vector3 BlendFallback()
    {
        var sum = weights[0] + weights[1] + weights[2]; double x = 0, y = 0, z = 0;
        for (var i = 0; i < 3; i++)
        {
            var weight = sum <= 0 ? i == target ? 1 : 0 : weights[i] / sum;
            x += CassettePalette.Fallbacks[i*3] * weight; y += CassettePalette.Fallbacks[i*3+1] * weight; z += CassettePalette.Fallbacks[i*3+2] * weight;
        }
        return new((float)x,(float)y,(float)z);
    }
    public double Pointer(double x, double y, bool press)
    {
        Check(); if (!press) return -1;
        var seek = -1d;
        foreach (var (b, timeMs) in hitBoxes) if (x >= b.X0 && x <= b.X1 && y >= b.Y0 && y <= b.Y1) { seek = timeMs; break; }
        if (pointerRipples.Count == 3) pointerRipples.RemoveAt(0);
        pointerRipples.Add((x, y, time)); return seek;
    }
    private void Check() { ObjectDisposedException.ThrowIf(disposed, this); cancellation.ThrowIfCancellationRequested(); }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        ClearLines(); art.Dispose(); painter.Dispose(); rasterizer.Dispose();
        timeline = []; cellOfLine = []; hitBoxes.Clear(); pointerRipples.Clear();
        project = null!; layout = null!;
    }
}
