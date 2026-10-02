using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Echo.Desktop.Features.Radio;

public partial class RadioStage : UserControl
{
    public RadioStage() => InitializeComponent();
    private void OnBack(object? sender, RoutedEventArgs args) { if (DataContext is RadioViewModel model) model.StageOpen = false; }
}
