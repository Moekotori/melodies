using System.Diagnostics;
using System.Security.Cryptography;
using Echo.Visuals;
using SkiaSharp;

// Scene-worker CPU / managed allocations, including original Canvas drawing. Final picture replay/GPU work is excluded.
internal static class BackgroundProfile
{
    public static void Run(SourceBackgroundEngine scene,string mode,string root,int width,int height)
    {
        for(var i=0;i<12;i++){using var warm=scene.Draw(i*1000d/30,1000d/30);}
        using var frozen=scene.Draw(1000,1000d/30);
        var initial=Pixels(frozen,width,height);
        File.WriteAllBytes(Path.Combine(root,mode+".png"),initial);
        const int count=120;var timings=new double[count];
        var allocated=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<count;i++)
        {
            var start=Stopwatch.GetTimestamp();
            using(var picture=scene.Draw(2000+i*1000d/30,1000d/30)){}
            timings[i]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        var bytes=(GC.GetAllocatedBytesForCurrentThread()-allocated)/count;
        // Cached samplers must not invalidate frames still held by the render thread after texture replacement.
        foreach(var art in new[]{"placeholder-light.png","placeholder-dark.png","placeholder-light.png"})
        {
            scene.SetCover(File.ReadAllBytes("src/Echo.Desktop/Resources/Artwork/"+art));
            using var replacement=scene.Draw(8000,1000d/30);
        }
        if(!SHA256.HashData(initial).AsSpan().SequenceEqual(SHA256.HashData(Pixels(frozen,width,height))))
            throw new InvalidDataException("Previously delivered picture changed after later frames.");
        Array.Sort(timings);
        Console.WriteLine(FormattableString.Invariant($"PROFILE {mode} cpu_p50_ms={timings[count/2]:F3} cpu_p95_ms={timings[(int)(count*.95)]:F3} managed_bytes_per_frame={bytes} frozen_picture=PASS"));
    }
    private static byte[] Pixels(SKPicture picture,int width,int height)
    {
        using var surface=SKSurface.Create(new SKImageInfo(width,height));
        surface.Canvas.Scale(width/picture.CullRect.Width,height/picture.CullRect.Height);
        surface.Canvas.DrawPicture(picture);
        using var image=surface.Snapshot();using var data=image.Encode(SKEncodedImageFormat.Png,100);
        return data.ToArray();
    }
}
