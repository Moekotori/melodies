using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Echo.Desktop.Features.Connect.HqPlayer;

public partial class ConnectHqPlayerView : UserControl
{
    public ConnectHqPlayerView() => InitializeComponent();
    private void OnEdited(object? sender, RoutedEventArgs e) => (DataContext as ConnectHqPlayerViewModel)?.Save();
}
