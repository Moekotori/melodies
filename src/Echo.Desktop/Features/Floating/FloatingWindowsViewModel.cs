using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Settings;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Floating;

// Local preferences and commands only; the window controller owns all resources.
public sealed partial class FloatingWindowsViewModel : ObservableObject
{
    public LocalizationService Text { get; }
    [ObservableProperty] private bool supportsPassthrough;
    [ObservableProperty] private bool miniEnabled;
    [ObservableProperty] private bool lyricsEnabled;
    [ObservableProperty] private bool miniLocked;
    [ObservableProperty] private bool lyricsLocked;
    [ObservableProperty] private bool miniTopmost = true;
    [ObservableProperty] private bool doubleLine = true;
    [ObservableProperty] private bool translation = true;
    [ObservableProperty] private bool romanization;
    [ObservableProperty] private bool hideWithoutLyrics;
    [ObservableProperty] private double fontSize = 32;
    [ObservableProperty] private double secondarySize = 20;
    [ObservableProperty] private double opacity = 1;
    [ObservableProperty] private string fontFamily = "avares://Echo.Desktop/Resources/Fonts#Outfit, Microsoft YaHei, sans-serif";
    [ObservableProperty] private string color = "#FFFFFF";
    [ObservableProperty] private string highlightColor = "#A8D6FF";
    [ObservableProperty] private double offsetMs;
    [ObservableProperty] private string diagnostic = "";
    public FloatingBounds? MiniBounds { get; private set; }
    public FloatingBounds? LyricsBounds { get; private set; }
    public event Action? PreferencesChanged;
    public event Action? ResetRequested;
    public event Action? RestoreRequested;
    public event Action? MinimizeRequested;
    internal bool Restoring { get; private set; }
    public FloatingWindowsViewModel(LocalizationService text)
    {
        Text = text;
        PropertyChanged += (_, e) => { if (!Restoring && e.PropertyName != nameof(Diagnostic)) PreferencesChanged?.Invoke(); };
    }
    [RelayCommand] private void ToggleMini() => MiniEnabled = !MiniEnabled;
    [RelayCommand] private void ToggleLyrics() => LyricsEnabled = !LyricsEnabled;
    [RelayCommand] private void ToggleLock() => LyricsLocked = !LyricsLocked;
    [RelayCommand] private void RestoreMain() => RestoreRequested?.Invoke();
    [RelayCommand] private void UseMini()
    { MiniEnabled = true; MinimizeRequested?.Invoke(); }
    [RelayCommand] private void ResetPosition()
    {
        MiniBounds = null; LyricsBounds = null; LyricsLocked = false;
        ResetRequested?.Invoke(); PreferencesChanged?.Invoke();
    }
    public FloatingPreferences Snapshot() => new(MiniEnabled, LyricsEnabled, MiniLocked, LyricsLocked, MiniTopmost,
        DoubleLine, Translation, Romanization, HideWithoutLyrics, FontSize, SecondarySize, Opacity,
        FontFamily, Color, HighlightColor, OffsetMs, MiniBounds, LyricsBounds);
    public void Restore(FloatingPreferences? saved)
    {
        if (saved is null) return;
        Restoring = true;
        try
        {
            MiniLocked = saved.MiniLocked; LyricsLocked = saved.LyricsLocked && SupportsPassthrough;
            MiniTopmost = saved.MiniTopmost; DoubleLine = saved.DoubleLine;
            Translation = saved.Translation; Romanization = saved.Romanization; HideWithoutLyrics = saved.HideWithoutLyrics;
            FontSize = Finite(saved.FontSize, 16, 72, 32); SecondarySize = Finite(saved.SecondarySize, 10, 40, 20);
            Opacity = Finite(saved.Opacity, .2, 1, 1); OffsetMs = Finite(saved.OffsetMs, -10000, 10000, 0);
            FontFamily = string.IsNullOrWhiteSpace(saved.FontFamily) || saved.FontFamily.Length > 256 ? FontFamily : saved.FontFamily;
            Color = ValidColor(saved.Color) ? saved.Color : "#FFFFFF";
            HighlightColor = ValidColor(saved.HighlightColor) ? saved.HighlightColor : "#A8D6FF";
            MiniBounds = saved.MiniBounds; LyricsBounds = saved.LyricsBounds;
            MiniEnabled = saved.MiniEnabled; LyricsEnabled = saved.LyricsEnabled;
        }
        finally { Restoring = false; }
        PreferencesChanged?.Invoke();
    }
    internal void Remember(bool mini, FloatingBounds bounds)
    {
        if (mini) MiniBounds = bounds; else LyricsBounds = bounds;
        if (!Restoring) PreferencesChanged?.Invoke();
    }
    private static double Finite(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    private static bool ValidColor(string? value) => value is { Length: 7 } && value[0] == '#' && value.AsSpan(1).IndexOfAnyExcept("0123456789ABCDEFabcdef") < 0;
}
