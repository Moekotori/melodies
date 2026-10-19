using Echo.Contracts.Settings;
using Echo.Core.Contracts;
using Echo.Core.Transport;
namespace Echo.Core.Client;
internal sealed class CoreSettingsClient(CoreWireClient wire) : ICoreSettings
{
    public Task<Dictionary<string, FeatureSetting>> ReadAsync(CancellationToken token)
        => wire.CallAsync<Dictionary<string, FeatureSetting>>("settings.features.read", CoreJson.Empty, token);
    public Task ConfirmAsync(string id, FeatureSetting value, CancellationToken token)
        => wire.CallAsync("settings.features.write", CoreJson.Value(new CoreSettingsRequest(id, value)), token);
}
