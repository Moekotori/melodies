using System.Diagnostics;
using Echo.Visuals;
using SkiaSharp;

internal static class PixelBackgroundSmoke
{
    public static void Run(string root)
    {
        var modes=new[]{"coverPixelCrt","coverPixelWarp","coverPixelTiles","coverPixelShuffle","coverPixelRain","coverPixelSkyline"};
        using var scene=new SourceBackgroundEngine();
        var cover=File.ReadAllBytes("src/Echo.Desktop/Resources/Artwork/placeholder-dark.png");
        scene.Build(Project(modes[0]),320,180);scene.SetCover(cover);
        foreach(var mode in modes)
        {
            scene.Update(Project(mode),320,180);
            scene.Pointer(.7,.4,true);
            using var picture=scene.Draw(1000,16.667,"[0.8,0.5,0.3]",true,1,120);
            using var surface=SKSurface.Create(new SKImageInfo(320,180));
            surface.Canvas.DrawPicture(picture);
            using var image=surface.Snapshot();using var png=image.Encode(SKEncodedImageFormat.Png,100);
            File.WriteAllBytes(Path.Combine(root,mode+".png"),png.ToArray());
            const int count=16;
            var allocated=GC.GetAllocatedBytesForCurrentThread();var clock=Stopwatch.StartNew();
            for(var i=0;i<count;i++)using(scene.Draw(2000+i*1000d/120,1000d/120,"[0.8,0.5,0.3]",true,i+2,120)){}
            clock.Stop();var bytes=(GC.GetAllocatedBytesForCurrentThread()-allocated)/count;
            Console.WriteLine(FormattableString.Invariant($"PASS {mode}: source_cpu_mean_ms={clock.Elapsed.TotalMilliseconds/count:F3}, managed_bytes_per_frame={bytes}, n={count}; picture recording excludes final GPU replay"));
            scene.PointerLeave();
        }
        var slept=false;
        for(var i=0;i<100;i++)
        {
            using var next=scene.TryDraw(4000+i*250,250,"[]",false,1000+i,240);
            if(next is null){slept=true;break;}
        }
        Check(slept,"silent skyline settles without an error");
        using var resumed=scene.TryDraw(30000,16.667,"[1,1,1]",true,2000,240);
        Check(resumed is not null,"resuming music wakes skyline");
        scene.Update(Project("coverPond"),320,180);
        using var original=scene.Draw(31000,16.667);
        Check(original.CullRect.Width>0,"original cover scene survives shared adapter changes");
        Console.WriteLine("PASS skyline silence/resume, retained shader switches, cover/pointer; target caps 120/240 accepted");
    }
    private static string Project(string mode)=>$$"""{"mode":"{{mode}}","dark":true,"trackKey":"pixel-smoke"}""";
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
