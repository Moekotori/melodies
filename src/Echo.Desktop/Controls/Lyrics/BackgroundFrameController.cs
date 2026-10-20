using Avalonia.Controls;

namespace Echo.Desktop.Controls.Lyrics;

// One outstanding callback follows the compositor; slow frames are skipped, never queued.
internal sealed class BackgroundFrameController : IDisposable
{
    private readonly Control owner;
    private readonly Action draw;
    private readonly Func<bool> needsFrame;
    private readonly Func<int> frameRate;
    private readonly Action<TimeSpan> tick;
    private readonly BackgroundFramePacer pacer = new();
    private bool pending, closed;
    public bool IsRunning { get; private set; }

    public BackgroundFrameController(Control owner, Action draw, Func<bool> needsFrame, Func<int> frameRate)
    {
        this.owner = owner; this.draw = draw; this.needsFrame = needsFrame; this.frameRate = frameRate;
        tick = Tick;
    }

    public void Update(bool visible)
    {
        var running = !closed && visible && needsFrame() && TopLevel.GetTopLevel(owner) is not null;
        if (running != IsRunning) pacer.Reset();
        IsRunning = running;
        if (running) Schedule();
    }

    private void Schedule()
    {
        if (pending) return;
        if (TopLevel.GetTopLevel(owner) is not { } top) { IsRunning = false; return; }
        pending = true; top.RequestAnimationFrame(tick);
    }

    private void Tick(TimeSpan time)
    {
        pending = false;
        if (!IsRunning || closed) return;
        if (!needsFrame()) { IsRunning = false; return; }
        if (pacer.ShouldDraw(time, frameRate())) draw();
        if (IsRunning) Schedule();
    }

    public void Dispose() { closed = true; IsRunning = false; }
}

// Fractional deadlines preserve 120 FPS on a 144 Hz display without a timer or double gating.
internal sealed class BackgroundFramePacer
{
    private double next = double.NaN;
    private int rate;
    public void Reset() => next = double.NaN;
    public bool ShouldDraw(TimeSpan time, int maximum)
    {
        maximum = Math.Clamp(maximum, 10, 240);
        if (maximum != rate) { rate = maximum; Reset(); }
        var now = time.TotalMilliseconds; var interval = 1000d / maximum;
        if (double.IsNaN(next)) next = now;
        if (now + .01 < next) return false;
        next += interval;
        if (next <= now) next = now + interval;
        return true;
    }
}
