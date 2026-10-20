using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Settings;

namespace Echo.Desktop.Theming;

public sealed record TextWeightChoice(int Offset, string Label)
{ public override string ToString() => Label; }
public sealed record TextSwatch(string Hex)
{ public IBrush Brush { get; } = new ImmutableSolidColorBrush(Color.Parse(Hex)); }

// Primary and secondary text roles share the theme snapshot, undo stack and single settings writer.
public sealed partial class ThemeViewModel
{
    public IReadOnlyList<TextWeightChoice> WeightChoices { get; } = [new(-100, "更细"), new(0, "标准"), new(100, "稍粗"), new(200, "更粗")];
    public IReadOnlyList<TextSwatch> TextSwatches { get; } = new[] { "#0F172A", "#334155", "#64748B", "#94A3B8", "#E2E8F0", "#4F46E5", "#0E7490", "#B45309", "#BE123C" }
        .Select(hex => new TextSwatch(hex)).ToArray();
    public TypographyPreferences Typography => Appearance.Typography ?? new();
    [ObservableProperty] private int primaryWeight;
    [ObservableProperty] private double primarySize = 100;
    [ObservableProperty] private string secondaryFont = "";
    [ObservableProperty] private string secondaryColor = "";
    [ObservableProperty] private int secondaryWeight;
    [ObservableProperty] private double secondarySize = 100;
    public TextWeightChoice SelectedPrimaryWeight { get => Choice(PrimaryWeight); set { if (value is not null) PrimaryWeight = value.Offset; } }
    public TextWeightChoice SelectedSecondaryWeight { get => Choice(SecondaryWeight); set { if (value is not null) SecondaryWeight = value.Offset; } }
    public bool HasSecondaryColorOverride => (EditTone == "深色" ? Typography.SecondaryDark : Typography.SecondaryLight) is not null;
    public bool HasSecondaryFont => SecondaryFont.Length > 0;
    private TextWeightChoice Choice(int offset) => WeightChoices.FirstOrDefault(c => c.Offset == offset) ?? WeightChoices[1];

    private static TypographyPreferences NormalizeTypography(TypographyPreferences t) => t with
    {
        PrimaryWeight = Weight(t.PrimaryWeight), SecondaryWeight = Weight(t.SecondaryWeight),
        PrimarySize = Finite(t.PrimarySize, 80, 140, 100), SecondarySize = Finite(t.SecondarySize, 80, 140, 100),
        SecondaryFont = t.SecondaryFont is { Length: > 0 and <= 120 } f && !f.Contains('/') && !f.Contains('\\') ? f : "",
        SecondaryFontFile = NormalizeFont(t.SecondaryFontFile),
        SecondaryLight = ThemeColorMath.TryHex(t.SecondaryLight ?? "", out var light) ? light : null,
        SecondaryDark = ThemeColorMath.TryHex(t.SecondaryDark ?? "", out var dark) ? dark : null
    };
    private static int Weight(int offset) => Math.Clamp(offset / 100 * 100, -100, 200);
    private void ChangeTypography(TypographyPreferences next) => ChangeAppearance(Appearance with { Typography = NormalizeTypography(next) });
    private void RestoreTypography()
    {
        var t = Typography;
        PrimaryWeight = t.PrimaryWeight; PrimarySize = t.PrimarySize; SecondaryFont = t.SecondaryFont;
        SecondaryWeight = t.SecondaryWeight; SecondarySize = t.SecondarySize;
    }
    private void RefreshSecondaryColor()
    {
        var wasSync = _sync; _sync = true; SecondaryColor = (EditTone == "深色" ? Typography.SecondaryDark : Typography.SecondaryLight) ?? ""; _sync = wasSync;
        OnPropertyChanged(nameof(HasSecondaryColorOverride));
    }
    partial void OnPrimaryWeightChanged(int value) { ChangeTypography(Typography with { PrimaryWeight = value }); OnPropertyChanged(nameof(SelectedPrimaryWeight)); }
    partial void OnPrimarySizeChanged(double value) => ChangeTypography(Typography with { PrimarySize = Math.Round(value / 5) * 5 });
    partial void OnSecondaryWeightChanged(int value) { ChangeTypography(Typography with { SecondaryWeight = value }); OnPropertyChanged(nameof(SelectedSecondaryWeight)); }
    partial void OnSecondarySizeChanged(double value) => ChangeTypography(Typography with { SecondarySize = Math.Round(value / 5) * 5 });
    partial void OnSecondaryFontChanged(string value)
    { ChangeTypography(Typography with { SecondaryFont = value?.Trim() ?? "" }); OnPropertyChanged(nameof(HasSecondaryFont)); }
    partial void OnSecondaryColorChanged(string value)
    {
        if (_sync) return;
        if (value.Length > 0 && !ThemeColorMath.TryHex(value, out _)) { Feedback = "文字颜色使用 HEX 或 RGBA，例如 #64748B"; return; }
        var color = value.Length == 0 ? null : value;
        ChangeTypography(EditTone == "深色" ? Typography with { SecondaryDark = color } : Typography with { SecondaryLight = color });
        Feedback = ""; OnPropertyChanged(nameof(HasSecondaryColorOverride));
    }
    [RelayCommand] private void ResetSecondaryColor() => SecondaryColor = "";
    [RelayCommand] private void FollowMainFont() { SetFontFile("secondary", null); SecondaryFont = ""; }
    [RelayCommand] private void PickPrimaryColor(string? hex) { if (hex is not null) PageTextColor = hex; }
    [RelayCommand] private void PickSecondaryColor(string? hex) { if (hex is not null) SecondaryColor = hex; }
    [RelayCommand] private void ResetTypography()
    {
        var next = _preferences with { Font = "Outfit", Appearance = NormalizeAppearance(Appearance with
            { Typography = null, MainFontFile = null, PageTextLight = null, PageTextDark = null }) };
        Commit(next); Restore(next); Feedback = "字体已恢复默认，可撤销";
    }
}
