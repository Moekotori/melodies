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
    public bool IsUser { get; }
    public CultureInfo Culture { get; }
    public FrozenDictionary<string, string> Strings { get; }
    public FrozenDictionary<string, CompositeFormat> Formats { get; }
    internal FrozenDictionary<string, string> ArgumentSignatures { get; }

    internal LanguagePack(string code, string nativeName, Dictionary<string, string> strings, bool isUser = false)
    {
        Culture = CultureInfo.GetCultureInfo(code); Code = Culture.Name; NativeName = nativeName; IsUser = isUser;
        Strings = strings.ToFrozenDictionary(StringComparer.Ordinal);
        Formats = strings.Where(pair => pair.Value.Contains('{') || pair.Value.Contains('}'))
            .ToFrozenDictionary(pair => pair.Key, pair => CompositeFormat.Parse(pair.Value), StringComparer.Ordinal);
        ArgumentSignatures = Formats.Keys.ToFrozenDictionary(key => key, key => Arguments(strings[key]), StringComparer.Ordinal);
    }

    private static string Arguments(string template)
    {
        var arguments = new SortedSet<int>();
        var fields = 0;
        for (var index = 0; index < template.Length; index++)
        {
            if (template[index] != '{') continue;
            if (index + 1 < template.Length && template[index + 1] == '{') { index++; continue; }
            var start = ++index;
            while (index < template.Length && char.IsAsciiDigit(template[index])) index++;
            arguments.Add(int.Parse(template.AsSpan(start, index - start), CultureInfo.InvariantCulture));
            if (++fields > 64) throw new InvalidDataException("A translation may contain at most 64 format fields.");
            while (index < template.Length && template[index] == ' ') index++;
            if (index < template.Length && template[index] == ',')
            {
                var alignmentStart = ++index;
                while (index < template.Length && template[index] != ':' && template[index] != '}') index++;
                if (!int.TryParse(template.AsSpan(alignmentStart, index - alignmentStart), CultureInfo.InvariantCulture, out var width)
                    || width < -256 || width > 256) throw new InvalidDataException("Translation alignment must be between -256 and 256.");
            }
            if (index < template.Length && template[index] == ':')
            {
                var formatStart = ++index;
                while (index < template.Length && template[index] != '}') index++;
                var format = template.AsSpan(formatStart, index - formatStart);
                if (format.Length > 64) throw new InvalidDataException("Translation format specifier exceeds 64 characters.");
                if (format.Length > 1 && char.IsAsciiLetter(format[0]) && IsDigits(format[1..])
                    && (!int.TryParse(format[1..], out var precision) || precision > 32))
                    throw new InvalidDataException("Translation numeric precision exceeds 32 digits.");
            }
        }
        return string.Join(',', arguments);
    }
    private static bool IsDigits(ReadOnlySpan<char> text)
    { foreach (var character in text) if (!char.IsAsciiDigit(character)) return false; return true; }
}
