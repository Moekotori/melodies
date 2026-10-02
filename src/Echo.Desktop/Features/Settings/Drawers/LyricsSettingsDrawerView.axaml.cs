using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Settings.Drawers;

public partial class LyricsSettingsDrawerView : UserControl
{
    private readonly ShellViewModel _shell;
    public LyricsSettingsDrawerView(ShellViewModel shell)
    { _shell = shell; InitializeComponent(); DataContext = shell.LyricsAppearance; }
    private void OnOpenLyrics(object? sender, RoutedEventArgs e)
    { _shell.CloseQuickSettingsCommand.Execute(null); _shell.NavigateCommand.Execute("lyrics"); }
    private void OnReset(object? sender, RoutedEventArgs e)
    {
        var appearance = _shell.LyricsAppearance;
        appearance.SelectedStyle = appearance.Styles[0]; appearance.SelectedLook = appearance.Looks[0];
        appearance.FontSize = 36; appearance.SecondarySize = 18; appearance.Font = appearance.Fonts[0];
        appearance.Alignment = appearance.Alignments[0]; appearance.Translation = true; appearance.Romanization = true;
        appearance.WordHighlight = true; appearance.Context = true; appearance.OffsetMs = 0;
    }
}
