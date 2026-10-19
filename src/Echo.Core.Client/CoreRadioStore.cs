using Echo.Contracts.Radio;
using Echo.Core.Contracts;
using Echo.Core.Transport;
namespace Echo.Core.Client;

internal sealed class CoreRadioStore(CoreWireClient wire) : IRadioStationStore
{
    private long revision;
    public async Task<IReadOnlyList<RadioStation>?> ReadAsync(CancellationToken token)
    {
        var state = await wire.CallAsync<CoreRadioState>("radio.read", CoreJson.Empty, token).ConfigureAwait(false);
        revision = state.Revision; return state.Stations;
    }
    public async Task WriteAsync(IReadOnlyList<RadioStation> stations, CancellationToken token)
    {
        await wire.CallAsync("radio.write", CoreJson.Value(new CoreRadioWrite(revision, stations)), token).ConfigureAwait(false);
        revision++;
    }
}
