using Avalonia;
using Avalonia.Controls;

namespace Echo.Desktop.Features.Dsp;

// Two independent sections, with the same wide / stacked geometry as ECHO's workbenches.
// Unlike UniformGrid, this does not stretch every section to the tallest form column.
public sealed class DspWorkbenchSplit : Grid
{
    public double MainWeight { get; set; } = 1.35;
    public double StackAt { get; set; } = 760;
    public double Gap { get; set; } = 40;
    public bool StretchContent { get; set; }
    private int layoutMode = -1;

    protected override Size MeasureOverride(Size availableSize)
    {
        var narrow = double.IsFinite(availableSize.Width) && availableSize.Width < StackAt;
        var mode = Children.Count < 2 || !Children[1].IsVisible ? 0 : narrow ? 1 : 2;
        if (layoutMode != mode)
        {
            layoutMode = mode;
            ColumnDefinitions.Clear(); RowDefinitions.Clear();
            if (mode != 2)
            {
                ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                if (mode == 1)
                {
                    RowDefinitions.Add(new RowDefinition(new GridLength(Gap)));
                    RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                }
            }
            else
            {
                ColumnDefinitions.Add(new ColumnDefinition(new GridLength(MainWeight, GridUnitType.Star)));
                ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Gap)));
                ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                RowDefinitions.Add(new RowDefinition(StretchContent ? GridLength.Star : GridLength.Auto));
            }
            for (var i = 0; i < Children.Count; i++)
            {
                SetColumn(Children[i], mode == 2 ? i * 2 : 0);
                SetRow(Children[i], mode == 1 ? i * 2 : 0);
                Children[i].VerticalAlignment = StretchContent && mode == 2
                    ? Avalonia.Layout.VerticalAlignment.Stretch : Avalonia.Layout.VerticalAlignment.Top;
            }
        }
        return base.MeasureOverride(availableSize);
    }
}
