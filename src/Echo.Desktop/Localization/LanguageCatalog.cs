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
    public LanguagePack Fallback { get; }
    private readonly FrozenDictionary<string, LanguagePack> _builtInPacks;

    public LanguageCatalog(IEnumerable<LanguagePack> packs, LanguagePack? sourceLanguage = null, LanguageCatalog? builtIn = null)
    {
        Packs = packs.ToFrozenDictionary(pack => pack.Code, StringComparer.OrdinalIgnoreCase);
        _builtInPacks = builtIn?._builtInPacks ?? Packs;
        if (!Packs.ContainsKey(FallbackCode)) throw new InvalidDataException("Missing base language pack.");
        // Keep the original source index even when a user overrides the Chinese pack.
        Fallback = sourceLanguage ?? Packs[FallbackCode];
        SourceKeys = Fallback.Strings.GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .ToFrozenDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);
        foreach (var pack in Packs.Values)
            ValidatePack(pack);
    }

    public void ValidatePack(LanguagePack pack)
    {
        foreach (var pair in pack.Strings)
        {
            if (!Fallback.Strings.ContainsKey(pair.Key)) throw new InvalidDataException($"Unknown translation key: {pack.Code}/{pair.Key}");
            var expected = Fallback.ArgumentSignatures.GetValueOrDefault(pair.Key) ?? "";
            var actual = pack.ArgumentSignatures.GetValueOrDefault(pair.Key) ?? "";
            if (expected != actual) throw new InvalidDataException($"Translation arguments differ: {pack.Code}/{pair.Key}");
        }
    }

    public LanguageCatalog WithUserPacks(IEnumerable<LanguagePack> users)
    {
        var packs = Packs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var user in users)
        {
            ValidatePack(user);
            var values = packs.TryGetValue(user.Code, out var builtIn)
                ? builtIn.Strings.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) : new(StringComparer.Ordinal);
            foreach (var pair in user.Strings) values[pair.Key] = pair.Value;
            packs[user.Code] = new LanguagePack(user.Code, user.NativeName, values, isUser: true);
        }
        return new LanguageCatalog(packs.Values, Fallback, this);
    }
    internal LanguagePack BuiltInFor(string code) => _builtInPacks.GetValueOrDefault(code) ?? Fallback;

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

    public static LanguagePack ReadPack(Stream stream, CancellationToken cancellation = default)
    {
        // Bound input even for non-seekable streams. No dynamic serializer/reflection is required.
        using var buffer = new MemoryStream();
        var block = new byte[4096];
        int count;
        cancellation.ThrowIfCancellationRequested();
        while ((count = stream.Read(block)) > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (buffer.Length + count > 256 * 1024) throw new InvalidDataException("Language pack exceeds 256 KiB.");
            buffer.Write(block, 0, count);
        }
        cancellation.ThrowIfCancellationRequested();
        using var json = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 4 });
        var root = json.RootElement;
        var code = root.GetProperty("code").GetString() ?? throw new InvalidDataException("Missing culture code.");
        var nativeName = root.GetProperty("nativeName").GetString() ?? throw new InvalidDataException("Missing language name.");
        if (code.Length is 0 or > 64 || code.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')
            || string.IsNullOrWhiteSpace(nativeName) || nativeName.Length > 80 || code.Equals("system", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid language metadata.");
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
