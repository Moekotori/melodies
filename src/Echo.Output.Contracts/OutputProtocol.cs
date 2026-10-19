using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Echo.Playback;

namespace Echo.Output;

public static class OutputProtocol
{
    public const int Version = 1;
    public const int StreamBufferBytes = 64 * 1024;
    public const int MaximumStreams = 4;
    public const int MaximumTickets = 1024;
}
public sealed record OutputStart(int Version, int Channels);
public sealed record OutputReady(int Version, Guid Generation, string MediaIdentity, Guid MediaClientId,
    string MediaBaseAddress, int Channels, JsonElement? NativeCapabilities);
public sealed record OutputCommand(Guid Generation, string Method, JsonElement Parameters);
public sealed record OutputEvent(Guid Generation, string Method, JsonElement Parameters);
public sealed record OutputDevices(string Mode);
public sealed record OutputChannels(int Channels, bool ForceRestart);
public sealed record OutputMediaTicket(string Id, long Length, string Extension)
{
    public override string ToString() => "Output media capability (opaque)";
}
public sealed record OutputMediaRequest(int Version, string Identity, Guid ClientId, string AccessToken,
    string Ticket, long Offset, long Count)
{
    public override string ToString() => "Output media request (credentials omitted)";
}
public sealed record OutputMediaReply(bool Available, long Length = 0, long Offset = 0, long Count = 0);
public static class OutputJson
{
    public static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, Info<T>());
    public static T Read<T>(JsonElement value) => JsonSerializer.Deserialize(value, Info<T>())!;
    private static JsonTypeInfo<T> Info<T>() => OutputJsonContext.Default.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>
        ?? throw new InvalidOperationException("Unknown Output protocol type.");
}
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OutputStart))]
[JsonSerializable(typeof(OutputReady))]
[JsonSerializable(typeof(OutputCommand))]
[JsonSerializable(typeof(OutputEvent))]
[JsonSerializable(typeof(OutputDevices))]
[JsonSerializable(typeof(OutputChannels))]
[JsonSerializable(typeof(OutputMediaTicket[]))]
[JsonSerializable(typeof(OutputMediaRequest))]
[JsonSerializable(typeof(OutputMediaReply))]
[JsonSerializable(typeof(AudioOutputDevice[]))]
public partial class OutputJsonContext : JsonSerializerContext;
