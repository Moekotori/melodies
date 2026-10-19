using Avalonia.Controls;

namespace Echo.Desktop.Features.Playlists;

// Playlist rows have no folder artwork column; title and artist own the flexible space.
public sealed class PlaylistColumnsGrid : Grid
{
    public PlaylistColumnsGrid()
    {
        ColumnDefinitions = new("28,2*,*,150,54,28"); ColumnSpacing = 16;
        SizeChanged += (_, _) =>
        {
            var album = Bounds.Width >= 620;
            var quality = Bounds.Width >= 900;
            ColumnDefinitions[2].Width = album ? new(1, GridUnitType.Star) : new(0);
            ColumnDefinitions[3].Width = new(quality ? 150 : 0);
            ColumnSpacing = Bounds.Width < 620 ? 8 : 16;
            foreach (var child in Children)
            {
                if (child.Classes.Contains("album-column")) child.IsVisible = album;
                if (child.Classes.Contains("quality-column")) child.IsVisible = quality;
            }
        };
    }
}
