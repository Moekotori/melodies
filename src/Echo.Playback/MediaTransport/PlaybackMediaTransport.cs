using System.Threading.Channels;

namespace Echo.Playback.MediaTransport;

/// <summary>One latest-state publication and at most 16 pending system commands.</summary>
public sealed class PlaybackMediaTransport : IAsyncDisposable
{
    private readonly PlaybackSessionCoordinator session;
    private readonly IMediaTransport transport;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Channel<MediaTransportCommand> commands = Channel.CreateBounded<MediaTransportCommand>(
        new BoundedChannelOptions(16) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Task worker;
    private int disposed;

    public PlaybackMediaTransport(PlaybackSessionCoordinator session, IMediaTransport transport)
    {
        this.session = session;
        this.transport = transport;
        session.Changed += Publish;
        session.QueueChanged += QueueChanged;
        transport.Command += OnCommand;
        worker = Task.Run(RunAsync);
        Publish(session.Snapshot);
    }

    public void Retry() => transport.Retry();
    private void Publish(PlaybackSnapshot snapshot)
    {
        if (Volatile.Read(ref disposed) == 0) transport.Publish(MediaTransportMapping.Create(snapshot, session.Queue, session.Repeat, session.QueueState));
    }
    private void QueueChanged(QueueSnapshot _) => Publish(session.Snapshot);
    private void OnCommand(MediaTransportCommand command)
    {
        if (Volatile.Read(ref disposed) == 0) commands.Writer.TryWrite(command);
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var command in commands.Reader.ReadAllAsync(lifetime.Token).ConfigureAwait(false))
            {
                try
                {
                    var token = lifetime.Token;
                    switch (command.Name)
                    {
                        case "play":
                            if (session.Snapshot.Status is not (PlaybackStatus.Playing or PlaybackStatus.Loading))
                                await session.ResumeAsync(token).ConfigureAwait(false);
                            break;
                        case "pause": await session.PauseAsync(token).ConfigureAwait(false); break;
                        case "stop": await session.StopAsync(token).ConfigureAwait(false); break;
                        case "next": await session.NextAsync(token).ConfigureAwait(false); break;
                        case "previous": await session.PreviousAsync(token).ConfigureAwait(false); break;
                        case "seek" when session.Snapshot.DurationSeconds > 0:
                            await session.SeekAsync(command.PositionSeconds, token).ConfigureAwait(false); break;
                        case "setRepeat":
                            await session.SetRepeatAsync(command.Mode switch { "track" => RepeatMode.One, "list" => RepeatMode.All, _ => RepeatMode.Off }, token).ConfigureAwait(false); break;
                    }
                }
                catch (Exception) when (!lifetime.IsCancellationRequested) { /* Session publishes playback failures. */ }
                // Unsupported shuffle requests are reset to the confirmed mode.
                Publish(session.Snapshot);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        session.Changed -= Publish;
        session.QueueChanged -= QueueChanged;
        transport.Command -= OnCommand;
        commands.Writer.TryComplete();
        await lifetime.CancelAsync().ConfigureAwait(false);
        await worker.ConfigureAwait(false);
        await transport.DisposeAsync().ConfigureAwait(false);
        lifetime.Dispose();
    }
}
