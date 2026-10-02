using Echo.Playback;

namespace Echo.Application.Playback;

public enum SleepTimerAction { Pause, Stop, Quit }
public sealed record SleepTimerState(bool Active, double RemainingSeconds, SleepTimerAction Action,
    bool Executing = false, string? Error = null);

/// <summary>One bounded worker and one deadline for the local playback owner; independent of visible windows.</summary>
public sealed class SleepTimerService : IAsyncDisposable
{
    private readonly PlaybackSessionCoordinator session;
    private readonly TimeProvider time;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Task worker;
    private DateTimeOffset deadline;
    private SleepTimerState state = new(false, 0, SleepTimerAction.Pause);
    private Func<Task>? quit;
    private int disposed;
    public SleepTimerState State => Volatile.Read(ref state);
    public event Action<SleepTimerState>? Changed;

    public SleepTimerService(PlaybackSessionCoordinator session, TimeProvider? time = null)
    {
        this.session = session; this.time = time ?? TimeProvider.System;
        worker = RunAsync();
    }

    public void SetQuitHandler(Func<Task>? handler) => Volatile.Write(ref quit, handler);

    public async Task StartAsync(double minutes, SleepTimerAction action, CancellationToken token = default)
    {
        if (!double.IsFinite(minutes) || minutes is < 0.001 or > 720 || !Enum.IsDefined(action))
            throw new ArgumentOutOfRangeException(nameof(minutes));
        if (action == SleepTimerAction.Quit && Volatile.Read(ref quit) is null)
            throw new InvalidOperationException("当前窗口无法处理退出，请选择暂停或停止。");
        await EnterAsync(token).ConfigureAwait(false);
        try
        {
            deadline = time.GetUtcNow().AddMinutes(minutes);
            Publish(new(true, minutes * 60, action));
        }
        finally { gate.Release(); }
    }

    public async Task CancelAsync(CancellationToken token = default)
    {
        await EnterAsync(token).ConfigureAwait(false);
        try { Publish(new(false, 0, state.Action)); }
        finally { gate.Release(); }
    }

    private async Task EnterAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!await gate.WaitAsync(0, token).ConfigureAwait(false))
            throw new InvalidOperationException("定时操作正在执行，请稍后重试。");
        if (Volatile.Read(ref disposed) != 0) { gate.Release(); throw new ObjectDisposedException(nameof(SleepTimerService)); }
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token).ConfigureAwait(false))
            {
                if (!await gate.WaitAsync(0, lifetime.Token).ConfigureAwait(false)) continue;
                try
                {
                    if (!state.Active) continue;
                    var remaining = Math.Max(0, (deadline - time.GetUtcNow()).TotalSeconds);
                    if (remaining > 0) { Publish(state with { RemainingSeconds = remaining }); continue; }
                    var action = state.Action;
                    Publish(new(false, 0, action, Executing: true));
                    try
                    {
                        if (action == SleepTimerAction.Pause) await session.PauseAsync(lifetime.Token).ConfigureAwait(false);
                        else await session.StopAsync(lifetime.Token).ConfigureAwait(false);
                        Publish(new(false, 0, action));
                        // Quit is posted after releasing this worker's gate, so shutdown can join it.
                        if (action == SleepTimerAction.Quit) _ = RequestQuitAsync();
                    }
                    catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                    catch (Exception error) { Publish(new(false, 0, action, Error: error.Message)); }
                }
                finally { gate.Release(); }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    private async Task RequestQuitAsync()
    {
        try
        {
            if (Volatile.Read(ref quit) is { } handler) await handler().ConfigureAwait(false);
            else Publish(new(false, 0, SleepTimerAction.Quit, Error: "退出窗口已关闭；播放已停止。"));
        }
        catch (Exception error) { Publish(new(false, 0, SleepTimerAction.Quit, Error: error.Message)); }
    }

    private void Publish(SleepTimerState value)
    {
        Volatile.Write(ref state, value);
        if (Changed is { } handlers)
            foreach (Action<SleepTimerState> handler in handlers.GetInvocationList())
                try { handler(value); } catch { /* Presentation cannot stop the timer. */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel(); await worker.ConfigureAwait(false);
        await gate.WaitAsync().ConfigureAwait(false);
        try { Changed = null; quit = null; Volatile.Write(ref state, new(false, 0, state.Action)); }
        finally { gate.Release(); }
        lifetime.Dispose();
        // Keep the tiny gate alive so an already submitted UI cancellation cannot race its disposal.
    }
}
