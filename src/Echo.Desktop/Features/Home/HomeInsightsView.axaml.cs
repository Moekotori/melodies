using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Echo.Desktop.Features.Home;

public partial class HomeInsightsView : UserControl
{
    public HomeInsightsView() { InitializeComponent(); SizeChanged += (_, _) => ApplyLayout(); }
    public void RefreshQuality()
    {
        if (DataContext is not HomeViewModel model) return;
        QualityMix.Children.Clear(); QualityMix.ColumnDefinitions.Clear();
        var total = Math.Max(1, model.Quality.Sum(item => item.Seconds));
        foreach (var (item, index) in model.Quality.Select((item, index) => (item, index)))
        {
            QualityMix.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Math.Max(.01, item.Seconds / total), GridUnitType.Star)));
            var segment = new Border { Opacity = 1 - index * .16, Margin = new(0, 0, 2, 0) };
            segment.Bind(Border.BackgroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("EchoAccent"));
            ToolTip.SetTip(segment, $"{item.Codec} · {item.Seconds / total:P0}"); Grid.SetColumn(segment, index); QualityMix.Children.Add(segment);
        }
        (FavoritesHost.Content as IDisposable)?.Dispose();
        FavoritesHost.Content = new HomeAlbumShelf(model.TopAlbums.Select(item => item.Album).ToArray(), model.Shell, album => model.OpenAlbumCommand.Execute(album), true);
    }
    private void ApplyLayout()
    {
        var narrow = Bounds.Width is > 0 and < 760;
        InsightsLayout.ColumnDefinitions = new(narrow ? "*" : "*,1.4*"); InsightsLayout.RowDefinitions = new(narrow ? "Auto,Auto" : "Auto");
        Grid.SetColumn(FavoritePanel, narrow ? 0 : 1); Grid.SetRow(FavoritePanel, narrow ? 1 : 0); FavoritePanel.Margin = new(0, narrow ? 28 : 0, 0, 0);
        var layout = TopTracksList.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.UniformGrid>().FirstOrDefault();
        if (layout is not null) layout.Columns = narrow ? 1 : 2;
    }
}
