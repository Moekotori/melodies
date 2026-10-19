using Echo.Visuals;
using SkiaSharp;

internal static class LifecycleChecks
{
    public static void Run()
    {
        if (typeof(SourceLyricsEngine).Assembly.GetManifestResourceNames().Any(n => n.EndsWith(".cassette.js", StringComparison.Ordinal)))
            throw new Exception("The production assembly still embeds the cassette script.");
        const string json = """{"phase":"night","title":"ECHO","durationMs":12000,"lines":[{"text":"你好 Echo 👋🏽","timeMs":0},{"text":"回声","timeMs":4000}]}""";
        using var cancellation = new CancellationTokenSource();
        using (var engine = new SourceLyricsEngine("cassette", cancellation: cancellation.Token))
        {
            Expect<InvalidDataException>(() => engine.Build(json,0,900));
            Expect<InvalidDataException>(() => engine.Build(new string(' ',262145),1440,900));
            foreach (var phase in new[] { "day", "sunset", "night" })
            {
                engine.Build(json.Replace("night",phase,StringComparison.Ordinal),1440,900);
                Expect<InvalidDataException>(() => engine.SetCover(new byte[4*1024*1024+1]));
                // Return to the same instance after a failed replacement; the next Build owns fresh resources.
                using var cover = SKSurface.Create(new SKImageInfo(64,64)); cover.Canvas.Clear(SKColors.OrangeRed);
                using var image = cover.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png,100);
                engine.SetCover(data.ToArray()); engine.SetCover(data.ToArray());
                for (var frame = 0; frame < 12; frame++)
                {
                    using var picture = engine.Draw(frame*100,frame<8,frame*100,100,frame<6);
                    if (picture.CullRect.Width != 1440 || picture.CullRect.Height != 900) throw new Exception("Backing dimensions changed.");
                    engine.Pointer(360,550,true);
                }
            }
            cancellation.Cancel();
            Expect<OperationCanceledException>(() => { using var picture = engine.Draw(1000,true,1000,33); });
            engine.Dispose(); engine.Dispose();
            Expect<ObjectDisposedException>(() => { using var picture = engine.Draw(1000,true,1000,33); });
        }
        using var cancelled = new SourceLyricsEngine("cassette",cancellation:new CancellationToken(true));
        Expect<OperationCanceledException>(() => cancelled.Build(json,1440,900));
        Console.WriteLine("PASS cassette has no script resource; repeated Build/cover/phase, motion/paused drawing, cancellation and idempotent disposal");
    }
    private static void Expect<T>(Action operation) where T:Exception
    {
        try { operation(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
