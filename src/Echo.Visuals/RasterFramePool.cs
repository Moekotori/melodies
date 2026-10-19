using SkiaSharp;

namespace Echo.Visuals;

// One scene worker writes; native image release callbacks return slots from any thread.
// At most three 4 MiB buffers, allocated on demand. A retained image is never overwritten.
public sealed class RasterFramePool : IDisposable
{
    private readonly object gate = new();
    private readonly List<Slot> slots = [];
    private bool disposed;

    public RasterImageFrame? Capture(SKBitmap source)
    {
        if ((long)source.RowBytes * source.Height > 4 * 1024 * 1024)
            throw new InvalidDataException("Scene frame pixel budget exceeded.");
        lock (gate)
        {
            if (disposed) return null;
            var slot = slots.FirstOrDefault(value => !value.Busy);
            if (slot is null)
            {
                if (slots.Count == 3) return null;
                slots.Add(slot = new Slot(this));
            }
            if (slot.Bitmap is not { } bitmap || bitmap.Info != source.Info || bitmap.RowBytes != source.RowBytes)
            {
                slot.Bitmap?.Dispose();
                slot.Bitmap = new SKBitmap(source.Info, source.RowBytes);
            }
            var target = slot.Bitmap ?? throw new InvalidOperationException("Unable to allocate scene frame.");
            source.GetPixelSpan().CopyTo(target.GetPixelSpan());
            slot.Busy = true;
            try
            {
                using var pixels = target.PeekPixels();
                var image = SKImage.FromPixels(pixels, static (_, context) =>
                {
                    var returned = (Slot)context;
                    returned.Owner.Release(returned);
                }, slot) ?? throw new InvalidOperationException("Unable to capture scene frame.");
                return new RasterImageFrame(image);
            }
            catch { Release(slot); throw; }
        }
    }

    private void Release(Slot slot)
    {
        lock (gate)
        {
            slot.Busy = false;
            if (disposed) { slot.Bitmap?.Dispose(); slot.Bitmap = null; }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            foreach (var slot in slots)
                if (!slot.Busy) { slot.Bitmap?.Dispose(); slot.Bitmap = null; }
            slots.Clear();
        }
    }

    private sealed class Slot(RasterFramePool owner)
    {
        public RasterFramePool Owner { get; } = owner;
        public SKBitmap? Bitmap;
        public bool Busy;
    }
}

// Control, pending delivery and render operations each hold an explicit reference.
public sealed class RasterImageFrame(SKImage image) : IDisposable
{
    private int references = 1;
    public SKImage Image { get; } = image;
    public int Width => Image.Width;
    public int Height => Image.Height;
    public RasterImageFrame Retain() { Interlocked.Increment(ref references); return this; }
    public void Dispose() { if (Interlocked.Decrement(ref references) == 0) Image.Dispose(); }
}
