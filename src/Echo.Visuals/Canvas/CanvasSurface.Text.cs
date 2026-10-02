using System.Globalization;
using SkiaSharp;

namespace Echo.Visuals.Canvas;

public sealed partial class CanvasSurface
{
    private SKFont Font(string text)
    {
        var match=System.Text.RegularExpressions.Regex.Match(font,@"(?:(\d{3})\s+)?([\d.]+)px\s+(.+)");
        var px=match.Success?float.Parse(match.Groups[2].Value,CultureInfo.InvariantCulture):16;
        var family=match.Success?match.Groups[3].Value.Split(',')[0].Trim(' ','"','\''):"sans-serif";
        var weight=match.Success&&match.Groups[1].Success?int.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture):400;
        var style=new SKFontStyle(weight,5,SKFontStyleSlant.Upright);
        var face=SKTypeface.FromFamilyName(family,style);
        if(text.Length>0&&face.GetGlyph(text[0])==0){face.Dispose();face=SKFontManager.Default.MatchCharacter(text[0])??SKTypeface.FromFamilyName("Microsoft YaHei",style);}
        return new(face,Math.Clamp(px,1,1024)){Edging=SKFontEdging.Antialias,Subpixel=true};
    }
    internal double Measure(string text)
    {using var f=Font(text);var result=f.MeasureText(text);f.Typeface.Dispose();return result;}
    internal void Text(string text,float x,float y,bool outline)
    {
        if(text.Length>4096)throw new InvalidOperationException("Canvas text budget exceeded.");
        using var f=Font(text);var w=f.MeasureText(text);
        if(align=="center")x-=w/2;else if(align is "right" or "end")x-=w;
        var m=f.Metrics;
        y=baseline switch{"top"=>y-m.Ascent,"middle"=>y-(m.Ascent+m.Descent)/2,"bottom"=>y-m.Descent,_=>y};
        Canvas.DrawText(text,x,y,f,outline?Stroke:Fill);f.Typeface.Dispose();
    }
    internal void Image(CanvasSurface source,Jint.Native.JsValue[] a)
    {
        float N(int i)=>(float)a[i].AsNumber();
        using var paint=new SKPaint{Color=SKColors.White.WithAlpha(globalAlpha),IsAntialias=true,BlendMode=Fill.BlendMode,ImageFilter=Fill.ImageFilter,ColorFilter=Fill.ColorFilter};
        if(a.Length==5)Canvas.DrawBitmap(source.bitmap!,N(3),N(4),paint);
        else if(a.Length==7)Canvas.DrawBitmap(source.bitmap!,CanvasPool.Rect(N(3),N(4),N(5),N(6)),paint);
        else if(a.Length==11)Canvas.DrawBitmap(source.bitmap!,CanvasPool.Rect(N(3),N(4),N(5),N(6)),CanvasPool.Rect(N(7),N(8),N(9),N(10)),paint);
        else throw new NotSupportedException("Canvas drawImage overload.");
    }
}
