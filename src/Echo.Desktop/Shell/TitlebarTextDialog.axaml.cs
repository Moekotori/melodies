using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Shell;

public partial class TitlebarTextDialog : Window
{
    public TitlebarTextDialog() => InitializeComponent();

    public TitlebarTextDialog(LocalizationService text, string value, Window owner) : this()
    {
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var palette = new ResourceDictionary();
            foreach (var key in new[] { "EchoPage", "EchoText", "EchoMuted", "EchoHeading", "EchoField", "EchoBorder", "EchoDivider", "EchoAccent", "EchoAccentText", "EchoFont", "EchoButtonText", "EchoOnAccent", "EchoFocus" })
                if (owner.TryFindResource(key, variant, out var resource)) palette[key] = resource;
            Resources.ThemeDictionaries[variant] = palette;
        }
        var localization = new WindowLocalizationBinding(this, text);
        TitleTextInput.Text = value;
        Opened += (_, _) => { TitleTextInput.Focus(); TitleTextInput.SelectAll(); };
        Closed += (_, _) => localization.Dispose();
    }

    private void OnSave(object? sender, RoutedEventArgs e) => Close(TitleTextInput.Text ?? "");
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
