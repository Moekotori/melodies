using Echo.Desktop.Bootstrap;
using Echo.Infrastructure.Media;
using SkiaSharp;

namespace Echo.Desktop.Features.Settings;

internal static class WallpaperImportValidation
{
    public static async Task ValidateAsync(string path, CancellationToken token)
    {
        if (WallpaperMediaFile.IsVideo(path))
        {
            var decoder = new AppearanceMediaDecoder(AppearanceMediaPath.Resolve(), AppearanceMediaPath.AnalysisHost());
            await decoder.ProbeVideoAsync(path, token).ConfigureAwait(false);
            return;
        }
        token.ThrowIfCancellationRequested();
        using var file = File.OpenRead(path);
        if (file.Length > 16 * 1024 * 1024) throw new InvalidDataException("背景图片最大 16 MiB");
        using var codec = SKCodec.Create(file);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 || codec.Info.Width > 8192 || codec.Info.Height > 8192
            || (long)codec.Info.Width * codec.Info.Height > 16 * 1024 * 1024)
            throw new InvalidDataException("图片无法读取或尺寸过大，请选择不超过 1600 万像素的图片");
    }
}
