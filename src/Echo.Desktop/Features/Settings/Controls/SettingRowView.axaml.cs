using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Echo.Desktop.Features.Settings.Models;

namespace Echo.Desktop.Features.Settings.Controls;

public partial class SettingRowView : UserControl
{
    private SettingRowViewModel? _model;
    private IDisposable? _highlightBrush;
    public SettingRowView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ArrangeEditor();
        AttachedToVisualTree += (_, _) =>
        {
            _model = DataContext as SettingRowViewModel;
            if (_model is not null)
            {
                RowEditor.ContentTemplate = (IDataTemplate)Resources[_model.Definition.Kind + "Editor"]!;
                RowEditor.Content = _model;
            }
            if (_model is not null) _model.PropertyChanged += OnRowChanged;
            ArrangeEditor();
            RevealMatch();
        };
        DetachedFromVisualTree += (_, _) => { if (_model is not null) _model.PropertyChanged -= OnRowChanged; _highlightBrush?.Dispose(); _highlightBrush = null; _model = null; };
    }
    private void ArrangeEditor()
    {
        // Keep switches/selects inline; larger editors flow beneath their label.
        var stacked = Bounds.Width < 520 && _model is { } model &&
            (model.IsAction || model.IsChoice || model.IsSlider || model.IsShortcut || model.IsText);
        Grid.SetColumn(RowEditor, stacked ? 0 : 1);
        Grid.SetRow(RowEditor, stacked ? 1 : 0);
        Grid.SetColumnSpan(RowEditor, stacked ? 2 : 1);
        RowEditor.Margin = new Avalonia.Thickness(0, stacked ? 10 : 0, 0, 0);
        RowEditor.MaxWidth = Math.Min(520, Math.Max(0, Bounds.Width));
        RowEditor.HorizontalAlignment = stacked ? Avalonia.Layout.HorizontalAlignment.Left : Avalonia.Layout.HorizontalAlignment.Right;
    }
    private void OnRowChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(SettingRowViewModel.IsHighlighted)) RevealMatch(); }
    private void RevealMatch()
    {
        if (_model?.IsHighlighted == true)
        {
            RowBorder.BorderThickness = new Avalonia.Thickness(1);
            _highlightBrush?.Dispose();
            _highlightBrush = RowBorder.Bind(Border.BorderBrushProperty, this.GetResourceObservable("EchoAccent"));
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (_model?.IsHighlighted == true) this.BringIntoView(); }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }
}
