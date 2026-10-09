using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Echo.Desktop.Features.Connect.SmartHome;

public partial class ConnectSmartHomeView : UserControl
{
    public ConnectSmartHomeView() => InitializeComponent();
    private void OnEdited(object? sender, RoutedEventArgs e) => (DataContext as ConnectSmartHomeViewModel)?.Save();
}
