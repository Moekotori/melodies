using Avalonia.Controls;

namespace Echo.Desktop.Features.Folders;

// Header and rows use the same native grid and responsive thresholds.
public sealed class FolderColumnsGrid : Grid
{
    public FolderColumnsGrid()
    {
        ColumnDefinitions = new("28,44,*,150,108,54,28"); ColumnSpacing = 12;
        SizeChanged += (_, _) =>
        {
            var narrow = Bounds.Width < 760;
            ColumnDefinitions[0].Width = new(narrow ? 22 : 28);
            ColumnDefinitions[3].Width = new(narrow ? 0 : 150);
            ColumnDefinitions[4].Width = new(narrow ? 0 : 108);
            ColumnSpacing = narrow ? 8 : 12;
            foreach (var child in Children)
                if (child.Classes.Contains("wide")) child.IsVisible = !narrow;
        };
    }
}
