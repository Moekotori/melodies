namespace Echo.Infrastructure.Media;

public static class WallpaperMediaFile
{
    public static bool IsVideo(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".mp4" or ".webm" or ".mkv" or ".mov" or ".m4v" or ".avi" or ".wmv" or ".gif";
    public static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".tif" or ".tiff";
}
