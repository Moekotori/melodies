using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.VisualTree;
using Echo.Contracts.Settings;

namespace Echo.Desktop.Bootstrap;

// Text size and weight preferences coerce TextBlock values, so authored sizes keep their ratios without per-view resources.
// Secondary text is the muted/subtle class. Custom-drawn lyrics keep their own typography settings.
internal static class TextRoleTypography
{
    private static double _primarySize = 1, _secondarySize = 1;
    private static int _primaryWeight, _secondaryWeight;

    public static void Install()
    {
        TextBlock.FontSizeProperty.OverrideMetadata<TextBlock>(new StyledPropertyMetadata<double>(defaultValue: 12,
            coerce: (target, value) => value * (IsSecondary(target) ? _secondarySize : _primarySize)));
        TextBlock.FontWeightProperty.OverrideMetadata<TextBlock>(new StyledPropertyMetadata<FontWeight>(defaultValue: FontWeight.Normal, 
            coerce: (target, value) => Shift(value, IsSecondary(target) ? _secondaryWeight : _primaryWeight)));
        // XAML may assign Classes after FontSize; re-coerce once the role is settled.
        Control.LoadedEvent.AddClassHandler<TextBlock>((block, _) => Coerce(block));
    }

    public static void Apply(TypographyPreferences? preferences)
    {
        var p = preferences ?? new();
        var primarySize = p.PrimarySize / 100; var secondarySize = p.SecondarySize / 100;
        if (primarySize == _primarySize && secondarySize == _secondarySize && p.PrimaryWeight == _primaryWeight && p.SecondaryWeight == _secondaryWeight) return;
        _primarySize = primarySize; _secondarySize = secondarySize; _primaryWeight = p.PrimaryWeight; _secondaryWeight = p.SecondaryWeight;
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        foreach (var window in desktop.Windows)
            foreach (var block in window.GetVisualDescendants().OfType<TextBlock>()) Coerce(block);
    }

    private static void Coerce(TextBlock block)
    { block.CoerceValue(TextBlock.FontSizeProperty); block.CoerceValue(TextBlock.FontWeightProperty); }
    private static bool IsSecondary(AvaloniaObject target)
        => target is StyledElement { Classes: var classes } && (classes.Contains("muted") || classes.Contains("subtle"));
    private static FontWeight Shift(FontWeight value, int offset)
        => offset == 0 ? value : (FontWeight)Math.Clamp((int)value + offset, 100, 900);
}
