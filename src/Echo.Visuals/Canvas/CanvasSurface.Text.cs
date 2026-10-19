using System.Globalization;
using SkiaSharp;

namespace Echo.Visuals.Canvas;

public sealed partial class CanvasSurface
{
    private SKFont Font(string text) => fonts.Get(font, text);
    internal string MeasureMetrics(string text)
    {
        var font = Font(text);
        var width = font.MeasureText(text, out var bounds);
        return FormattableString.Invariant($"{{\"width\":{width},\"actualBoundingBoxLeft\":{-bounds.Left},\"actualBoundingBoxRight\":{bounds.Right},\"actualBoundingBoxAscent\":{-bounds.Top},\"actualBoundingBoxDescent\":{bounds.Bottom}}}");
    }
    internal double Measure(string text)
    {var f=Font(text);var result=f.MeasureText(text);return result;}
    internal void Text(string text,float x,float y,bool outline)
    {
        if(text.Length>4096)throw new InvalidOperationException("Canvas text budget exceeded.");
        var f=Font(text);var w=f.MeasureText(text);
        if(align=="center")x-=w/2;else if(align is "right" or "end")x-=w;
        var m=f.Metrics;
        y=baseline switch{"top"=>y-m.Ascent,"middle"=>y-(m.Ascent+m.Descent)/2,"bottom"=>y-m.Descent,_=>y};
        Canvas.DrawText(text,x,y,f,outline?Stroke:Fill);
    }
    internal void Image(CanvasSurface source,Jint.Native.JsValue[] a)
    {
        float N(int i)=>(float)a[i].AsNumber();
        using var paint=new SKPaint{Color=SKColors.White.WithAlpha(globalAlpha),IsAntialias=true,BlendMode=Fill.BlendMode,ImageFilter=Fill.ImageFilter,ColorFilter=Fill.ColorFilter};
        if (source.pictureMode)
        {
            var sourcePicture = source.Picture();
            var sourceRect = a.Length == 11 ? CanvasPool.Rect(N(3), N(4), N(5), N(6)) : SKRect.Create(source.Width, source.Height);
            var target = a.Length switch
            {
                5 => SKRect.Create(N(3), N(4), source.Width, source.Height),
                7 => CanvasPool.Rect(N(3), N(4), N(5), N(6)),
                11 => CanvasPool.Rect(N(7), N(8), N(9), N(10)),
                _ => throw new NotSupportedException("Canvas drawImage overload.")
            };
            if (Fill.BlendMode == SKBlendMode.Src) Clear(target.Left, target.Top, target.Width, target.Height);
            Canvas.Save(); Canvas.ClipRect(target);
            Canvas.Translate(target.Left, target.Top); Canvas.Scale(target.Width / sourceRect.Width, target.Height / sourceRect.Height);
            Canvas.Translate(-sourceRect.Left, -sourceRect.Top); Canvas.DrawPicture(sourcePicture, paint); Canvas.Restore();
        }
        else
        {
            using var image=SKImage.FromBitmap(source.bitmap!);
            var sampling=smoothImages?new SKSamplingOptions(SKFilterMode.Linear,SKMipmapMode.Nearest):new SKSamplingOptions(SKFilterMode.Nearest);
            if(a.Length==5)Canvas.DrawImage(image,N(3),N(4),sampling,paint);
            else if(a.Length==7)Canvas.DrawImage(image,CanvasPool.Rect(N(3),N(4),N(5),N(6)),sampling,paint);
            else if(a.Length==11)Canvas.DrawImage(image,CanvasPool.Rect(N(3),N(4),N(5),N(6)),CanvasPool.Rect(N(7),N(8),N(9),N(10)),sampling,paint);
            else throw new NotSupportedException("Canvas drawImage overload.");
        }
    }
}
