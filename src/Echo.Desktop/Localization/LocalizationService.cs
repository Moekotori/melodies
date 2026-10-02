namespace Echo.Desktop.Localization;

// One owner per window. Lookup is a frozen-dictionary read; only a language change raises an event.
public sealed class LocalizationService
{
    private readonly HashSet<string> _invalidUserFormats = new(StringComparer.Ordinal);
    public LanguageCatalog Catalog { get; private set; }
    public LanguagePack Current { get; private set; }
    public string Preference { get; private set; } = "system";
    public event Action? Changed;
    public IReadOnlyList<LanguageChoice> Languages { get; private set; }

    public LocalizationService(LanguageCatalog? catalog = null)
    {
        Catalog = catalog ?? LanguageCatalog.Default; Current = Catalog.Resolve("system");
        Languages = CreateChoices();
    }
    public bool SetLanguage(string? preference)
    {
        preference = NormalizePreference(preference);
        if (Preference == preference) return false;
        Preference = preference; Current = Catalog.Resolve(preference);
        _invalidUserFormats.Clear();
        Languages = CreateChoices(); Changed?.Invoke(); return true;
    }
    public void ReplaceCatalog(LanguageCatalog catalog, string? selectedCode = null)
    {
        Catalog = catalog; Preference = NormalizePreference(selectedCode ?? Preference);
        _invalidUserFormats.Clear();
        Current = Catalog.Resolve(Preference); Languages = CreateChoices(); Changed?.Invoke();
    }
    private string NormalizePreference(string? preference) => string.IsNullOrWhiteSpace(preference) || preference.Length > 64
        || preference.Equals("system", StringComparison.OrdinalIgnoreCase) ? "system"
        : Catalog.Packs.TryGetValue(preference, out var supported) ? supported.Code : preference;
    public string this[string key] => Current.Strings.GetValueOrDefault(key) ?? Catalog.Fallback.Strings.GetValueOrDefault(key) ?? key;
    // Bridge for existing catalog definitions. New code should use stable keys directly.
    public string Translate(string source) => Catalog.SourceKeys.TryGetValue(source, out var key) ? this[key] : source;
    public string Format(string key, params object?[] arguments)
    {
        if (Current.IsUser && _invalidUserFormats.Contains(key)) return FormatFallback(key, arguments);
        var pack = Current.Strings.ContainsKey(key) ? Current : Catalog.Fallback;
        try { return pack.Formats.TryGetValue(key, out var format) ? string.Format(Current.Culture, format, arguments) : this[key]; }
        catch (FormatException) when (Current.IsUser)
        {
            // Runtime argument types can reject otherwise valid .NET format specifiers.
            // Memoize the fallback, so a broken user specifier does not repeatedly throw during updates.
            _invalidUserFormats.Add(key); return FormatFallback(key, arguments);
        }
    }
    private string FormatFallback(string key, object?[] arguments)
    {
        var original = Catalog.BuiltInFor(Current.Code);
        if (original.Formats.TryGetValue(key, out var format) || Catalog.Fallback.Formats.TryGetValue(key, out format))
            return string.Format(Current.Culture, format, arguments);
        return original.Strings.GetValueOrDefault(key) ?? Catalog.Fallback.Strings.GetValueOrDefault(key) ?? key;
    }
    private LanguageChoice[] CreateChoices()
    {
        var choices = new List<LanguageChoice> { new("system", this["language.followSystem"]) };
        choices.AddRange(Catalog.Packs.Values.OrderBy(pack => pack.Code, StringComparer.Ordinal)
            .Select(pack => new LanguageChoice(pack.Code, pack.IsUser ? Format("language.customLabel", pack.NativeName) : pack.NativeName)));
        if (Preference != "system" && !Catalog.Packs.ContainsKey(Preference))
            choices.Add(new(Preference, Format("language.unavailable", Preference)));
        return choices.ToArray();
    }
}
