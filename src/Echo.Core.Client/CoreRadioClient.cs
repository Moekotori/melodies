using Echo.Contracts.Radio;
using Echo.Core.Contracts;
using Echo.Core.Transport;
namespace Echo.Core.Client;

internal sealed class CoreRadioClient : IRadioMetadataMonitor, IRadioDirectory, IRadioStreamResolver
{
    private readonly CoreWireClient wire;
    private CoreRadioMetadata metadata = new(null, null);
    public string? StreamUrl => Volatile.Read(ref metadata).Url;
    public string? StreamTitle => Volatile.Read(ref metadata).Title;
    public event Action? Changed;
    public CoreRadioClient(CoreWireClient wire) { this.wire = wire; wire.Received += Receive; }
    private void Receive(CoreMessage message)
    { if (message.Method == "radio.metadata") { Volatile.Write(ref metadata, CoreJson.Read<CoreRadioMetadata>(message.Payload)); Changed?.Invoke(); } }
    public async Task InitializeAsync(CancellationToken token)
    { Volatile.Write(ref metadata, await wire.CallAsync<CoreRadioMetadata>("radio.metadata", CoreJson.Empty, token).ConfigureAwait(false)); }
    public Task<RadioDirectoryResult> SearchAsync(RadioDirectoryQuery query, CancellationToken token)
        => wire.CallAsync<RadioDirectoryResult>("radio.search", CoreJson.Value(new CoreRadioSearch(query)), token);
    public Task<RadioStreamInfo> ResolveAsync(string url, CancellationToken token)
        => wire.CallAsync<RadioStreamInfo>("radio.resolve", CoreJson.Value(url), token);
    public void Follow(string? url, bool keepTitle) { } // Core follows its authoritative session, independent of view visibility.
    public void Cancel() { } // Requests carry their own cancellation tokens.
    public void Dispose() { } // A view does not own this shared Core projection.
    public void Detach() => wire.Received -= Receive;
}
