using SkiaSharp;

namespace Echo.Desktop.Controls.Lyrics;

// One worker-local, bounded decoded image. Slider changes reuse it; hide/switch/dispose releases it.
internal sealed class BackgroundWallpaperAsset:IDisposable
{
    public string Path{get;}
    public SKImage Image{get;}
    public BackgroundWallpaperAsset(string path)
    {
        Path=path;
        using var file=File.OpenRead(path);
        if(file.Length>32*1024*1024)throw new InvalidDataException("Wallpaper file budget exceeded.");
        using var codec=SKCodec.Create(file)??throw new InvalidDataException("Invalid wallpaper image.");
        var ratio=Math.Min(1,1600d/Math.Max(codec.Info.Width,codec.Info.Height));var size=codec.GetScaledDimensions((float)ratio);
        if((long)size.Width*size.Height>16*1024*1024)throw new InvalidDataException("Wallpaper decode budget exceeded.");
        using var decoded=new SKBitmap(new SKImageInfo(size.Width,size.Height,SKColorType.Bgra8888,SKAlphaType.Premul));
        var result=codec.GetPixels(decoded.Info,decoded.GetPixels());
        if(result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))throw new InvalidDataException("Wallpaper image decode failed.");
        var origin=codec.EncodedOrigin;
        var swapped=origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var w=swapped?size.Height:size.Width;var h=swapped?size.Width:size.Height;
        var fit=Math.Min(1,1600d/Math.Max(w,h));
        using var oriented=new SKBitmap(new SKImageInfo(Math.Max(1,(int)(w*fit)),Math.Max(1,(int)(h*fit)),SKColorType.Bgra8888,SKAlphaType.Premul));
        using(var canvas=new SKCanvas(oriented))
        using(var image=SKImage.FromBitmap(decoded))
        {
            canvas.Scale((float)fit);canvas.Concat(Orientation(origin,size.Width,size.Height));
            canvas.DrawImage(image,0,0,new SKSamplingOptions(SKFilterMode.Linear,SKMipmapMode.Nearest));
        }
        Image=SKImage.FromBitmap(oriented);
    }
    private static SKMatrix Orientation(SKEncodedOrigin origin,float w,float h)=>origin switch
    {
        SKEncodedOrigin.TopRight=>new(-1,0,w,0,1,0,0,0,1),
        SKEncodedOrigin.BottomRight=>new(-1,0,w,0,-1,h,0,0,1),
        SKEncodedOrigin.BottomLeft=>new(1,0,0,0,-1,h,0,0,1),
        SKEncodedOrigin.LeftTop=>new(0,1,0,1,0,0,0,0,1),
        SKEncodedOrigin.RightTop=>new(0,-1,h,1,0,0,0,0,1),
        SKEncodedOrigin.RightBottom=>new(0,-1,h,-1,0,w,0,0,1),
        SKEncodedOrigin.LeftBottom=>new(0,1,0,-1,0,w,0,0,1),
        _=>SKMatrix.Identity
    };
    public void Dispose()=>Image.Dispose();
}
