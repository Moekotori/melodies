using Avalonia;
using Avalonia.Controls;

namespace Echo.Desktop.Features.Songs;

public partial class SongTrackRow
{
    public static readonly StyledProperty<string> LayoutProperty =
        AvaloniaProperty.Register<SongTrackRow, string>(nameof(Layout), "list");
    public string Layout { get => GetValue(LayoutProperty); set => SetValue(LayoutProperty, value); }

    // Capped star columns keep title, album, spec and duration clustered on wide windows instead of drifting to the edges.
    // The header uses the same definitions so columns stay aligned.
    internal static ColumnDefinitions RichColumns() => new()
    {
        new(56, GridUnitType.Pixel),
        new(1.6, GridUnitType.Star) { MaxWidth = 560 },
        new(1, GridUnitType.Star) { MaxWidth = 300 },
        new(0.6, GridUnitType.Star) { MaxWidth = 160 },
        new(130, GridUnitType.Pixel),
        new(64, GridUnitType.Pixel),
        new(56, GridUnitType.Pixel),
    };

    // List keeps the cover row. Dense is a coverless table. Split leaves details to the inspector.
    private void ApplyLayout()
    {
        var dense = Layout == "dense";
        var split = Layout == "split";
        var width = Bounds.Width;
        var narrow = width > 0 && width < (dense ? 650 : 700);
        // Wide list rows give album, genre and codec their own columns instead of stacking them under the title.
        var rich = !dense && !split && !narrow;
        CoverHost.IsVisible = !dense;
        CoverHost.Width = CoverHost.Height = split ? 40 : 56;
        Subtitle.IsVisible = !dense && !split;
        Subtitle.Text = rich ? subtitleArtist : subtitleJoined;
        Tags.IsVisible = !dense && !split && !rich;
        Genre.IsVisible = rich;
        ArtistColumn.IsVisible = dense || split;
        AlbumGroup.IsVisible = (dense && !narrow) || rich;
        SpecColumn.IsVisible = dense && !narrow; SpecColumn.Text = specInline;
        SpecGroup.IsVisible = rich;
        TitleText.FontSize = dense ? 13 : 15;
        RowActions.HorizontalAlignment = dense || split ? Avalonia.Layout.HorizontalAlignment.Right : Avalonia.Layout.HorizontalAlignment.Left;
        Columns.ColumnSpacing = dense ? 10 : narrow ? 8 : 16;
        Columns.Height = dense ? 34 : split ? 48 : 68;
        Columns.Margin = new Thickness(dense ? 4 : 10, 0);
        if (dense)
        {
            Columns.ColumnDefinitions = new ColumnDefinitions(narrow ? "*,*,56" : "*,*,*,110,56");
            Grid.SetColumn(Main, 0); Grid.SetColumn(ArtistColumn, 1); Grid.SetColumn(AlbumGroup, 2); Grid.SetColumn(SpecColumn, 3);
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
        Columns.Margin = new Thickness(10, 0, rich ? 28 : 10, 0);
        Columns.ColumnDefinitions = rich ? RichColumns() : new ColumnDefinitions("56,*,0,0,0,64,56");
        Grid.SetColumn(CoverHost, 0); Grid.SetColumn(Main, 1); Grid.SetColumn(AlbumGroup, 2); Grid.SetColumn(Genre, 3); Grid.SetColumn(SpecGroup, 4);
        Grid.SetColumn(RowActions, 5); Grid.SetColumn(Duration, 6);
    }
}
