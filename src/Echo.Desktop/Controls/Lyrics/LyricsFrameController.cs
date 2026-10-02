using Avalonia.Threading;

namespace Echo.Desktop.Controls.Lyrics;

// One visible scene, one capped driver. It unregisters at pause, stale clock and detach.
internal sealed class LyricsFrameController : IDisposable
{
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render);
    private readonly Func<bool> needsFrame;
    private readonly Func<int>? maximumFrameRate;
    public bool IsRunning => timer.IsEnabled;
    public LyricsFrameController(Action draw, Func<bool> needsFrame, Func<int>? maximumFrameRate = null)
    {
        this.needsFrame = needsFrame;
        this.maximumFrameRate = maximumFrameRate;
        timer.Tick += (_, _) => { if (!this.needsFrame()) timer.Stop(); else draw(); };
    }
    public void Update(bool visible, bool focused)
    {
        timer.Interval = TimeSpan.FromSeconds(1.0 / Math.Min(focused ? 60 : 30, Math.Clamp(maximumFrameRate?.Invoke() ?? 60, 1, 60)));
        if (visible && needsFrame()) timer.Start(); else timer.Stop();
    }
    public void Dispose() => timer.Stop();
}
