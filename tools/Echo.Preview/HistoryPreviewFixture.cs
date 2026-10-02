using SkiaSharp;

internal static class HistoryPreviewFixture
{
    private static readonly string[] Titles = ["星河", "Soft Landing", "海岸线", "慢慢", "Blue Hour", "Chasing Echoes", "入夜", "雨后的窗"];
    private static readonly string[] Artists = ["ECHO Ensemble", "Luna", "Sea & Stars", "Hikari"];
    private static readonly string[] Albums = ["Blue Hour", "Warm Light", "海岸线", "Echoes at Dawn"];
    private static readonly string[] Grounds = ["#223457", "#C46853", "#36655F", "#596186", "#2E4163", "#B48970", "#20394D", "#7A5974"];
    public static (string Path, string Title, string Artist, string Album) Create(string media, int index)
    {
        var folder = Path.Combine(media, "album-" + index); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "song.wav"); File.WriteAllBytes(path, new byte[44]);
        using var surface = SKSurface.Create(new SKImageInfo(160,160)); var canvas = surface.Canvas;
        canvas.Clear(SKColor.Parse(Grounds[index]));
        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(244,231,214,190) };
        canvas.DrawCircle(95,52,28,paint);
        for(var line=0;line<6;line++)
        {
            using var curve = new SKPath(); curve.MoveTo(-10,100+line*13);
            curve.CubicTo(35,60+line*14,95,140+line*10,180,77+line*13);
            paint.Style=SKPaintStyle.Stroke; paint.StrokeWidth=1.5f; paint.Color=new SKColor(244,231,214,(byte)(110-line*10)); canvas.DrawPath(curve,paint);
        }
        using var image=surface.Snapshot(); using var bytes=image.Encode(SKEncodedImageFormat.Png,100);
        using(var file=File.Create(Path.Combine(folder,"cover.png"))) bytes.SaveTo(file);
        return (path,(index%2==0 ? "夜航 · " : "")+Titles[index],Artists[index%4],Albums[index%4]);
    }
}
