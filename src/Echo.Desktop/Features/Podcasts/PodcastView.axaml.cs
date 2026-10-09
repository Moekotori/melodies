using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Desktop.Shell;
using System.Windows.Input;

namespace Echo.Desktop.Features.Podcasts;

public partial class PodcastView : UserControl, IDisposable
{
    private bool disposed;

    public PodcastView() => InitializeComponent();

    public PodcastView(ShellViewModel shell) : this()
    {
        DataContext = new PodcastViewModel(shell);
        AttachedToVisualTree += (_, _) => _ = ((PodcastViewModel)DataContext!).LoadAsync();
        DetachedFromVisualTree += (_, _) => Dispose();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (DataContext is PodcastViewModel model) model.Dispose();
    }

    private void OnEpisodeMenu(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button button || button.DataContext is not PodcastEpisodeRow row || DataContext is not PodcastViewModel model) return;
        var menu = new MenuFlyout();
        menu.Items.Add(Item("下一首播放", model.QueueCommand, row));
        menu.Items.Add(Item("从头播放", model.RestartCommand, row));
        menu.Items.Add(Item(row.Episode.Played ? "标记未听" : "标记已听", model.MarkCommand, row));
        if (row.Episode.Website is not null) menu.Items.Add(Item("单集原网页", model.OpenSourceCommand, row));
        menu.ShowAt(button);
    }

    private static MenuItem Item(string header, ICommand command, PodcastEpisodeRow row) =>
        new() { Header = header, Command = command, CommandParameter = row };
}
