namespace Echo.Desktop.Features.Search;

// Shared across query generations: slow cancelled calls still count against the two-call limit.
internal sealed class GlobalSearchConcurrency : IDisposable
{
    private readonly SemaphoreSlim slots = new(2, 2);
    private readonly object gate = new();
    private int readers;
    private bool closed;
    public async Task<T> ReadAsync<T>(Func<Task<T>> read, CancellationToken token)
    {
        lock (gate) { ObjectDisposedException.ThrowIf(closed, this); readers++; }
        try
        {
            await slots.WaitAsync(token);
            try { token.ThrowIfCancellationRequested(); return await read(); }
            finally { slots.Release(); }
        }
        finally { lock (gate) if (--readers == 0 && closed) slots.Dispose(); }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (closed) return;
            closed = true;
            if (readers == 0) slots.Dispose();
        }
    }
}
