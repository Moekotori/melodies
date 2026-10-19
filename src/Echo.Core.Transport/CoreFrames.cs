using System.Buffers.Binary;
using System.Text.Json;
using Echo.Core.Contracts;

namespace Echo.Core.Transport;

public static class CoreFrames
{
    public static async Task<CoreMessage?> ReadAsync(Stream stream, CancellationToken token, int limit = CoreProtocol.MaximumFrameBytes)
    {
        var header = new byte[4];
        var first = await stream.ReadAsync(header.AsMemory(0, 1), token).ConfigureAwait(false);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 || length > limit) throw new InvalidDataException("Core frame exceeds its budget.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return JsonSerializer.Deserialize(bytes, CoreJson.Info<CoreMessage>()) ?? throw new InvalidDataException("Empty Core frame.");
    }

    public static async Task WriteAsync(Stream stream, CoreMessage message, CancellationToken token)
    {
        // A single frame is allocated only after admission, never queued as unbounded serialized buffers.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message with
            { Payload = message.Payload.ValueKind == JsonValueKind.Undefined ? CoreJson.Empty : message.Payload }, CoreJson.Info<CoreMessage>());
        if (bytes.Length > CoreProtocol.MaximumFrameBytes) throw new InvalidDataException("Core frame exceeds its budget.");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, token).ConfigureAwait(false);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }
}

public sealed class CoreRemoteException(string code, string message, bool outcomeUnknown = false) : IOException(message)
{
    public string Code { get; } = code;
    public bool OutcomeUnknown { get; } = outcomeUnknown;
}
