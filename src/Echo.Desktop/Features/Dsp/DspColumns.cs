using Avalonia;
using Avalonia.Controls.Primitives;

namespace Echo.Desktop.Features.Dsp;

// A small, finite set of controls wraps as a unit; no timers or retained copies of module state.
public sealed class DspColumns : UniformGrid
{
    public double MinimumColumnWidth { get; set; } = 360;
    public int MaximumColumns { get; set; } = 2;
    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = double.IsFinite(availableSize.Width)
            ? Math.Clamp((int)(availableSize.Width / MinimumColumnWidth), 1, MaximumColumns) : MaximumColumns;
        if (Columns != columns) Columns = columns;
        return base.MeasureOverride(availableSize);
    }
}
