using Echo.Contracts.Settings;
using Echo.Desktop.Features.Lyrics;

namespace Echo.Desktop.Features.Settings.Runtime;

public sealed class LyricsSettingsModule : IDisposable
{
    private readonly FeaturePreferences preferences;
    private readonly Dictionary<string, Action<FeatureSetting>> consumers;
    public LyricsSettingsModule(SettingsFeatureRuntime runtime, FeaturePreferences preferences, LyricsAppearance lyrics)
    {
        this.preferences = preferences;
        consumers = new(StringComparer.Ordinal)
        {
            ["lyrics-enabled"] = value => lyrics.Enabled = value.Enabled,
            ["lyrics-hide-song-info"] = value => lyrics.HideSongInfo = value.Enabled,
            ["lyrics-current-translation"] = value => lyrics.CurrentTranslationOnly = value.Enabled,
            ["lyrics-context-blur"] = value => lyrics.ContextBlur = value.Enabled,
            ["lyrics-line-height"] = value => lyrics.LineHeightPercent = Number(value, 80, 200),
            ["lyrics-context-opacity"] = value => lyrics.ContextOpacityPercent = Number(value, 0, 100),
            ["lyrics-max-characters"] = value => lyrics.MaxCharacters = value.Selection switch
                { "自动" => 0, "12" => 12, "18" => 18, "24" => 24, "32" => 32, _ => throw new ArgumentException("无效的每行字数。") }
        };
        foreach (var (id, consume) in consumers)
            runtime.RegisterChange(id, (value, _) => { consume(value); return Task.CompletedTask; });
        preferences.Changed += Restore;
    }
    private static double Number(FeatureSetting value, double min, double max) => double.IsFinite(value.Number) && value.Number >= min && value.Number <= max
        ? value.Number : throw new ArgumentOutOfRangeException(nameof(value), "歌词参数超出允许范围。");
    private void Restore(string id)
    {
        if (id.Length != 0) return;
        var snapshot = preferences.Snapshot();
        foreach (var (key, consume) in consumers)
            if (snapshot.TryGetValue(key, out var value)) try { consume(value); } catch (ArgumentException) { }
    }
    public void Dispose() => preferences.Changed -= Restore;
}
