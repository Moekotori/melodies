using SkiaSharp;

namespace Echo.Desktop.Controls.Lyrics;

// Read-only display asset. Decode/grade once on the scene worker; no watcher, production database or media mutation.
internal sealed class BackgroundWallpaperScene:IDisposable
{
    private readonly SKPicture picture;
    public BackgroundWallpaperScene(BackgroundSceneProject project,int width,int height,BackgroundWallpaperAsset? asset)
    {
        var scale=Math.Min(1,Math.Sqrt(1280d*720/(width*(double)height)));
        width=Math.Max(1,(int)(width*scale));height=Math.Max(1,(int)(height*scale));
        using var recorder=new SKPictureRecorder();var canvas=recorder.BeginRecording(SKRect.Create(width,height));
        canvas.Clear(SKColor.Parse(project.Dark?"#0C1016":"#F7F8FA"));
        if(project.WallpaperPath.Length>0)
        {
            var image=asset!.Image;
            var brightness=(float)(project.Brightness/100);
            using var grading=SKColorFilter.CreateColorMatrix([brightness,0,0,0,0,0,brightness,0,0,0,0,0,brightness,0,0,0,0,0,1,0]);
            using var blur=SKImageFilter.CreateBlur((float)(project.Blur*scale),(float)(project.Blur*scale));
            using var paint=new SKPaint{Color=SKColors.White.WithAlpha((byte)Math.Round(project.Opacity*2.55)),ColorFilter=grading,ImageFilter=blur};
            var bleed=project.Blur>0?(float)(42*scale):0f;var target=SKRect.Create(-bleed,-bleed,width+2*bleed,height+2*bleed);
            var fit=Math.Max(target.Width/image.Width,target.Height/image.Height);
            var source=SKRect.Create((image.Width-target.Width/fit)/2,(image.Height-target.Height/fit)/2,target.Width/fit,target.Height/fit);
            canvas.DrawImage(image,source,target,new SKSamplingOptions(SKFilterMode.Linear),paint);
        }
        picture=recorder.EndRecording();
    }
    public SKPicture Draw()
    {using var recorder=new SKPictureRecorder();recorder.BeginRecording(picture.CullRect).DrawPicture(picture);return recorder.EndRecording();}
    public void Dispose()=>picture.Dispose();
}
