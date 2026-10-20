using System.Text.Json;
using Echo.Contracts.Radio;

namespace Echo.Infrastructure.Radio;

public static class RadioDirectoryParser
{
    public static RadioDirectoryQuery Normalize(RadioDirectoryQuery query)
    {
        var country = Trim(query.CountryCode, 2).ToUpperInvariant();
        if (country.Length != 2 || country.Any(character => character is < 'A' or > 'Z')) country = "";
        var offset = query.Offset;
        if (offset < 0 || offset > RadioDirectoryLimits.MaxOffset) offset = 0;
        return new(Trim(query.Name, 128), country, Trim(query.Language, 64), Trim(query.Tag, 64), offset);
    }

    public static RadioDirectoryResult Parse(ReadOnlyMemory<byte> json, int offset)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        if (document.RootElement.ValueKind != JsonValueKind.Array
            || document.RootElement.GetArrayLength() > RadioDirectoryLimits.PageSize + 1)
            throw new InvalidDataException("电台目录响应无效。");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stations = new List<RadioDirectoryStation>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (stations.Count == RadioDirectoryLimits.PageSize) break;
            if (Read(item) is { } station && seen.Add(station.Uuid)) stations.Add(station);
        }
        var hasMore = document.RootElement.GetArrayLength() > RadioDirectoryLimits.PageSize && offset < RadioDirectoryLimits.MaxOffset;
        return new(stations, hasMore, null);
    }

    public static bool IsOfficialHost(string host)
    {
        const string suffix = ".api.radio-browser.info";
        if (!host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) || host.Contains('/')) return false;
        var label = host[..^suffix.Length];
        return label.Length is > 0 and <= 63 && !label.Contains('.')
            && label.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
    }

    private static RadioDirectoryStation? Read(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("lastcheckok", out var ok) || ok.ValueKind != JsonValueKind.Number || !ok.TryGetInt32(out var check) || check != 1)
            return null;
        var uuid = Text(item, "stationuuid", 36);
        var name = Text(item, "name", 160);
        if (uuid.Length != 36 || name.Length == 0) return null;
        var stream = Text(item, "url_resolved", 2048);
        if (stream.Length == 0) stream = Text(item, "url", 2048);
        try
        {
            var url = RadioHttpAddress.Normalize(stream);
            var bitrate = item.TryGetProperty("bitrate", out var rate) && rate.ValueKind == JsonValueKind.Number && rate.TryGetInt32(out var number)
                ? Math.Clamp(number, 0, 10_000) : 0;
            return new(uuid, name, url, Text(item, "countrycode", 2), Text(item, "language", 64),
                Text(item, "tags", 256), Text(item, "codec", 16), bitrate);
        }
        catch (IOException) { return null; }
    }

    private static string Text(JsonElement item, string name, int limit)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return "";
        var text = value.GetString()?.Trim() ?? "";
        return text.Length <= limit ? text : text[..limit];
    }

    private static string Trim(string? value, int limit)
    {
        var text = value?.Trim() ?? "";
        return text.Length <= limit ? text : text[..limit];
    }
}
