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
    private async void ImportPreset(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => await DspPresetDialogs.ImportAsync(this);
    private async void ExportPreset(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => await DspPresetDialogs.ExportAsync(this);
    private void ArrangeEditor()
    {
        var narrow = Bounds.Width < 760;
        var plotHeight = Math.Clamp(Bounds.Height * .38, 200, 310);
        EqPlot.Height = narrow ? Math.Min(210, plotHeight) : plotHeight;
        EqToolbar.ColumnDefinitions = new(narrow ? "*" : "*,Auto");
        EqToolbar.RowDefinitions = new(narrow ? "Auto,Auto" : "Auto");
        Grid.SetColumn(EqTools, narrow ? 0 : 1); Grid.SetRow(EqTools, narrow ? 1 : 0);
        EqTools.Margin = narrow ? new Thickness(0, 8, 0, 0) : default;
        PresetPicker.Width = Bounds.Width < 520 ? 156 : 240;
        TrimControls.ColumnDefinitions = new(narrow ? "*" : "*,*");
        TrimControls.RowDefinitions = new(narrow ? "Auto,Auto" : "Auto");
        Grid.SetColumn(TiltField, narrow ? 0 : 1); Grid.SetRow(TiltField, narrow ? 1 : 0);
        if (narrow) TiltField.Margin = new Thickness(0, 8, 0, 0); else TiltField.Margin = default;
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
