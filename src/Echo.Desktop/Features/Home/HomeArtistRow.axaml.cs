using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Home;

public partial class HomeArtistRow : UserControl
{
    private HomeViewModel? model;
    public HomeArtistRow()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            model = this.FindAncestorOfType<HomeView>()?.Model;
            if (model is null || DataContext is not ListeningArtist item) return;
            Rank.Text = (model.TopArtists.TakeWhile(artist => artist != item).Count() + 1).ToString("D2");
            Count.Text = model.Shell.Text.Format("home.plays", item.Plays);
        };
        DetachedFromVisualTree += (_, _) => model = null;
    }
    private void OnOpen(object? sender, RoutedEventArgs args)
    { if (DataContext is ListeningArtist item) model?.OpenArtistCommand.Execute(item.Name); }
}
