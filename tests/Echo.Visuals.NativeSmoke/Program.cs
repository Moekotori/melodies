using Echo.Visuals.Cassette;
using SkiaSharp;

// This executable references only the actual production native scene module, with no Jint/JS dependency.
const string fixture = """{"phase":"PHASE","title":"ECHO 回声","durationMs":16000,"lines":[{"text":"把这一刻唱给你听","timeMs":4000,"translation":"Sing this moment to you","words":[{"text":"把这一刻","startMs":4000,"endMs":5500},{"text":"唱给你听","startMs":5500,"endMs":7400}]},{"text":"Café 👨‍👩‍👧‍👦","timeMs":8000}]}""";
var root = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/lyrics-csharp/aot-frames");
Directory.CreateDirectory(root);
if (typeof(CassetteEngine).Assembly.GetManifestResourceNames().Any(n => n.EndsWith(".js",StringComparison.Ordinal))) throw new Exception("The native module embeds scripts.");
foreach (var phase in new[] { "day", "sunset", "night" })
{
    using var stop = new CancellationTokenSource();
    using var engine = new CassetteEngine(stop.Token);
    var receipt = engine.Build(fixture.Replace("PHASE",phase,StringComparison.Ordinal),1440,900);
    using var artwork = new SKBitmap(256,256); artwork.Erase(SKColors.CornflowerBlue);
    engine.SetCover(artwork);
    for (var i = 0; i < 12; i++)
    {
        using var picture = engine.Draw(5800+i*100,i<8,i*100,100,i<6);
        if (picture.CullRect.Width != 1440 || picture.CullRect.Height != 900) throw new Exception("Unexpected render resolution.");
        if (i == 11)
        {
            using var surface = SKSurface.Create(new SKImageInfo(1440,900)); surface.Canvas.DrawPicture(picture);
            using var image = surface.Snapshot(); using var png = image.Encode(SKEncodedImageFormat.Png,100);
            File.WriteAllBytes(Path.Combine(root,phase+".png"),png.ToArray());
        }
        engine.Pointer(360,550,true);
    }
    stop.Cancel();
    try { using var ignored = engine.Draw(7000,true,1500,33,true); throw new Exception("Cancellation was ignored."); }
    catch (OperationCanceledException) { }
    engine.Dispose(); engine.Dispose();
    try { using var ignored = engine.Draw(7000,true,1600,33); throw new Exception("Disposed engine drew a frame."); }
    catch (ObjectDisposedException) { }
    Console.WriteLine("PASS native scene, motion, artwork, pause, cancel and dispose: " + receipt);
}
