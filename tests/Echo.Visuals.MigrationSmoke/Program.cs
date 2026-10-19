using System.Diagnostics;
using Echo.Visuals;
using SkiaSharp;

if (args.Contains("--lifecycle")) { LifecycleChecks.Run(); return; }
var root = Path.GetFullPath(args[0]);
var verify = args.Contains("--verify");
var motionOnly = args.Contains("--motion-only");
Directory.CreateDirectory(root);
const string fixture = """
{"fontSize":36,"secondarySize":18,"phase":"PHASE","title":"此刻的回声 Echo","durationMs":40000,"lines":[
{"text":"夜色沿着窗边流动","timeMs":0},
{"text":"把这一刻唱给你听","timeMs":4000,"translation":"Sing this moment to you","words":[{"text":"把","startMs":4000,"endMs":4450},{"text":"这一刻","startMs":4450,"endMs":5300},{"text":"唱给","startMs":5300,"endMs":6100},{"text":"你听","startMs":6100,"endMs":7300}]},
{"text":"让旋律带着我们远行","timeMs":8000},{"text":"微光落在你的眼睛","timeMs":12000},
{"text":"Every note has an echo, a long line across the sky","timeMs":16000,"translation":"每一个音符都有回声"},
{"text":"Café 👨‍👩‍👧‍👦 你好","timeMs":20000},{"text":"我们一起听见","timeMs":24000},{"text":"明天的声音","timeMs":28000}]}
""";
var watch = Stopwatch.StartNew();
foreach (var phase in new[] { "day", "sunset", "night" })
foreach (var (w, h) in new[] { (1440, 900), (800, 1200), (3840, 2160) })
{
    if (motionOnly && (phase != "day" || w != 1440)) continue;
    if (args.Contains("--quick") && (phase != "day" || w != 1440)) continue;
    using var engine = new SourceLyricsEngine("cassette");
    Console.WriteLine(engine.Build(fixture.Replace("PHASE", phase, StringComparison.Ordinal), w, h));
    // The same actual cover must pass through both renderers' crop and upload path.
    using (var cover = SKSurface.Create(new SKImageInfo(128, 96)))
    {
        cover.Canvas.Clear(SKColors.CornflowerBlue);
        using var paint = new SKPaint { Color = SKColors.Coral };
        cover.Canvas.DrawCircle(64, 48, 28, paint);
        using var image = cover.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        engine.SetCover(data.ToArray());
    }
    var timestamps = motionOnly ? new[] { 3760d, 4000, 4300, 6000, 7880, 8100, 8400, 10000 } : new[] { 6000d, 6001, 12000, 17000, 21000 };
    var frame = 0;
    foreach (var ms in timestamps)
    {
        var now = motionOnly ? ++frame * 100 : ms;
        using var picture = engine.Draw(ms, motionOnly, now, motionOnly ? 100 : 33, motionOnly);
        var pw = Math.Max(1, (int)picture.CullRect.Width);
        var ph = Math.Max(1, (int)picture.CullRect.Height);
        using var surface = SKSurface.Create(new SKImageInfo(pw, ph));
        surface.Canvas.DrawPicture(picture);
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        var path = Path.Combine(root, $"{(motionOnly ? "motion-" : "")}{phase}-{w}x{h}-{ms}.png");
        if (!verify) File.WriteAllBytes(path, png.ToArray());
        else
        {
            using var expected = SKBitmap.Decode(path);
            using var actual = SKBitmap.FromImage(image);
            if (expected.Width != pw || expected.Height != ph) throw new Exception("Backing resolution changed: " + path);
            var left = expected.GetPixelSpan(); var right = actual.GetPixelSpan();
            long total = 0; var maximum = 0; var changed = 0;
            for (var i = 0; i < left.Length; i++)
            {
                var delta = Math.Abs(left[i] - right[i]);
                total += delta; maximum = Math.Max(maximum, delta); if (delta > 0) changed++;
            }
            Console.WriteLine($"PIXELS {Path.GetFileName(path)} mean={(double)total / left.Length:F5} max={maximum} changed={changed}");
            // Native float shader boundaries may differ by one 8-bit channel step; geometry and hits must match.
            if (maximum > 1 || (double)total / left.Length > .0001)
            {
                File.WriteAllBytes(Path.ChangeExtension(path, ".actual.png"), png.ToArray());
                Environment.ExitCode = 1;
            }
        }
        var hits = new List<string>();
        for (var y = 0; y <= h; y += 40)
        for (var x = 0; x <= w; x += 40)
        {
            var seek = engine.Pointer(x, y, true);
            if (seek >= 0) hits.Add($"{x},{y}:{seek}");
        }
        var hitPath = Path.ChangeExtension(path, ".hits"); var hitText = string.Join('\n', hits);
        if (!verify) File.WriteAllText(hitPath, hitText);
        else if (File.ReadAllText(hitPath) != hitText) throw new Exception("Seek hit boxes changed: " + hitPath);
        if (motionOnly) engine.Pointer(360, 550, true);
    }
    using (engine.Draw(6200, false, 22000, 33)) { }
    var allocated = GC.GetAllocatedBytesForCurrentThread(); var start = watch.Elapsed.TotalMilliseconds;
    for (var i = 0; i < 8; i++) using (engine.Draw(6200 + i, false, 22000 + i * 33, 33)) { }
    Console.WriteLine($"PROFILE {phase} {w}x{h} record-ms={(watch.Elapsed.TotalMilliseconds - start) / 8:F3} bytes/frame={(GC.GetAllocatedBytesForCurrentThread() - allocated) / 8}");
}
Console.WriteLine($"Cassette frames and seek hit boxes complete: {watch.Elapsed.TotalSeconds:F1}s");
