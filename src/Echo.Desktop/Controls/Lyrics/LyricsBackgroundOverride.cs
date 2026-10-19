using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Echo.Desktop.Features.Lyrics;

namespace Echo.Desktop.Controls.Lyrics;

// The original source override replaces decoration, then applies one shared readability veil.
internal sealed class LyricsBackgroundOverride:IDisposable
{
    private readonly LyricsCoverWash cover=new();
    private IBrush ground=Brushes.Black;
    private IBrush horizontal=Brushes.Transparent,vertical=Brushes.Transparent;
    private IReadOnlyList<IBrush> theme=[];
    public void Configure(bool dark)
    {
        ground=new SolidColorBrush(Color.Parse(dark?"#0C1016":"#F7F8FA"));
        horizontal=Gradient(false,dark?[(0,.34,"#05080E"),(.46,0,"#05080E"),(1,.12,"#05080E")]:[(0,.2,"#0C121E"),(.48,0,"#0C121E"),(1,.06,"#FFFFFF")]);
        vertical=Gradient(true,dark?[(0,.08,"#04070C"),(.58,0,"#04070C"),(1,.32,"#04070C")]:[(0,.03,"#FFFFFF"),(.6,0,"#FFFFFF"),(1,.18,"#0C121E")]);
        theme=dark?[]:[new RadialGradientBrush {Center=new(.12,.18,RelativeUnit.Relative),GradientOrigin=new(.12,.18,RelativeUnit.Relative),RadiusX=new(.7,RelativeUnit.Relative),RadiusY=new(.65,RelativeUnit.Relative),GradientStops=[new(Color.FromArgb(41,75,85,232),0),new(Colors.Transparent,.65)]},
            new RadialGradientBrush{Center=new(.88,.82,RelativeUnit.Relative),GradientOrigin=new(.88,.82,RelativeUnit.Relative),RadiusX=new(.7,RelativeUnit.Relative),RadiusY=new(.7,RelativeUnit.Relative),GradientStops=[new(Color.FromArgb(41,125,138,158),0),new(Colors.Transparent,.7)]}];
    }
    public void Draw(DrawingContext dc,Rect bounds,LyricsAppearance appearance,Bitmap? image)
    {
        var mode=appearance.BackgroundMode;
        if(mode is "theme" or "cover")
        {
            dc.DrawRectangle(ground,null,bounds);
            if(mode=="theme")foreach(var brush in theme)dc.DrawRectangle(brush,null,bounds);
            if(mode=="cover"&&image is not null)
            {
                var extra=appearance.BackgroundBlur>=12?.06:0;var dest=bounds.Inflate(new Thickness(bounds.Width*extra,bounds.Height*extra));
                cover.Prepare(image,dest.Width,dest.Height,appearance.BackgroundBlur,appearance.BackgroundBrightness/100,1.02);
                if(cover.Image is {} wash)using(dc.PushOpacity(appearance.BackgroundOpacity/100))dc.DrawImage(wash,new Rect(wash.Size),dest);
            }
        }
        dc.DrawRectangle(horizontal,null,bounds);dc.DrawRectangle(vertical,null,bounds);
    }
    private static IBrush Gradient(bool vertical,(double Offset,double Alpha,string Color)[] stops)
    {
        var brush=new LinearGradientBrush{StartPoint=new(0,0,RelativeUnit.Relative),EndPoint=new(vertical?0:1,vertical?1:0,RelativeUnit.Relative)};
        foreach(var stop in stops){var color=Color.Parse(stop.Color);brush.GradientStops.Add(new(Color.FromArgb((byte)Math.Round(stop.Alpha*255),color.R,color.G,color.B),stop.Offset));}
        return brush;
    }
    public void Dispose()=>cover.Dispose();
}
