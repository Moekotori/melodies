using System.Globalization;
using System.Text.RegularExpressions;
using Jint.Native;
using SkiaSharp;

namespace Echo.Visuals.Canvas;

public sealed partial class CanvasSurface : IDisposable
{
    private readonly CanvasFontCache fonts;
    internal CanvasSurface(CanvasFontCache fonts) => this.fonts = fonts;
    private SKBitmap? bitmap;
    private SKCanvas? canvas;
    private SKPath path = new();
    internal SKPaint Fill {get;} = new(){IsAntialias=true,Style=SKPaintStyle.Fill};
    internal SKPaint Stroke {get;} = new(){IsAntialias=true,Style=SKPaintStyle.Stroke};
    internal SKCanvas Canvas=>canvas!;
    internal SKPath Path=>path;
    internal int Width=>bitmap?.Width??0;
    internal int Height=>bitmap?.Height??0;
    private readonly Stack<(SKPaint Fill,SKPaint Stroke,SKMatrix Matrix)> states=[];
    private string font = "16px sans-serif", align="start", baseline="alphabetic";
    private byte globalAlpha=255;
    private bool smoothImages=true;
    internal void Resize(int width,int height)
    {
        if(Width==width&&Height==height)return;
        var recorded = pictureMode; ReleaseRecording();
        canvas?.Dispose();bitmap?.Dispose();
        bitmap=new(width,height,SKColorType.Bgra8888,SKAlphaType.Premul);canvas=new(bitmap);canvas.Clear(SKColors.Transparent);
        while(states.Count>0){var s=states.Pop();s.Fill.Dispose();s.Stroke.Dispose();}
        if (recorded) EnablePicture();
    }
    internal void BeginPath(){path.Dispose();path=new();}
    internal void Save(){Canvas.Save();states.Push((Fill.Clone(),Stroke.Clone(),Canvas.TotalMatrix));}
    internal void Restore(){if(states.Count==0)return;Canvas.Restore();var s=states.Pop();Copy(s.Fill,Fill);Copy(s.Stroke,Stroke);s.Fill.Dispose();s.Stroke.Dispose();effectsKey=null;}
    private static void Copy(SKPaint from,SKPaint to){to.Color=from.Color;to.Shader=from.Shader;to.BlendMode=from.BlendMode;to.StrokeWidth=from.StrokeWidth;to.PathEffect=from.PathEffect;to.ImageFilter=from.ImageFilter;to.ColorFilter=from.ColorFilter;to.StrokeCap=from.StrokeCap;to.StrokeJoin=from.StrokeJoin;}
    internal void Configure(JsValue[] a,IReadOnlyDictionary<int,CanvasGradient> gradients)
    {
        var alpha=globalAlpha=(byte)Math.Clamp(a[4].AsNumber()*255,0,255);
        PaintStyle(Fill,a[2].AsString(),alpha,gradients);PaintStyle(Stroke,a[3].AsString(),alpha,gradients);
        Stroke.StrokeWidth=(float)a[5].AsNumber();
        font=a[6].AsString();align=a[7].AsString();baseline=a[8].AsString();
        var blend=a[9].AsString() switch {"lighter"=>SKBlendMode.Plus,"screen"=>SKBlendMode.Screen,"overlay"=>SKBlendMode.Overlay,"multiply"=>SKBlendMode.Multiply,"destination-in"=>SKBlendMode.DstIn,"destination-out"=>SKBlendMode.DstOut,"source-in"=>SKBlendMode.SrcIn,"copy"=>SKBlendMode.Src,_=>SKBlendMode.SrcOver};
        Fill.BlendMode=Stroke.BlendMode=blend;
        Stroke.StrokeCap=a[10].AsString() switch{"round"=>SKStrokeCap.Round,"square"=>SKStrokeCap.Square,_=>SKStrokeCap.Butt};
        Stroke.StrokeJoin=a[11].AsString() switch{"round"=>SKStrokeJoin.Round,"bevel"=>SKStrokeJoin.Bevel,_=>SKStrokeJoin.Miter};
        smoothImages=a.Length<=17||a[17].AsBoolean();
        Effects(a);
    }
    private static void PaintStyle(SKPaint paint,string style,byte alpha,IReadOnlyDictionary<int,CanvasGradient> gradients)
    {
        if(style.StartsWith('@')&&int.TryParse(style.AsSpan(1),out var id)&&gradients.TryGetValue(id,out var gradient)){paint.Shader=gradient.Shader();paint.Color=SKColors.White.WithAlpha(alpha);}
        else {paint.Shader=null;var c=Color(style);paint.Color=c.WithAlpha((byte)(c.Alpha*alpha/255));}
    }
    internal static SKColor Color(string value)
    {
        if (value.Length == 9 && value[0] == '#' && uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgba))
            return new((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
        if(SKColor.TryParse(value,out var result))return result;
        if(value.StartsWith("rgb",StringComparison.Ordinal))
        {
            var numbers=Numbers().Matches(value).Select(m=>float.Parse(m.Value,CultureInfo.InvariantCulture)).ToArray();
            if(numbers.Length>=3)return new((byte)Math.Clamp(numbers[0],0,255),(byte)Math.Clamp(numbers[1],0,255),(byte)Math.Clamp(numbers[2],0,255),numbers.Length>3?(byte)Math.Clamp(numbers[3]*255,0,255):(byte)255);
        }
        if(value.StartsWith("hsl",StringComparison.Ordinal))
        {
            var n=Numbers().Matches(value).Select(m=>float.Parse(m.Value,CultureInfo.InvariantCulture)).ToArray();
            if(n.Length>=3)return SKColor.FromHsl(n[0],n[1],n[2],n.Length>3?(byte)Math.Clamp(n[3]*255,0,255):(byte)255);
        }
        return value switch{"white"=>SKColors.White,"black"=>SKColors.Black,"transparent"=>SKColors.Transparent,_=>SKColors.White};
    }
    internal void Arc(double x,double y,double rx,double ry,double rotation,double start,double end,bool reverse)
    {
        var delta=end-start; if(!reverse){while(delta<0)delta+=Math.PI*2;delta=Math.Min(delta,Math.PI*2);}else{while(delta>0)delta-=Math.PI*2;delta=Math.Max(delta,-Math.PI*2);}
        // Small bounded segments preserve Canvas's rotation and arbitrary ellipse aspect.
        var count=Math.Clamp((int)Math.Ceiling(Math.Abs(delta)*24),1,152);
        for(var i=0;i<=count;i++){var a=start+delta*i/count;var px=rx*Math.Cos(a);var py=ry*Math.Sin(a);var p=new SKPoint((float)(x+px*Math.Cos(rotation)-py*Math.Sin(rotation)),(float)(y+px*Math.Sin(rotation)+py*Math.Cos(rotation)));if(i==0&&Path.IsEmpty)Path.MoveTo(p);else Path.LineTo(p);}
    }
    internal void Dash(string value)
    { var intervals=value.Split(',',StringSplitOptions.RemoveEmptyEntries).Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();Stroke.PathEffect=intervals.Length>0?SKPathEffect.CreateDash(intervals,0):null; }
    public byte[] Encode()
    {Canvas.Flush();using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Png,100);return data.ToArray();}
    public void Dispose(){ReleaseRecording();canvas?.Dispose();bitmap?.Dispose();path.Dispose();Fill.Dispose();Stroke.Dispose();imageFilter?.Dispose();colorFilter?.Dispose();while(states.Count>0){var s=states.Pop();s.Fill.Dispose();s.Stroke.Dispose();}}
    [GeneratedRegex(@"[-+]?(?:\d*\.)?\d+")] private static partial Regex Numbers();
}
