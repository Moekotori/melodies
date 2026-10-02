using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Echo.Desktop.Features.Taskbar;
using SkiaSharp;

internal static class TaskbarVisualCapture
{
    // Product pixels and glyphs inside an illustrative shell frame; not an Explorer screenshot.
    public static void Save(string output)
    {
        using var artwork = new Bitmap(AssetLoader.Open(new Uri("avares://Echo.Desktop/Resources/Artwork/placeholder-light.png")));
        using var result = new SKBitmap(700, 450);
        using var canvas = new SKCanvas(result); canvas.Clear(SKColor.Parse("#D9D5E3"));
        using var typeface = SKTypeface.FromFamilyName("Microsoft YaHei");
        using var font = new SKFont(typeface, 17);
        using var paint = new SKPaint { IsAntialias = true };
        for (var index = 0; index < 2; index++)
        {
            var dark = index == 1; var x = index * 330 + 30;
            paint.Color = SKColor.Parse(dark ? "#25242C" : "#F5F4F7");
            canvas.DrawRoundRect(SKRect.Create(x, 28, 310, 364), 14, 14, paint);
            paint.Color = dark ? SKColors.White : SKColor.Parse("#50505D");
            canvas.DrawText("老人と海 - ヨルシカ", x + 24, 63, font, paint);
            using var cover = Decode(TaskbarArtworkRenderer.Cover(artwork, dark, false, "老人と海", "ヨルシカ"), 256);
            canvas.DrawBitmap(cover, x + 27, 76);
            var icons = TaskbarArtworkRenderer.Icons(dark); var order = new[] { 0, 1, 3, 4, 6 };
            for (var button = 0; button < 5; button++)
            {
                paint.Color = SKColor.Parse(dark ? "#38363F" : "#FFFFFF");
                canvas.DrawRoundRect(SKRect.Create(x + 32 + button * 50, 340, 42, 40), 5, 5, paint);
                using var icon = Decode(icons[order[button]], 32);
                canvas.DrawBitmap(icon, x + 37 + button * 50, 344);
            }
        }
        paint.Color = SKColor.Parse("#5A5566"); font.Size = 13;
        canvas.DrawText("产品封面与按钮资源预览 · Windows 系统外框由任务栏绘制", 30, 430, font, paint);
        using var data = result.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(Path.Combine(output, "taskbar-preview.png")); data.SaveTo(file);
        using var fallback = Decode(TaskbarArtworkRenderer.Cover(null, false, true, "暂无播放", "打开歌曲开始播放"), 256);
        using var fallbackData = fallback.Encode(SKEncodedImageFormat.Png, 100);
        using var fallbackFile = File.Create(Path.Combine(output, "taskbar-empty.png")); fallbackData.SaveTo(fallbackFile);
    }
    private static SKBitmap Decode(byte[] bytes, int size)
    {
        var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
        Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length); return bitmap;
    }
}
