using CommunityToolkit.Mvvm.ComponentModel;

namespace Echo.Desktop.Features.Lyrics;

public sealed partial class LyricsAppearance
{
    // Persisted by FeaturePreferences; the lyric-file settings store retains its original fields.
    [ObservableProperty] private bool enabled = true;
    [ObservableProperty] private bool hideSongInfo;
    [ObservableProperty] private bool currentTranslationOnly;
    [ObservableProperty] private double lineHeightPercent = 110;
    [ObservableProperty] private int maxCharacters;
    [ObservableProperty] private double contextOpacityPercent = 49;
    [ObservableProperty] private bool contextBlur;
}
