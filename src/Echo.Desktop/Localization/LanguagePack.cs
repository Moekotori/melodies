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

    internal LanguagePack(string code, string nativeName, Dictionary<string, string> strings)
    {
        Code = code; NativeName = nativeName; Culture = CultureInfo.GetCultureInfo(code);
        Strings = strings.ToFrozenDictionary(StringComparer.Ordinal);
        Formats = strings.Where(pair => pair.Value.Contains('{') || pair.Value.Contains('}'))
            .ToFrozenDictionary(pair => pair.Key, pair => CompositeFormat.Parse(pair.Value), StringComparer.Ordinal);
    }
}
