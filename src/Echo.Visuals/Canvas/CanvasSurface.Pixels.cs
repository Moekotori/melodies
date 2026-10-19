using System.Runtime.InteropServices;
using Jint.Native;
using Jint.Native.Object;
using SkiaSharp;

namespace Echo.Visuals.Canvas;

public sealed partial class CanvasSurface
{
    internal byte Alpha(int x,int y)=>bitmap!.GetPixel(x,y).Alpha;
    internal void Alpha(int x,int y,byte alpha)=>bitmap!.SetPixel(x,y,new(255,255,255,alpha));
    internal RasterFrame Snapshot()
    {
        Canvas.Flush();var bytes=new byte[checked(bitmap!.RowBytes*Height)];
        Marshal.Copy(bitmap.GetPixels(),bytes,0,bytes.Length);
        return new(bytes,Width,Height,bitmap.RowBytes);
    }
    internal RasterImageFrame? Snapshot(RasterFramePool pool)
    {
        Canvas.Flush();
        return pool.Capture(bitmap!);
    }
    internal void WritePixelBytes(string base64, int width, int height, int x, int y)
    {
        if (width < 1 || height < 1 || (long)width * height > 512 * 512)
            throw new InvalidDataException("Source scene pixel-write budget exceeded.");
        var bytes = Convert.FromBase64String(base64);
        if (bytes.Length != width * height * 4) throw new InvalidDataException("Source scene pixel count mismatch.");
        using var image = SKImage.FromPixelCopy(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul), bytes);
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
        Canvas.DrawImage(image, x, y, paint);
    }
    internal void Grain()
    {
        for(var y=0;y<Height;y++)for(var x=0;x<Width;x++)
        {var v=(byte)Random.Shared.Next(255);bitmap!.SetPixel(x,y,new(v,v,v,255));}
    }
    internal void PaperNoise()
    {
        for(var y=0;y<Height;y++)for(var x=0;x<Width;x++)
        {
            var c=bitmap!.GetPixel(x,y);var n=(Random.Shared.NextDouble()-.5)*22;
            byte Channel(byte value)=>(byte)Math.Clamp(Math.Round(value+n,MidpointRounding.ToEven),0,255);
            bitmap.SetPixel(x,y,new(Channel(c.Red),Channel(c.Green),Channel(c.Blue),c.Alpha));
        }
    }
    internal string ReadPixels(int x,int y,int width,int height)
    {
        MaterializePixels();
        if(width<0||height<0||(long)width*height>4*1024*1024)throw new InvalidOperationException("Canvas pixel read budget exceeded.");
        var data=new byte[checked(width*height*4)];
        for(var row=0;row<height;row++)for(var col=0;col<width;col++)
        {
            if(x+col<0||y+row<0||x+col>=Width||y+row>=Height)continue;
            var c=bitmap!.GetPixel(x+col,y+row);var offset=(row*width+col)*4;
            data[offset]=c.Red;data[offset+1]=c.Green;data[offset+2]=c.Blue;data[offset+3]=c.Alpha;
        }
        return Convert.ToBase64String(data);
    }
    internal void WritePixels(ObjectInstance values,int width,int height,int x,int y)
    {
        if((long)width*height>4*1024*1024)throw new InvalidOperationException("Canvas pixel write budget exceeded.");
        var length=checked(width*height*4);
        if(values.Get("length").AsNumber()!=length)throw new InvalidDataException("Canvas pixel buffer length.");
        byte r=0,g=0,b=0;
        for(var index=0;index<length;index++)
        {
            var value=(byte)Math.Clamp(values.Get((JsValue)index).AsNumber(),0,255);
            switch(index%4){case 0:r=value;break;case 1:g=value;break;case 2:b=value;break;case 3:
                var n=index/4;var px=x+n%width;var py=y+n/width;
                if(px>=0&&py>=0&&px<Width&&py<Height)bitmap!.SetPixel(px,py,new(r,g,b,value));break;}
        }
    }
    internal SKShader Pattern(string repeat)
        => pictureMode ? Picture().ToShader(repeat is "repeat" or "repeat-x" ? SKShaderTileMode.Repeat : SKShaderTileMode.Decal,
            repeat is "repeat" or "repeat-y" ? SKShaderTileMode.Repeat : SKShaderTileMode.Decal) : SKShader.CreateBitmap(bitmap!,repeat is "repeat" or "repeat-x"?SKShaderTileMode.Repeat:SKShaderTileMode.Decal,
            repeat is "repeat" or "repeat-y"?SKShaderTileMode.Repeat:SKShaderTileMode.Decal);
    internal string Transform()
    {
        var m=Canvas.TotalMatrix;
        return FormattableString.Invariant($"{{\"a\":{m.ScaleX},\"b\":{m.SkewY},\"c\":{m.SkewX},\"d\":{m.ScaleY},\"e\":{m.TransX},\"f\":{m.TransY}}}");
    }
}
