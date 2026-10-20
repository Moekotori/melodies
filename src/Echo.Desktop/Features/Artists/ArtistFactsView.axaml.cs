using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Echo.Desktop.Features.Artists;

public partial class ArtistFactsView : UserControl
{
    public ArtistFactsView()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            var columns = Bounds.Width < 520 ? 2 : Bounds.Width < 840 ? 3 : 4;
            foreach (var list in new[] { BasicFacts, PublicFactsGrid, PublicMembersGrid })
                if (list.ItemsPanelRoot is Avalonia.Controls.Primitives.UniformGrid grid) grid.Columns = columns;
        };
    }
    private async void OnExternal(object? sender, RoutedEventArgs args)
    { if (sender is Button { Tag: string url }) await ArtistExternalLink.OpenAsync(this, url, DataContext as ArtistOverviewViewModel); }
}
