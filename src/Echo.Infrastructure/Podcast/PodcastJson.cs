using System.Text.Json.Serialization;
using Echo.Contracts.Podcast;

namespace Echo.Infrastructure.Podcast;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PodcastFeed))]
[JsonSerializable(typeof(PodcastEpisode))]
internal partial class PodcastJsonContext : JsonSerializerContext;

internal readonly record struct PodcastValidators(string? Etag, string? LastModified)
{
    public static PodcastValidators Read(string? etag, string? modified) => new(Clean(etag), Clean(modified));
    public PodcastValidators Or(PodcastValidators previous) => new(Etag ?? previous.Etag, LastModified ?? previous.LastModified);
    private static string? Clean(string? value)
        => value is { Length: > 0 and <= 1024 } && !value.Contains('\r') && !value.Contains('\n') ? value : null;
}
