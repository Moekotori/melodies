using System.Text.Json;
using System.Text.Json.Serialization;

namespace Echo.Core.Contracts;

public enum DspResourceOperation { Library, SavePreset, DeletePreset, SaveProfile, DeleteProfile, ImportEqualizer, ImportImpulse, Banks, ResolveBank, SearchOpra, OpraPreset, DeleteBank, BrowseOpra, FavoriteOpra, RecentOpra }
public sealed record DspResourceRequest(int Version, string RequestId, DspResourceOperation Operation,
    string? Id = null, string? Name = null, string? Path = null, string? Query = null, int Offset = 0, JsonElement? State = null, bool Refresh = false);
public sealed record DspResourceResponse(int Version, string RequestId, string CoreId, string Generation, long Revision, JsonElement? Result, string? Error);
public interface IDspResourceClient
{
    Task<JsonElement> ExecuteAsync(DspResourceOperation operation, string? id = null, string? name = null,
        string? path = null, string? query = null, int offset = 0, JsonElement? state = null, bool refresh = false, CancellationToken token = default);
}
public static class CoreDspProtocol
{
    public const int Version = 1;
    public static string PipeName => "ecd1-" + CoreMetadataProtocol.CoreId.ToLowerInvariant();
}
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DspResourceRequest))]
[JsonSerializable(typeof(DspResourceResponse))]
public partial class CoreDspJson : JsonSerializerContext;
