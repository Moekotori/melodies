using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Channels;
using Echo.Core.Contracts;

namespace Echo.Core.Transport;

public interface ICoreRequestHandler
{
    Guid Instance { get; }
    CorePlaybackFrame? Capture();
    event Action? Changed;
    event Action<CoreMessage>? Notice;
    Task<JsonElement> DispatchAsync(string method, JsonElement payload, string role, CancellationToken token);
    ValueTask ConnectedAsync(CorePeer peer, CancellationToken token) => ValueTask.CompletedTask;
    ValueTask DisconnectedAsync(CorePeer peer) => ValueTask.CompletedTask;
    Task<JsonElement> DispatchForPeerAsync(string method, JsonElement payload, CorePeer peer, CancellationToken token)
        => DispatchAsync(method, payload, peer.Role, token);
}

public sealed record CorePeer(Guid ConnectionId, Guid ClientId, string Role);

public sealed partial class CoreServer(CoreEndpoint endpoint, ICoreRequestHandler handler, string remoteRole = "remote-control", bool reliableNotices = false) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, (Stream Stream, Task Task)> clients = new();
    private readonly SemaphoreSlim requestBudget = new(16, 16);
    private readonly SemaphoreSlim controlBudget = new(8, 8);
    private readonly SemaphoreSlim clientBudget = new(CoreProtocol.MaximumClients, CoreProtocol.MaximumClients);
    private readonly CancellationTokenSource stop = new();
    public int ClientCount => clients.Count;
    public void Disconnect(Guid connectionId) { if (clients.TryGetValue(connectionId, out var peer)) peer.Stream.Dispose(); }

    public async Task RunAsync(CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, stop.Token);
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                NamedPipeServerStream? pipe = null;
                try
                {
                    // One listening pipe in addition to admitted clients; all transports share the same budget.
                    pipe = new NamedPipeServerStream(endpoint.PipeName, PipeDirection.InOut, CoreProtocol.MaximumClients + 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(lifetime.Token).ConfigureAwait(false);
                }
                catch { pipe?.Dispose(); throw; }
                Admit(pipe, lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    private bool Admit(Stream stream, CancellationToken token, RemoteCoreAccess? remote = null)
    {
        if (!clientBudget.Wait(0)) { stream.Dispose(); return false; }
        var id = Guid.NewGuid();
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        clients[id] = (stream, accepted.Task);
        _ = ServeAsync(id, stream, accepted, token, remote);
        return true;
    }

    private async Task ServeAsync(Guid id, Stream pipe, TaskCompletionSource completion, CancellationToken token, RemoteCoreAccess? remote)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        CorePeer? peer = null;
        var calls = new ConcurrentDictionary<long, (CancellationTokenSource Cancel, Task Task)>();
        var writes = new SemaphoreSlim(1, 1);
        var signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        var notices = Channel.CreateBounded<CoreMessage>(new BoundedChannelOptions(16) { FullMode = reliableNotices ? BoundedChannelFullMode.Wait : BoundedChannelFullMode.DropOldest, SingleReader = true });
        void Changed() => signals.Writer.TryWrite(true);
        void Notice(CoreMessage notice)
        {
            if (remote is null || remoteRole != "remote-control" || notice.Method is "library.listening" or "radio.changed" or "core.quit")
                if (!notices.Writer.TryWrite(notice) && reliableNotices) pipe.Dispose();
        }
        Task? updates = null, notifications = null;
        async Task Write(CoreMessage message)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            await writes.WaitAsync(deadline.Token).ConfigureAwait(false);
            try { await CoreFrames.WriteAsync(pipe, message, deadline.Token).ConfigureAwait(false); }
            finally { writes.Release(); }
        }
        try
        {
            using var handshakeLimit = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            handshakeLimit.CancelAfter(TimeSpan.FromSeconds(5));
            if (remote is not null) pipe = await RemoteCoreTransport.AcceptAsync(pipe, remote, handshakeLimit.Token).ConfigureAwait(false);
            var first = await CoreFrames.ReadAsync(pipe, handshakeLimit.Token, 4096).ConfigureAwait(false);
            if (first is null || first.Kind != "hello") return;
            var hello = JsonSerializer.Deserialize(first.Payload, CoreWireJson.Default.CoreHello)!;
            if (hello.Version != CoreProtocol.Version || hello.Identity != endpoint.Identity || (remote is null ? hello.Role is not ("control" or "bridge") : hello.Role != remoteRole || !remote.Authorize(hello)))
            { await Write(new("welcome", Failure: new("incompatible", "Core 协议、身份或角色不匹配。"))).ConfigureAwait(false); return; }
            peer = new(id, hello.ClientId, hello.Role);
            await handler.ConnectedAsync(peer, handshakeLimit.Token).ConfigureAwait(false);
            await Write(new("welcome", Payload: JsonSerializer.SerializeToElement(new CoreWelcome(CoreProtocol.Version, handler.Instance,
                endpoint.Identity, System.Diagnostics.Stopwatch.Frequency), CoreWireJson.Default.CoreWelcome), Playback: BridgePlaybackProjection.Apply(handler.Capture(), hello.Role))).ConfigureAwait(false);
            if (remote is not null) remoteClients[id] = (hello, pipe);
            handler.Changed += Changed; handler.Notice += Notice;
            Changed();
            updates = Task.Run(async () =>
            {
                long queueVersion = -1;
                while (await signals.Reader.WaitToReadAsync(lifetime.Token).ConfigureAwait(false))
                {
                    while (signals.Reader.TryRead(out _)) { }
                    await Task.Delay(100, lifetime.Token).ConfigureAwait(false);
                    var frame = handler.Capture();
                    if (frame is null) continue;
                    var currentQueue = frame.State.Queue;
                    if (queueVersion == currentQueue.Version)
                        frame = frame with { State = frame.State with { Queue = CorePlaybackFrames.EmptyQueue }, HasQueue = false };
                    queueVersion = currentQueue.Version;
                    await Write(new("event", Method: "playback.state", Playback: BridgePlaybackProjection.Apply(frame, hello.Role))).ConfigureAwait(false);
                }
            }, lifetime.Token);
            _ = updates.ContinueWith(faulted => { _ = faulted.Exception; try { lifetime.Cancel(); pipe.Dispose(); } catch (ObjectDisposedException) { } }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            notifications = Task.Run(async () =>
            {
                await foreach (var notice in notices.Reader.ReadAllAsync(lifetime.Token).ConfigureAwait(false))
                    await Write(notice).ConfigureAwait(false);
            }, lifetime.Token);
            _ = notifications.ContinueWith(faulted => { _ = faulted.Exception; try { lifetime.Cancel(); pipe.Dispose(); } catch (ObjectDisposedException) { } }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            while (await CoreFrames.ReadAsync(pipe, lifetime.Token, CoreProtocol.MaximumRequestBytes).ConfigureAwait(false) is { } request)
            {
                if (remote is not null && !remote.Authorize(hello)) throw new UnauthorizedAccessException();
                if (request.Instance != handler.Instance)
                { await Write(new("response", request.Id, Failure: new("core_changed", "Core 实例已变更，旧请求未执行。", true))).ConfigureAwait(false); continue; }
                if (request.Kind == "cancel") { if (calls.TryGetValue(request.Id, out var running)) running.Cancel.Cancel(); continue; }
                if (request.Kind != "request" || request.Id <= 0 || calls.ContainsKey(request.Id)) throw new InvalidDataException("Invalid request identity.");
                var priority = CoreProtocol.IsPriority(request.Method);
                var budget = priority ? controlBudget : requestBudget;
                if (calls.Count >= CoreProtocol.MaximumPendingPerClient || !priority && calls.Count >= CoreProtocol.MaximumPendingPerClient - 2 || !budget.Wait(0))
                { await Write(new("response", request.Id, Failure: new("busy", "Core 请求容量已满。"))).ConfigureAwait(false); continue; }
                var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                calls[request.Id] = (cancellation, done.Task);
                var operation = request;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var response = await handler.DispatchForPeerAsync(operation.Method, operation.Payload, peer, cancellation.Token).ConfigureAwait(false);
                        var state = operation.Method.StartsWith("playback.", StringComparison.Ordinal) || operation.Method.StartsWith("sleep.", StringComparison.Ordinal) ? handler.Capture() : null;
                        await Write(new("response", operation.Id, Payload: response, Playback: BridgePlaybackProjection.Apply(state, hello.Role))).ConfigureAwait(false);
                    }
                    catch (Exception error)
                    {
                        try { await Write(new("response", operation.Id, Failure: Failure(error))).ConfigureAwait(false); }
                        catch (Exception) { lifetime.Cancel(); pipe.Dispose(); }
                    }
                    finally
                    {
                        calls.TryRemove(operation.Id, out _); cancellation.Dispose(); budget.Release(); done.TrySetResult();
                    }
                }, CancellationToken.None);
            }
        }
        catch (Exception) { /* A malformed or disconnected control releases only this connection. */ }
        finally
        {
            handler.Changed -= Changed; handler.Notice -= Notice;
            await lifetime.CancelAsync().ConfigureAwait(false); pipe.Dispose();
            foreach (var call in calls.Values) try { call.Cancel.Cancel(); } catch (ObjectDisposedException) { }
            try { await Task.WhenAll(calls.Values.Select(call => call.Task)).ConfigureAwait(false); } catch (Exception) { }
            foreach (var work in new[] { updates, notifications }) if (work is not null) try { await work.ConfigureAwait(false); } catch (Exception) { }
            if (peer is not null) try { await handler.DisconnectedAsync(peer).ConfigureAwait(false); } catch (Exception) { }
            writes.Dispose(); remoteClients.TryRemove(id, out _); clients.TryRemove(id, out _); clientBudget.Release(); completion.TrySetResult();
        }
    }

    private static CoreFailure Failure(Exception error) => error switch
    {
        Echo.Contracts.Library.LibraryChangedException => new("library_changed", "曲库已更新，请刷新当前页。"),
        Echo.Contracts.Library.LibraryBusyException => new("library_busy", "曲库繁忙，请稍后重试。"),
        OperationCanceledException => new("canceled", "操作已取消；已提交的写入请核对回执。", true),
        ArgumentException => new("invalid", error.Message),
        InvalidOperationException => new("conflict", error.Message),
        UnauthorizedAccessException => new("forbidden", "没有此操作的访问权限。"),
        _ => new("failed", "Core 操作未完成，请检查连接或文件权限。", true)
    };

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync().ConfigureAwait(false);
        foreach (var client in clients.Values) client.Stream.Dispose();
        await Task.WhenAll(clients.Values.Select(client => client.Task)).ConfigureAwait(false);
        stop.Dispose(); requestBudget.Dispose(); controlBudget.Dispose(); clientBudget.Dispose();
    }
}
