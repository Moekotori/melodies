using System.Text.Json;
using System.Text.Json.Serialization;
using Echo.Contracts.Library;

namespace Echo.Infrastructure.Metadata;

internal static class TrackSourceJson
{
    public static string? Encode(TrackSource? source) => source is null ? null :
        JsonSerializer.Serialize(source, SourceJsonContext.Default.TrackSource);
    public static TrackSource? Decode(string? json) => json is null ? null :
        JsonSerializer.Deserialize(json, SourceJsonContext.Default.TrackSource);
}
[JsonSerializable(typeof(TrackSource))]
[JsonSerializable(typeof(OsuScanFacts))]
internal partial class SourceJsonContext : JsonSerializerContext;
