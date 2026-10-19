using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Desktop.Localization;
using Echo.Desktop.Theming;
using Avalonia.Styling;

namespace Echo.Desktop.Features.Floating;

public partial class DesktopLyricsWindow
{
    private Window? settingsWindow;
    private void OpenSettings(object? sender, RoutedEventArgs e)
    {
        if (settingsWindow is { } existing) { existing.Activate(); return; }
        var panel = new Window
        {
            Title = shell.Text["floating.settings"], Width = 380, Height = 640,
            MinWidth = 340, MinHeight = 360, ShowInTaskbar = false,
            Content = new ScrollViewer
            {
                Content = new DesktopLyricsSettingsView { DataContext = shell.Floating, Margin = new Thickness(20) }
            }
        };
        settingsWindow = panel;
        var localization = new WindowLocalizationBinding(panel, shell.Text);
        void Theme()
        {
            panel.RequestedThemeVariant = RequestedThemeVariant;
            foreach (var dark in new[] { false, true })
                panel.Resources.ThemeDictionaries[dark ? ThemeVariant.Dark : ThemeVariant.Light]
                    = ThemeResourcePalette.Create(shell.Theme.Tone(dark), shell.Theme.Snapshot, dark, null);
        }
        Theme();
        shell.Theme.Changed += Theme;
        panel.Closed += (_, _) =>
        {
            localization.Dispose(); shell.Theme.Changed -= Theme;
            settingsWindow = null;
            if (!closed) ShowChrome(IsPointerOver, shell.Floating.LyricsLocked);
        };
        panel.Show(this);
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is not null)
        {
            var area = screen.WorkingArea;
            var scaling = screen.Scaling;
            var width = (int)Math.Ceiling(panel.Width * scaling);
            var height = (int)Math.Ceiling(panel.Height * scaling);
            var right = Position.X + (int)Math.Ceiling(Bounds.Width * scaling) + 12;
            var x = right + width <= area.Right ? right : Position.X - width - 12;
            panel.Position = new(Math.Clamp(x, area.X, Math.Max(area.X, area.Right - width)),
                Math.Clamp(Position.Y, area.Y, Math.Max(area.Y, area.Bottom - height)));
        }
        ShowChrome(true, shell.Floating.LyricsLocked);
    }
}
