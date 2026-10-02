using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Echo.Infrastructure.Search;

/// <summary>Shared immutable dictionaries, loaded on first cross-script search; no query cache.</summary>
internal static class ChineseSearchVariants
{
    private sealed class Table
    {
        private readonly Dictionary<string, string> _words = new(StringComparer.Ordinal);
        private readonly int _maxLength;
        public Table(IEnumerable<string> dictionaries)
        {
            foreach (var dictionary in dictionaries.Reverse())
                foreach (var entry in dictionary.Split('|'))
                {
                    var parts = entry.Split(' ');
                    _words[parts[0]] = parts[1];
                    _maxLength = Math.Max(_maxLength, parts[0].Length);
                }
        }
        private (int Length, string? Value) Match(string value, int offset)
        {
            var lookup = _words.GetAlternateLookup<ReadOnlySpan<char>>();
            for (var length = Math.Min(_maxLength, value.Length - offset); length > 0; length--)
                if (lookup.TryGetValue(value.AsSpan(offset, length), out var replacement)) return (length, replacement);
            return (0, null);
        }
        public IEnumerable<string> Segment(string value)
        {
            var pending = 0;
            for (var i = 0; i < value.Length;)
            {
                var match = Match(value, i);
                if (match.Length == 0) { i += char.IsSurrogatePair(value, i) ? 2 : 1; continue; }
                if (pending < i) yield return value[pending..i];
                yield return value.Substring(i, match.Length);
                i += match.Length;
                pending = i;
            }
            if (pending < value.Length) yield return value[pending..];
        }
        public string Convert(string value)
        {
            var output = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length;)
            {
                var match = Match(value, i);
                if (match.Length > 0) { output.Append(match.Value); i += match.Length; }
                else { output.Append(value[i]); i++; }
            }
            return output.ToString();
        }
    }
    private sealed record Converter(Table? Segmentation, Table[] Chain)
    {
        public string Convert(string value) => string.Concat((Segmentation?.Segment(value) ?? [value])
            .Select(segment => Chain.Aggregate(segment, (text, table) => table.Convert(text))));
    }
    private static readonly Lazy<Converter[]> Converters = new(Load);
    private static Converter[] Load()
    {
        using var resource = typeof(ChineseSearchVariants).Assembly.GetManifestResourceStream("Echo.Infrastructure.Search.Data.opencc-search.json.gz")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        var dictionaries = json.RootElement.GetProperty("dictionaries").EnumerateArray().Select(x => x.GetString()!).ToArray();
        var tables = new Dictionary<string, Table>();
        Table Get(int[] ids)
        {
            var key = string.Join(',', ids);
            if (!tables.TryGetValue(key, out var table)) tables[key] = table = new(ids.Select(id => dictionaries[id]));
            return table;
        }
        return json.RootElement.GetProperty("converters").EnumerateArray().Select(config => new Converter(
            config.GetProperty("segmentation") is var segmentation && segmentation.ValueKind == JsonValueKind.Number ? Get([segmentation.GetInt32()]) : null,
            config.GetProperty("chain").EnumerateArray().Select(group => Get(group.EnumerateArray().Select(x => x.GetInt32()).ToArray())).ToArray())).ToArray();
    }
    public static List<string> Expand(string value, bool enabled)
    {
        if (!enabled) return [value];
        value = value.Normalize(NormalizationForm.FormKC).Trim();
        if (value.Length == 0) return [];
        return new[] { value }.Concat(Converters.Value.Select(converter => converter.Convert(value))).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToList();
    }
}
