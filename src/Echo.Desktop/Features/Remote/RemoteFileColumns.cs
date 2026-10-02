using Avalonia.Controls;

namespace Echo.Desktop.Features.Remote;

// The same column geometry is used by headers and virtualized file rows.
public sealed class RemoteFileColumns : Grid
{
    private int? mode;
    public RemoteFileColumns()
    {
        ColumnDefinitions = new("52,*,76,94,112,78");
        SizeChanged += (_, args) =>
        {
            var next = args.NewSize.Width < 520 ? 2 : args.NewSize.Width < 720 ? 1 : 0;
            if (mode == next) return;
            mode = next; ColumnDefinitions = new(next switch
            { 2 => "44,*,0,70,0,72", 1 => "48,*,62,78,0,78", _ => "52,*,76,94,112,78" });
            foreach (var child in Children.OfType<TextBlock>())
            {
                if (child.Classes.Contains("file-date")) child.IsVisible = next == 0;
                if (child.Classes.Contains("file-type")) child.IsVisible = next != 2;
            }
        };
    }
}
