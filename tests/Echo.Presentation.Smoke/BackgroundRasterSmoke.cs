using System.Diagnostics;
using System.Reflection;
using Echo.Desktop.Controls.Lyrics;
using Echo.Visuals;
using SkiaSharp;

// Exercise the production picture-to-texture path, including retained render-operation lifetime.
internal static class BackgroundRasterSmoke
{
    public static void Run()
    {
        using var scene = new SourceBackgroundEngine();
        scene.Build("""{"mode":"coverPond","dark":true,"trackKey":"raster-smoke"}""", 320, 180);
        scene.SetCover(File.ReadAllBytes("src/Echo.Desktop/Resources/Artwork/placeholder-dark.png"));
        using var output = SKSurface.Create(new SKImageInfo(1280, 720));
        var raster = new BackgroundSceneRaster();
        var retained = raster.Retain();
        raster.Dispose(); // A queued render operation must survive view disposal.
        using (retained)
        {
            var oldMs = 0d; var newMs = 0d;
            long oldBytes = 0, newBytes = 0;
            for (var i = 0; i < 5; i++)
            {
                var picture = scene.Draw(1000 + i * 200, 200);
                using var frame = new SourceSceneFrame(picture, (int)picture.CullRect.Width, (int)picture.CullRect.Height);
                var bounds = SKRect.Create(1280, 720);
                var allocated = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
                output.Canvas.Save(); output.Canvas.Scale(1280f / frame.Width, 720f / frame.Height);
                output.Canvas.DrawPicture(picture); output.Canvas.Restore();
                var oldTime = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var oldAllocation = GC.GetAllocatedBytesForCurrentThread() - allocated;
                if (i == 4) Save(output, "direct");
                allocated = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp();
                retained.Draw(frame, output.Canvas, null, bounds, 1);
                var newTime = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var newAllocation = GC.GetAllocatedBytesForCurrentThread() - allocated;
                if (i == 4) Save(output, "budgeted");
                if (i > 0) { oldMs += oldTime; newMs += newTime; oldBytes += oldAllocation; newBytes += newAllocation; }
                var surface = (SKSurface)typeof(BackgroundSceneRaster).GetField("surface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(retained)!;
                Check(surface.Canvas.DeviceClipBounds.Width == frame.Width && surface.Canvas.DeviceClipBounds.Height == frame.Height, "shader surface follows source budget");
                using var before = output.Snapshot(); using var beforePixels = before.PeekPixels();
                var pixel = beforePixels.GetPixelColor(640, 360);
                output.Canvas.Clear(SKColors.Transparent);
                retained.Draw(frame, output.Canvas, null, bounds, 1);
                using var after = output.Snapshot(); using var afterPixels = after.PeekPixels();
                Check(afterPixels.GetPixelColor(640, 360) == pixel && pixel.Alpha > 0, "cached frame survives redraw");
            }
            Console.WriteLine(FormattableString.Invariant($"PASS coverPond software replay 320x180 -> 1280x720, n=4 warmed frames: direct_mean_ms={oldMs / 4:F2}, budgeted_mean_ms={newMs / 4:F2}, managed_bytes={oldBytes / 4}/{newBytes / 4}; retained lifetime and redraw passed. GPU/window FPS excluded."));
        }
        Check(typeof(BackgroundSceneRaster).GetField("surface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(raster) is null, "last operation releases native surface");
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static void Save(SKSurface surface, string name)
    {
        Directory.CreateDirectory("misc/background-fix");
        using var image = surface.Snapshot(); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes("misc/background-fix/" + name + ".png", png.ToArray());
    }
}
