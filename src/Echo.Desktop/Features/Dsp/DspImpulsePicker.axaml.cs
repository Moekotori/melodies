using Avalonia.Controls;
using Avalonia.Interactivity;
namespace Echo.Desktop.Features.Dsp;
public sealed partial class DspImpulsePicker : UserControl
{
    public DspImpulsePicker() => InitializeComponent();
    private async void ImportImpulse(object? sender, RoutedEventArgs e) => await DspPresetDialogs.ImportImpulseAsync(this);
}
