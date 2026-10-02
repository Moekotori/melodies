using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Desktop.Shell;
namespace Echo.Desktop.Features.Queue;
public partial class QueueDrawer : UserControl
{
    public QueueDrawer() => InitializeComponent();
    private async void OnAdd(object? sender, RoutedEventArgs args)
    { if (DataContext is ShellViewModel shell) await QueueFilePicker.AddAsync(this, shell.Player.Queue); }
}
