using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Features.Albums;

namespace Echo.Desktop.Controls.Lyrics;

internal readonly record struct BackgroundSceneKey(string Mode,bool Dark,string TrackKey,Bitmap? Cover,ArtworkColorSample? Sample,
    int Width,int Height,string WallpaperPath,double Blur,double Brightness,double Opacity);
internal sealed record BackgroundRgb(byte R,byte G,byte B);
internal sealed record BackgroundSample(BackgroundRgb AverageRgb,BackgroundRgb DominantRgb);
internal sealed record BackgroundSceneProject(string Mode,bool Dark,string TrackKey,BackgroundSample? Sample,string WallpaperPath,double Blur,double Brightness,double Opacity)
{
    [JsonIgnore] public byte[]? Cover {get;init;}
}
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BackgroundSceneProject))]
[JsonSerializable(typeof(float[]))]
internal partial class BackgroundSceneJson:JsonSerializerContext;
internal static class BackgroundSceneProjectBuilder
{
    public static BackgroundSceneProject Create(LyricsViewModel model,bool dark,byte[]? cover)
    {
        var sample=model.CoverColors;
        var colors=sample is null?null:new BackgroundSample(new(sample.Average.R,sample.Average.G,sample.Average.B),new(sample.Dominant.R,sample.Dominant.G,sample.Dominant.B));
        return new(model.Appearance.BackgroundMode,dark,model.Player.CurrentFilePath,colors,model.Appearance.WallpaperPath,model.Appearance.BackgroundBlur,model.Appearance.BackgroundBrightness,model.Appearance.BackgroundOpacity){Cover=cover};
    }
}
