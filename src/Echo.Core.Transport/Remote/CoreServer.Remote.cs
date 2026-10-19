using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Echo.Core.Contracts;

namespace Echo.Core.Transport;

public sealed partial class CoreServer
{
    private readonly ConcurrentDictionary<Guid, (CoreHello Hello, Stream Stream)> remoteClients = new();
    public async Task RunRemoteAsync(IPEndPoint address, RemoteCoreAccess access, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, stop.Token);
        var listener = new TcpListener(address); listener.Start(CoreProtocol.MaximumClients);
        var reload = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                while (await timer.WaitForNextTickAsync(lifetime.Token).ConfigureAwait(false))
                {
                    access.Reload();
                    foreach (var peer in remoteClients.Values)
                        if (!access.Authorize(peer.Hello))
                            try { peer.Stream.Dispose(); } catch (IOException) { }
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        });
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(lifetime.Token).ConfigureAwait(false);
                client.NoDelay = true;
                // TLS and application handshake both consume a client slot and have a single 5s deadline.
                if (!Admit(client.GetStream(), lifetime.Token, access)) client.Dispose();
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { lifetime.Cancel(); listener.Stop(); await reload.ConfigureAwait(false); }
    }
}
