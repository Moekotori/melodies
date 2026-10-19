using Avalonia.Controls;
using Avalonia.Interactivity;
namespace Echo.Desktop.Features.Dsp;
public partial class DspHeadphoneView : UserControl
{
    public DspHeadphoneView() => InitializeComponent();
    private async void ImportCurve(object? sender, RoutedEventArgs e) => await DspPresetDialogs.ImportAsync(this);
    private async void ExportCurve(object? sender, RoutedEventArgs e) => await DspPresetDialogs.ExportAsync(this);
}
