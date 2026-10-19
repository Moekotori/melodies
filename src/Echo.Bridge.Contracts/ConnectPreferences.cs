using Echo.Contracts.Settings;


namespace Echo.Bridge;

public static class ConnectPreferences
{
    public static string Read(IBridgePreferences preferences, string id) => preferences.Read(id, new()).Selection;
    public static bool Enabled(IBridgePreferences preferences, string id) => preferences.Read(id, new()).Enabled;

    public static void Write(IBridgePreferences preferences, string id, string selection, bool enabled = false)
    {
        if (id.Length is 0 or > 120) return;
        if (selection.Length > 4096) selection = selection[..4096];
        try { preferences.Confirm(id, new FeatureSetting(enabled, selection)); }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
    }

    public static IReadOnlyList<string> ReadKeys(IBridgePreferences preferences, string id)
        => Read(preferences, id).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(key => key.Trim()).Where(key => key.Length is > 0 and <= 160 && !key.Contains('\r')).Distinct(StringComparer.Ordinal).Take(24).ToArray();

    public static void WriteKeys(IBridgePreferences preferences, string id, IEnumerable<string> keys)
        => Write(preferences, id, string.Join('\n', keys.Where(key => key.Length is > 0 and <= 160).Distinct(StringComparer.Ordinal).Take(24)));
}
