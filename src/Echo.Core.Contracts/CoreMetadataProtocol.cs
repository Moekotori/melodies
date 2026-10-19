using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Echo.Core.Contracts;

public sealed record CoreMetadataRequest(int Version, string RequestId, AlbumInformationRequest Information);
public sealed record CoreMetadataResponse(int Version, string RequestId, string CoreId, string Generation,
    long Revision, AlbumInformationResult? Information, string? Error);

// Only the album-information capability is exposed during this migration phase.
public static class CoreMetadataProtocol
{
    public const int Version = 1;
    public const int MaxBytes = 512 * 1024;
    public static string CoreId => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        Environment.UserName + "|" + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))))[..24];
    public static string PipeName => "ecm1-" + CoreId.ToLowerInvariant();
    public static async Task WriteAsync(Stream stream, byte[] payload, CancellationToken token)
    {
        if (payload.Length > MaxBytes) throw new IOException("Core message exceeds its budget.");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, token).ConfigureAwait(false);
        await stream.WriteAsync(payload, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }
    public static async Task<byte[]> ReadAsync(Stream stream, CancellationToken token)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 2 or > MaxBytes) throw new IOException("Invalid Core message size.");
        var data = new byte[length]; await stream.ReadExactlyAsync(data, token).ConfigureAwait(false); return data;
    }
}
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(CoreMetadataRequest))]
[JsonSerializable(typeof(CoreMetadataResponse))]
[JsonSerializable(typeof(AlbumInformationResult))]
public partial class CoreMetadataJson : JsonSerializerContext;
