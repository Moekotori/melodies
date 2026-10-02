using System.Threading.Channels;

namespace Echo.Playback;

internal sealed class PlaybackCommandLane : IAsyncDisposable
{
    private readonly Channel<Func<Task>> channel = Channel.CreateBounded<Func<Task>>(new BoundedChannelOptions(64)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Task worker;
    public PlaybackCommandLane() => worker = RunAsync();

    public async Task InvokeAsync(Func<Task> action, CancellationToken token = default)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await channel.Writer.WriteAsync(async () =>
        {
            if (token.IsCancellationRequested) { completion.TrySetCanceled(token); return; }
            try { await action().ConfigureAwait(false); completion.TrySetResult(); }
            catch (Exception e) { completion.TrySetException(e); }
        }, token).ConfigureAwait(false);
        // Cancellation applies at admission. An issued native mutation must finish or fault.
        await completion.Task.ConfigureAwait(false);
    }

    public bool Post(Func<Task> action) => channel.Writer.TryWrite(action);
    private async Task RunAsync()
    {
        await foreach (var action in channel.Reader.ReadAllAsync().ConfigureAwait(false))
            await action().ConfigureAwait(false);
    }
    public async ValueTask DisposeAsync() { channel.Writer.TryComplete(); await worker.ConfigureAwait(false); }
}
