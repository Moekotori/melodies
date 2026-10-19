using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Desktop.Shell;
namespace Echo.Desktop.Features.Queue;
public partial class QueueDrawer : UserControl
{
    public QueueDrawer()
    {
        InitializeComponent();
        _ = new QueuePresentationBinding(this, () => (DataContext as ShellViewModel)?.Player?.Queue);
        SizeChanged += (_, _) => QueueSearch.IsVisible = Bounds.Height >= 500;
    }
    private void OnShortcuts(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not ShellViewModel shell) return;
        shell.CloseQueueDrawerCommand.Execute(null);
        shell.NavigateCommand.Execute("settings");
        shell.Settings.SelectCategoryCommand.Execute("shortcuts");
    }
    private void OnMode(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not ShellViewModel shell || sender is not Button button) return;
        var queue = shell.Player.Queue;
        var menu = new ContextMenu();
        foreach (var (key, command, active) in new[]
        {
            ("queue.drawer.sequential", queue.SequentialCommand, queue.IsSequential),
            ("queue.drawer.shuffle", queue.ShuffleCommand, queue.IsShuffle),
            ("queue.drawer.repeatOne", queue.RepeatOneCommand, queue.IsRepeatOne),
            ("queue.drawer.repeatAll", queue.RepeatAllCommand, queue.IsRepeatAll)
        }) menu.Items.Add(new MenuItem { Header = shell.Text[key], Command = command, Icon = active ? "✓" : null });
        button.ContextMenu = menu; menu.Open(button);
    }
    private async void OnAdd(object? sender, RoutedEventArgs args)
    { if (DataContext is ShellViewModel shell) await QueueFilePicker.AddAsync(this, shell.Player.Queue); }
}
