using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Echo.Core.Transport;

namespace Echo.Output.Host;

internal sealed class LoopbackMediaProxy : IAsyncDisposable
{
    private readonly RemoteCoreProfile profile;
    private readonly CancellationTokenSource stop = new();
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly SemaphoreSlim budget = new(OutputProtocol.MaximumStreams);
    private readonly ConcurrentDictionary<Guid, (Stream Stream, Task Work)> clients = new();
    private readonly object gate = new();
    private readonly Dictionary<string, LinkedListNode<OutputMediaTicket>> tickets = new(StringComparer.Ordinal);
    private readonly LinkedList<OutputMediaTicket> recent = new();
    private readonly Task accept;
    public string BaseAddress { get; }
    private readonly string authority;
    public LoopbackMediaProxy(RemoteCoreProfile profile)
    {
        if (profile.Role != "output-media") throw new ArgumentException("需要 Core 媒体配对文件。");
        this.profile = profile; listener.Start(OutputProtocol.MaximumStreams);
        authority = "127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
        BaseAddress = "http://" + authority + "/media/"; accept = RunAsync();
    }
    public void Publish(OutputMediaTicket[] values)
    {
        if (values.Length > OutputProtocol.MaximumTickets) throw new ArgumentException("媒体能力过多。");
        lock (gate)
        {
            foreach (var ticket in values)
            {
                if (ticket.Id is not { Length: 64 } || !ticket.Id.All(char.IsAsciiHexDigit) || ticket.Length < 0 || ticket.Extension.Length > 16) throw new ArgumentException("无效的媒体能力。");
                if (tickets.Remove(ticket.Id, out var old)) recent.Remove(old);
                tickets[ticket.Id] = recent.AddLast(ticket);
                while (tickets.Count > OutputProtocol.MaximumTickets) { var first = recent.First!; tickets.Remove(first.Value.Id); recent.RemoveFirst(); }
            }
        }
    }
    public void Clear() { lock (gate) { tickets.Clear(); recent.Clear(); } }
    private OutputMediaTicket? Find(string id) { lock (gate) return tickets.TryGetValue(id, out var value) ? value.Value : null; }
    public bool Owns(string url)
    {
        if (!url.StartsWith(BaseAddress, StringComparison.Ordinal)) return false;
        var id = url[BaseAddress.Length..].Split('/')[0]; return Find(id) is not null;
    }
    private async Task RunAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false); client.NoDelay = true;
                if (!budget.Wait(0)) { client.Dispose(); continue; }
                var id = Guid.NewGuid(); var stream = client.GetStream();
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                clients[id] = (stream, done.Task); _ = ServeAsync(id, stream, done);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (SocketException) when (stop.IsCancellationRequested) { }
    }
    private async Task ServeAsync(Guid id, Stream stream, TaskCompletionSource done)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); limit.CancelAfter(5000);
            var request = await MediaRangeRequest.ReadAsync(stream, authority, Find, limit.Token).ConfigureAwait(false);
            var ticket = Find(request.Ticket) ?? throw new UnauthorizedAccessException();
            var head = $"HTTP/1.1 {(request.Partial ? "206 Partial Content" : "200 OK")}\r\nContent-Type: application/octet-stream\r\nAccept-Ranges: bytes\r\nContent-Length: {request.Count}\r\nConnection: close\r\nCache-Control: no-store\r\n";
            if (request.Partial) head += $"Content-Range: bytes {request.Start}-{request.Start + request.Count - 1}/{ticket.Length}\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head + "\r\n"), limit.Token).ConfigureAwait(false);
            if (request.Method == "GET" && request.Count > 0)
                await EncodedMediaReader.CopyAsync(profile, ticket, request.Start, request.Count, stream, stop.Token).ConfigureAwait(false);
        }
        catch (Exception) { /* Close without retaining local URLs or source capabilities in logs. */ }
        finally { stream.Dispose(); clients.TryRemove(id, out _); budget.Release(); done.TrySetResult(); }
    }
    public async ValueTask DisposeAsync()
    {
        Clear(); await stop.CancelAsync().ConfigureAwait(false); listener.Stop();
        foreach (var client in clients.Values) client.Stream.Dispose();
        await accept.ConfigureAwait(false); await Task.WhenAll(clients.Values.Select(client => client.Work)).ConfigureAwait(false);
        budget.Dispose(); stop.Dispose();
    }
}
