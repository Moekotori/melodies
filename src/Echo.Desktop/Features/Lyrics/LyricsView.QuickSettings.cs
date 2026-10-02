using Avalonia.Interactivity;

namespace Echo.Desktop.Features.Lyrics;

public partial class LyricsView
{
    private void OnOpenSettingsDrawer(object? sender, RoutedEventArgs e) => shell.OpenQuickSettingsCommand.Execute("lyrics");
    private void OnOpenAudioDrawer(object? sender, RoutedEventArgs e) => shell.OpenQuickSettingsCommand.Execute("audio");
}
