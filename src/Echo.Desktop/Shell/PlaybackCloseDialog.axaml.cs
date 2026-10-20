using Avalonia.Controls;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Shell;

public enum PlaybackCloseChoice { Cancel, Background, StopAndClose }

public partial class PlaybackCloseDialog : Window
{
    public PlaybackCloseDialog() => InitializeComponent();

    public PlaybackCloseDialog(LocalizationService text) : this()
    {
        var localization = new WindowLocalizationBinding(this, text);
        Closed += (_, _) => localization.Dispose();
    }

    private void OnBackground(object? sender, Avalonia.Interactivity.RoutedEventArgs args) => Close(PlaybackCloseChoice.Background);
    private void OnStop(object? sender, Avalonia.Interactivity.RoutedEventArgs args) => Close(PlaybackCloseChoice.StopAndClose);
    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs args) => Close(PlaybackCloseChoice.Cancel);
}
