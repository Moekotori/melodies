using Echo.Visuals;
using SkiaSharp;
using System.Globalization;

var root=Path.GetFullPath(args.Length>0?args[0]:"misc/backgrounds");Directory.CreateDirectory(root);
if(args.Contains("--pixel-smoke")){PixelBackgroundSmoke.Run(root);return;}
var reactive=args.Contains("--reactive");var transitions=args.Contains("--transitions");
var sequence=args.Contains("--sequence");var light=args.Contains("--light");
var modes=args.Skip(1).Where(a=>!a.StartsWith("--")).ToArray();
if(modes.Length==0)modes=["coverFlow","flowingLight","geometricAmbience","liquidLight","pulseRidge","halftone","contour","sakura","bfs","coverLiquid","coverPrism","coverFrost","coverPond","coverHolo","coverVinyl","coverNeon","coverPixelCrt","coverPixelWarp","coverPixelTiles","coverPixelShuffle","coverPixelRain","coverPixelSkyline"];
var failed=false;
foreach(var mode in modes)
{
    try
    {
        var width=sequence?320:640;var height=sequence?180:360;
        using var scene=new SourceBackgroundEngine();
        scene.Build($$"""{"mode":"{{mode}}","dark":{{(light?"false":"true")}},"trackKey":"echo-visual-probe-0"}""",width,height);
        scene.SetCover(File.ReadAllBytes("src/Echo.Desktop/Resources/Artwork/placeholder-dark.png"));
        if(args.Contains("--profile")){BackgroundProfile.Run(scene,mode,root,width,height);continue;}
        if(sequence)
        {
            // 30fps, same frame sequence as the browser reference; actual source integration, no time jumps.
            for(var frame=0;frame<=240;frame++)
            {
                if(transitions&&frame==121)
                {
                    scene.Update($$"""{"mode":"{{mode}}","dark":false,"trackKey":"echo-visual-probe-1"}""",width,height);
                    if(mode.StartsWith("cover",StringComparison.Ordinal))scene.SetCover(File.ReadAllBytes("src/Echo.Desktop/Resources/Artwork/placeholder-light.png"));
                }
                if(frame==121)scene.Pointer(.72,.31,true);
                if(frame==181)scene.PointerLeave();
                var bucket=frame/3;
                var values=reactive?"["+string.Join(',',Enumerable.Range(0,32).Select(i=>((float)(.45+.3*Math.Sin(bucket*.37+i*.21))).ToString("G9",CultureInfo.InvariantCulture)))+"]":"[]";
                using var picture=scene.Draw(frame*1000d/30,frame==0?0:1000d/30,values,reactive&&frame<180,bucket);
                if(transitions?frame is 120 or 123 or 132 or 150:frame is 0 or 30 or 120 or 240)Save(picture,$"{mode}-{frame}",width,height);
            }
        }
        else
        {
            using var picture=scene.Draw(6000,16.667);
            if(!args.Contains("--compile-only"))Save(picture,mode,width,height);
        }
        Console.WriteLine("PASS "+mode+(sequence?" 0/1/4/8s + pointer press/leave":""));
    }
    catch(Exception e){failed=true;Console.WriteLine("FAIL "+mode+" "+e);}
}
Environment.ExitCode=failed?1:0;
void Save(SKPicture picture,string name,int width,int height)
{
    using var output=SKSurface.Create(new SKImageInfo(width,height));
    output.Canvas.Scale(width/picture.CullRect.Width,height/picture.CullRect.Height);output.Canvas.DrawPicture(picture);
    using var image=output.Snapshot();using var png=image.Encode(SKEncodedImageFormat.Png,100);
    File.WriteAllBytes(Path.Combine(root,name+".png"),png.ToArray());
}
