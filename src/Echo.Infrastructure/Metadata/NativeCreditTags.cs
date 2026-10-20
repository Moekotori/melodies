using System.Text.Json;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;

namespace Echo.Infrastructure.Metadata;

internal static class NativeCreditTags
{
    public static IReadOnlyList<CreditTag>? Read(JsonElement result)
    {
        if (result.TryGetProperty("creditTagsExceeded", out var exceeded) && exceeded.GetBoolean())
            throw new MetadataReadException("metadata_credit_limit");
        if (!result.TryGetProperty("creditTags", out var values)) return null;
        var tags = new List<CreditTag>();
        foreach (var value in values.EnumerateArray())
        {
            tags.Add(new(value.GetProperty("tag").GetString()!, value.GetProperty("value").GetString()!));
            if (tags.Count > 64) throw new MetadataReadException("metadata_credit_limit");
        }
        MetadataRules.ValidateCredits(tags); return tags;
    }
}
