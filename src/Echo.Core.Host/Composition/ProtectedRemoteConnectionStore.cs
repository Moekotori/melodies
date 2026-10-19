using Echo.Platform.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using Echo.Contracts.Remote;


namespace Echo.Core.Host.Composition;

// The source catalog serializes all writes. Credentials use the existing platform protection.
public sealed class ProtectedRemoteConnectionStore(string directory) : IRemoteConnectionStore
{
    private readonly ProtectedCredentials credentials = new(directory);
    public async Task<IReadOnlyList<RemoteConnection>> ReadAsync(CancellationToken token)
    {
        var values = await credentials.ReadAsync("remote-sources", token).ConfigureAwait(false);
        return values.TryGetValue("sources", out var json)
            ? JsonSerializer.Deserialize(json, RemoteStoreJsonContext.Default.RemoteConnectionArray) ?? [] : [];
    }
    public Task WriteAsync(IReadOnlyList<RemoteConnection> connections, CancellationToken token)
    {
        var json = JsonSerializer.Serialize(connections.ToArray(), RemoteStoreJsonContext.Default.RemoteConnectionArray);
        if (JsonEncodedText.Encode(json).EncodedUtf8Bytes.Length > 28000) throw new IOException("来源配置超过保存上限，请缩短地址或减少来源。");
        return credentials.SaveAsync("remote-sources", new() { ["sources"] = json }, token);
    }
}
[JsonSerializable(typeof(RemoteConnection[]))]
internal partial class RemoteStoreJsonContext : JsonSerializerContext;
