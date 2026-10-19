using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Echo.Core.Contracts;
using Echo.Core.Transport;

namespace Echo.Output.Client;

/// <summary>Encoded bytes only; binary streaming is separate from control JSON and never carries decoded PCM.</summary>
public sealed class OutputMediaServer(CoreEndpoint endpoint, IPEndPoint address, Guid clientId) : IAsyncDisposable
{
    public MediaTicketRegistry Tickets { get; } = new();
    public string Identity => endpoint.Identity;
    public Guid ClientId => clientId;
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentDictionary<Guid, (Stream Stream, Task Work)> peers = new();
    private readonly SemaphoreSlim budget = new(OutputProtocol.MaximumStreams);
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object gate = new();
    private Task? running;
    public async Task StartAsync(CancellationToken token)
    {
        lock (gate) running ??= RunAsync();
        await ready.Task.WaitAsync(token).ConfigureAwait(false);
    }
    private async Task RunAsync()
    {
        var listener = new TcpListener(address);
        try
        {
            using var access = RemoteCoreAccess.Open(endpoint);
            listener.Start(OutputProtocol.MaximumStreams); ready.TrySetResult();
            var reload = ReloadAsync(access);
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false); client.NoDelay = true;
                    if (!budget.Wait(0)) { client.Dispose(); continue; }
                    var id = Guid.NewGuid(); var stream = client.GetStream();
                    var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    peers[id] = (stream, done.Task);
                    _ = ServeAsync(id, stream, access, done);
                }
            }
            finally
            {
                listener.Stop(); stop.Cancel();
                foreach (var peer in peers.Values) peer.Stream.Dispose();
                await Task.WhenAll(peers.Values.Select(peer => peer.Work)).ConfigureAwait(false);
                await reload.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error) { ready.TrySetException(error); throw; }
        finally { listener.Stop(); }
    }
    private async Task ReloadAsync(RemoteCoreAccess access)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try { while (await timer.WaitForNextTickAsync(stop.Token).ConfigureAwait(false)) access.Reload(); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }
    private async Task ServeAsync(Guid id, Stream stream, RemoteCoreAccess access, TaskCompletionSource done)
    {
        try
        {
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); handshake.CancelAfter(5000);
            stream = await RemoteCoreTransport.AcceptAsync(stream, access, handshake.Token).ConfigureAwait(false);
            var frame = await CoreFrames.ReadAsync(stream, handshake.Token, 4096).ConfigureAwait(false);
            if (frame is not { Kind: "output.media" }) return;
            var request = OutputJson.Read<OutputMediaRequest>(frame.Payload);
            var hello = new CoreHello(request.Version, request.Identity, "output-media", request.ClientId, request.AccessToken);
            if (request.Version != OutputProtocol.Version || request.Identity != Identity || request.ClientId != clientId || !access.Authorize(hello)) return;
            await using var file = Tickets.Open(request.Ticket);
            if (request.Offset < 0 || request.Offset > file.Length || request.Count < 0 || request.Count > file.Length - request.Offset) return;
            await CoreFrames.WriteAsync(stream, new("output.media.ready", Payload: OutputJson.Value(new OutputMediaReply(true, file.Length, request.Offset, request.Count))), handshake.Token).ConfigureAwait(false);
            file.Position = request.Offset;
            var buffer = new byte[OutputProtocol.StreamBufferBytes]; var remaining = request.Count;
            while (remaining > 0)
            {
                if (!Tickets.Contains(request.Ticket) || !access.Authorize(hello)) return;
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); limit.CancelAfter(10000);
                var count = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), limit.Token).ConfigureAwait(false);
                if (count == 0) return;
                await stream.WriteAsync(buffer.AsMemory(0, count), limit.Token).ConfigureAwait(false); remaining -= count;
            }
        }
        catch (Exception) { /* No paths, capabilities or authentication material are logged. */ }
        finally { stream.Dispose(); peers.TryRemove(id, out _); budget.Release(); done.TrySetResult(); }
    }
    public async ValueTask DisposeAsync()
    {
        Tickets.Clear(); await stop.CancelAsync().ConfigureAwait(false);
        foreach (var peer in peers.Values) peer.Stream.Dispose();
        if (running is not null) try { await running.ConfigureAwait(false); } catch (Exception) { }
        stop.Dispose(); budget.Dispose();
    }
}
