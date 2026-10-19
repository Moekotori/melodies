using Avalonia.Media.Imaging;

namespace Echo.Desktop.Theming;

// The display and the single grading worker share immutable source pixels without a permanent Skia copy.
internal sealed class WallpaperStillImage(Bitmap image) : IDisposable
{
    private int references = 1;
    public Bitmap Image { get; } = image;
    public WallpaperStillImage Retain() { Interlocked.Increment(ref references); return this; }
    public void Dispose() { if (Interlocked.Decrement(ref references) == 0) Image.Dispose(); }
}
