namespace Echo.Desktop.Localization;

// One owner per window. Lookup is a frozen-dictionary read; only a language change raises an event.
public sealed class LocalizationService
{
    public LanguageCatalog Catalog { get; }
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
        preference = preference == "system" || preference is null ? "system"
            : Catalog.Packs.TryGetValue(preference, out var supported) ? supported.Code : "system";
        if (Preference == preference) return false;
        Preference = preference; Current = Catalog.Resolve(preference);
        Languages = CreateChoices(); Changed?.Invoke(); return true;
    }
    public string this[string key] => Current.Strings.GetValueOrDefault(key) ?? Catalog.Fallback.Strings.GetValueOrDefault(key) ?? key;
    // Bridge for existing catalog definitions. New code should use stable keys directly.
    public string Translate(string source) => Catalog.SourceKeys.TryGetValue(source, out var key) ? this[key] : source;
    public string Format(string key, params object?[] arguments)
    {
        var pack = Current.Strings.ContainsKey(key) ? Current : Catalog.Fallback;
        return pack.Formats.TryGetValue(key, out var format) ? string.Format(Current.Culture, format, arguments) : this[key];
    }
    private LanguageChoice[] CreateChoices() => new[] { new LanguageChoice("system", this["language.followSystem"]) }
        .Concat(Catalog.Packs.Values.OrderBy(pack => pack.Code, StringComparer.Ordinal)
            .Select(pack => new LanguageChoice(pack.Code, pack.NativeName))).ToArray();
}
