using Avalonia.Controls;

namespace Echo.Desktop.Features.Folders;

// Header and rows use the same native grid and responsive thresholds.
public sealed class FolderColumnsGrid : Grid
{
    public FolderColumnsGrid()
    {
        ColumnDefinitions = new("32,*,160,144,58,32"); ColumnSpacing = 14;
        SizeChanged += (_, _) =>
        {
            var narrow = Bounds.Width < 650;
            ColumnDefinitions[0].Width = new(narrow ? 24 : 32);
            ColumnDefinitions[2].Width = new(narrow ? 0 : 160);
            ColumnDefinitions[3].Width = new(narrow ? 0 : 144);
            ColumnSpacing = narrow ? 8 : 14;
            foreach (var child in Children)
                if (child.Classes.Contains("wide")) child.IsVisible = !narrow;
        };
    }
}
