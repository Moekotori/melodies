using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Echo.Desktop.Features.Shortcuts;

public partial class ShortcutCellView : UserControl
{
    public ShortcutCellView() => InitializeComponent();
}
public partial class ShortcutSettingsView : UserControl
{
    private IReadOnlyList<ShortcutRow>? _renderedRows;
    private bool? _compact;
    public ShortcutSettingsView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => (DataContext as ShortcutSettingsViewModel)?.CancelRecording();
        SizeChanged += (_, _) => ResizeColumns();
        LayoutUpdated += (_, _) => ResizeColumns();
    }
    private void ResizeColumns()
    {
        var compact = Bounds.Width < 620;
        var rows = (DataContext as ShortcutSettingsViewModel)?.VisibleRows;
        if (_compact == compact && _renderedRows == rows) return;
        _compact = compact; _renderedRows = rows;
        // Fixed 33 rows; inspect once when the breakpoint or filtered rows change.
        foreach (var grid in this.GetVisualDescendants().OfType<Grid>().Where(x => x.Classes.Contains("shortcut-row-grid")).Prepend(ColumnHeader))
        {
            grid.ColumnDefinitions = new(compact ? "*,*" : "*,230,230");
            grid.RowDefinitions = new(compact ? "Auto,Auto" : "Auto");
            grid.RowSpacing = compact ? 10 : 0;
            for (var i = 0; i < grid.Children.Count; i++)
            {
                var child = grid.Children[i];
                Grid.SetColumn(child, compact ? Math.Max(0, i - 1) : i);
                Grid.SetRow(child, compact && i > 0 ? 1 : 0);
                Grid.SetColumnSpan(child, compact && i == 0 ? 2 : 1);
            }
        }
    }
}
