using Echo.Contracts.Lyrics;
using Echo.Lyrics;

namespace Echo.Core.Lyrics;

// The existing typed resource contract remains the only Control entry point.
internal sealed class CoreLyricsSource(ILyricsSource local, Func<string, CancellationToken, Task<LyricsIdentity?>> identify,
    bool enabled, NetworkLyricsMatcher? network = null) : ILyricsSource, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly NetworkLyricsMatcher matcher = network ?? new();
    private readonly CancellationTokenSource stop = new();
    private CancellationTokenSource epoch = new();
    private bool active = enabled;
    private bool closed;

    public void SetEnabled(bool value)
    {
        CancellationTokenSource previous;
        lock (gate)
        {
            if (closed || active == value) return;
            active = value; previous = epoch; epoch = new();
        }
        previous.Cancel(); previous.Dispose();
    }

    public async Task<LyricsText?> ReadAsync(string audioPath, string? explicitPath, CancellationToken cancellation)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, stop.Token);
        var text = await local.ReadAsync(audioPath, explicitPath, lifetime.Token).ConfigureAwait(false);
        if (explicitPath is not null || !string.IsNullOrWhiteSpace(text?.Text)) return text;
        CancellationTokenSource lookup;
        lock (gate)
        {
            if (!active || closed) return null;
            lookup = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, epoch.Token);
        }
        using (lookup)
        {
            try
            {
                var identity = await identify(audioPath, lookup.Token).ConfigureAwait(false);
                var result = identity is null ? null : await matcher.MatchAsync(identity, lookup.Token).ConfigureAwait(false);
                lookup.Token.ThrowIfCancellationRequested();
                return result;
            }
            catch (OperationCanceledException) when (!lifetime.IsCancellationRequested) { return null; }
        }
    }
    public async ValueTask DisposeAsync()
    {
        lock (gate) { if (closed) return; closed = true; }
        await stop.CancelAsync().ConfigureAwait(false);
        await matcher.DisposeAsync().ConfigureAwait(false);
        if (local is IAsyncDisposable disposable) await disposable.DisposeAsync().ConfigureAwait(false);
        epoch.Dispose();
    }
}
