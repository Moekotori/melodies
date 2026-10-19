using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Settings;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Floating;

public sealed record FloatingChoice(string Value, string Label);

// Local preferences and commands only; the window controller owns all resources.
public sealed partial class FloatingWindowsViewModel : ObservableObject
{
    private static readonly HashSet<string> Persisted = [
        nameof(MiniEnabled), nameof(LyricsEnabled), nameof(MiniLocked), nameof(LyricsLocked), nameof(MiniTopmost),
        nameof(WordHighlight), nameof(DoubleLine), nameof(Translation), nameof(Romanization), nameof(HideWithoutLyrics),
        nameof(FontSize), nameof(SecondarySize), nameof(Opacity), nameof(FontFamily), nameof(Color), nameof(HighlightColor), nameof(OffsetMs),
        nameof(ScalePercent), nameof(ColorMode), nameof(StrokeColor), nameof(GradientStart), nameof(GradientEnd),
        nameof(TextDirection), nameof(TextAlign), nameof(Interpolated), nameof(DropShadow), nameof(StrokeEnabled), nameof(IndependentRows),
        nameof(UnplayedTop), nameof(UnplayedBottom), nameof(OutlinePlayed), nameof(OutlineUnplayed),
        nameof(LowerPlayedTop), nameof(LowerPlayedBottom), nameof(LowerUnplayedTop), nameof(LowerUnplayedBottom),
        nameof(LowerOutlinePlayed), nameof(LowerOutlineUnplayed), nameof(FontFilePath)];

    public LocalizationService Text { get; }
    [ObservableProperty] private bool supportsPassthrough;
    [ObservableProperty] private bool miniEnabled;
    [ObservableProperty] private bool lyricsEnabled;
    [ObservableProperty] private bool miniLocked;
    [ObservableProperty] private bool lyricsLocked;
    [ObservableProperty] private bool miniTopmost = true;
    [ObservableProperty] private bool doubleLine = true;
    [ObservableProperty] private bool translation = true;
    [ObservableProperty] private bool romanization = true;
    [ObservableProperty] private bool hideWithoutLyrics;
    [ObservableProperty] private double fontSize = 34;
    [ObservableProperty] private double secondarySize = 19;
    [ObservableProperty] private double opacity = 0.96;
    [ObservableProperty] private string fontFamily = DesktopLyricsFont.Default;
    [ObservableProperty] private string color = "#FFFFFF";
    [ObservableProperty] private string highlightColor = "#A8D6FF";
    [ObservableProperty] private double offsetMs;
    [ObservableProperty] private double scalePercent = 100;
    [ObservableProperty] private string colorMode = "theme";
    [ObservableProperty] private string strokeColor = "#111827";
    [ObservableProperty] private string gradientStart = "#4F46E5";
    [ObservableProperty] private string gradientEnd = "#EC4899";
    [ObservableProperty] private string textDirection = "horizontal";
    [ObservableProperty] private string textAlign = "center";
    [ObservableProperty] private bool wordHighlight = true;
    [ObservableProperty] private bool interpolated = true;
    [ObservableProperty] private bool dropShadow = true;
    [ObservableProperty] private bool strokeEnabled = true;
    [ObservableProperty] private bool independentRows;
    [ObservableProperty] private string unplayedTop = "#FFFFFF";
    [ObservableProperty] private string unplayedBottom = "#CCCCCC";
    [ObservableProperty] private string outlinePlayed = "#111827";
    [ObservableProperty] private string outlineUnplayed = "#111827";
    [ObservableProperty] private string lowerPlayedTop = "#FFFFFF";
    [ObservableProperty] private string lowerPlayedBottom = "#FFFFFF";
    [ObservableProperty] private string lowerUnplayedTop = "#FFFFFF";
    [ObservableProperty] private string lowerUnplayedBottom = "#CCCCCC";
    [ObservableProperty] private string lowerOutlinePlayed = "#111827";
    [ObservableProperty] private string lowerOutlineUnplayed = "#111827";
    [ObservableProperty] private string fontFilePath = "";
    [ObservableProperty] private bool canNudgeLyrics;
    [ObservableProperty] private string diagnostic = "";
    public bool IsCustomMode => ColorMode == "custom";
    public bool IsGradientMode => ColorMode == "gradient";
    public bool IsVertical => TextDirection == "vertical";
    public bool HasOffset => CanNudgeLyrics && Math.Abs(OffsetMs) >= 1;
    public string OffsetText => DesktopLyricsLayout.FormatOffset(OffsetMs);
    partial void OnOffsetMsChanged(double value) { OnPropertyChanged(nameof(OffsetText)); OnPropertyChanged(nameof(HasOffset)); }
    partial void OnCanNudgeLyricsChanged(bool value) => OnPropertyChanged(nameof(HasOffset));
    public string OpacityText => $"{Math.Round(Opacity * 100):0}%";
    public string ScaleText => $"{Math.Round(ScalePercent):0}%";
    public string FontFileName => string.IsNullOrWhiteSpace(FontFilePath) ? "" : Path.GetFileName(FontFilePath);
    public IReadOnlyList<FloatingChoice> ColorModeChoices { get; private set; } = [];
    public IReadOnlyList<FloatingChoice> DirectionChoices { get; private set; } = [];
    public IReadOnlyList<FloatingChoice> AlignChoices { get; private set; } = [];
    public FloatingChoice ColorModeChoice
    {
        get => ColorModeChoices.First(choice => choice.Value == ColorMode);
        set { if (value is not null && value.Value != ColorMode) ColorMode = value.Value; }
    }
    public FloatingChoice DirectionChoice
    {
        get => DirectionChoices.First(choice => choice.Value == TextDirection);
        set { if (value is not null && value.Value != TextDirection) TextDirection = value.Value; }
    }
    public FloatingChoice AlignChoice
    {
        get => AlignChoices.First(choice => choice.Value == TextAlign);
        set { if (value is not null && value.Value != TextAlign) TextAlign = value.Value; }
    }
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
        RebuildChoices();
        Text.Changed += RebuildChoices;
        PropertyChanged += (_, e) =>
        {
            if (!Restoring && e.PropertyName is not null && Persisted.Contains(e.PropertyName)) PreferencesChanged?.Invoke();
        };
    }
    partial void OnColorModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsCustomMode)); OnPropertyChanged(nameof(IsGradientMode)); OnPropertyChanged(nameof(ColorModeChoice));
    }
    partial void OnTextDirectionChanged(string value)
    { OnPropertyChanged(nameof(IsVertical)); OnPropertyChanged(nameof(DirectionChoice)); }
    partial void OnTextAlignChanged(string value) => OnPropertyChanged(nameof(AlignChoice));
    partial void OnOpacityChanged(double value) => OnPropertyChanged(nameof(OpacityText));
    partial void OnScalePercentChanged(double value) => OnPropertyChanged(nameof(ScaleText));
    partial void OnFontFilePathChanged(string value) => OnPropertyChanged(nameof(FontFileName));
    private void RebuildChoices()
    {
        ColorModeChoices = [new("theme", Text["floating.colorMode.theme"]), new("custom", Text["floating.colorMode.custom"]), new("gradient", Text["floating.colorMode.gradient"])];
        DirectionChoices = [new("horizontal", Text["floating.direction.horizontal"]), new("vertical", Text["floating.direction.vertical"])];
        AlignChoices = [new("center", Text["floating.align.center"]), new("left", Text["floating.align.left"])];
        OnPropertyChanged(nameof(ColorModeChoices)); OnPropertyChanged(nameof(DirectionChoices)); OnPropertyChanged(nameof(AlignChoices));
        OnPropertyChanged(nameof(ColorModeChoice)); OnPropertyChanged(nameof(DirectionChoice)); OnPropertyChanged(nameof(AlignChoice));
    }
    [RelayCommand] private void ToggleMini() => MiniEnabled = !MiniEnabled;
    [RelayCommand] private void ToggleLyrics() => LyricsEnabled = !LyricsEnabled;
    [RelayCommand] private void ToggleLock() => LyricsLocked = !LyricsLocked;
    [RelayCommand] private void RestoreMain() => RestoreRequested?.Invoke();
    [RelayCommand] private void NudgeOffset(string? direction) => OffsetMs = Math.Clamp(OffsetMs + (direction == "back" ? -500 : 500), -10000, 10000);
    [RelayCommand] private void UseMini()
    { MiniEnabled = true; MinimizeRequested?.Invoke(); }
    [RelayCommand] private void ResetFont() { FontFilePath = ""; FontFamily = DesktopLyricsFont.Default; }
    [RelayCommand] private void ResetPosition()
    {
        MiniBounds = null; LyricsBounds = null; LyricsLocked = false;
        ResetRequested?.Invoke(); PreferencesChanged?.Invoke();
    }
    public FloatingPreferences Snapshot() => new(MiniEnabled, LyricsEnabled, MiniLocked, LyricsLocked, MiniTopmost,
        DoubleLine, Translation, Romanization, HideWithoutLyrics, FontSize, SecondarySize, Opacity,
        FontFamily, Color, HighlightColor, OffsetMs, MiniBounds, LyricsBounds,
        ScalePercent, ColorMode, StrokeColor, GradientStart, GradientEnd, TextDirection, TextAlign,
        Interpolated, DropShadow, StrokeEnabled, IndependentRows,
        UnplayedTop, UnplayedBottom, OutlinePlayed, OutlineUnplayed,
        LowerPlayedTop, LowerPlayedBottom, LowerUnplayedTop, LowerUnplayedBottom, LowerOutlinePlayed, LowerOutlineUnplayed,
        FontFilePath, WordHighlight);
    public void Restore(FloatingPreferences? saved)
    {
        if (saved is null) return;
        Restoring = true;
        try
        {
            MiniLocked = saved.MiniLocked; LyricsLocked = saved.LyricsLocked && SupportsPassthrough;
            MiniTopmost = saved.MiniTopmost; DoubleLine = saved.DoubleLine;
            Translation = saved.Translation; Romanization = saved.Romanization; HideWithoutLyrics = saved.HideWithoutLyrics;
            FontSize = Finite(saved.FontSize, 18, 72, 34); SecondarySize = Finite(saved.SecondarySize, 12, 48, 19);
            Opacity = Finite(saved.Opacity, .35, 1, .96); OffsetMs = Finite(saved.OffsetMs, -10000, 10000, 0);
            ScalePercent = Finite(saved.ScalePercent, 75, 170, 100);
            FontFamily = string.IsNullOrWhiteSpace(saved.FontFamily) || saved.FontFamily.Length > 256 ? FontFamily : saved.FontFamily;
            Color = ValidColor(saved.Color) ? saved.Color : "#FFFFFF";
            HighlightColor = ValidColor(saved.HighlightColor) ? saved.HighlightColor : "#A8D6FF";
            ColorMode = saved.ColorMode is "custom" or "gradient" or "theme" ? saved.ColorMode : "theme";
            StrokeColor = ValidColor(saved.StrokeColor) ? saved.StrokeColor : "#111827";
            GradientStart = ValidColor(saved.GradientStart) ? saved.GradientStart : "#4F46E5";
            GradientEnd = ValidColor(saved.GradientEnd) ? saved.GradientEnd : "#EC4899";
            TextDirection = saved.TextDirection == "vertical" ? "vertical" : "horizontal";
            TextAlign = saved.TextAlign == "left" ? "left" : "center";
            WordHighlight = saved.WordHighlight; Interpolated = saved.Interpolated; DropShadow = saved.DropShadow; StrokeEnabled = saved.StrokeEnabled;
            IndependentRows = saved.IndependentRows;
            UnplayedTop = ValidColor(saved.UnplayedTop) ? saved.UnplayedTop : "#FFFFFF";
            UnplayedBottom = ValidColor(saved.UnplayedBottom) ? saved.UnplayedBottom : "#CCCCCC";
            OutlinePlayed = ValidColor(saved.OutlinePlayed) ? saved.OutlinePlayed : StrokeColor;
            OutlineUnplayed = ValidColor(saved.OutlineUnplayed) ? saved.OutlineUnplayed : StrokeColor;
            LowerPlayedTop = ValidColor(saved.LowerPlayedTop) ? saved.LowerPlayedTop : Color;
            LowerPlayedBottom = ValidColor(saved.LowerPlayedBottom) ? saved.LowerPlayedBottom : GradientEnd;
            LowerUnplayedTop = ValidColor(saved.LowerUnplayedTop) ? saved.LowerUnplayedTop : UnplayedTop;
            LowerUnplayedBottom = ValidColor(saved.LowerUnplayedBottom) ? saved.LowerUnplayedBottom : UnplayedBottom;
            LowerOutlinePlayed = ValidColor(saved.LowerOutlinePlayed) ? saved.LowerOutlinePlayed : OutlinePlayed;
            LowerOutlineUnplayed = ValidColor(saved.LowerOutlineUnplayed) ? saved.LowerOutlineUnplayed : OutlineUnplayed;
            FontFilePath = saved.FontFilePath is { Length: > 0 and <= 1024 } && !saved.FontFilePath.Contains('\n') ? saved.FontFilePath : "";
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
    internal static bool ValidColor(string? value) => value is { Length: 7 } && value[0] == '#' && value.AsSpan(1).IndexOfAnyExcept("0123456789ABCDEFabcdef") < 0;
}
