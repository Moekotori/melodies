using Echo.Playback;
namespace Echo.Platform.Windows;

// SetThreadExecutionState is thread-scoped. A lazily created owner thread retains and releases it on that same thread.
public sealed class WindowsServicePlaybackWake : IPlaybackWake
{
    private readonly object gate = new();
    private readonly AutoResetEvent signal = new(false);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? owner;
    private bool desired, closed;
    public event Action<string?>? DiagnosticChanged;
    public void Update(bool required)
    {
        lock (gate)
        {
            if (closed || desired == required) return;
            desired = required;
            if (owner is null)
            { owner = new Thread(Run, 256 * 1024) { IsBackground = true, Name = "ECHO playback wake" }; owner.Start(); }
            signal.Set();
        }
    }
    private void Run()
    {
        try
        {
            using var wake = new WindowsPlaybackWake();
            while (true)
            {
                signal.WaitOne();
                bool required;
                lock (gate) { if (closed) break; required = desired; }
                try { wake.Update(required); DiagnosticChanged?.Invoke(null); }
                catch (Exception) { DiagnosticChanged?.Invoke("无法更新后台播放的系统唤醒状态。"); }
            }
        }
        finally { signal.Dispose(); released.TrySetResult(); }
    }
    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (!closed)
            {
                closed = true;
                if (owner is null) { signal.Dispose(); released.TrySetResult(); }
                else signal.Set();
            }
        }
        await released.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
    }
}
