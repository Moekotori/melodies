using Avalonia.Threading;

namespace Echo.Desktop.Controls.Lyrics;

// One visible scene, one capped driver. It unregisters at pause, stale clock and detach.
internal sealed class LyricsFrameController : IDisposable
{
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render);
    private readonly Func<bool> needsFrame;
    public bool IsRunning => timer.IsEnabled;
    public LyricsFrameController(Action draw, Func<bool> needsFrame)
    {
        this.needsFrame = needsFrame;
        timer.Tick += (_, _) => { if (!this.needsFrame()) timer.Stop(); else draw(); };
    }
    public void Update(bool visible, bool focused)
    {
        timer.Interval = TimeSpan.FromSeconds(1.0 / (focused ? 60 : 30));
        if (visible && needsFrame()) timer.Start(); else timer.Stop();
    }
    public void Dispose() => timer.Stop();
}
