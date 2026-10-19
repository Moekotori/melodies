using Echo.Contracts.Settings;
using Echo.Core.Contracts;
using Echo.Core.Transport;
namespace Echo.Core.Client;
internal sealed class CoreBroadcastClient(CoreWireClient wire) : ILocalBroadcast
{
    private CoreBroadcastState state = new(false, "http://127.0.0.1:18888");
    public string Address => Volatile.Read(ref state).Address;
    public bool IsRunning => Volatile.Read(ref state).Running;
    public async Task SetEnabledAsync(bool enabled, CancellationToken cancellation)
    { Volatile.Write(ref state, await wire.CallAsync<CoreBroadcastState>("broadcast.configure", CoreJson.Value(new CoreBroadcastRequest(enabled)), cancellation).ConfigureAwait(false)); }
}
