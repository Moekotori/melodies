using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using Echo.Contracts.Library;
using Echo.Core.Contracts;

namespace Echo.Core.Transport;

/// <summary>A bounded connection. Disconnect fails pending calls; mutating calls are never automatically replayed.</summary>
public sealed class CoreWireClient : IAsyncDisposable
{
    private Stream pipe;
    private readonly CoreEndpoint? endpoint;
    private readonly RemoteCoreProfile? remote;
    private readonly string role;
    private readonly SemaphoreSlim reconnecting = new(1, 1);
    private int connected = 1;
    public bool IsConnected => Volatile.Read(ref connected) != 0 && Volatile.Read(ref closed) == 0;
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim writes = new(1, 1);
    private readonly SemaphoreSlim dataSlots = new(CoreProtocol.MaximumPendingPerClient - 2);
    private readonly SemaphoreSlim slots = new(CoreProtocol.MaximumPendingPerClient);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<CoreMessage>> pending = new();
    private Task? reader;
    private Task? heartbeat;
    private long nextId;
    private int closed;
    public CoreWelcome Welcome { get; private set; } = null!;
    public CorePlaybackFrame? InitialPlayback { get; private set; }
    public event Action<CoreMessage>? Received;
    public event Action<Exception>? Disconnected;

    private CoreWireClient(Stream pipe, CoreEndpoint? endpoint, string role, RemoteCoreProfile? remote = null)
    { this.pipe = pipe; this.endpoint = endpoint; this.role = role; this.remote = remote; }

    public static async Task<CoreWireClient> ConnectAsync(CoreEndpoint endpoint, string role, CancellationToken token)
    {
        var opened = await OpenAsync(endpoint, role, token).ConfigureAwait(false);
        return new(opened.Pipe, endpoint, role) { Welcome = opened.Welcome, InitialPlayback = opened.Playback };
    }

    public static async Task<CoreWireClient> ConnectRemoteAsync(RemoteCoreProfile profile, CancellationToken token)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token); limit.CancelAfter(TimeSpan.FromSeconds(10));
        var opened = await OpenAsync(null, profile.Role, limit.Token, profile).ConfigureAwait(false);
        return new(opened.Pipe, null, profile.Role, profile) { Welcome = opened.Welcome, InitialPlayback = LocalClock(opened.Playback) };
    }

    private static async Task<(Stream Pipe, CoreWelcome Welcome, CorePlaybackFrame? Playback)> OpenAsync(CoreEndpoint? endpoint, string role, CancellationToken token, RemoteCoreProfile? remote = null)
    {
        Stream? pipe = null;
        try
        {
            var identity = remote?.Identity ?? endpoint!.Identity;
            if (remote is not null) pipe = await RemoteCoreTransport.ConnectAsync(remote, token).ConfigureAwait(false);
            else
            {
                var local = new NamedPipeClientStream(".", endpoint!.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                pipe = local; await local.ConnectAsync(token).ConfigureAwait(false);
            }
            await CoreFrames.WriteAsync(pipe, new("hello", Payload: JsonSerializer.SerializeToElement(new CoreHello(CoreProtocol.Version, identity, role, remote?.ClientId ?? Guid.NewGuid(), remote?.AccessToken), CoreWireJson.Default.CoreHello)), token).ConfigureAwait(false);
            var reply = await CoreFrames.ReadAsync(pipe, token).ConfigureAwait(false) ?? throw new IOException("Core handshake closed.");
            if (reply.Failure is { } error) throw new CoreRemoteException(error.Code, error.Message);
            var welcome = JsonSerializer.Deserialize(reply.Payload, CoreWireJson.Default.CoreWelcome)!;
            if (reply.Kind != "welcome" || welcome.Version != CoreProtocol.Version || welcome.Identity != identity)
                throw new CoreRemoteException("incompatible", "Core 版本或曲库身份不匹配。");
            return (pipe, welcome, reply.Playback);
        }
        catch { pipe?.Dispose(); throw; }
    }

    public async Task ReconnectAsync(CancellationToken token = default)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, stop.Token);
        await reconnecting.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref closed) != 0, this);
            if (IsConnected) return;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var limit = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); limit.CancelAfter(1000);
                    var opened = await OpenAsync(endpoint, role, limit.Token, remote).ConfigureAwait(false);
                    pipe.Dispose(); pipe = opened.Pipe; Welcome = opened.Welcome; InitialPlayback = remote is null ? opened.Playback : LocalClock(opened.Playback);
                    Volatile.Write(ref connected, 1);
                    Notify(new("event", Method: "core.reconnected", Playback: InitialPlayback, Payload: CoreJson.Empty));
                    reader = Task.Run(ReadAsync); return;
                }
                catch (Exception error) when (attempt < 2 && !lifetime.IsCancellationRequested && error is IOException or OperationCanceledException)
                { await Task.Delay(250 * (attempt + 1), lifetime.Token).ConfigureAwait(false); }
            }
        }
        finally { reconnecting.Release(); }
    }

    public void Start()
    {
        reader ??= ReadAsync();
        if (remote is not null) heartbeat ??= HeartbeatAsync();
    }

    private async Task HeartbeatAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(stop.Token).ConfigureAwait(false))
            {
                if (!IsConnected) continue;
                var observed = pipe;
                try
                {
                    using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); limit.CancelAfter(5000);
                    await CallAsync("core.info", CoreJson.Empty, limit.Token).ConfigureAwait(false);
                }
                catch (CoreRemoteException error) when (error.Code == "busy") { }
                catch (Exception)
                {
                    if (stop.IsCancellationRequested) return;
                    if (ReferenceEquals(pipe, observed))
                        try { observed.Dispose(); } catch (IOException) { }
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (stop.IsCancellationRequested) { }
    }

    public async Task<JsonElement> CallAsync(string method, JsonElement payload, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref closed) != 0, this);
        if (!IsConnected) throw new CoreRemoteException("disconnected", "Core 连接已断开，请重试连接。", true);
        token.ThrowIfCancellationRequested();
        var priority = CoreProtocol.IsPriority(method);
        if (!priority && !dataSlots.Wait(0)) throw new CoreRemoteException("busy", "Core 数据请求容量已满，播放控制仍可使用。");
        if (!slots.Wait(0))
        { if (!priority) dataSlots.Release(); throw new CoreRemoteException("busy", "Core 待处理请求已满，请稍后重试。"); }
        var id = Interlocked.Increment(ref nextId);
        var reply = new TaskCompletionSource<CoreMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = reply;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, stop.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            await SendAsync(new("request", id, method, payload, Instance: Welcome.Instance), deadline.Token).ConfigureAwait(false);
            CoreMessage result;
            try { result = await reply.Task.WaitAsync(deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                if (remote is not null && !token.IsCancellationRequested && !stop.IsCancellationRequested) pipe.Dispose();
                // Cancel only this connection's request. Accepted Core jobs have independent task IDs.
                _ = CancelRequestAsync(id);
                throw;
            }
            if (result.Failure is { } failure)
            {
                if (failure.Code == "library_changed") throw new LibraryChangedException();
                if (failure.Code == "library_busy") throw new LibraryBusyException();
                if (failure.Code == "canceled") throw new OperationCanceledException(failure.Message, token);
                throw new CoreRemoteException(failure.Code, failure.Message, failure.OutcomeUnknown);
            }
            return result.Payload;
        }
        finally { pending.TryRemove(id, out _); slots.Release(); if (!priority) dataSlots.Release(); }
    }

    public async Task<T> CallAsync<T>(string method, JsonElement payload, CancellationToken token = default)
        => CoreJson.Read<T>(await CallAsync(method, payload, token).ConfigureAwait(false));

    private async Task CancelRequestAsync(long id)
    {
        try { using var limit = new CancellationTokenSource(1000); await SendAsync(new("cancel", id, Instance: Welcome.Instance), limit.Token).ConfigureAwait(false); }
        catch (Exception) { }
    }

    private async Task SendAsync(CoreMessage message, CancellationToken token)
    {
        var destination = pipe;
        await writes.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(destination, pipe) || !IsConnected) throw new CoreRemoteException("disconnected", "Core 连接已变更，旧请求未重发。", true);
            await CoreFrames.WriteAsync(destination, message, token).ConfigureAwait(false);
        }
        finally { writes.Release(); }
    }

    private async Task ReadAsync()
    {
        var observed = pipe;
        try
        {
            while (await CoreFrames.ReadAsync(observed, stop.Token).ConfigureAwait(false) is { } message)
            {
                // A command reply publishes confirmed state before its awaiting control resumes.
                Notify(message);
                if (message.Kind == "response" && pending.TryGetValue(message.Id, out var waiting)) waiting.TrySetResult(message);
            }
            throw new IOException("Core 连接已断开。");
        }
        catch (Exception error)
        {
            if (Volatile.Read(ref closed) == 0 && ReferenceEquals(pipe, observed))
            {
                Volatile.Write(ref connected, 0);
                foreach (var item in pending.Values)
                    item.TrySetException(new CoreRemoteException("disconnected", "Core 连接已断开，操作结果可能尚未确认。", true));
                try { Disconnected?.Invoke(error); } catch (Exception) { }
            }
        }
    }

    private static CorePlaybackFrame? LocalClock(CorePlaybackFrame? frame) => frame is null ? null : frame with
    { State = frame.State with { ClockTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(), Level = null } };

    private void Notify(CoreMessage message)
    {
        // Stopwatch epochs/frequencies belong to each machine. Anchor the received observation locally.
        if (remote is not null && message.Playback is not null) message = message with { Playback = LocalClock(message.Playback) };
        if (Received is not { } handlers) return;
        foreach (Action<CoreMessage> handler in handlers.GetInvocationList()) try { handler(message); } catch (Exception) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref closed, 1) != 0) return;
        await stop.CancelAsync().ConfigureAwait(false);
        pipe.Dispose();
        foreach (var item in pending.Values) item.TrySetException(new ObjectDisposedException(nameof(CoreWireClient)));
        if (reader is not null) await reader.ConfigureAwait(false);
        if (heartbeat is not null) await heartbeat.ConfigureAwait(false);
        stop.Dispose();
        Received = null; Disconnected = null;
    }
}
