using Avalonia.Threading;

namespace Echo.Desktop.Controls.Lyrics;

public sealed partial class JizuraSceneControl
{
    // Keep a quick hide/show warm, but release the interpreter, canvases and allocated frame slots when parked.
    private readonly DispatcherTimer retirement = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool resourcesResident;

    private bool UpdateResidency()
    {
        if (!active) { ReleaseScene(); return false; }
        if (motion?.IsWindowVisible == true) { retirement.Stop(); return true; }
        if (resourcesResident && !retirement.IsEnabled) retirement.Start();
        return false;
    }

    private void RetireScene(object? sender, EventArgs args)
    {
        retirement.Stop();
        if (!closed && motion?.IsWindowVisible != true) ReleaseScene();
    }

    private void ReleaseScene()
    {
        retirement.Stop();
        if (!resourcesResident) return;
        resourcesResident = false; version++; lastRequest = 0;
        frame?.Dispose(); frame = null;
        Interlocked.Exchange(ref pending, null)?.Frame?.Dispose();
        worker?.Request(new(version, null, 0, "", "", "", 0, false, false));
        InvalidateVisual();
    }
}
