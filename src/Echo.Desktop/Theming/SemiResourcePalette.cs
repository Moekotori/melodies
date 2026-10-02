using Avalonia.Controls;
using Avalonia.Media;

namespace Echo.Desktop.Theming;

// Window-owned brush resources: Semi keeps its templates; ECHO owns the current palette.
// Rebuilt only on theme changes, with no I/O, subscriptions or retained control references.
internal static class SemiResourcePalette
{
    public static void Apply(ResourceDictionary resources, Func<string, Color> color, bool dark)
    {
        void Set(string tone, params string[] keys)
        {
            var brush = new SolidColorBrush(color(tone));
            foreach (var key in keys) resources[key] = brush;
        }
        Set("buttonText", "ButtonDefaultPrimaryForeground");
        Set("panelSoft", "ButtonDefaultBackground");
        Set("rowHover", "ButtonDefaultPointeroverBackground", "ComboBoxSelectorPointeroverBackground");
        Set("rowActive", "ButtonDefaultPressedBackground", "ComboBoxSelectorPressedBackground",
            "ComboBoxItemSelectedBackground", "ComboBoxItemSelectedPointeroverBackground");
        Set("onAccent", "ButtonSolidForeground", "ButtonSolidPrimaryPressedForeground");
        Set("accent", "ButtonSolidPrimaryBackground", "ButtonSolidPrimaryBorderBrush",
            "ComboBoxSelectorFocusBorderBrush", "ComboBoxSelectorPressedBorderBrush",
            "TextBoxFocusBorderBrush", "SliderTrackForeground", "SliderThumbBorderBrush",
            "ProgressBarIndicatorBrush", "ProgressBarPrimaryForeground",
            "CheckBoxPointeroverBorderBrush", "CheckBoxCheckedDefaultBackground", "CheckBoxCheckedDefaultBorderBrush",
            "ToggleSwitchContainerCheckedDefaultBackground");
        var hover = ThemeColorMath.Mix(color("accent"), dark ? Colors.White : Colors.Black, .08);
        var pressed = ThemeColorMath.Mix(color("accent"), dark ? Colors.White : Colors.Black, .16);
        foreach (var key in new[] { "ButtonSolidPrimaryPointeroverBackground", "ButtonSolidPrimaryPointeroverBorderBrush",
            "CheckBoxCheckedPointeroverBackground", "CheckBoxCheckedPointeroverBorderBrush",
            "ToggleSwitchContainerCheckedPointeroverBackground", "SliderThumbPointeroverBorderBrush",
            "TextBoxBorderedPointeroverBorderBrush" }) resources[key] = new SolidColorBrush(hover);
        foreach (var key in new[] { "ButtonSolidPrimaryPressedBackground", "ButtonSolidPrimaryPressedBorderBrush",
            "CheckBoxCheckedPressedBackground", "CheckBoxCheckedPressedBorderBrush", "CheckBoxPressedBorderBrush",
            "ToggleSwitchContainerCheckedPressedBackground", "SliderThumbPressedBorderBrush" }) resources[key] = new SolidColorBrush(pressed);
        Set("field", "ComboBoxSelectorBackground", "ComboBoxSelectorFocusBackground", "ComboBoxPopupBackground");
        Set("border", "ComboBoxSelectorBorderBrush", "ComboBoxPopupBorderBrush");
        Set("text", "ComboBoxItemForeground", "ComboBoxItemPointeroverForeground", "ComboBoxItemFocusForeground",
            "ComboBoxItemPressedForeground", "ComboBoxItemSelectedForeground", "TextBoxForeground", "TextBoxTextCaretBrush",
            "CheckBoxForeground", "ProgressBarTextForeground");
        Set("muted", "ComboBoxSelectorPlaceHolderForeground", "ComboBoxIconDefaultForeground", "TextBoxPlaceholderForeground",
            "TextBoxInnerForeground", "ButtonDefaultDisabledForeground", "ComboBoxDisabledForeground", "TextBoxDisabledForeground");
        Set("rowHover", "ComboBoxItemPointeroverBackground", "ComboBoxItemFocusBackground", "ComboBoxItemPressedBackground");
        resources["ToggleSwitchContainerCheckedDisabledBackground"] = new SolidColorBrush(color("accent"), .3);
    }
}
