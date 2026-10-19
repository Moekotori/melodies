using System.Globalization;
using System.Text.RegularExpressions;
using Jint.Native;
using SkiaSharp;

namespace Echo.Visuals.Canvas;

public sealed partial class CanvasSurface
{
    private string? effectsKey;
    private SKImageFilter? imageFilter;
    private SKColorFilter? colorFilter;
    private void Effects(JsValue[] a)
    {
        var css=a[12].AsString();var shadow=a[13].AsString();
        var blur=(float)a[14].AsNumber();var dx=(float)a[15].AsNumber();var dy=(float)a[16].AsNumber();
        var key=FormattableString.Invariant($"{css}|{shadow}|{blur}|{dx}|{dy}");
        if(effectsKey==key)return;effectsKey=key;
        Fill.ImageFilter=Stroke.ImageFilter=null;Fill.ColorFilter=Stroke.ColorFilter=null;
        imageFilter?.Dispose();colorFilter?.Dispose();imageFilter=null;colorFilter=null;
        var color=Color(shadow);
        if(color.Alpha>0&&(blur>0||dx!=0||dy!=0))imageFilter=SKImageFilter.CreateDropShadow(dx,dy,blur/2,blur/2,color);
        foreach(Match match in FilterFunctions().Matches(css))
        {
            var amount=float.Parse(match.Groups[2].Value,CultureInfo.InvariantCulture);var name=match.Groups[1].Value;
            if(match.Groups[3].Value=="%")amount/=100;
            if(name=="blur")
            {
                var next=SKImageFilter.CreateBlur(Math.Clamp(amount/2,0,128),Math.Clamp(amount/2,0,128),imageFilter);
                imageFilter?.Dispose();imageFilter=next;
            }
            else if(name is "brightness" or "contrast")
            {
                amount=Math.Max(0,amount);
                var offset=name=="contrast"?.5f*(1-amount):0;
                var next=SKColorFilter.CreateColorMatrix([amount,0,0,0,offset,0,amount,0,0,offset,0,0,amount,0,offset,0,0,0,1,0]);
                Compose(next);
            }
            else if(name is "invert" or "grayscale")
            {
                amount=Math.Clamp(amount,0,1);
                var matrix=name=="invert"?new float[]{1-2*amount,0,0,0,amount,0,1-2*amount,0,0,amount,0,0,1-2*amount,0,amount,0,0,0,1,0}
                    :new float[]{1-.7874f*amount,.7152f*amount,.0722f*amount,0,0,.2126f*amount,1-.2848f*amount,.0722f*amount,0,0,.2126f*amount,.7152f*amount,1-.9278f*amount,0,0,0,0,0,1,0};
                Compose(SKColorFilter.CreateColorMatrix(matrix));
            }
        }
        Fill.ImageFilter=Stroke.ImageFilter=imageFilter;Fill.ColorFilter=Stroke.ColorFilter=colorFilter;
    }
    private void Compose(SKColorFilter next)
    {
        if(colorFilter is null){colorFilter=next;return;}
        var combined=SKColorFilter.CreateCompose(next,colorFilter);
        next.Dispose();colorFilter.Dispose();colorFilter=combined;
    }
    [GeneratedRegex(@"([a-z-]+)\(([\d.]+)(%|px)?\)")]private static partial Regex FilterFunctions();
}
