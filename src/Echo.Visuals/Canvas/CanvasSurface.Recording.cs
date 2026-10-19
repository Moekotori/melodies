using SkiaSharp;

namespace Echo.Visuals.Canvas;

public sealed partial class CanvasSurface
{
    private SKPictureRecorder? recorder;
    private SKCanvas? rasterCanvas;
    private SKPicture? recording;
    private bool pictureMode, pictureDirty;
    internal void EnablePicture()
    {
        if (pictureMode) return;
        pictureMode = true; rasterCanvas = canvas;
        recorder = new(); canvas = recorder.BeginRecording(SKRect.Create(Width, Height)); canvas.ClipRect(SKRect.Create(Width, Height));
        pictureDirty = true;
    }
    internal void TouchPicture() { if (pictureMode) pictureDirty = true; }
    internal void Clear(float x, float y, float width, float height)
    {
        if (pictureMode && Canvas.IsClipRect && Canvas.DeviceClipBounds == SKRectI.Create(Width, Height)
            && Canvas.TotalMatrix.MapRect(SKRect.Create(x, y, width, height)).Contains(SKRect.Create(Width, Height)))
        {
            var matrix = Canvas.TotalMatrix;
            recorder!.EndRecording().Dispose(); recorder.Dispose(); recording?.Dispose(); recording = null;
            recorder = new(); canvas = recorder.BeginRecording(SKRect.Create(Width, Height)); canvas.ClipRect(SKRect.Create(Width, Height));
            foreach (var state in states.Reverse()) { canvas.SetMatrix(state.Matrix); canvas.Save(); }
            canvas.SetMatrix(matrix); pictureDirty = true;
        }
        else
        {
            using var paint = new SKPaint { BlendMode = SKBlendMode.Clear };
            Canvas.DrawRect(SKRect.Create(x, y, width, height), paint); TouchPicture();
        }
    }
    internal void FillRectangle(float x, float y, float width, float height)
    {
        if (pictureMode && Fill.BlendMode == SKBlendMode.Src) Clear(x, y, width, height);
        Canvas.DrawRect(SKRect.Create(x, y, width, height), Fill);
    }
    private SKPicture Picture()
    {
        if (!pictureMode) throw new InvalidOperationException("Surface is not recorded.");
        if (!pictureDirty && recording is not null) return recording;
        var matrix = Canvas.TotalMatrix;
        var next = recorder!.EndRecording(); recorder.Dispose(); recording?.Dispose(); recording = next;
        recorder = new(); canvas = recorder.BeginRecording(SKRect.Create(Width, Height)); canvas.ClipRect(SKRect.Create(Width, Height)); canvas.DrawPicture(next);
        foreach (var state in states.Reverse()) { canvas.SetMatrix(state.Matrix); canvas.Save(); }
        canvas.SetMatrix(matrix); pictureDirty = false;
        return next;
    }
    internal SKPicture CapturePicture()
    {
        using var copy = new SKPictureRecorder(); var output = copy.BeginRecording(SKRect.Create(Width, Height));
        output.DrawPicture(Picture()); return copy.EndRecording();
    }
    private void ReleaseRecording()
    {
        if (recorder is not null) { recorder.Dispose(); recorder = null; canvas = rasterCanvas; rasterCanvas = null; }
        recording?.Dispose(); recording = null; pictureMode = false; pictureDirty = false;
    }
    private void MaterializePixels()
    {
        if (!pictureMode) return;
        rasterCanvas!.Clear(SKColors.Transparent); rasterCanvas.DrawPicture(Picture()); rasterCanvas.Flush();
    }
    internal SKImage SnapshotImage()
    {
        if (pictureMode)
        {
            using var surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            surface.Canvas.DrawPicture(Picture()); return surface.Snapshot();
        }
        Canvas.Flush(); using var copy = bitmap!.Copy(); return SKImage.FromBitmap(copy);
    }
}
