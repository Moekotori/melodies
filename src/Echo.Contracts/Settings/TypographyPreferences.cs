namespace Echo.Contracts.Settings;

// Display-only text roles. The primary family and color stay in ThemePreferences.Font and AppearancePreferences.PageText*.
// Weight offsets are CSS weight steps (-100..200); sizes are percentages of each authored size.
public sealed record TypographyPreferences(int PrimaryWeight = 0, double PrimarySize = 100,
    string SecondaryFont = "", ImportedFont? SecondaryFontFile = null, string? SecondaryLight = null, string? SecondaryDark = null,
    int SecondaryWeight = 0, double SecondarySize = 100);
