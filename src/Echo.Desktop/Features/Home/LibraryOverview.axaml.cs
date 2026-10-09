using Avalonia.Controls;
using Echo.Desktop.Controls;
using Avalonia.VisualTree;

namespace Echo.Desktop.Features.Home;

public partial class LibraryOverview : UserControl
{
    public LibraryOverview()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ApplyLayout();
    }

    private void ApplyLayout()
    {
        var compact = Bounds.Width <= 900;
        Tiles.Columns = Bounds.Width <= 680 ? 2 : 4;
        Tiles.Rows = Tiles.Columns == 2 ? 2 : 1;
        foreach (var tile in Tiles.Children.OfType<Button>())
        {
            tile.MinHeight = Tiles.Columns == 2 ? 88 : compact ? 76 : 96;
            tile.Padding = new(compact ? 12 : 24, compact ? 14 : 18);
            if (tile.Content is StackPanel copy) copy.Spacing = compact ? 10 : 16;
            foreach (var icon in tile.GetVisualDescendants().OfType<GlyphIcon>())
                icon.Width = icon.Height = compact ? 28 : 36;
            foreach (var number in tile.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Classes.Contains("heading")))
                number.FontSize = compact ? 24 : Math.Clamp(Bounds.Width * .026, 24, 30);
        }
    }
}
