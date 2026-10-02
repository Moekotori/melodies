using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;

namespace Echo.Desktop.Localization;

// Embedded JSON is read once, before the desktop event loop. Adding a pack needs no registry edit.
public sealed class LanguageCatalog
{
    private const string ResourcePrefix = "Echo.Desktop.Languages.";
    private static readonly Lazy<LanguageCatalog> BuiltIn = new(LoadEmbedded);
    public static LanguageCatalog Default => BuiltIn.Value;
    public const string FallbackCode = "zh-CN";
    public FrozenDictionary<string, LanguagePack> Packs { get; }
    public FrozenDictionary<string, string> SourceKeys { get; }
    public LanguagePack Fallback => Packs[FallbackCode];

    public LanguageCatalog(IEnumerable<LanguagePack> packs)
    {
        Packs = packs.ToFrozenDictionary(pack => pack.Code, StringComparer.OrdinalIgnoreCase);
        if (!Packs.ContainsKey(FallbackCode)) throw new InvalidDataException("Missing base language pack.");
        SourceKeys = Fallback.Strings.GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .ToFrozenDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);
        foreach (var pack in Packs.Values)
        foreach (var pair in pack.Strings)
        {
            if (!Fallback.Strings.ContainsKey(pair.Key)) throw new InvalidDataException($"Unknown translation key: {pack.Code}/{pair.Key}");
            var expected = Fallback.ArgumentSignatures.GetValueOrDefault(pair.Key) ?? "";
            var actual = pack.ArgumentSignatures.GetValueOrDefault(pair.Key) ?? "";
            if (expected != actual) throw new InvalidDataException($"Translation arguments differ: {pack.Code}/{pair.Key}");
        }
    }

    public LanguagePack Resolve(string? code)
    {
        if (code is null || code == "system") code = CultureInfo.CurrentUICulture.Name;
        if (Packs.TryGetValue(code, out var exact)) return exact;
        try
        {
            var culture = CultureInfo.GetCultureInfo(code);
            while (culture.Parent.Name.Length > 0)
            {
                culture = culture.Parent;
                if (Packs.TryGetValue(culture.Name, out var parent)) return parent;
            }
            return Packs.Values.OrderBy(pack => pack.Code, StringComparer.Ordinal)
                .FirstOrDefault(pack => pack.Culture.TwoLetterISOLanguageName == culture.TwoLetterISOLanguageName) ?? Fallback;
        }
        catch (CultureNotFoundException) { return Fallback; }
    }

    public static LanguagePack ReadPack(Stream stream)
    {
        // Bound input even for non-seekable streams. No dynamic serializer/reflection is required.
        using var buffer = new MemoryStream();
        var block = new byte[4096];
        int count;
        while ((count = stream.Read(block)) > 0)
        {
            if (buffer.Length + count > 256 * 1024) throw new InvalidDataException("Language pack exceeds 256 KiB.");
            buffer.Write(block, 0, count);
        }
        using var json = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 4 });
        var root = json.RootElement;
        var code = root.GetProperty("code").GetString() ?? throw new InvalidDataException("Missing culture code.");
        var nativeName = root.GetProperty("nativeName").GetString() ?? throw new InvalidDataException("Missing language name.");
        if (code.Length > 64 || nativeName.Length is 0 or > 80 || code == "system") throw new InvalidDataException("Invalid language metadata.");
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in root.GetProperty("strings").EnumerateObject())
        {
            var value = pair.Value.GetString();
            if (strings.Count >= 4096 || pair.Name.Length is 0 or > 160 || value is null || value.Length > 4096
                || !strings.TryAdd(pair.Name, value)) throw new InvalidDataException("Invalid or duplicate translation entry.");
        }
        return new LanguagePack(code, nativeName, strings);
    }

    private static LanguageCatalog LoadEmbedded()
    {
        var assembly = typeof(LanguageCatalog).Assembly;
        var names = assembly.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
        if (names.Length is 0 or > 16) throw new InvalidDataException("Language pack count must be between 1 and 16.");
        var packs = new List<LanguagePack>();
        foreach (var name in names)
        {
            using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException("Missing embedded language pack.");
            packs.Add(ReadPack(stream));
        }
        return new LanguageCatalog(packs);
    }
}
