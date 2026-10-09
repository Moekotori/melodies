using Avalonia;
using Avalonia.Controls;

namespace Echo.Desktop.Features.Songs;

public partial class SongTrackRow
{
    public static readonly StyledProperty<string> LayoutProperty =
        AvaloniaProperty.Register<SongTrackRow, string>(nameof(Layout), "list");
    public string Layout { get => GetValue(LayoutProperty); set => SetValue(LayoutProperty, value); }

    // List keeps the cover row. Dense is a coverless table. Split leaves details to the inspector.
    private void ApplyLayout()
    {
        var dense = Layout == "dense";
        var split = Layout == "split";
        var width = Bounds.Width;
        var narrow = width > 0 && width < (dense ? 650 : 700);
        CoverHost.IsVisible = !dense;
        CoverHost.Width = CoverHost.Height = split ? 40 : 52;
        Subtitle.IsVisible = !dense && !split;
        Tags.IsVisible = !dense && !split;
        Genre.IsVisible = !dense && !split && !narrow;
        ArtistColumn.IsVisible = dense || split;
        AlbumColumn.IsVisible = dense && !narrow;
        SpecColumn.IsVisible = dense && !narrow;
        TitleText.FontSize = dense ? 13 : 15;
        RowActions.HorizontalAlignment = dense || split ? Avalonia.Layout.HorizontalAlignment.Right : Avalonia.Layout.HorizontalAlignment.Left;
        Columns.ColumnSpacing = dense ? 10 : narrow ? 8 : 12;
        Columns.Height = dense ? 34 : split ? 48 : 72;
        Columns.Margin = new Thickness(dense ? 4 : 8, 0);
        if (dense)
        {
            Columns.ColumnDefinitions = new ColumnDefinitions(narrow ? "*,*,56" : "*,*,*,110,56");
            Grid.SetColumn(Main, 0); Grid.SetColumn(ArtistColumn, 1); Grid.SetColumn(AlbumColumn, 2); Grid.SetColumn(SpecColumn, 3);
            Grid.SetColumn(Duration, narrow ? 2 : 4); Grid.SetColumn(RowActions, narrow ? 1 : 3);
            return;
        }
        if (split)
        {
            Columns.ColumnDefinitions = new ColumnDefinitions("44,*,*,56");
            Grid.SetColumn(CoverHost, 0); Grid.SetColumn(Main, 1); Grid.SetColumn(ArtistColumn, 2);
            Grid.SetColumn(Duration, 3); Grid.SetColumn(RowActions, 2);
            return;
        }
        Columns.ColumnDefinitions = new ColumnDefinitions(narrow ? "52,*,0,64,56" : "52,*,140,64,56");
        Grid.SetColumn(CoverHost, 0); Grid.SetColumn(Main, 1); Grid.SetColumn(Genre, 2);
        Grid.SetColumn(RowActions, 3); Grid.SetColumn(Duration, 4);
    }
}
