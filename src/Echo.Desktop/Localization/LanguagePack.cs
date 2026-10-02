using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace Echo.Desktop.Localization;

public sealed record LanguageChoice(string Code, string Label);

// Immutable, shared between windows. No visual objects or mutable process-wide culture.
public sealed class LanguagePack
{
    public string Code { get; }
    public string NativeName { get; }
    public CultureInfo Culture { get; }
    public FrozenDictionary<string, string> Strings { get; }
    public FrozenDictionary<string, CompositeFormat> Formats { get; }
    internal FrozenDictionary<string, string> ArgumentSignatures { get; }

    internal LanguagePack(string code, string nativeName, Dictionary<string, string> strings)
    {
        Code = code; NativeName = nativeName; Culture = CultureInfo.GetCultureInfo(code);
        Strings = strings.ToFrozenDictionary(StringComparer.Ordinal);
        Formats = strings.Where(pair => pair.Value.Contains('{') || pair.Value.Contains('}'))
            .ToFrozenDictionary(pair => pair.Key, pair => CompositeFormat.Parse(pair.Value), StringComparer.Ordinal);
        ArgumentSignatures = Formats.Keys.ToFrozenDictionary(key => key, key => Arguments(strings[key]), StringComparer.Ordinal);
    }

    private static string Arguments(string template)
    {
        var arguments = new SortedSet<int>();
        for (var index = 0; index < template.Length; index++)
        {
            if (template[index] != '{') continue;
            if (index + 1 < template.Length && template[index + 1] == '{') { index++; continue; }
            var start = ++index;
            while (index < template.Length && char.IsAsciiDigit(template[index])) index++;
            arguments.Add(int.Parse(template.AsSpan(start, index - start), CultureInfo.InvariantCulture));
        }
        return string.Join(',', arguments);
    }
}
