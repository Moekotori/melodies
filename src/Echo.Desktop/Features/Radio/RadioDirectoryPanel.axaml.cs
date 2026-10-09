using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Echo.Desktop.Features.Radio;

public partial class RadioDirectoryPanel : UserControl
{
    public RadioDirectoryPanel() => InitializeComponent();

    private async void OnAdd(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: RadioDirectoryRow row }) return;
        if (this.GetVisualAncestors().OfType<RadioView>().FirstOrDefault() is not { } owner) return;
        try { owner.Model.Directory.LimitMessage = ""; await owner.Model.Directory.AddAsync(row); }
        catch (Exception error) { owner.Model.Directory.LimitMessage = error.Message; }
    }
}
