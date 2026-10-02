using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Echo.Desktop.Features.Dsp;

public partial class EqEditorView : UserControl
{
    private bool revealQueued;
    public EqEditorView()
    {
        InitializeComponent(); SizeChanged += (_, _) => { ArrangeEditor(); QueueReveal(); };
        EqBands.SelectionChanged += (_, _) => QueueReveal();
    }
    private void ArrangeEditor()
    {
        var narrow = Bounds.Width < 740;
        EqCurve.Height = narrow ? 242 : 300;
        var toolbarNarrow = Bounds.Width < 510;
        EqToolbar.ColumnDefinitions = new(toolbarNarrow ? "*" : "*,Auto");
        EqToolbar.RowDefinitions = new(toolbarNarrow ? "Auto,Auto" : "Auto");
        Grid.SetColumn(EqTools, toolbarNarrow ? 0 : 1); Grid.SetRow(EqTools, toolbarNarrow ? 1 : 0);
        EqTools.Margin = toolbarNarrow ? new Avalonia.Thickness(0, 8, 0, 0) : default;
        PresetPicker.Width = Bounds.Width < 350 ? 174 : 238;
    }
    private void QueueReveal()
    {
        if (revealQueued) return; revealQueued = true;
        Dispatcher.UIThread.Post(() => { revealQueued = false; RevealBand(); }, DispatcherPriority.Loaded);
    }
    private void RevealBand()
    {
        if (!this.IsAttachedToVisualTree() || EqBands.SelectedItem is null
            || EqBands.ContainerFromItem(EqBands.SelectedItem) is not { } container
            || EqBands.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is not { } scroll
            || scroll.Viewport.Width <= 0 || container.TranslatePoint(default, scroll) is not { } point) return;
        var delta = point.X < 0 ? point.X : point.X + container.Bounds.Width > scroll.Viewport.Width
            ? point.X + container.Bounds.Width - scroll.Viewport.Width : 0;
        if (Math.Abs(delta) > .5) scroll.Offset = new Avalonia.Vector(Math.Max(0, scroll.Offset.X + delta), 0);
    }
}
